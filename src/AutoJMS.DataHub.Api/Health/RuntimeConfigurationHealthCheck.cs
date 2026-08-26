using AutoJMS.DataHub.Api.Configuration;
using AutoJMS.DataHub.Api.Manifests;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace AutoJMS.DataHub.Api.Health;

/// <summary>
/// Three outcomes, and the difference between them is what happens to traffic:
/// Unhealthy means this host cannot do its job and must stay out of rotation; Degraded
/// means it serves every read and every enrollment correctly but an operator needs to
/// know something; Healthy means nothing to report. /health/ready maps Degraded to 200
/// precisely so that "worth telling you about" does not have to mean "take the site down".
/// </summary>
public sealed class RuntimeConfigurationHealthCheck(
    DataHubRuntimeOptions options,
    IManifestRootProbe manifestRootProbe) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var missing = new List<string>();
        if (!options.HasValidChannel) missing.Add("DATAHUB_CHANNEL (staging or production)");
        if (!HasMatchingEnvironmentAndChannel(options.EnvironmentName, options.Channel))
            missing.Add("ASPNETCORE_ENVIRONMENT/DATAHUB_CHANNEL environment/channel mismatch");
        if (string.IsNullOrWhiteSpace(options.ConnectionString)) missing.Add("ConnectionStrings__DataHub");
        if (!HasSecret(options.DeviceTokenSigningKey)) missing.Add("DATAHUB_DEVICE_TOKEN_SIGNING_KEY");
        if (!HasSecret(options.EnrollmentPepper)) missing.Add("DATAHUB_ENROLLMENT_PEPPER");

        var isProduction = string.Equals(options.Channel, DataHubRuntimeOptions.AllowedProductionChannel, StringComparison.Ordinal);
        var stagingIssuerEnabled = StagingTestIssuerPolicy.IsEnabled(options.EnvironmentName, options.AllowStagingTestIssuer)
            && string.Equals(options.Channel, DataHubRuntimeOptions.AllowedStagingChannel, StringComparison.Ordinal);
        if (isProduction)
        {
            // Was unconditional, which made a correctly-configured production host
            // report Unhealthy forever — and /health/ready is what the deploy gate
            // and the proxy read, so the instance could never be brought into
            // service. Report on the key material that actually decides whether
            // IdentityServiceCollectionExtensions can wire the RSA validator.
            if (!Auth.RsaLicenseAssertionValidator.HasKeyMaterial(options))
                missing.Add("DATAHUB_LICENSE_ASSERTION_PUBLIC_KEY or DATAHUB_LICENSE_ASSERTION_PUBLIC_KEY_PATH");
        }
        else if (stagingIssuerEnabled)
        {
            if (!HasSecret(options.StagingTestSigningKey))
                missing.Add("DATAHUB_STAGING_TEST_SIGNING_KEY");
        }
        else
        {
            missing.Add("staging license verifier (enable the staging test issuer or install the signed-assertion verifier)");
        }

        // Identity variables all carry defaults, so the answerable question is whether the
        // operator set them. Two distinct faults hide behind a default: a license
        // issuer/audience that does not match what the license server mints rejects every
        // real assertion, and an unsuffixed device issuer/audience makes a staging device
        // token indistinguishable from a production one, because both channels land on the
        // same pair. Fatal on production; on staging both sides can legitimately default to
        // the same value under the test issuer, so it is only worth saying out loud.
        if (isProduction && options.DefaultedIdentityVariables.Count > 0)
            missing.Add(DefaultedIdentities(options));

        // The control-plane root is a mounted volume, so its absence means the mount is
        // wrong — not that the first publish has yet to happen. Left unreported, every
        // /configs/* GET answers 404 and each station silently falls back to the BASE
        // runtime policy, downgrading paid tiers across the whole fleet. Failing loudly
        // here is the cheaper of the two outcomes.
        var manifestRoot = manifestRootProbe.Probe(options.ManifestRoot);
        if (manifestRoot.State == ManifestRootState.Missing)
            missing.Add($"DATAHUB_MANIFEST_ROOT '{options.ManifestRoot}' is not a readable directory ({manifestRoot.Detail})");

        if (missing.Count > 0)
            return Task.FromResult(HealthCheckResult.Unhealthy(
                "Missing or invalid configuration: " + string.Join(", ", missing)));

        var gaps = new List<string>();

        // Reported, not enforced. A missing publish token closes PUT
        // /api/v1/admin/manifests/** with a 503 and a logged error; it must not take
        // the host out of rotation, because reads and enrollment are unaffected and
        // an unready host serves nobody. It used to ride along on a Healthy result, where
        // a green dashboard hid the fact that no release could ever reach the fleet.
        if (!HasSecret(options.ManifestAdminToken))
            gaps.Add("DATAHUB_ADMIN_TOKEN is not configured, so manifest publishing is closed");

        if (!isProduction && options.DefaultedIdentityVariables.Count > 0)
            gaps.Add(DefaultedIdentities(options));

        // Objects already published stay readable, so this costs publishing only.
        if (manifestRoot.State == ManifestRootState.ReadOnly)
            gaps.Add($"DATAHUB_MANIFEST_ROOT '{options.ManifestRoot}' is not writable ({manifestRoot.Detail})");

        // Startup drops these from the trust list and, if none survive, falls back to the
        // built-in ranges — so the service works, but X-Forwarded-For is being trusted on a
        // basis the operator did not choose, and the per-IP limits partition accordingly.
        var malformedProxyNetworks = DataHubRuntimeOptions.MalformedTrustedProxyNetworks(options.TrustedProxyNetworks);
        if (malformedProxyNetworks.Count > 0)
            gaps.Add("DATAHUB_TRUSTED_PROXY_NETWORKS has entries that do not parse and are ignored ("
                + string.Join(", ", malformedProxyNetworks) + ")");

        var result = gaps.Count == 0
            ? HealthCheckResult.Healthy("Runtime configuration is valid.")
            : HealthCheckResult.Degraded("Runtime configuration is serviceable with gaps: " + string.Join(", ", gaps));
        return Task.FromResult(result);
    }

    private static string DefaultedIdentities(DataHubRuntimeOptions options)
        => "identity variables left at built-in defaults (" + string.Join(", ", options.DefaultedIdentityVariables) + ")";

    private static bool HasSecret(string? value) => !string.IsNullOrWhiteSpace(value) && value.Trim().Length >= 32;

    private static bool HasMatchingEnvironmentAndChannel(string? environmentName, string? channel)
        => string.Equals(environmentName, "Staging", StringComparison.OrdinalIgnoreCase)
            ? string.Equals(channel, DataHubRuntimeOptions.AllowedStagingChannel, StringComparison.Ordinal)
            : string.Equals(environmentName, "Production", StringComparison.OrdinalIgnoreCase)
                && string.Equals(channel, DataHubRuntimeOptions.AllowedProductionChannel, StringComparison.Ordinal);
}

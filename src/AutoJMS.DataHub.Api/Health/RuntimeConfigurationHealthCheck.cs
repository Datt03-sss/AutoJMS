using AutoJMS.DataHub.Api.Configuration;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace AutoJMS.DataHub.Api.Health;

public sealed class RuntimeConfigurationHealthCheck(DataHubRuntimeOptions options) : IHealthCheck
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

        // Reported, not enforced. A missing publish token closes PUT
        // /api/v1/admin/manifests/** with a 503 and a logged error; it must not take
        // the host out of rotation, because reads and enrollment are unaffected and
        // an unready host serves nobody.
        var notes = HasSecret(options.ManifestAdminToken)
            ? "Runtime configuration is valid."
            : "Runtime configuration is valid. DATAHUB_ADMIN_TOKEN is not configured, so manifest publishing is closed.";

        var result = missing.Count == 0
            ? HealthCheckResult.Healthy(notes)
            : HealthCheckResult.Unhealthy("Missing or invalid configuration: " + string.Join(", ", missing));
        return Task.FromResult(result);
    }

    private static bool HasSecret(string? value) => !string.IsNullOrWhiteSpace(value) && value.Trim().Length >= 32;

    private static bool HasMatchingEnvironmentAndChannel(string? environmentName, string? channel)
        => string.Equals(environmentName, "Staging", StringComparison.OrdinalIgnoreCase)
            ? string.Equals(channel, DataHubRuntimeOptions.AllowedStagingChannel, StringComparison.Ordinal)
            : string.Equals(environmentName, "Production", StringComparison.OrdinalIgnoreCase)
                && string.Equals(channel, DataHubRuntimeOptions.AllowedProductionChannel, StringComparison.Ordinal);
}

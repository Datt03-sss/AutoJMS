using AutoJMS.DataHub.Api.Configuration;
using AutoJMS.DataHub.Api.Health;
using AutoJMS.DataHub.Api.Manifests;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace AutoJMS.DataHub.Api.Tests.Health;

public sealed class RuntimeConfigurationHealthCheckTests
{
    /// <summary>
    /// The manifest root is stubbed in every case that is not about the manifest root, so
    /// these tests never touch a real filesystem and never depend on whether the machine
    /// running them happens to have a directory at the configured path.
    /// </summary>
    private sealed class StubManifestRootProbe(ManifestRootState state) : IManifestRootProbe
    {
        public ManifestRootProbeResult Probe(string root)
            => new(state, state == ManifestRootState.Writable ? null : "stubbed");
    }

    private static RuntimeConfigurationHealthCheck Check(
        DataHubRuntimeOptions options,
        ManifestRootState manifestRoot = ManifestRootState.Writable)
        => new(options, new StubManifestRootProbe(manifestRoot));

    /// <summary>A production host with nothing wrong with it; each test spoils one thing.</summary>
    private static DataHubRuntimeOptions ValidProduction() => new()
    {
        Channel = "production",
        EnvironmentName = "Production",
        ConnectionString = "Host=postgres;Database=datahub;Username=datahub;Password=test",
        DeviceTokenSigningKey = new string('d', 32),
        EnrollmentPepper = new string('e', 32),
        LicenseAssertionPublicKeyPem = "-----BEGIN PUBLIC KEY-----\nMIIB\n-----END PUBLIC KEY-----",
        ManifestAdminToken = new string('m', 32)
    };

    [Fact]
    public async Task Check_is_unhealthy_when_channel_or_required_secrets_are_missing()
    {
        var check = Check(new DataHubRuntimeOptions());

        var result = await check.CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(HealthStatus.Unhealthy, result.Status);
        Assert.Contains("DATAHUB_CHANNEL", result.Description);
        Assert.Contains("ConnectionStrings__DataHub", result.Description);
        Assert.Contains("DATAHUB_DEVICE_TOKEN_SIGNING_KEY", result.Description);
        Assert.Contains("DATAHUB_ENROLLMENT_PEPPER", result.Description);
    }

    [Fact]
    public async Task Check_is_unhealthy_when_production_has_only_a_staging_test_key()
    {
        var options = ValidProduction();
        options.LicenseAssertionPublicKeyPem = "";
        options.AllowStagingTestIssuer = true;
        options.StagingTestSigningKey = new string('s', 32);

        var result = await Check(options).CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(HealthStatus.Unhealthy, result.Status);
        // A staging HMAC key is not license key material, so the production path must
        // still name the public key it actually needs.
        Assert.Contains("DATAHUB_LICENSE_ASSERTION_PUBLIC_KEY", result.Description);
    }

    [Fact]
    public async Task Check_is_healthy_for_production_once_the_license_public_key_is_present()
    {
        // Regression: this used to report Unhealthy unconditionally on the production
        // channel, so /health/ready never went green and a correctly-configured host
        // could not be brought into service.
        var result = await Check(ValidProduction()).CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(HealthStatus.Healthy, result.Status);
    }

    [Fact]
    public async Task A_missing_publish_token_is_reported_without_taking_the_host_out_of_rotation()
    {
        var options = ValidProduction();
        options.ManifestAdminToken = "";

        var result = await Check(options).CheckHealthAsync(new HealthCheckContext());

        // Reads and enrollment do not depend on the publish token, so the host stays in
        // rotation — Degraded maps to 200 on /health/ready. It is no longer folded into a
        // Healthy result, where a green dashboard hid the fact that no release could reach
        // the fleet.
        Assert.Equal(HealthStatus.Degraded, result.Status);
        Assert.Contains("DATAHUB_ADMIN_TOKEN", result.Description);
    }

    [Fact]
    public async Task An_absent_manifest_root_takes_the_host_out_of_rotation()
    {
        // A missing root means the volume is not mounted where the API expects it. Every
        // /configs/* GET would 404 and every station would silently fall back to the BASE
        // runtime policy, so this must be louder than a note.
        var result = await Check(ValidProduction(), ManifestRootState.Missing)
            .CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(HealthStatus.Unhealthy, result.Status);
        Assert.Contains("DATAHUB_MANIFEST_ROOT", result.Description);
    }

    [Fact]
    public async Task A_read_only_manifest_root_only_costs_publishing()
    {
        // Objects already published stay readable, so the fleet is still served.
        var result = await Check(ValidProduction(), ManifestRootState.ReadOnly)
            .CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(HealthStatus.Degraded, result.Status);
        Assert.Contains("DATAHUB_MANIFEST_ROOT", result.Description);
    }

    [Fact]
    public async Task Production_must_not_inherit_the_default_issuer_and_audience()
    {
        // The defaults are shared with staging, so a production host that inherited them
        // would accept a staging device token and reject every real license assertion.
        var options = ValidProduction();
        options.DefaultedIdentityVariables = ["DATAHUB_LICENSE_ASSERTION_ISSUER"];

        var result = await Check(options).CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(HealthStatus.Unhealthy, result.Status);
        Assert.Contains("DATAHUB_LICENSE_ASSERTION_ISSUER", result.Description);
    }

    [Fact]
    public async Task Staging_may_inherit_the_default_issuer_and_audience_but_is_told()
    {
        // Under the staging test issuer the minting script defaults to the same value, so
        // the pair still matches and enrollment works. Worth saying, not worth a 503.
        var check = Check(new DataHubRuntimeOptions
        {
            Channel = "staging",
            EnvironmentName = "Staging",
            ConnectionString = "Host=postgres;Database=datahub;Username=datahub;Password=test",
            DeviceTokenSigningKey = new string('d', 32),
            EnrollmentPepper = new string('e', 32),
            AllowStagingTestIssuer = true,
            StagingTestSigningKey = new string('s', 32),
            ManifestAdminToken = new string('m', 32),
            DefaultedIdentityVariables = ["DATAHUB_LICENSE_ASSERTION_ISSUER"]
        });

        var result = await check.CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(HealthStatus.Degraded, result.Status);
        Assert.Contains("DATAHUB_LICENSE_ASSERTION_ISSUER", result.Description);
    }

    [Fact]
    public async Task Unparseable_proxy_networks_are_named_rather_than_dropped_in_silence()
    {
        var options = ValidProduction();
        options.TrustedProxyNetworks = "10.0.0.0/8,not-a-cidr";

        var result = await Check(options).CheckHealthAsync(new HealthCheckContext());

        // Startup already ignores the bad entry; the point of reporting it is that the
        // operator's intended trust list is not the one deciding whose X-Forwarded-For
        // is honoured.
        Assert.Equal(HealthStatus.Degraded, result.Status);
        Assert.Contains("not-a-cidr", result.Description);
    }

    [Fact]
    public async Task Check_accepts_staging_test_issuer_only_with_all_staging_requirements()
    {
        var check = Check(new DataHubRuntimeOptions
        {
            Channel = "staging",
            ConnectionString = "Host=postgres;Database=datahub;Username=datahub;Password=test",
            DeviceTokenSigningKey = new string('d', 32),
            EnrollmentPepper = new string('e', 32),
            AllowStagingTestIssuer = true,
            StagingTestSigningKey = new string('s', 32),
            ManifestAdminToken = new string('m', 32),
            EnvironmentName = "Staging"
        });

        var result = await check.CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(HealthStatus.Healthy, result.Status);
    }

    [Fact]
    public async Task Check_is_unhealthy_when_staging_has_no_available_assertion_validator()
    {
        var check = Check(new DataHubRuntimeOptions
        {
            Channel = "staging",
            ConnectionString = "Host=postgres;Database=datahub;Username=datahub;Password=test",
            DeviceTokenSigningKey = new string('d', 32),
            EnrollmentPepper = new string('e', 32),
            AllowStagingTestIssuer = false,
            EnvironmentName = "Staging"
        });

        var result = await check.CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(HealthStatus.Unhealthy, result.Status);
        Assert.Contains("staging license verifier", result.Description, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("Staging", "production")]
    [InlineData("Production", "staging")]
    public async Task Check_is_unhealthy_when_environment_and_channel_are_mismatched(string environmentName, string channel)
    {
        var check = Check(new DataHubRuntimeOptions
        {
            EnvironmentName = environmentName,
            Channel = channel,
            ConnectionString = "Host=postgres;Database=datahub;Username=datahub;Password=test",
            DeviceTokenSigningKey = new string('d', 32),
            EnrollmentPepper = new string('e', 32),
            StagingTestSigningKey = new string('s', 32),
            AllowStagingTestIssuer = true
        });

        var result = await check.CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(HealthStatus.Unhealthy, result.Status);
        Assert.Contains("environment/channel", result.Description, StringComparison.OrdinalIgnoreCase);
    }
}

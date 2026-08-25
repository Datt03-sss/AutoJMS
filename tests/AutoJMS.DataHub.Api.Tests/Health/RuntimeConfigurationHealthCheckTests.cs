using AutoJMS.DataHub.Api.Configuration;
using AutoJMS.DataHub.Api.Health;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace AutoJMS.DataHub.Api.Tests.Health;

public sealed class RuntimeConfigurationHealthCheckTests
{
    [Fact]
    public async Task Check_is_unhealthy_when_channel_or_required_secrets_are_missing()
    {
        var check = new RuntimeConfigurationHealthCheck(new DataHubRuntimeOptions());

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
        var check = new RuntimeConfigurationHealthCheck(new DataHubRuntimeOptions
        {
            Channel = "production",
            ConnectionString = "Host=postgres;Database=datahub;Username=datahub;Password=test",
            DeviceTokenSigningKey = new string('d', 32),
            EnrollmentPepper = new string('e', 32),
            AllowStagingTestIssuer = true,
            StagingTestSigningKey = new string('s', 32),
            EnvironmentName = "Production"
        });

        var result = await check.CheckHealthAsync(new HealthCheckContext());

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
        var check = new RuntimeConfigurationHealthCheck(new DataHubRuntimeOptions
        {
            Channel = "production",
            ConnectionString = "Host=postgres;Database=datahub;Username=datahub;Password=test",
            DeviceTokenSigningKey = new string('d', 32),
            EnrollmentPepper = new string('e', 32),
            LicenseAssertionPublicKeyPem = "-----BEGIN PUBLIC KEY-----\nMIIB\n-----END PUBLIC KEY-----",
            EnvironmentName = "Production"
        });

        var result = await check.CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(HealthStatus.Healthy, result.Status);
    }

    [Fact]
    public async Task A_missing_publish_token_is_reported_without_taking_the_host_out_of_rotation()
    {
        var check = new RuntimeConfigurationHealthCheck(new DataHubRuntimeOptions
        {
            Channel = "production",
            ConnectionString = "Host=postgres;Database=datahub;Username=datahub;Password=test",
            DeviceTokenSigningKey = new string('d', 32),
            EnrollmentPepper = new string('e', 32),
            LicenseAssertionPublicKeyPem = "-----BEGIN PUBLIC KEY-----\nMIIB\n-----END PUBLIC KEY-----",
            EnvironmentName = "Production",
            ManifestAdminToken = ""
        });

        var result = await check.CheckHealthAsync(new HealthCheckContext());

        // Reads and enrollment do not depend on the publish token; readiness must not
        // either. The operator still gets told.
        Assert.Equal(HealthStatus.Healthy, result.Status);
        Assert.Contains("DATAHUB_ADMIN_TOKEN", result.Description);
    }

    [Fact]
    public async Task Check_accepts_staging_test_issuer_only_with_all_staging_requirements()
    {
        var check = new RuntimeConfigurationHealthCheck(new DataHubRuntimeOptions
        {
            Channel = "staging",
            ConnectionString = "Host=postgres;Database=datahub;Username=datahub;Password=test",
            DeviceTokenSigningKey = new string('d', 32),
            EnrollmentPepper = new string('e', 32),
            AllowStagingTestIssuer = true,
            StagingTestSigningKey = new string('s', 32),
            EnvironmentName = "Staging"
        });

        var result = await check.CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(HealthStatus.Healthy, result.Status);
    }

    [Fact]
    public async Task Check_is_unhealthy_when_staging_has_no_available_assertion_validator()
    {
        var check = new RuntimeConfigurationHealthCheck(new DataHubRuntimeOptions
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
        var check = new RuntimeConfigurationHealthCheck(new DataHubRuntimeOptions
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

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
            LicenseAssertionValidationKey = string.Empty,
            AllowStagingTestIssuer = true,
            StagingTestSigningKey = new string('s', 32),
            EnvironmentName = "Production"
        });

        var result = await check.CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(HealthStatus.Unhealthy, result.Status);
        Assert.Contains("DATAHUB_LICENSE_ASSERTION_VALIDATION_KEY", result.Description);
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
}

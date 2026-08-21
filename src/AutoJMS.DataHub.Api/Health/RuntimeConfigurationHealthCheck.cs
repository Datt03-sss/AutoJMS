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
        if (string.IsNullOrWhiteSpace(options.ConnectionString)) missing.Add("ConnectionStrings__DataHub");
        if (!HasSecret(options.DeviceTokenSigningKey)) missing.Add("DATAHUB_DEVICE_TOKEN_SIGNING_KEY");
        if (!HasSecret(options.EnrollmentPepper)) missing.Add("DATAHUB_ENROLLMENT_PEPPER");

        var stagingIssuerEnabled = StagingTestIssuerPolicy.IsEnabled(options.EnvironmentName, options.AllowStagingTestIssuer);
        if (string.Equals(options.Channel, DataHubRuntimeOptions.AllowedProductionChannel, StringComparison.Ordinal)
            || !stagingIssuerEnabled)
        {
            if (!HasSecret(options.LicenseAssertionValidationKey))
                missing.Add("DATAHUB_LICENSE_ASSERTION_VALIDATION_KEY");
        }
        else if (!HasSecret(options.StagingTestSigningKey))
        {
            missing.Add("DATAHUB_STAGING_TEST_SIGNING_KEY");
        }

        var result = missing.Count == 0
            ? HealthCheckResult.Healthy("Runtime configuration is valid.")
            : HealthCheckResult.Unhealthy("Missing or invalid configuration: " + string.Join(", ", missing));
        return Task.FromResult(result);
    }

    private static bool HasSecret(string? value) => !string.IsNullOrWhiteSpace(value) && value.Trim().Length >= 32;
}

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
            missing.Add("production license verifier integration (asymmetric issuer/JWKS)");
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

        var result = missing.Count == 0
            ? HealthCheckResult.Healthy("Runtime configuration is valid.")
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

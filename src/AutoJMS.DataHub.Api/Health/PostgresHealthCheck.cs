using AutoJMS.DataHub.Api.Infrastructure;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace AutoJMS.DataHub.Api.Health;

public sealed class PostgresHealthCheck(IDataHubDatabaseProbe databaseProbe) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var available = await databaseProbe.CanConnectAsync(cancellationToken);
        return available
            ? HealthCheckResult.Healthy("PostgreSQL is reachable.")
            : HealthCheckResult.Unhealthy("PostgreSQL is unavailable.");
    }
}

using System.Net.Sockets;
using AutoJMS.DataHub.Api.Configuration;
using AutoJMS.DataHub.Api.Infrastructure;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Npgsql;

namespace AutoJMS.DataHub.Api.Health;

/// <summary>
/// Degraded, never Unhealthy. A horizon that outlives a retention policy is a real fault an
/// operator must fix, but the host is still serving every read and every write correctly —
/// and <c>/health/ready</c> maps Unhealthy to 503, which docker-compose reads as "do not
/// route to this container". Removing a working host from rotation would be a self-inflicted
/// outage on top of a configuration mistake.
///
/// Separate from <see cref="RuntimeConfigurationHealthCheck"/> because that one is
/// deliberately pure configuration with no I/O; giving it a database read would change what
/// its failures mean.
/// </summary>
public sealed class IngestHorizonHealthCheck(
    DataHubRuntimeOptions options,
    IRetentionPolicyReader reader) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<RetentionDeletePolicy> policies;
        try
        {
            policies = await reader.ReadEventDeletePoliciesAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is NpgsqlException or TimeoutException or InvalidOperationException or SocketException)
        {
            // "Can we reach the database" is PostgresHealthCheck's question, and it carries
            // the same ready tag. Answering it again here would double-count one outage and
            // point the operator at the wrong setting.
            return HealthCheckResult.Healthy(
                "The ingest horizon invariant was not evaluated; the database check owns that report.");
        }

        var violations = IngestHorizonInvariant.Violations(policies, options.IngestHorizon);
        return violations.Count == 0
            ? HealthCheckResult.Healthy("The ingest horizon is shorter than every event retention policy.")
            : HealthCheckResult.Degraded("Ingest horizon invariant violated: " + string.Join("; ", violations));
    }
}

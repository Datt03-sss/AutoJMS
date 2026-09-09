using System.Net.Sockets;
using AutoJMS.DataHub.Api.Configuration;
using AutoJMS.DataHub.Api.Infrastructure;
using Npgsql;

namespace AutoJMS.DataHub.Api.Health;

/// <summary>
/// Refuses the boot when a retention policy would delete events inside the ingest horizon.
///
/// Fail-fast is the right shape here because the damage is silent and cumulative: past the
/// dedupe window, <c>ON CONFLICT DO NOTHING</c> has nothing left to conflict with, so the
/// same scans are re-accepted on every retry and rebuild projection state from history the
/// server has already decided to forget. A host that starts and quietly does that is worse
/// than a host that does not start.
///
/// An unreadable database is explicitly NOT a boot refusal. That would turn a transient
/// outage into one that needs a human, and reporting an unreachable database already
/// belongs to the readiness probe.
/// </summary>
public static class IngestHorizonStartupCheck
{
    public static async Task RunAsync(
        IServiceProvider services,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var options = services.GetRequiredService<DataHubRuntimeOptions>();
        var reader = services.GetRequiredService<IRetentionPolicyReader>();

        IReadOnlyList<RetentionDeletePolicy> policies;
        try
        {
            policies = await reader.ReadEventDeletePoliciesAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is NpgsqlException or TimeoutException or InvalidOperationException or SocketException)
        {
            logger.LogWarning(
                exception,
                "The ingest horizon invariant could not be checked at startup because the database was unreadable. The readiness probe will report the database, and the health check re-evaluates the invariant on every poll.");
            return;
        }

        var violations = IngestHorizonInvariant.Violations(policies, options.IngestHorizon);
        if (violations.Count > 0)
            throw new InvalidOperationException(
                "Refusing to start: the ingest horizon is not shorter than event retention. "
                + string.Join("; ", violations)
                + ". Raise the retention policy's delete_after, or lower DATAHUB_INGEST_HORIZON_DAYS.");
    }
}

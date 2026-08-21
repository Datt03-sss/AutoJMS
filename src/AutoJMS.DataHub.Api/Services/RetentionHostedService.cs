using AutoJMS.DataHub.Api.Configuration;
using AutoJMS.DataHub.Api.Infrastructure;

namespace AutoJMS.DataHub.Api.Services;

public sealed class RetentionHostedService(
    RetentionRepository repository,
    DataHubRuntimeOptions options,
    ILogger<RetentionHostedService> logger,
    TimeProvider clock) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(options.RetentionInterval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                var result = await repository.RunOnceAsync(options.RetentionBatchSize, stoppingToken);
                if (result != RetentionRunResult.Empty)
                    logger.LogInformation(
                        "DataHub retention removed {Events} events, {Changes} changes, {AuditLogs} audit logs, and {Idempotency} idempotency records at {UtcNow}.",
                        result.DeletedEvents,
                        result.DeletedChanges,
                        result.DeletedAuditLogs,
                        result.DeletedIdempotencyRecords,
                        clock.GetUtcNow());
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                // Retention is best-effort. A transient database outage must not
                // terminate the API or affect ingest availability.
                logger.LogWarning(exception, "DataHub retention pass failed; it will retry on the next interval.");
            }
        }
    }
}

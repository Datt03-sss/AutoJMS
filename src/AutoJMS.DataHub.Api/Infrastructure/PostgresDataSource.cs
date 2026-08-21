using AutoJMS.DataHub.Api.Configuration;
using Npgsql;

namespace AutoJMS.DataHub.Api.Infrastructure;

public interface IDataHubDatabaseProbe
{
    Task<bool> CanConnectAsync(CancellationToken cancellationToken);
}

public sealed class PostgresDataSource : IAsyncDisposable
{
    private readonly NpgsqlDataSource? _dataSource;

    public PostgresDataSource(DataHubRuntimeOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.ConnectionString)) return;

        try
        {
            var builder = new NpgsqlConnectionStringBuilder(options.ConnectionString)
            {
                MaxPoolSize = Math.Clamp(options.MaximumPoolSize, 1, 100),
                MinPoolSize = 0,
                ApplicationName = "AutoJMS.DataHub.Api"
            };
            _dataSource = NpgsqlDataSource.Create(builder.ConnectionString);
        }
        catch (ArgumentException)
        {
            // Readiness reports a malformed connection string as unhealthy; the
            // process should still expose liveness for orchestrator diagnostics.
            _dataSource = null;
        }
    }

    public bool IsConfigured => _dataSource is not null;

    public ValueTask<NpgsqlConnection> OpenConnectionAsync(CancellationToken cancellationToken)
    {
        if (_dataSource is null)
            throw new InvalidOperationException("The PostgreSQL data source is not configured.");
        return _dataSource.OpenConnectionAsync(cancellationToken);
    }

    public async Task<bool> CanConnectAsync(CancellationToken cancellationToken)
    {
        if (_dataSource is null) return false;
        try
        {
            await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
            await using var command = new NpgsqlCommand("""
                SELECT (
                    (SELECT count(*)
                       FROM pg_class c
                       JOIN pg_namespace n ON n.oid = c.relnamespace
                      WHERE n.nspname = 'public'
                        AND c.relkind IN ('r', 'p')
                        AND c.relname IN (
                            'schema_migrations', 'sites', 'devices', 'site_fetch_leases',
                            'site_change_counters', 'waybill_scan_events',
                            'waybill_projections', 'dashboard_changes',
                            'jms_event_policies', 'idempotency_records',
                            'retention_policies', 'audit_logs')) >= 12
                    AND EXISTS (SELECT 1 FROM schema_migrations WHERE version = '001_core')
                    AND EXISTS (SELECT 1 FROM schema_migrations WHERE version = '002_seed_policies')
                    AND EXISTS (SELECT 1 FROM schema_migrations WHERE version = '003_seed_retention')
                    AND EXISTS (SELECT 1 FROM schema_migrations WHERE version = '004_projection_slot_payloads')
                    AND EXISTS (SELECT 1 FROM schema_migrations WHERE version = '005_change_retention_floor')
                    AND EXISTS (SELECT 1 FROM jms_event_policies)
                    AND EXISTS (SELECT 1 FROM retention_policies)
                );
                """, connection);
            return (bool)(await command.ExecuteScalarAsync(cancellationToken) ?? false);
        }
        catch (NpgsqlException)
        {
            return false;
        }
        catch (TimeoutException)
        {
            return false;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
    }

    public ValueTask DisposeAsync()
        => _dataSource is null ? ValueTask.CompletedTask : _dataSource.DisposeAsync();
}

public sealed class NpgsqlDatabaseProbe(PostgresDataSource dataSource) : IDataHubDatabaseProbe
{
    public Task<bool> CanConnectAsync(CancellationToken cancellationToken)
        => dataSource.CanConnectAsync(cancellationToken);
}

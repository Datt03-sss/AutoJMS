using System.Globalization;
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
            _dataSource = NpgsqlDataSource.Create(BuildConnectionString(options));
        }
        catch (ArgumentException)
        {
            // Readiness reports a malformed connection string as unhealthy; the
            // process should still expose liveness for orchestrator diagnostics.
            _dataSource = null;
        }
    }

    /// <summary>
    /// Applies the pool limits and the server-side deadlines every connection must carry.
    ///
    /// The deadlines are PostgreSQL <em>startup</em> options rather than a per-transaction
    /// <c>SET LOCAL</c>, which is what makes them impossible to forget: a repository that
    /// never issues a SET still gets a bounded statement, and <c>DISCARD ALL</c> on pool
    /// return resets to these values instead of clearing them. Repositories with a
    /// narrower need — ingest's <c>lock_timeout</c> — still layer SET LOCAL on top.
    /// </summary>
    public static string BuildConnectionString(DataHubRuntimeOptions options)
    {
        var builder = new NpgsqlConnectionStringBuilder(options.ConnectionString)
        {
            MaxPoolSize = Math.Clamp(options.MaximumPoolSize, 1, 100),
            MinPoolSize = 0,
            ApplicationName = "AutoJMS.DataHub.Api"
        };

        var seconds = Math.Clamp(
            options.DatabaseStatementTimeoutSeconds,
            DataHubRuntimeOptions.MinimumStatementTimeoutSeconds,
            DataHubRuntimeOptions.MaximumStatementTimeoutSeconds);

        // idle_in_transaction_session_timeout covers the failure statement_timeout cannot:
        // a transaction that finished its statements and then lost its client keeps every
        // FOR UPDATE row locked until TCP notices. Twice the statement budget, because a
        // transaction legitimately runs several statements back to back.
        var deadlines = string.Create(
            CultureInfo.InvariantCulture,
            $"-c statement_timeout={seconds}s -c idle_in_transaction_session_timeout={seconds * 2}s");

        // Operator settings go last on purpose: PostgreSQL applies -c options in order, so
        // a deployment that needs a different budget overrides these without a code change.
        builder.Options = string.IsNullOrWhiteSpace(builder.Options)
            ? deadlines
            : deadlines + " " + builder.Options;

        // Deliberately above the server deadline. Equal values race, and whoever wins
        // decides how the failure looks; with the server first, a runaway query always
        // ends as PostgreSQL 57014 with the backend already gone, rather than Npgsql
        // walking away from a statement that keeps running and keeps its locks.
        builder.CommandTimeout = seconds + 5;

        return builder.ConnectionString;
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

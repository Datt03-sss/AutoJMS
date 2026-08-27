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

    /// <summary>
    /// The migrations this image will not start without, in the order they apply. One
    /// entry per file in <c>backend/datahub/migrations</c>, named by the version each
    /// file records in <c>schema_migrations</c> — which is its own file stem.
    ///
    /// This list is what readiness means, so it has to be complete: the previous
    /// version stopped at 005 and a host that had never applied 006 reported ready,
    /// which is a probe answering a question about an older build. It is a compile-time
    /// list rather than a directory read because the API image does not ship the SQL
    /// files — it declares the schema it needs, and the deploy applies them. Keeping
    /// the two in step is <c>SchemaContractTests</c>' job, so a migration added without
    /// this line fails a test instead of quietly widening what "ready" accepts.
    /// </summary>
    public static readonly string[] RequiredMigrations =
    [
        "001_core",
        "002_seed_policies",
        "003_seed_retention",
        "004_projection_slot_payloads",
        "005_change_retention_floor",
        "006_revocation_and_retention_indexes"
    ];

    /// <summary>Every table those migrations create. Same contract, same test.</summary>
    public static readonly string[] RequiredTables =
    [
        "schema_migrations", "sites", "devices", "site_fetch_leases",
        "site_change_counters", "waybill_scan_events", "waybill_projections",
        "dashboard_changes", "jms_event_policies", "idempotency_records",
        "retention_policies", "audit_logs", "revoked_device_credentials"
    ];

    /// <summary>
    /// The probe's query, built once. Public so a test can read it: it is assembled by
    /// string interpolation, and without a live PostgreSQL in the unit suite the only
    /// way to catch a malformed clause before deployment is to look at the text.
    /// </summary>
    public static readonly string ReadinessSql = BuildReadinessSql();

    private static string BuildReadinessSql()
    {
        // Interpolated as SQL literals, which is safe for exactly one reason: both
        // arrays are compile-time constants declared above. The guard makes that
        // reason enforced rather than assumed — a name with a quote in it fails at
        // type initialisation, on startup, instead of at the first readiness poll.
        foreach (var identifier in RequiredTables.Concat(RequiredMigrations))
        {
            if (identifier.Length == 0 || !identifier.All(ch => char.IsAsciiLetterOrDigit(ch) || ch == '_'))
                throw new InvalidOperationException($"Readiness identifier '{identifier}' is not a bare identifier.");
        }

        var tables = string.Join(", ", RequiredTables.Select(table => $"'{table}'"));
        var migrations = string.Join(", ", RequiredMigrations.Select(version => $"'{version}'"));

        // `=` rather than `>=`: the IN list bounds the count at its own length, so the
        // old `>= 12` next to a list of exactly 12 names read as though a spare table
        // somewhere could satisfy it. One aggregate over schema_migrations replaces one
        // EXISTS per version — the same answer in a single scan, and it no longer needs
        // a new line of SQL per migration.
        return $"""
            SELECT (
                (SELECT count(*)
                   FROM pg_class c
                   JOIN pg_namespace n ON n.oid = c.relnamespace
                  WHERE n.nspname = 'public'
                    AND c.relkind IN ('r', 'p')
                    AND c.relname IN ({tables})) = {RequiredTables.Length}
                AND (SELECT count(*)
                       FROM schema_migrations
                      WHERE version IN ({migrations})) = {RequiredMigrations.Length}
                AND EXISTS (SELECT 1 FROM jms_event_policies)
                AND EXISTS (SELECT 1 FROM retention_policies)
            );
            """;
    }

    public async Task<bool> CanConnectAsync(CancellationToken cancellationToken)
    {
        if (_dataSource is null) return false;
        try
        {
            await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
            await using var command = new NpgsqlCommand(ReadinessSql, connection);
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

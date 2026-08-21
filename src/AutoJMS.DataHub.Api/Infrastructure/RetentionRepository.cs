using Npgsql;
using NpgsqlTypes;

namespace AutoJMS.DataHub.Api.Infrastructure;

public sealed record RetentionRunResult(int DeletedEvents, int DeletedChanges, int DeletedAuditLogs, int DeletedIdempotencyRecords)
{
    public static RetentionRunResult Empty { get; } = new(0, 0, 0, 0);
}

/// <summary>
/// Applies only the allow-listed retention clocks. Policies are data, but table
/// and column names are deliberately not interpolated from arbitrary rows.
/// </summary>
public sealed class RetentionRepository(PostgresDataSource dataSource)
{
    public async Task<RetentionRunResult> RunOnceAsync(int batchSize, CancellationToken cancellationToken)
    {
        batchSize = Math.Clamp(batchSize, 100, 5000);
        // Each category gets its own short transaction. Ingest locks
        // idempotency -> counter -> event; keeping retention categories in one
        // transaction would create the inverse counter/event or counter/idempotency
        // order and can deadlock under concurrent traffic.
        var deletedIdempotency = await RunPartAsync(DeleteExpiredIdempotencyAsync, batchSize, cancellationToken);
        var deletedChanges = await RunPartAsync(DeleteChangesAsync, batchSize, cancellationToken);
        var deletedEvents = await RunPartAsync(DeleteEventsAsync, batchSize, cancellationToken);
        var deletedAudit = await RunPartAsync(DeleteAuditLogsAsync, batchSize, cancellationToken);
        return new RetentionRunResult(deletedEvents, deletedChanges, deletedAudit, deletedIdempotency);
    }

    private async Task<int> RunPartAsync(
        Func<NpgsqlConnection, NpgsqlTransaction, int, CancellationToken, Task<int>> operation,
        int batchSize,
        CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using var lockCommand = new NpgsqlCommand(
            "SELECT pg_try_advisory_xact_lock(hashtext('autojms.datahub.retention'));",
            connection,
            transaction);
        var acquired = (bool)(await lockCommand.ExecuteScalarAsync(cancellationToken) ?? false);
        if (!acquired)
        {
            await transaction.RollbackAsync(cancellationToken);
            return 0;
        }

        var deleted = await operation(connection, transaction, batchSize, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return deleted;
    }

    private static Task<int> DeleteEventsAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        int batchSize,
        CancellationToken cancellationToken)
    {
        // Event IDs are optional historical references, not foreign keys. Keep the
        // winner IDs stable when observations expire so retention does not mutate
        // projection state without allocating a dashboard change.
        const string sql = """
            WITH candidates AS (
                SELECT e.id
                  FROM waybill_scan_events e
                  LEFT JOIN retention_policies site_policy
                    ON site_policy.site_id = e.site_id
                   AND site_policy.table_name = 'waybill_scan_events'
                  LEFT JOIN retention_policies global_policy
                    ON global_policy.site_id IS NULL
                   AND global_policy.table_name = 'waybill_scan_events'
                 WHERE COALESCE(site_policy.delete_after, global_policy.delete_after) IS NOT NULL
                   AND e.event_occurred_at < now() - COALESCE(site_policy.delete_after, global_policy.delete_after)
                 ORDER BY e.id
                 LIMIT @batch_size
            )
            DELETE FROM waybill_scan_events e
             USING candidates c
             WHERE e.id = c.id;
            """;
        return ExecuteDeleteAsync(connection, transaction, sql, null, batchSize, cancellationToken);
    }

    private static async Task<int> DeleteChangesAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        int batchSize,
        CancellationToken cancellationToken)
    {
        const string sitesSql = """
            WITH retention_state AS (
                SELECT c.site_id,
                       max(c.change_seq) AS max_seq,
                       min(c.change_seq) FILTER (
                           WHERE NOT (
                               COALESCE(site_policy.delete_after, global_policy.delete_after) IS NOT NULL
                               AND c.change_at < now() - COALESCE(site_policy.delete_after, global_policy.delete_after)
                           )
                       ) AS first_recent_seq
                  FROM dashboard_changes c
                  LEFT JOIN retention_policies site_policy
                    ON site_policy.site_id = c.site_id
                   AND site_policy.table_name = 'dashboard_changes'
                  LEFT JOIN retention_policies global_policy
                    ON global_policy.site_id IS NULL
                   AND global_policy.table_name = 'dashboard_changes'
                 GROUP BY c.site_id
            )
            SELECT site_id
              FROM retention_state
             WHERE max_seq IS NOT NULL
               AND (first_recent_seq IS NULL OR first_recent_seq > 1)
             ORDER BY site_id;
            """;
        var siteIds = new List<Guid>();
        await using (var sitesCommand = new NpgsqlCommand(sitesSql, connection, transaction))
        await using (var sitesReader = await sitesCommand.ExecuteReaderAsync(cancellationToken))
        {
            while (await sitesReader.ReadAsync(cancellationToken))
                siteIds.Add(sitesReader.GetGuid(0));
        }

        // Ingest locks the site counter before inserting dashboard_changes. Take
        // the same lock order before deleting changes and advancing the floor.
        if (siteIds.Count == 0) return 0;
        const string lockSql = """
            SELECT site_id
              FROM site_change_counters
             WHERE site_id = ANY(@site_ids)
             ORDER BY site_id
             FOR UPDATE;
            """;
        await using (var lockCommand = new NpgsqlCommand(lockSql, connection, transaction))
        {
            lockCommand.Parameters.Add("site_ids", NpgsqlDbType.Array | NpgsqlDbType.Uuid).Value = siteIds.ToArray();
            await using var lockReader = await lockCommand.ExecuteReaderAsync(cancellationToken);
            while (await lockReader.ReadAsync(cancellationToken)) { }
        }

        const string sql = """
            WITH retention_state AS (
                SELECT c.site_id,
                       max(c.change_seq) AS max_seq,
                       min(c.change_seq) FILTER (
                           WHERE NOT (
                               COALESCE(site_policy.delete_after, global_policy.delete_after) IS NOT NULL
                               AND c.change_at < now() - COALESCE(site_policy.delete_after, global_policy.delete_after)
                           )
                       ) AS first_recent_seq
                  FROM dashboard_changes c
                  LEFT JOIN retention_policies site_policy
                    ON site_policy.site_id = c.site_id
                   AND site_policy.table_name = 'dashboard_changes'
                  LEFT JOIN retention_policies global_policy
                    ON global_policy.site_id IS NULL
                   AND global_policy.table_name = 'dashboard_changes'
                 WHERE c.site_id = ANY(@site_ids)
                 GROUP BY c.site_id
            ), eligible_prefix AS (
                SELECT site_id,
                       CASE WHEN first_recent_seq IS NULL THEN max_seq
                            ELSE first_recent_seq - 1 END AS cutoff_seq
                  FROM retention_state
            ), candidates AS (
                SELECT c.site_id, c.change_seq
                  FROM dashboard_changes c
                  JOIN eligible_prefix p
                    ON p.site_id = c.site_id
                   AND c.change_seq <= p.cutoff_seq
                 ORDER BY c.site_id, c.change_seq
                 LIMIT @batch_size
            )
            -- Only remove a prefix whose every row is old. A newer row with a
            -- lower sequence must retain all later rows so cursor recovery is
            -- either complete or returns RESYNC_REQUIRED.
            DELETE FROM dashboard_changes c
             USING candidates x
            WHERE c.site_id = x.site_id AND c.change_seq = x.change_seq
             RETURNING c.site_id, c.change_seq;
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.Add("site_ids", NpgsqlDbType.Array | NpgsqlDbType.Uuid).Value = siteIds.ToArray();
        command.Parameters.AddWithValue("batch_size", batchSize);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var deleted = 0;
        var floors = new Dictionary<Guid, long>();
        while (await reader.ReadAsync(cancellationToken))
        {
            deleted++;
            var siteId = reader.GetGuid(0);
            var sequence = reader.GetInt64(1);
            floors[siteId] = floors.TryGetValue(siteId, out var current) ? Math.Max(current, sequence) : sequence;
        }
        await reader.CloseAsync();

        foreach (var (siteId, sequence) in floors)
        {
            const string floorSql = """
                UPDATE site_change_counters
                   SET pruned_through_seq = GREATEST(pruned_through_seq, @sequence)
                 WHERE site_id = @site_id;
                """;
            await using var floorCommand = new NpgsqlCommand(floorSql, connection, transaction);
            floorCommand.Parameters.AddWithValue("site_id", siteId);
            floorCommand.Parameters.AddWithValue("sequence", sequence);
            await floorCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        return deleted;
    }

    private static Task<int> DeleteAuditLogsAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        int batchSize,
        CancellationToken cancellationToken)
    {
        const string sql = """
            DELETE FROM audit_logs
             WHERE id IN (
                 SELECT a.id
                   FROM audit_logs a
                   LEFT JOIN retention_policies site_policy
                     ON site_policy.site_id = a.site_id
                    AND site_policy.table_name = 'audit_logs'
                   LEFT JOIN retention_policies global_policy
                     ON global_policy.site_id IS NULL
                    AND global_policy.table_name = 'audit_logs'
                  WHERE COALESCE(site_policy.delete_after, global_policy.delete_after) IS NOT NULL
                    AND a.at < now() - COALESCE(site_policy.delete_after, global_policy.delete_after)
                  ORDER BY a.at, a.id
                  LIMIT @batch_size
             );
            """;
        return ExecuteDeleteAsync(connection, transaction, sql, null, batchSize, cancellationToken);
    }

    private static Task<int> DeleteExpiredIdempotencyAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        int batchSize,
        CancellationToken cancellationToken)
    {
        const string sql = """
            DELETE FROM idempotency_records
             WHERE (site_id, key) IN (
                 SELECT site_id, key
                   FROM idempotency_records
                  WHERE expires_at <= now()
                  ORDER BY expires_at, site_id, key
                  LIMIT @batch_size
             );
            """;
        return ExecuteDeleteAsync(connection, transaction, sql, null, batchSize, cancellationToken);
    }

    private static async Task<int> ExecuteDeleteAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string sql,
        TimeSpan? age,
        int batchSize,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        if (age is not null)
            command.Parameters.AddWithValue("age", age.Value);
        command.Parameters.AddWithValue("batch_size", batchSize);
        return await command.ExecuteNonQueryAsync(cancellationToken);
    }
}

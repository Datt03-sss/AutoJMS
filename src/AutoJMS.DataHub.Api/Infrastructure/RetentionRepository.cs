using AutoJMS.DataHub.Api.Configuration;
using Npgsql;
using NpgsqlTypes;

namespace AutoJMS.DataHub.Api.Infrastructure;

public sealed record RetentionRunResult(
    int DeletedEvents,
    int DeletedChanges,
    int DeletedAuditLogs,
    int DeletedIdempotencyRecords,
    int DeletedProjections = 0,
    int EmittedTombstones = 0)
{
    public static RetentionRunResult Empty { get; } = new(0, 0, 0, 0);
}

/// <summary>
/// Applies only the allow-listed retention clocks. Policies are data, but table
/// and column names are deliberately not interpolated from arbitrary rows.
/// </summary>
public sealed class RetentionRepository(PostgresDataSource dataSource)
{
    public async Task<RetentionRunResult> RunOnceAsync(int batchSize, TimeSpan tombstoneRetention, CancellationToken cancellationToken)
    {
        batchSize = Math.Clamp(batchSize, 100, 5000);
        // Clamped here as well as at the configuration edge, because this is the value
        // that decides how long a delete notice survives. A caller passing zero would
        // prune every tombstone on the next pass and reintroduce exactly the silent
        // divergence they exist to prevent.
        var tombstoneFloor = TimeSpan.FromDays(Math.Clamp(
            tombstoneRetention.TotalDays,
            DataHubRuntimeOptions.MinimumTombstoneRetentionDays,
            DataHubRuntimeOptions.MaximumTombstoneRetentionDays));

        // Each category gets its own short transaction. Ingest locks
        // idempotency -> counter -> event; keeping retention categories in one
        // transaction would create the inverse counter/event or counter/idempotency
        // order and can deadlock under concurrent traffic.
        var deletedIdempotency = await RunPartAsync(DeleteExpiredIdempotencyAsync, 0, batchSize, cancellationToken);
        // Projections before changes, so a tombstone emitted in this pass is measured
        // against the floor by the same pass that could prune it — never pruned first
        // and written afterwards.
        var projections = await RunPartAsync(DeleteProjectionsAsync, (Tombstones: 0, Deleted: 0), batchSize, cancellationToken);
        var deletedChanges = await RunPartAsync(
            (connection, transaction, batch, ct) => DeleteChangesAsync(connection, transaction, batch, tombstoneFloor, ct),
            0,
            batchSize,
            cancellationToken);
        var deletedEvents = await RunPartAsync(DeleteEventsAsync, 0, batchSize, cancellationToken);
        var deletedAudit = await RunPartAsync(DeleteAuditLogsAsync, 0, batchSize, cancellationToken);
        return new RetentionRunResult(
            deletedEvents,
            deletedChanges,
            deletedAudit,
            deletedIdempotency,
            projections.Deleted,
            projections.Tombstones);
    }

    private async Task<T> RunPartAsync<T>(
        Func<NpgsqlConnection, NpgsqlTransaction, int, CancellationToken, Task<T>> operation,
        T notAcquired,
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
            return notAcquired;
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

    /// <summary>
    /// Publishes a <c>delete</c> tombstone for every projection whose retention clock has
    /// expired, and only then removes the projection — in that order, in one transaction,
    /// so a row can never leave the server without the change feed saying so. Before this
    /// existed there was no way to express "this waybill is gone": a station that had
    /// pulled it kept it in local SQLite forever.
    ///
    /// Opt-in on purpose. <c>003_seed_retention.sql</c> seeds no policy for
    /// <c>waybill_projections</c>, so this part finds nothing and changes nothing until an
    /// operator inserts one. A waybill that is merely old is not a waybill that was
    /// deleted, and an age-based default would tell every station to drop history it still
    /// works from. While it is off the pass stops at a single-row probe instead of scanning
    /// the projection table to prove there is nothing to do.
    ///
    /// Operator constraint worth knowing before inserting that policy: the projection is
    /// derived from <c>waybill_scan_events</c>, so a projection clock shorter than the
    /// event clock lets a later re-ingest of a surviving event rebuild the row the
    /// tombstone just announced as deleted.
    /// </summary>
    private static async Task<(int Tombstones, int Deleted)> DeleteProjectionsAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        int batchSize,
        CancellationToken cancellationToken)
    {
        // Probe before the scan. The candidate query filters on
        // COALESCE(site_policy.delete_after, global_policy.delete_after) IS NOT NULL, which
        // the planner can only evaluate row by row, so with no policy at all it still walked
        // the whole of waybill_projections — measured at 10,430 buffer hits over 50k rows —
        // every RetentionInterval, forever, to return nothing. An index on updated_at does not
        // help: ORDER BY site_id, waybill_no LIMIT keeps the planner on the primary key. This
        // probe reads one buffer and repeats the same predicate exactly, so a policy that is
        // switched on is never skipped by it.
        const string policyProbeSql = """
            SELECT 1
              FROM retention_policies
             WHERE table_name = 'waybill_projections'
               AND delete_after IS NOT NULL
             LIMIT 1;
            """;
        await using (var probeCommand = new NpgsqlCommand(policyProbeSql, connection, transaction))
        {
            var probe = await probeCommand.ExecuteScalarAsync(cancellationToken);
            if (probe is null or DBNull) return (0, 0);
        }

        const string candidatesSql = """
            SELECT p.site_id, p.waybill_no
              FROM waybill_projections p
              LEFT JOIN retention_policies site_policy
                ON site_policy.site_id = p.site_id
               AND site_policy.table_name = 'waybill_projections'
              LEFT JOIN retention_policies global_policy
                ON global_policy.site_id IS NULL
               AND global_policy.table_name = 'waybill_projections'
             WHERE COALESCE(site_policy.delete_after, global_policy.delete_after) IS NOT NULL
               AND p.updated_at < now() - COALESCE(site_policy.delete_after, global_policy.delete_after)
             ORDER BY p.site_id, p.waybill_no
             LIMIT @batch_size;
            """;
        var candidates = new List<(Guid SiteId, string WaybillNo)>();
        await using (var candidatesCommand = new NpgsqlCommand(candidatesSql, connection, transaction))
        {
            candidatesCommand.Parameters.AddWithValue("batch_size", batchSize);
            await using var candidatesReader = await candidatesCommand.ExecuteReaderAsync(cancellationToken);
            while (await candidatesReader.ReadAsync(cancellationToken))
                candidates.Add((candidatesReader.GetGuid(0), candidatesReader.GetString(1)));
        }

        if (candidates.Count == 0) return (0, 0);

        // Ingest locks the site counter before it touches either the projection or the
        // change feed, so taking that lock first here is what keeps the two paths from
        // acquiring projection and change rows in opposite orders. Reading change_seq
        // under the lock is also what makes the sequences allocated below safe to hand out.
        var siteIds = candidates.Select(candidate => candidate.SiteId).Distinct().OrderBy(siteId => siteId).ToArray();
        const string counterSql = """
            SELECT site_id, change_seq
              FROM site_change_counters
             WHERE site_id = ANY(@site_ids)
             ORDER BY site_id
             FOR UPDATE;
            """;
        var counters = new Dictionary<Guid, long>();
        await using (var counterCommand = new NpgsqlCommand(counterSql, connection, transaction))
        {
            counterCommand.Parameters.Add("site_ids", NpgsqlDbType.Array | NpgsqlDbType.Uuid).Value = siteIds;
            await using var counterReader = await counterCommand.ExecuteReaderAsync(cancellationToken);
            while (await counterReader.ReadAsync(cancellationToken))
                counters[counterReader.GetGuid(0)] = counterReader.GetInt64(1);
        }

        // A site with no counter row cannot be given a sequence, so its projections stay.
        // Deleting them would be the silent disappearance this whole part exists to
        // prevent: the row would go and no client would ever be told.
        var tombstoneSiteIds = new List<Guid>(candidates.Count);
        var tombstoneSeqs = new List<long>(candidates.Count);
        var tombstoneKeys = new List<string>(candidates.Count);
        foreach (var candidate in candidates)
        {
            if (!counters.TryGetValue(candidate.SiteId, out var sequence)) continue;
            sequence++;
            counters[candidate.SiteId] = sequence;
            tombstoneSiteIds.Add(candidate.SiteId);
            tombstoneSeqs.Add(sequence);
            tombstoneKeys.Add(candidate.WaybillNo);
        }

        if (tombstoneKeys.Count == 0) return (0, 0);

        // One statement, because a data-modifying CTE always runs to completion: there is
        // no ordering of failures that commits the delete without the tombstone. The body
        // carries only the key — a delete notice needs no payload, and an empty one would
        // be indistinguishable from a projection whose fields were all cleared.
        const string sql = """
            WITH inserted AS (
                INSERT INTO dashboard_changes (site_id, change_seq, entity_type, entity_key, operation, body)
                SELECT t.site_id, t.change_seq, 'waybill_projection', t.waybill_no, 'delete',
                       jsonb_build_object('waybill_no', t.waybill_no)
                  FROM unnest(@site_ids::uuid[], @change_seqs::bigint[], @waybill_nos::text[])
                         AS t(site_id, change_seq, waybill_no)
                RETURNING site_id, change_seq, entity_key
            ), bumped AS (
                UPDATE site_change_counters c
                   SET change_seq = allocated.max_seq
                  FROM (SELECT site_id, max(change_seq) AS max_seq FROM inserted GROUP BY site_id) allocated
                 WHERE c.site_id = allocated.site_id
            )
            -- Driven by `inserted`, so the projection goes only where a tombstone landed.
            DELETE FROM waybill_projections p
             USING inserted i
             WHERE p.site_id = i.site_id AND p.waybill_no = i.entity_key;
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.Add("site_ids", NpgsqlDbType.Array | NpgsqlDbType.Uuid).Value = tombstoneSiteIds.ToArray();
        command.Parameters.Add("change_seqs", NpgsqlDbType.Array | NpgsqlDbType.Bigint).Value = tombstoneSeqs.ToArray();
        command.Parameters.Add("waybill_nos", NpgsqlDbType.Array | NpgsqlDbType.Text).Value = tombstoneKeys.ToArray();
        var deleted = await command.ExecuteNonQueryAsync(cancellationToken);
        // The two counts can differ by design: a concurrent delete of the same projection
        // leaves the tombstone standing, which is still the correct announcement.
        return (tombstoneKeys.Count, deleted);
    }

    private static async Task<int> DeleteChangesAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        int batchSize,
        TimeSpan tombstoneRetention,
        CancellationToken cancellationToken)
    {
        // Tombstones are held to their own, much longer clock. The cost is that pruning
        // removes only a contiguous prefix, so a retained tombstone pins every later row
        // of that site's feed until it expires; that is accepted deliberately, because an
        // ordinary change a client missed is recoverable from a snapshot and a deletion
        // is not.
        const string retentionFilter = """
                           WHERE NOT (
                               CASE WHEN c.operation = 'delete'
                                    THEN c.change_at < now() - @tombstone_retention
                                    ELSE COALESCE(site_policy.delete_after, global_policy.delete_after) IS NOT NULL
                                         AND c.change_at < now() - COALESCE(site_policy.delete_after, global_policy.delete_after)
                               END
                           )
            """;
        const string sitesSql = $"""
            WITH retention_state AS (
                SELECT c.site_id,
                       max(c.change_seq) AS max_seq,
                       min(c.change_seq) FILTER (
            {retentionFilter}
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
        {
            sitesCommand.Parameters.AddWithValue("tombstone_retention", tombstoneRetention);
            await using var sitesReader = await sitesCommand.ExecuteReaderAsync(cancellationToken);
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

        const string sql = $"""
            WITH retention_state AS (
                SELECT c.site_id,
                       max(c.change_seq) AS max_seq,
                       min(c.change_seq) FILTER (
            {retentionFilter}
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
        command.Parameters.AddWithValue("tombstone_retention", tombstoneRetention);
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

        // One statement for every site, not one per site: this transaction already
        // holds row locks, and a per-site round trip extends the hold for as long as
        // the loop takes. GREATEST keeps the same never-move-backwards semantics.
        if (floors.Count > 0)
        {
            const string floorSql = """
                UPDATE site_change_counters c
                   SET pruned_through_seq = GREATEST(c.pruned_through_seq, f.seq)
                  FROM unnest(@floor_site_ids::uuid[], @floor_seqs::bigint[]) AS f(site_id, seq)
                 WHERE c.site_id = f.site_id;
                """;
            await using var floorCommand = new NpgsqlCommand(floorSql, connection, transaction);
            floorCommand.Parameters.Add("floor_site_ids", NpgsqlDbType.Array | NpgsqlDbType.Uuid).Value = floors.Keys.ToArray();
            floorCommand.Parameters.Add("floor_seqs", NpgsqlDbType.Array | NpgsqlDbType.Bigint).Value = floors.Values.ToArray();
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

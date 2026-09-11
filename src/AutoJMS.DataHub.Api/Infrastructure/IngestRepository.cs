using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AutoJMS.DataHub.Api.Auth;
using AutoJMS.DataHub.Api.Domain;
using Npgsql;
using NpgsqlTypes;

namespace AutoJMS.DataHub.Api.Infrastructure;

/// <summary>
/// The single PostgreSQL transaction boundary for bulk and interactive JMS
/// observations. The caller selects whether lease fencing is required; all
/// event, reducer, cursor and idempotency work is shared.
/// </summary>
public sealed class IngestRepository(
    PostgresDataSource dataSource,
    ProjectionReducer reducer,
    JmsEventPolicyRepository policyRepository,
    IngestHorizonPolicy horizonPolicy,
    TimeProvider timeProvider,
    TerminalPolicy terminalPolicy)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    public async Task<IngestOperationResult> IngestAsync(
        Guid siteId,
        Guid deviceId,
        long? leaderTerm,
        bool requireFence,
        string idempotencyKey,
        IngestRequest request,
        CancellationToken cancellationToken)
    {
        if (siteId == Guid.Empty || deviceId == Guid.Empty)
            return IngestOperationResult.Failure(StatusCodes.Status400BadRequest, ApiProblemCodes.BadRequest, "siteId and device identity are required.");
        if (request is null || request.Items is null || request.Items.Count == 0)
            return IngestOperationResult.Failure(StatusCodes.Status422UnprocessableEntity, "VALIDATION_FAILED", "At least one observation is required.");
        if (request.Items.Count > 200)
            return IngestOperationResult.Failure(StatusCodes.Status413PayloadTooLarge, "PAYLOAD_TOO_LARGE", "A request may contain at most 200 observations.");
        var normalizedKey = idempotencyKey.Trim();
        if (normalizedKey.Length is < 8 or > 128)
            return IngestOperationResult.Failure(StatusCodes.Status400BadRequest, ApiProblemCodes.BadRequest, "Idempotency-Key must contain between 8 and 128 characters.");

        var bodyHash = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(request, JsonOptions))).ToLowerInvariant();
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        // lock_timeout is the ingest-specific half: a competing ingest fails fast instead
        // of blocking for the whole statement budget. statement_timeout now arrives with
        // the connection (PostgresDataSource.BuildConnectionString) and is restated here
        // only so the transaction's budget is readable where the locks are taken.
        //
        // It used to read 60s, which could never fire — Npgsql's default CommandTimeout is
        // 30s, so the client abandoned the statement first and the server kept both the
        // query and its FOR UPDATE rows on site_change_counters. Matching the connection
        // default puts the cancellation back on the server, where it releases the locks.
        await using (var deadlines = new NpgsqlCommand(
            "SET LOCAL lock_timeout = '5s'; SET LOCAL statement_timeout = '30s';",
            connection,
            transaction))
        {
            await deadlines.ExecuteNonQueryAsync(cancellationToken);
        }

        // Fence before looking up an idempotency replay. A stale bulk leader must
        // not receive a successful response for an old key after it lost the lease.
        if (requireFence)
        {
            if (leaderTerm is null || leaderTerm < 1)
            {
                await transaction.RollbackAsync(cancellationToken);
                return IngestOperationResult.Failure(StatusCodes.Status409Conflict, ApiProblemCodes.LeaderFenced, "X-Leader-Term is required for bulk ingest.");
            }

            if (!await CheckFenceAsync(connection, transaction, siteId, deviceId, leaderTerm.Value, forUpdate: false, cancellationToken: cancellationToken))
            {
                await transaction.RollbackAsync(cancellationToken);
                return IngestOperationResult.Failure(StatusCodes.Status409Conflict, ApiProblemCodes.LeaderFenced, "The device, term, or lease expiry no longer matches the current leader.");
            }
        }

        await using (var cleanup = new NpgsqlCommand(
            "DELETE FROM idempotency_records WHERE site_id = @site_id AND key = @key AND expires_at <= now();",
            connection,
            transaction))
        {
            cleanup.Parameters.AddWithValue("site_id", siteId);
            cleanup.Parameters.AddWithValue("key", normalizedKey);
            await cleanup.ExecuteNonQueryAsync(cancellationToken);
        }

        var existing = await ReadIdempotencyAsync(connection, transaction, siteId, normalizedKey, cancellationToken);
        if (existing is not null)
        {
            if (!string.Equals(existing.Value.BodyHash, bodyHash, StringComparison.Ordinal))
            {
                await transaction.RollbackAsync(cancellationToken);
                return IngestOperationResult.Failure(StatusCodes.Status409Conflict, "IDEMPOTENCY_KEY_REUSED", "The idempotency key is bound to a different request body.");
            }

            if (requireFence && !await CheckFenceAsync(connection, transaction, siteId, deviceId, leaderTerm!.Value, forUpdate: true, cancellationToken: cancellationToken))
            {
                await transaction.RollbackAsync(cancellationToken);
                return IngestOperationResult.Failure(StatusCodes.Status409Conflict, ApiProblemCodes.LeaderFenced, "The device, term, or lease expiry no longer matches the current leader.");
            }
            await transaction.CommitAsync(cancellationToken);
            if (existing.Value.Response is null)
                return IngestOperationResult.Failure(StatusCodes.Status409Conflict, "IDEMPOTENCY_IN_PROGRESS", "The idempotency key is still being processed.");
            var replay = existing.Value.Response with { Replayed = true };
            return IngestOperationResult.Success(replay, []);
        }

        // Parsed once, here, rather than once in this loop and again in the item loop. The
        // event batch cannot be assembled until every item's UTC value is known, so the
        // second parse had nothing left to buy.
        var scanTimes = new ScanTimeParseResult[request.Items.Count];
        for (var i = 0; i < request.Items.Count; i++)
            scanTimes[i] = ScanTimeParser.Parse(request.Items[i].ScanTime);

        // Below the replay lookup on purpose. The horizon's past bound widens as the clock
        // advances, so checking above it would let a batch that already committed be answered
        // 422 on retry instead of replaying its recorded response. Idempotency wins; the price
        // is that a first-time violation now costs a connection and a transaction.
        // Items whose scanTime does not parse are left alone here and handled by the existing
        // path below, which keeps the parse-error code and message as they were.
        var now = timeProvider.GetUtcNow();
        foreach (var scanTime in scanTimes)
        {
            if (!scanTime.Success) continue;

            // Whole-batch failure by design: the ≤200 items commit or roll back together,
            // so accepting a subset would break the one guarantee bulk ingest makes.
            if (horizonPolicy.FindViolation(scanTime.UtcValue!.Value, now) is { } violation)
            {
                await transaction.RollbackAsync(cancellationToken);
                return IngestOperationResult.Failure(
                    StatusCodes.Status422UnprocessableEntity,
                    IngestHorizonPolicy.ProblemCode,
                    violation);
            }
        }

        // Claim the key before touching observations. A competing request with
        // the same key blocks on this row and replays the committed response,
        // rather than running the reducer twice.
        if (!await ReserveIdempotencyAsync(connection, transaction, siteId, normalizedKey, bodyHash, cancellationToken))
        {
            var competing = await ReadIdempotencyAsync(connection, transaction, siteId, normalizedKey, cancellationToken);
            if (competing is null)
            {
                // The reserve lost the row, yet the winner is invisible here: ON CONFLICT DO
                // NOTHING skips a speculative insertion rather than waiting on it, so a peer
                // that has not committed leaves nothing for this snapshot to read. That is the
                // concurrent duplicate idempotency exists to absorb, so the answer is "retry" —
                // not "you bound this key to a different body", which sends the caller hunting
                // a client bug that is not there.
                await transaction.RollbackAsync(cancellationToken);
                return IngestOperationResult.Failure(StatusCodes.Status409Conflict, "IDEMPOTENCY_IN_PROGRESS", "The idempotency key is still being processed.");
            }

            if (!string.Equals(competing.Value.BodyHash, bodyHash, StringComparison.Ordinal))
            {
                await transaction.RollbackAsync(cancellationToken);
                return IngestOperationResult.Failure(StatusCodes.Status409Conflict, "IDEMPOTENCY_KEY_REUSED", "The idempotency key is bound to a different request body.");
            }

            if (requireFence && !await CheckFenceAsync(connection, transaction, siteId, deviceId, leaderTerm!.Value, forUpdate: true, cancellationToken: cancellationToken))
            {
                await transaction.RollbackAsync(cancellationToken);
                return IngestOperationResult.Failure(StatusCodes.Status409Conflict, ApiProblemCodes.LeaderFenced, "The device, term, or lease expiry no longer matches the current leader.");
            }
            await transaction.CommitAsync(cancellationToken);
            return competing.Value.Response is null
                ? IngestOperationResult.Failure(StatusCodes.Status409Conflict, "IDEMPOTENCY_IN_PROGRESS", "The idempotency key is still being processed.")
                : IngestOperationResult.Success(competing.Value.Response with { Replayed = true }, []);
        }

        var policies = await policyRepository.LoadAsync(connection, transaction, cancellationToken);
        // Serialize all projection reads/writes for a site before reducing. A
        // projection row may not exist yet; locking the counter prevents two
        // concurrent first observations from both reducing from null and then
        // overwriting one another at the upsert boundary.
        var startingSequence = await ReadCounterAsync(connection, transaction, siteId, cancellationToken);
        if (startingSequence is null)
        {
            await transaction.RollbackAsync(cancellationToken);
            return IngestOperationResult.Failure(StatusCodes.Status404NotFound, ApiProblemCodes.NotFound, "The site change counter has not been provisioned.");
        }

        // Reported here rather than beside the horizon check, so precedence is exactly what
        // the statement-per-item loop produced: a horizon violation still outranks a parse
        // error, an unprovisioned counter still outranks both, and the item named is still
        // the first bad one in request order.
        foreach (var scanTime in scanTimes)
        {
            if (scanTime.Success) continue;
            await transaction.RollbackAsync(cancellationToken);
            return IngestOperationResult.Failure(StatusCodes.Status400BadRequest, ScanTimeParser.InvalidScanTimeCode, scanTime.ErrorMessage ?? "scanTime is invalid.");
        }

        var observations = new JmsObservation[request.Items.Count];
        var fingerprints = new string[request.Items.Count];
        for (var i = 0; i < request.Items.Count; i++)
        {
            observations[i] = request.Items[i] with
            {
                SiteId = siteId,
                WaybillNo = request.Items[i].WaybillNo.Trim()
            };
            fingerprints[i] = EventFingerprintV1.Compute(observations[i], scanTimes[i].UtcValue!.Value);
        }

        // Phase 1 — every event insert in one round trip.
        //
        // A 200-item bulk used to spend 200 sequential network exchanges here, each a full
        // latency hop taken while this transaction already holds site_change_counters under
        // FOR UPDATE, so the wire time was also lock-hold time for every other writer at the
        // site. The statement text, the parameter set and the ON CONFLICT DO NOTHING
        // RETURNING id dedupe are unchanged per item, and the server still executes them in
        // request order inside this transaction — a batch removes the round trips, not the
        // statements, so §26's dedupe contract and the lock behaviour are untouched.
        var eventIds = new long?[observations.Length];
        await using (var eventBatch = new NpgsqlBatch(connection, transaction))
        {
            for (var i = 0; i < observations.Length; i++)
                eventBatch.BatchCommands.Add(BuildInsertEventCommand(observations[i], scanTimes[i].UtcValue!.Value, fingerprints[i]));

            await using var eventReader = await eventBatch.ExecuteReaderAsync(cancellationToken);
            for (var i = 0; i < observations.Length; i++)
            {
                // No row means the fingerprint was already present: RETURNING yields nothing
                // when DO NOTHING skips the insert. Same signal ExecuteScalar gave as null.
                eventIds[i] = await eventReader.ReadAsync(cancellationToken) ? eventReader.GetInt64(0) : (long?)null;
                if (i + 1 < observations.Length && !await eventReader.NextResultAsync(cancellationToken))
                    throw new InvalidOperationException($"The event batch returned {i + 1} results for {observations.Length} observations.");
            }
        }

        var accepted = 0;
        var duplicates = 0;
        var terminalLocked = 0;
        // One probe per distinct waybill, not per item: a 200-item batch usually touches a
        // handful of waybills. First-appearance order, which is the order the loop probed in.
        var acceptedWaybills = new List<string>();
        var seenAccepted = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < eventIds.Length; i++)
        {
            if (eventIds[i] is null)
            {
                duplicates++;
                continue;
            }

            accepted++;
            if (seenAccepted.Add(observations[i].WaybillNo))
                acceptedWaybills.Add(observations[i].WaybillNo);
        }

        // Phase 2 — the same probes, one round trip. true means blocked: terminal, or
        // tombstoned by an earlier purge. Kept in step with the terminal marks this batch
        // makes, below. Probe order is irrelevant — none of them takes a lock.
        var terminalGuard = new Dictionary<string, bool>(StringComparer.Ordinal);
        if (acceptedWaybills.Count > 0)
        {
            await using var guardBatch = new NpgsqlBatch(connection, transaction);
            foreach (var waybillNo in acceptedWaybills)
                guardBatch.BatchCommands.Add(BuildTerminalGuardCommand(siteId, waybillNo));

            await using var guardReader = await guardBatch.ExecuteReaderAsync(cancellationToken);
            for (var i = 0; i < acceptedWaybills.Count; i++)
            {
                terminalGuard[acceptedWaybills[i]] =
                    await guardReader.ReadAsync(cancellationToken) && !guardReader.IsDBNull(0) && guardReader.GetBoolean(0);
                if (i + 1 < acceptedWaybills.Count && !await guardReader.NextResultAsync(cancellationToken))
                    throw new InvalidOperationException($"The terminal-guard batch returned {i + 1} results for {acceptedWaybills.Count} waybills.");
            }
        }

        // Phase 3 — the projection reads, still FOR UPDATE, still one statement per distinct
        // unblocked waybill, still in first-appearance order, now in one round trip.
        //
        // §5bis.1 of the audit stopped at this step: collapsing the reads into a single
        // `waybill_no = ANY(...)` would lock waybills the statement-per-item loop never
        // reached, which is a concurrency change rather than a performance one. A batch does
        // not have that problem — the same statements run against the same rows in the same
        // order, so the transaction's lock set and lock acquisition order are identical. The
        // set is chosen after the guard for the same reason the loop chose it there: a
        // blocked waybill is never read.
        //
        // The guard can flip to true mid-batch when this batch establishes a terminal mark,
        // but only while processing an item of that same waybill — which is after its
        // projection was already read. So the set below is exactly what the loop read.
        var readableWaybills = acceptedWaybills.Where(waybillNo => !terminalGuard[waybillNo]).ToList();
        var storedProjections = new Dictionary<string, WaybillProjection>(StringComparer.Ordinal);
        if (readableWaybills.Count > 0)
        {
            await using var projectionBatch = new NpgsqlBatch(connection, transaction);
            foreach (var waybillNo in readableWaybills)
                projectionBatch.BatchCommands.Add(BuildReadProjectionCommand(siteId, waybillNo));

            await using var projectionReader = await projectionBatch.ExecuteReaderAsync(cancellationToken);
            for (var i = 0; i < readableWaybills.Count; i++)
            {
                if (await projectionReader.ReadAsync(cancellationToken))
                    storedProjections[readableWaybills[i]] = MapProjection(projectionReader, siteId, readableWaybills[i]);
                if (i + 1 < readableWaybills.Count && !await projectionReader.NextResultAsync(cancellationToken))
                    throw new InvalidOperationException($"The projection batch returned {i + 1} results for {readableWaybills.Count} waybills.");
            }
        }

        var changedByWaybill = new Dictionary<string, (WaybillProjection Projection, ProjectionBody Body)>(StringComparer.Ordinal);
        var seenWaybills = new Dictionary<string, WaybillProjection>(StringComparer.Ordinal);
        var terminalByWaybill = new Dictionary<string, (DateTimeOffset At, int StateCode)>(StringComparer.Ordinal);

        // Reduction is now pure: every row it needs is already in hand, so nothing in this
        // loop touches the network.
        for (var i = 0; i < observations.Length; i++)
        {
            if (eventIds[i] is null) continue;
            var observation = observations[i];

            // Consulted after the event insert, not before: dedupe and history stay complete
            // whatever this decides, so a blocked scan is recorded once and never
            // re-accepted. The count is separate from `accepted` because the event WAS
            // accepted — it just did not move the projection.
            if (terminalGuard[observation.WaybillNo])
            {
                // No projection mutation, no version bump, no change_seq, no
                // dashboard_changes row — and deliberately no rollback: the rest of the
                // batch is unaffected and commits.
                terminalLocked++;
                continue;
            }

            WaybillProjection? current;
            if (seenWaybills.TryGetValue(observation.WaybillNo, out var cached))
                current = cached;
            else if (storedProjections.TryGetValue(observation.WaybillNo, out var stored))
                current = stored;
            else
                current = null;
            var eventValue = new JmsEvent
            {
                SiteId = siteId,
                WaybillNo = observation.WaybillNo,
                EventOccurredAt = scanTimes[i].UtcValue!.Value,
                EventFingerprint = fingerprints[i],
                Code = observation.Code,
                Name = observation.ScanTypeName,
                Status = observation.Status,
                Payload = observation.Payload,
                EventId = eventIds[i]
            };
            var next = reducer.Reduce(current, eventValue, policies);
            seenWaybills[observation.WaybillNo] = next;

            // Inert in P1: TerminalPolicy is empty, so this never fires. OD-6 = B, so the
            // stamp is the server's observed time rather than the event's — a device that
            // uploads a month-old terminal scan must not make the purge clock retroactive.
            if (terminalPolicy.IsTerminal(next.CurrentState?.Code))
            {
                terminalByWaybill[observation.WaybillNo] = (timeProvider.GetUtcNow(), next.CurrentState!.Code!.Value);
                // A later item in this same batch for this waybill is now blocked too.
                terminalGuard[observation.WaybillNo] = true;
            }

            // Force into the write set when a terminal mark was just established — even if
            // the reducer reported no version change (out-of-order scan that lost IsWinner).
            // Without this, is_terminal stays false in the database and the next batch reads
            // false, accepts the same scans again, and the client sees inconsistent results.
            if (next.Version != (current?.Version ?? 0) || terminalByWaybill.ContainsKey(observation.WaybillNo))
                changedByWaybill[observation.WaybillNo] = (next, ProjectionBody.From(next, DateTimeOffset.UtcNow));
        }

        var changed = changedByWaybill.Values.ToList();
        var doorbells = new List<ChangeDoorbell>(changed.Count);
        long? firstSeq = null;
        long? lastSeq = null;
        if (changed.Count > 0)
        {
            // checked(sequence + 1) below throws OverflowException, which no caller
            // catches, so the site's ingest would answer 500 with no code an operator
            // could act on. Refuse up front with a named failure instead. The
            // idempotency key stays reserved-and-rolled-back either way.
            if (long.MaxValue - changed.Count < startingSequence.Value)
            {
                await transaction.RollbackAsync(cancellationToken);
                return IngestOperationResult.Failure(
                    StatusCodes.Status500InternalServerError,
                    "COUNTER_OVERFLOW",
                    "The site change sequence counter has reached its maximum value and must be reset.");
            }

            // Phase 4 — the writes. Sequence allocation is unchanged: still one change_seq per
            // changed waybill, still allocated under the FOR UPDATE taken on
            // site_change_counters, still in the same order, so the numbers a given batch
            // produces are identical. Only the delivery changed — upsert, change row and the
            // final counter update leave in one round trip instead of 2N+1.
            var sequence = startingSequence.Value;
            await using var writeBatch = new NpgsqlBatch(connection, transaction);
            foreach (var entry in changed)
            {
                sequence = checked(sequence + 1);
                var body = entry.Body with { Version = entry.Projection.Version };
                var terminalMark = terminalByWaybill.TryGetValue(entry.Projection.WaybillNo, out var mark)
                    ? mark
                    : ((DateTimeOffset At, int StateCode)?)null;
                writeBatch.BatchCommands.Add(BuildUpsertProjectionCommand(entry.Projection, body.UpdatedAt, sequence, terminalMark));
                writeBatch.BatchCommands.Add(BuildInsertChangeCommand(siteId, sequence, entry.Projection.WaybillNo, body));
                doorbells.Add(new ChangeDoorbell(siteId, sequence, "waybill_projection", entry.Projection.WaybillNo));
                firstSeq ??= sequence;
                lastSeq = sequence;
            }

            // Last in the batch, as it was last in the loop: the counter may only advance once
            // every row that consumed a sequence has been written.
            writeBatch.BatchCommands.Add(BuildUpdateCounterCommand(siteId, sequence));
            await writeBatch.ExecuteNonQueryAsync(cancellationToken);
        }

        var response = new IngestResponse(siteId, accepted, duplicates, changed.Count, false, firstSeq, lastSeq, terminalLocked);
        await InsertIdempotencyAsync(connection, transaction, siteId, normalizedKey, bodyHash, response, cancellationToken);
        await AuditRepository.AppendAsync(
            connection,
            transaction,
            siteId,
            $"device:{deviceId:D}",
            requireFence ? "jms.bulk_ingest" : "jms.interactive_ingest",
            new { deviceId, accepted, duplicates, changedProjections = changed.Count, terminalLocked, firstSeq, lastSeq },
            cancellationToken);
        if (requireFence && !await CheckFenceAsync(connection, transaction, siteId, deviceId, leaderTerm!.Value, forUpdate: true, cancellationToken: cancellationToken))
        {
            await transaction.RollbackAsync(cancellationToken);
            return IngestOperationResult.Failure(StatusCodes.Status409Conflict, ApiProblemCodes.LeaderFenced, "The device, term, or lease expiry no longer matches the current leader.");
        }
        await transaction.CommitAsync(cancellationToken);
        return IngestOperationResult.Success(response, doorbells);
    }

    private static async Task<(string BodyHash, IngestResponse? Response)?> ReadIdempotencyAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid siteId,
        string key,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT body_sha256, response, status_code
              FROM idempotency_records
             WHERE site_id = @site_id AND key = @key AND expires_at > now()
             FOR UPDATE;
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("site_id", siteId);
        command.Parameters.AddWithValue("key", key.Trim());
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return null;
        var hash = reader.GetString(0);
        var json = reader.GetFieldValue<string>(1);
        var statusCode = reader.GetInt32(2);
        var response = statusCode == 0 ? null : JsonSerializer.Deserialize<IngestResponse>(json, JsonOptions)
            ?? throw new InvalidOperationException("Stored idempotency response is invalid.");
        return (hash, response);
    }

    private static async Task<bool> ReserveIdempotencyAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid siteId,
        string key,
        string bodyHash,
        CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO idempotency_records (site_id, key, body_sha256, response, status_code, expires_at)
            VALUES (@site_id, @key, @body_hash, '{}'::jsonb, 0, now() + interval '24 hours')
            ON CONFLICT (site_id, key) DO NOTHING
            RETURNING 1;
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("site_id", siteId);
        command.Parameters.AddWithValue("key", key.Trim());
        command.Parameters.AddWithValue("body_hash", bodyHash);
        var value = await command.ExecuteScalarAsync(cancellationToken);
        return value is not null and not DBNull;
    }

    private static async Task<bool> CheckFenceAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid siteId,
        Guid deviceId,
        long term,
        bool forUpdate,
        CancellationToken cancellationToken)
    {
        var sql = $"""
            SELECT leader_device_id = @device_id
               AND leader_term = @term
               -- PostgreSQL now() is transaction-start time. Fencing must use
               -- wall-clock time so a long-running batch cannot outlive its lease.
               AND lease_expires_at > clock_timestamp()
              FROM site_fetch_leases
             WHERE site_id = @site_id
             {(forUpdate ? "FOR UPDATE" : "")};
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("site_id", siteId);
        command.Parameters.AddWithValue("device_id", deviceId);
        command.Parameters.AddWithValue("term", term);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) && !reader.IsDBNull(0) && reader.GetBoolean(0);
    }

    private static async Task<long?> ReadCounterAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid siteId,
        CancellationToken cancellationToken)
    {
        const string sql = "SELECT change_seq FROM site_change_counters WHERE site_id = @site_id FOR UPDATE;";
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("site_id", siteId);
        var value = await command.ExecuteScalarAsync(cancellationToken);
        return value is null or DBNull ? null : Convert.ToInt64(value, System.Globalization.CultureInfo.InvariantCulture);
    }

    private const string UpdateCounterSql = "UPDATE site_change_counters SET change_seq = @sequence WHERE site_id = @site_id;";

    private static NpgsqlBatchCommand BuildUpdateCounterCommand(Guid siteId, long sequence)
    {
        var command = new NpgsqlBatchCommand(UpdateCounterSql);
        command.Parameters.AddWithValue("site_id", siteId);
        command.Parameters.AddWithValue("sequence", sequence);
        return command;
    }

    private const string InsertEventSql = """
        INSERT INTO waybill_scan_events (
            site_id, waybill_no, event_fingerprint, event_occurred_at,
            scan_type_code, scan_type_name, status, network_code,
            operator_code, package_number, task_code, payload)
        VALUES (@site_id, @waybill_no, @fingerprint, @occurred_at,
                @code, @scan_type_name, @status, @network_code,
                @operator_code, @package_number, @task_code, @payload)
        ON CONFLICT (site_id, event_fingerprint) DO NOTHING
        RETURNING id;
        """;

    private static NpgsqlBatchCommand BuildInsertEventCommand(
        JmsObservation observation,
        DateTimeOffset occurredAt,
        string fingerprint)
    {
        var command = new NpgsqlBatchCommand(InsertEventSql);
        command.Parameters.AddWithValue("site_id", observation.SiteId);
        command.Parameters.AddWithValue("waybill_no", observation.WaybillNo.Trim());
        command.Parameters.AddWithValue("fingerprint", fingerprint);
        command.Parameters.AddWithValue("occurred_at", occurredAt);
        AddNullable(command.Parameters, "code", observation.Code);
        AddNullable(command.Parameters, "scan_type_name", observation.ScanTypeName);
        AddNullable(command.Parameters, "status", observation.Status);
        AddNullable(command.Parameters, "network_code", observation.ScanNetworkCode);
        AddNullable(command.Parameters, "operator_code", observation.ScanByCode);
        AddNullable(command.Parameters, "package_number", observation.PackageNumber);
        AddNullable(command.Parameters, "task_code", observation.TaskCode);
        var payload = observation.Payload is { } element ? element.GetRawText() : "{}";
        command.Parameters.Add(new NpgsqlParameter("payload", NpgsqlDbType.Jsonb) { Value = payload });
        return command;
    }

    /// <summary>
    /// True when this waybill may no longer move: it is terminal, or a tombstone survives
    /// from a purge that already removed it. Two EXISTS rather than a join because both are
    /// primary-key probes and either alone is decisive.
    ///
    /// No FOR UPDATE. The projection row is locked a moment later by
    /// <see cref="ReadProjectionSql"/>, and a tombstone is written only by the retention
    /// purge, which never runs inside this transaction.
    /// </summary>
    private const string TerminalGuardSql = """
        SELECT EXISTS (SELECT 1
                         FROM waybill_projections
                        WHERE site_id = @site_id AND waybill_no = @waybill_no AND is_terminal)
            OR EXISTS (SELECT 1
                         FROM waybill_tombstones
                        WHERE site_id = @site_id AND waybill_no = @waybill_no);
        """;

    private static NpgsqlBatchCommand BuildTerminalGuardCommand(Guid siteId, string waybillNo)
    {
        var command = new NpgsqlBatchCommand(TerminalGuardSql);
        command.Parameters.AddWithValue("site_id", siteId);
        command.Parameters.AddWithValue("waybill_no", waybillNo.Trim());
        return command;
    }

    private const string ReadProjectionSql = """
        SELECT state_code, state_name, state_status, state_event_at, state_fingerprint, state_event_id, state_kind, state_payload,
               last_activity_code, last_activity_name, last_activity_status, last_activity_kind, last_activity_at,
               last_activity_fingerprint, last_activity_event_id, last_activity_payload,
               inventory_code, inventory_name, inventory_status, inventory_event_at, inventory_fingerprint, inventory_event_id, inventory_payload,
               payload, reducer_version, version
          FROM waybill_projections
         WHERE site_id = @site_id AND waybill_no = @waybill_no
         FOR UPDATE;
        """;

    private static NpgsqlBatchCommand BuildReadProjectionCommand(Guid siteId, string waybillNo)
    {
        var command = new NpgsqlBatchCommand(ReadProjectionSql);
        command.Parameters.AddWithValue("site_id", siteId);
        command.Parameters.AddWithValue("waybill_no", waybillNo.Trim());
        return command;
    }

    /// <summary>
    /// Internal rather than private because <see cref="ReopenRepository"/> needs the same
    /// twenty-six-ordinal mapping and two copies of it would drift. Visibility only: the
    /// statement and the mapping are untouched — both were lifted out verbatim so the batched
    /// ingest path shares them — so §26's rule against rewriting the ingest read still holds.
    /// </summary>
    internal static async Task<WaybillProjection?> ReadProjectionAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid siteId,
        string waybillNo,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(ReadProjectionSql, connection, transaction);
        command.Parameters.AddWithValue("site_id", siteId);
        command.Parameters.AddWithValue("waybill_no", waybillNo.Trim());
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return null;
        return MapProjection(reader, siteId, waybillNo);
    }

    private static WaybillProjection MapProjection(NpgsqlDataReader reader, Guid siteId, string waybillNo)
    {
        var reducerVersion = reader.GetInt32(24);
        var version = reader.GetInt64(25);
        return new WaybillProjection(
            siteId,
            waybillNo,
            ReadSlot(reader, 0, 1, 2, 6, 3, 4, 5, 7),
            ReadSlot(reader, 8, 9, 10, 11, 12, 13, 14, 15),
            ReadSlot(reader, 16, 17, 18, -1, 19, 20, 21, 22, JmsEventKind.Inventory),
            reducerVersion,
            version);
    }

    private static ProjectionSlot? ReadSlot(
        NpgsqlDataReader reader,
        int codeOrdinal,
        int nameOrdinal,
        int statusOrdinal,
        int kindOrdinal,
        int occurredOrdinal,
        int fingerprintOrdinal,
        int eventIdOrdinal,
        int payloadOrdinal,
        JmsEventKind? forcedKind = null)
    {
        if (reader.IsDBNull(occurredOrdinal) || reader.IsDBNull(fingerprintOrdinal)) return null;
        var kind = forcedKind ?? ParseKind(reader.IsDBNull(kindOrdinal) ? null : reader.GetString(kindOrdinal));
        return new ProjectionSlot(
            kind,
            reader.IsDBNull(codeOrdinal) ? null : reader.GetInt32(codeOrdinal),
            reader.IsDBNull(nameOrdinal) ? null : reader.GetString(nameOrdinal),
            statusOrdinal < 0 || reader.IsDBNull(statusOrdinal) ? null : reader.GetString(statusOrdinal),
            reader.GetFieldValue<DateTimeOffset>(occurredOrdinal),
            reader.GetString(fingerprintOrdinal),
            ParseJson(reader.GetFieldValue<string>(payloadOrdinal)),
            reader.IsDBNull(eventIdOrdinal) ? null : reader.GetInt64(eventIdOrdinal));
    }

    private static JmsEventKind ParseKind(string? value)
        => value switch
        {
            "state_transition" => JmsEventKind.StateTransition,
            "inventory" => JmsEventKind.Inventory,
            "communication" => JmsEventKind.Communication,
            _ => JmsEventKind.Activity
        };

    /// <summary>
    /// SQL used by <see cref="UpsertProjectionAsync"/>. Exposed as <c>internal</c> so that
    /// tests can assert the one-way terminal semantics (OR / COALESCE) without a database.
    /// </summary>
    internal const string UpsertProjectionSql = """
        INSERT INTO waybill_projections (
            site_id, waybill_no,
            state_code, state_name, state_status, state_event_at, state_fingerprint, state_event_id, state_kind, state_payload,
            last_activity_code, last_activity_name, last_activity_status, last_activity_kind, last_activity_at,
            last_activity_fingerprint, last_activity_event_id, last_activity_payload,
            inventory_code, inventory_name, inventory_status, inventory_event_at, inventory_fingerprint, inventory_event_id, inventory_payload,
            payload, reducer_version, version, updated_at,
            last_change_seq, is_terminal, terminal_at, terminal_state_code)
        VALUES (@site_id, @waybill_no,
                @state_code, @state_name, @state_status, @state_event_at, @state_fingerprint, @state_event_id, @state_kind, @state_payload,
                @activity_code, @activity_name, @activity_status, @activity_kind, @activity_event_at,
                @activity_fingerprint, @activity_event_id, @activity_payload,
                @inventory_code, @inventory_name, @inventory_status, @inventory_event_at, @inventory_fingerprint, @inventory_event_id, @inventory_payload,
                @payload, @reducer_version, @version, @updated_at,
                @last_change_seq, @is_terminal, @terminal_at, @terminal_state_code)
        ON CONFLICT (site_id, waybill_no) DO UPDATE SET
            state_code = EXCLUDED.state_code,
            state_name = EXCLUDED.state_name,
            state_status = EXCLUDED.state_status,
            state_event_at = EXCLUDED.state_event_at,
            state_fingerprint = EXCLUDED.state_fingerprint,
            state_event_id = EXCLUDED.state_event_id,
            state_kind = EXCLUDED.state_kind,
            state_payload = EXCLUDED.state_payload,
            last_activity_code = EXCLUDED.last_activity_code,
            last_activity_name = EXCLUDED.last_activity_name,
            last_activity_status = EXCLUDED.last_activity_status,
            last_activity_kind = EXCLUDED.last_activity_kind,
            last_activity_at = EXCLUDED.last_activity_at,
            last_activity_fingerprint = EXCLUDED.last_activity_fingerprint,
            last_activity_event_id = EXCLUDED.last_activity_event_id,
            last_activity_payload = EXCLUDED.last_activity_payload,
            inventory_code = EXCLUDED.inventory_code,
            inventory_name = EXCLUDED.inventory_name,
            inventory_status = EXCLUDED.inventory_status,
            inventory_event_at = EXCLUDED.inventory_event_at,
            inventory_fingerprint = EXCLUDED.inventory_fingerprint,
            inventory_event_id = EXCLUDED.inventory_event_id,
            inventory_payload = EXCLUDED.inventory_payload,
            payload = EXCLUDED.payload,
            reducer_version = EXCLUDED.reducer_version,
            version = EXCLUDED.version,
            updated_at = EXCLUDED.updated_at,
            last_change_seq = EXCLUDED.last_change_seq,
            -- Terminal is one-way here. An ordinary later upsert must not clear a mark
            -- an earlier one made; only the reopen endpoint does that, explicitly.
            is_terminal = waybill_projections.is_terminal OR EXCLUDED.is_terminal,
            terminal_at = COALESCE(waybill_projections.terminal_at, EXCLUDED.terminal_at),
            terminal_state_code = COALESCE(waybill_projections.terminal_state_code, EXCLUDED.terminal_state_code);
        """;

    /// <summary>
    /// <paramref name="changeSeq"/> is the sequence this transaction allocated for the row,
    /// written only for rows it actually writes — existing rows are never backfilled.
    /// <paramref name="terminal"/> is null except when the reducer produced a terminal code,
    /// which cannot happen while <see cref="TerminalPolicy"/> is empty.
    /// </summary>
    private static NpgsqlBatchCommand BuildUpsertProjectionCommand(
        WaybillProjection projection,
        DateTimeOffset updatedAt,
        long changeSeq,
        (DateTimeOffset At, int StateCode)? terminal)
    {
        var command = new NpgsqlBatchCommand(UpsertProjectionSql);
        command.Parameters.AddWithValue("site_id", projection.SiteId);
        command.Parameters.AddWithValue("waybill_no", projection.WaybillNo);
        AddSlot(command.Parameters, "state", projection.CurrentState);
        AddSlot(command.Parameters, "activity", projection.LatestActivity);
        AddSlot(command.Parameters, "inventory", projection.Inventory);
        var body = ProjectionBody.From(projection, updatedAt);
        var payload = body.Payload?.GetRawText() ?? "{}";
        command.Parameters.Add(new NpgsqlParameter("payload", NpgsqlDbType.Jsonb) { Value = payload });
        command.Parameters.AddWithValue("reducer_version", projection.ReducerVersion);
        command.Parameters.AddWithValue("version", Math.Max(projection.Version, 1));
        command.Parameters.AddWithValue("updated_at", updatedAt);
        command.Parameters.AddWithValue("last_change_seq", changeSeq);
        command.Parameters.AddWithValue("is_terminal", terminal is not null);
        AddNullable(command.Parameters, "terminal_at", terminal?.At);
        AddNullable(command.Parameters, "terminal_state_code", terminal?.StateCode);
        return command;
    }

    private static void AddSlot(NpgsqlParameterCollection parameters, string prefix, ProjectionSlot? slot)
    {
        AddNullable(parameters, $"{prefix}_code", slot?.Code);
        AddNullable(parameters, $"{prefix}_name", slot?.Name);
        AddNullable(parameters, $"{prefix}_status", slot?.Status);
        AddNullable(parameters, $"{prefix}_event_at", slot?.EventOccurredAt);
        AddNullable(parameters, $"{prefix}_fingerprint", slot?.EventFingerprint);
        AddNullable(parameters, $"{prefix}_event_id", slot?.EventId);
        AddJsonbNullable(parameters, $"{prefix}_payload", slot?.Payload);
        if (prefix != "activity" && prefix != "inventory")
            AddNullable(parameters, $"{prefix}_kind", slot?.Kind.ToWireValue());
        else if (prefix == "activity")
            AddNullable(parameters, "activity_kind", slot?.Kind.ToWireValue());
    }

    private const string InsertChangeSql = """
        INSERT INTO dashboard_changes (site_id, change_seq, entity_type, entity_key, operation, body)
        VALUES (@site_id, @sequence, 'waybill_projection', @waybill_no, 'upsert', @body);
        """;

    private static NpgsqlBatchCommand BuildInsertChangeCommand(
        Guid siteId,
        long sequence,
        string waybillNo,
        ProjectionBody body)
    {
        var command = new NpgsqlBatchCommand(InsertChangeSql);
        command.Parameters.AddWithValue("site_id", siteId);
        command.Parameters.AddWithValue("sequence", sequence);
        command.Parameters.AddWithValue("waybill_no", waybillNo);
        command.Parameters.Add(new NpgsqlParameter("body", NpgsqlDbType.Jsonb) { Value = JsonSerializer.Serialize(body, JsonOptions) });
        return command;
    }

    private static async Task InsertIdempotencyAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid siteId,
        string key,
        string bodyHash,
        IngestResponse response,
        CancellationToken cancellationToken)
    {
        const string sql = """
            UPDATE idempotency_records
               SET response = @response,
                   status_code = 200,
                   expires_at = now() + interval '24 hours'
             WHERE site_id = @site_id AND key = @key AND body_sha256 = @body_hash;
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("site_id", siteId);
        command.Parameters.AddWithValue("key", key.Trim());
        command.Parameters.AddWithValue("body_hash", bodyHash);
        command.Parameters.Add("response", NpgsqlDbType.Jsonb).Value = JsonSerializer.Serialize(response, JsonOptions);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static JsonElement? ParseJson(string value)
    {
        using var document = JsonDocument.Parse(string.IsNullOrWhiteSpace(value) ? "{}" : value);
        return document.RootElement.Clone();
    }

    // NpgsqlParameterCollection rather than NpgsqlCommand so the same helpers serve both a
    // standalone command and a batched one — NpgsqlBatchCommand is not an NpgsqlCommand.
    private static void AddNullable(NpgsqlParameterCollection parameters, string name, object? value)
        => parameters.AddWithValue(name, value ?? DBNull.Value);

    private static void AddJsonbNullable(NpgsqlParameterCollection parameters, string name, JsonElement? value)
    {
        parameters.Add(new NpgsqlParameter(name, NpgsqlDbType.Jsonb)
        {
            Value = value is { } element ? element.GetRawText() : "{}"
        });
    }
}

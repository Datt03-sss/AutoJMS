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
    JmsEventPolicyRepository policyRepository)
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

        // Claim the key before touching observations. A competing request with
        // the same key blocks on this row and replays the committed response,
        // rather than running the reducer twice.
        if (!await ReserveIdempotencyAsync(connection, transaction, siteId, normalizedKey, bodyHash, cancellationToken))
        {
            var competing = await ReadIdempotencyAsync(connection, transaction, siteId, normalizedKey, cancellationToken);
            if (competing is null || !string.Equals(competing.Value.BodyHash, bodyHash, StringComparison.Ordinal))
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

        var accepted = 0;
        var duplicates = 0;
        var changedByWaybill = new Dictionary<string, (WaybillProjection Projection, ProjectionBody Body)>(StringComparer.Ordinal);
        var seenWaybills = new Dictionary<string, WaybillProjection>(StringComparer.Ordinal);

        foreach (var input in request.Items)
        {
            var parsed = ScanTimeParser.Parse(input.ScanTime);
            if (!parsed.Success)
            {
                await transaction.RollbackAsync(cancellationToken);
                return IngestOperationResult.Failure(StatusCodes.Status400BadRequest, ScanTimeParser.InvalidScanTimeCode, parsed.ErrorMessage ?? "scanTime is invalid.");
            }

            var observation = input with
            {
                SiteId = siteId,
                WaybillNo = input.WaybillNo.Trim()
            };
            var fingerprint = EventFingerprintV1.Compute(observation, parsed.UtcValue!.Value);
            var eventId = await InsertEventAsync(connection, transaction, observation, parsed.UtcValue.Value, fingerprint, cancellationToken);
            if (eventId is null)
            {
                duplicates++;
                continue;
            }

            accepted++;
            var current = seenWaybills.TryGetValue(observation.WaybillNo, out var cached)
                ? cached
                : await ReadProjectionAsync(connection, transaction, siteId, observation.WaybillNo, cancellationToken);
            var eventValue = new JmsEvent
            {
                SiteId = siteId,
                WaybillNo = observation.WaybillNo,
                EventOccurredAt = parsed.UtcValue.Value,
                EventFingerprint = fingerprint,
                Code = observation.Code,
                Name = observation.ScanTypeName,
                Status = observation.Status,
                Payload = observation.Payload,
                EventId = eventId
            };
            var next = reducer.Reduce(current, eventValue, policies);
            seenWaybills[observation.WaybillNo] = next;
            if (next.Version != (current?.Version ?? 0))
                changedByWaybill[observation.WaybillNo] = (next, ProjectionBody.From(next, DateTimeOffset.UtcNow));
        }

        var changed = changedByWaybill.Values.ToList();
        var doorbells = new List<ChangeDoorbell>(changed.Count);
        long? firstSeq = null;
        long? lastSeq = null;
        if (changed.Count > 0)
        {
            var sequence = startingSequence.Value;
            foreach (var entry in changed)
            {
                sequence = checked(sequence + 1);
                var body = entry.Body with { Version = entry.Projection.Version };
                await UpsertProjectionAsync(connection, transaction, entry.Projection, body.UpdatedAt, cancellationToken);
                await InsertChangeAsync(connection, transaction, siteId, sequence, entry.Projection.WaybillNo, body, cancellationToken);
                doorbells.Add(new ChangeDoorbell(siteId, sequence, "waybill_projection", entry.Projection.WaybillNo));
                firstSeq ??= sequence;
                lastSeq = sequence;
            }

            await UpdateCounterAsync(connection, transaction, siteId, sequence, cancellationToken);
        }

        var response = new IngestResponse(siteId, accepted, duplicates, changed.Count, false, firstSeq, lastSeq);
        await InsertIdempotencyAsync(connection, transaction, siteId, normalizedKey, bodyHash, response, cancellationToken);
        await AuditRepository.AppendAsync(
            connection,
            transaction,
            siteId,
            $"device:{deviceId:D}",
            requireFence ? "jms.bulk_ingest" : "jms.interactive_ingest",
            new { deviceId, accepted, duplicates, changedProjections = changed.Count, firstSeq, lastSeq },
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

    private static async Task UpdateCounterAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid siteId,
        long sequence,
        CancellationToken cancellationToken)
    {
        const string sql = "UPDATE site_change_counters SET change_seq = @sequence WHERE site_id = @site_id;";
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("site_id", siteId);
        command.Parameters.AddWithValue("sequence", sequence);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<long?> InsertEventAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        JmsObservation observation,
        DateTimeOffset occurredAt,
        string fingerprint,
        CancellationToken cancellationToken)
    {
        const string sql = """
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
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("site_id", observation.SiteId);
        command.Parameters.AddWithValue("waybill_no", observation.WaybillNo.Trim());
        command.Parameters.AddWithValue("fingerprint", fingerprint);
        command.Parameters.AddWithValue("occurred_at", occurredAt);
        AddNullable(command, "code", observation.Code);
        AddNullable(command, "scan_type_name", observation.ScanTypeName);
        AddNullable(command, "status", observation.Status);
        AddNullable(command, "network_code", observation.ScanNetworkCode);
        AddNullable(command, "operator_code", observation.ScanByCode);
        AddNullable(command, "package_number", observation.PackageNumber);
        AddNullable(command, "task_code", observation.TaskCode);
        var payload = observation.Payload is { } element ? element.GetRawText() : "{}";
        command.Parameters.Add("payload", NpgsqlDbType.Jsonb).Value = payload;
        var value = await command.ExecuteScalarAsync(cancellationToken);
        return value is null or DBNull ? null : Convert.ToInt64(value, System.Globalization.CultureInfo.InvariantCulture);
    }

    private static async Task<WaybillProjection?> ReadProjectionAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid siteId,
        string waybillNo,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT state_code, state_name, state_status, state_event_at, state_fingerprint, state_event_id, state_kind, state_payload,
                   last_activity_code, last_activity_name, last_activity_status, last_activity_kind, last_activity_at,
                   last_activity_fingerprint, last_activity_event_id, last_activity_payload,
                   inventory_code, inventory_name, inventory_status, inventory_event_at, inventory_fingerprint, inventory_event_id, inventory_payload,
                   payload, reducer_version, version
              FROM waybill_projections
             WHERE site_id = @site_id AND waybill_no = @waybill_no
             FOR UPDATE;
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("site_id", siteId);
        command.Parameters.AddWithValue("waybill_no", waybillNo.Trim());
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return null;
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

    private static async Task UpsertProjectionAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        WaybillProjection projection,
        DateTimeOffset updatedAt,
        CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO waybill_projections (
                site_id, waybill_no,
                state_code, state_name, state_status, state_event_at, state_fingerprint, state_event_id, state_kind, state_payload,
                last_activity_code, last_activity_name, last_activity_status, last_activity_kind, last_activity_at,
                last_activity_fingerprint, last_activity_event_id, last_activity_payload,
                inventory_code, inventory_name, inventory_status, inventory_event_at, inventory_fingerprint, inventory_event_id, inventory_payload,
                payload, reducer_version, version, updated_at)
            VALUES (@site_id, @waybill_no,
                    @state_code, @state_name, @state_status, @state_event_at, @state_fingerprint, @state_event_id, @state_kind, @state_payload,
                    @activity_code, @activity_name, @activity_status, @activity_kind, @activity_at,
                    @activity_fingerprint, @activity_event_id, @activity_payload,
                    @inventory_code, @inventory_name, @inventory_status, @inventory_event_at, @inventory_fingerprint, @inventory_event_id, @inventory_payload,
                    @payload, @reducer_version, @version, @updated_at)
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
                updated_at = EXCLUDED.updated_at;
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("site_id", projection.SiteId);
        command.Parameters.AddWithValue("waybill_no", projection.WaybillNo);
        AddSlot(command, "state", projection.CurrentState);
        AddSlot(command, "activity", projection.LatestActivity);
        AddSlot(command, "inventory", projection.Inventory);
        var body = ProjectionBody.From(projection, updatedAt);
        var payload = body.Payload?.GetRawText() ?? "{}";
        command.Parameters.Add("payload", NpgsqlDbType.Jsonb).Value = payload;
        command.Parameters.AddWithValue("reducer_version", projection.ReducerVersion);
        command.Parameters.AddWithValue("version", Math.Max(projection.Version, 1));
        command.Parameters.AddWithValue("updated_at", updatedAt);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static void AddSlot(NpgsqlCommand command, string prefix, ProjectionSlot? slot)
    {
        AddNullable(command, $"{prefix}_code", slot?.Code);
        AddNullable(command, $"{prefix}_name", slot?.Name);
        AddNullable(command, $"{prefix}_status", slot?.Status);
        AddNullable(command, $"{prefix}_event_at", slot?.EventOccurredAt);
        AddNullable(command, $"{prefix}_fingerprint", slot?.EventFingerprint);
        AddNullable(command, $"{prefix}_event_id", slot?.EventId);
        AddJsonbNullable(command, $"{prefix}_payload", slot?.Payload);
        if (prefix != "activity" && prefix != "inventory")
            AddNullable(command, $"{prefix}_kind", slot?.Kind.ToWireValue());
        else if (prefix == "activity")
            AddNullable(command, "activity_kind", slot?.Kind.ToWireValue());
    }

    private static async Task InsertChangeAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid siteId,
        long sequence,
        string waybillNo,
        ProjectionBody body,
        CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO dashboard_changes (site_id, change_seq, entity_type, entity_key, operation, body)
            VALUES (@site_id, @sequence, 'waybill_projection', @waybill_no, 'upsert', @body);
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("site_id", siteId);
        command.Parameters.AddWithValue("sequence", sequence);
        command.Parameters.AddWithValue("waybill_no", waybillNo);
        command.Parameters.Add("body", NpgsqlDbType.Jsonb).Value = JsonSerializer.Serialize(body, JsonOptions);
        await command.ExecuteNonQueryAsync(cancellationToken);
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

    private static void AddNullable(NpgsqlCommand command, string name, object? value)
        => command.Parameters.AddWithValue(name, value ?? DBNull.Value);

    private static void AddJsonbNullable(NpgsqlCommand command, string name, JsonElement? value)
    {
        command.Parameters.Add(name, NpgsqlDbType.Jsonb).Value = value is { } element
            ? element.GetRawText()
            : "{}";
    }
}

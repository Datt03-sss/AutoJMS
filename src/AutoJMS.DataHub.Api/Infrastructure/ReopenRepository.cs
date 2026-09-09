using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AutoJMS.DataHub.Api.Auth;
using AutoJMS.DataHub.Api.Domain;
using Npgsql;
using NpgsqlTypes;

namespace AutoJMS.DataHub.Api.Infrastructure;

public sealed record ReopenResponse(
    Guid SiteId,
    string WaybillNo,
    long Version,
    long? ChangeSeq,
    bool WasTerminal,
    bool Replayed);

public sealed record ReopenResult(
    bool Succeeded,
    int StatusCode,
    string? ProblemCode,
    string? Detail,
    ReopenResponse? Response,
    IReadOnlyList<ChangeDoorbell> Doorbells)
{
    public static ReopenResult Success(ReopenResponse response, IReadOnlyList<ChangeDoorbell> doorbells)
        => new(true, StatusCodes.Status200OK, null, null, response, doorbells);

    public static ReopenResult Failure(int statusCode, string code, string detail)
        => new(false, statusCode, code, detail, null, []);
}

/// <summary>
/// Clears a terminal mark so a waybill can receive observations again.
///
/// The counter read/update and the change insert are written out here rather than shared
/// with <see cref="IngestRepository"/> on purpose: §26 forbids rewriting the ingest
/// <c>change_seq</c> allocation, and lifting it into a shared helper is a rewrite of exactly
/// that block. The projection read IS shared, because that one is a twenty-six-ordinal
/// mapping and two copies of it would drift.
/// </summary>
public sealed class ReopenRepository(PostgresDataSource dataSource, TimeProvider timeProvider)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Keys live in <c>idempotency_records</c>, whose primary key <c>(site_id, key)</c> is
    /// shared with ingest — and ingest deserializes whatever it finds there as an
    /// <c>IngestResponse</c>. The prefix makes a collision impossible rather than unlikely.
    /// </summary>
    private const string KeyPrefix = "reopen:";

    public async Task<ReopenResult> ReopenAsync(
        Guid siteId,
        string waybillNo,
        string idempotencyKey,
        string actor,
        CancellationToken cancellationToken)
    {
        if (siteId == Guid.Empty)
            return ReopenResult.Failure(StatusCodes.Status400BadRequest, ApiProblemCodes.BadRequest, "siteId is required.");

        var normalizedWaybill = (waybillNo ?? "").Trim();
        if (normalizedWaybill.Length is 0 or > 64)
            return ReopenResult.Failure(StatusCodes.Status422UnprocessableEntity, "VALIDATION_FAILED", "waybillNo must contain between 1 and 64 characters.");

        var normalizedKey = KeyPrefix + idempotencyKey.Trim();

        // The request carries no body, so the hash binds the key to the only inputs there
        // are. Without it, the same key aimed at a second waybill would replay the first
        // one's response and silently do nothing.
        var bodyHash = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes($"{siteId:D}\n{normalizedWaybill}"))).ToLowerInvariant();

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using (var deadlines = new NpgsqlCommand(
            "SET LOCAL lock_timeout = '5s'; SET LOCAL statement_timeout = '30s';",
            connection,
            transaction))
        {
            await deadlines.ExecuteNonQueryAsync(cancellationToken);
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
                return ReopenResult.Failure(StatusCodes.Status409Conflict, "IDEMPOTENCY_KEY_REUSED", "The idempotency key is bound to a different waybill.");
            }

            await transaction.CommitAsync(cancellationToken);
            return existing.Value.Response is null
                ? ReopenResult.Failure(StatusCodes.Status409Conflict, "IDEMPOTENCY_IN_PROGRESS", "The idempotency key is still being processed.")
                : ReopenResult.Success(existing.Value.Response with { Replayed = true }, []);
        }

        if (!await ReserveIdempotencyAsync(connection, transaction, siteId, normalizedKey, bodyHash, cancellationToken))
        {
            var competing = await ReadIdempotencyAsync(connection, transaction, siteId, normalizedKey, cancellationToken);
            if (competing is null || !string.Equals(competing.Value.BodyHash, bodyHash, StringComparison.Ordinal))
            {
                await transaction.RollbackAsync(cancellationToken);
                return ReopenResult.Failure(StatusCodes.Status409Conflict, "IDEMPOTENCY_KEY_REUSED", "The idempotency key is bound to a different waybill.");
            }
            await transaction.CommitAsync(cancellationToken);
            return competing.Value.Response is null
                ? ReopenResult.Failure(StatusCodes.Status409Conflict, "IDEMPOTENCY_IN_PROGRESS", "The idempotency key is still being processed.")
                : ReopenResult.Success(competing.Value.Response with { Replayed = true }, []);
        }

        // Counter first, then the projection — the same lock order ingest takes, so the two
        // can never deadlock against each other.
        var startingSequence = await ReadCounterAsync(connection, transaction, siteId, cancellationToken);
        if (startingSequence is null)
        {
            await transaction.RollbackAsync(cancellationToken);
            return ReopenResult.Failure(StatusCodes.Status404NotFound, ApiProblemCodes.NotFound, "The site change counter has not been provisioned.");
        }

        var projection = await IngestRepository.ReadProjectionAsync(connection, transaction, siteId, normalizedWaybill, cancellationToken);
        if (projection is null)
        {
            // Gone and gone-for-good are different answers. A tombstone means the purge
            // already removed the row, so there is nothing left to reopen and a retry will
            // never help — 410, not 404.
            var tombstoned = await HasTombstoneAsync(connection, transaction, siteId, normalizedWaybill, cancellationToken);
            await transaction.RollbackAsync(cancellationToken);
            return tombstoned
                ? ReopenResult.Failure(StatusCodes.Status410Gone, "WAYBILL_PURGED", "The waybill was purged and only a tombstone remains; it cannot be reopened.")
                : ReopenResult.Failure(StatusCodes.Status404NotFound, ApiProblemCodes.NotFound, "No projection exists for this waybill.");
        }

        var wasTerminal = await ReadIsTerminalAsync(connection, transaction, siteId, normalizedWaybill, cancellationToken);
        if (!wasTerminal)
        {
            // A no-op success. An operator who was not sure whether the waybill was terminal
            // must not churn the change feed by asking.
            var unchanged = new ReopenResponse(siteId, normalizedWaybill, projection.Version, null, false, false);
            await InsertIdempotencyAsync(connection, transaction, siteId, normalizedKey, bodyHash, unchanged, cancellationToken);
            await AuditRepository.AppendAsync(
                connection, transaction, siteId, actor, "waybill.reopen_noop",
                new { waybillNo = normalizedWaybill, version = projection.Version },
                cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return ReopenResult.Success(unchanged, []);
        }

        if (startingSequence.Value == long.MaxValue)
        {
            await transaction.RollbackAsync(cancellationToken);
            return ReopenResult.Failure(StatusCodes.Status500InternalServerError, "COUNTER_OVERFLOW", "The site change sequence counter has reached its maximum value and must be reset.");
        }

        var sequence = checked(startingSequence.Value + 1);
        var now = timeProvider.GetUtcNow();
        var newVersion = await ClearTerminalAsync(connection, transaction, siteId, normalizedWaybill, sequence, now, cancellationToken);

        // The feed carries the row's current body so a station applies the reopened state
        // without a second fetch. Version comes from the UPDATE's RETURNING rather than from
        // the read, because the UPDATE is what incremented it.
        var body = ProjectionBody.From(projection with { Version = newVersion }, now);
        await InsertChangeAsync(connection, transaction, siteId, sequence, normalizedWaybill, body, cancellationToken);
        await UpdateCounterAsync(connection, transaction, siteId, sequence, cancellationToken);

        var response = new ReopenResponse(siteId, normalizedWaybill, newVersion, sequence, true, false);
        await InsertIdempotencyAsync(connection, transaction, siteId, normalizedKey, bodyHash, response, cancellationToken);
        await AuditRepository.AppendAsync(
            connection, transaction, siteId, actor, "waybill.reopen",
            new { waybillNo = normalizedWaybill, version = newVersion, changeSeq = sequence },
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return ReopenResult.Success(response, [new ChangeDoorbell(siteId, sequence, "waybill_projection", normalizedWaybill)]);
    }

    private static async Task<(string BodyHash, ReopenResponse? Response)?> ReadIdempotencyAsync(
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
        command.Parameters.AddWithValue("key", key);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return null;
        var hash = reader.GetString(0);
        var json = reader.GetFieldValue<string>(1);
        var statusCode = reader.GetInt32(2);
        var response = statusCode == 0
            ? null
            : JsonSerializer.Deserialize<ReopenResponse>(json, JsonOptions)
              ?? throw new InvalidOperationException("Stored reopen idempotency response is invalid.");
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
        command.Parameters.AddWithValue("key", key);
        command.Parameters.AddWithValue("body_hash", bodyHash);
        var value = await command.ExecuteScalarAsync(cancellationToken);
        return value is not null and not DBNull;
    }

    private static async Task InsertIdempotencyAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid siteId,
        string key,
        string bodyHash,
        ReopenResponse response,
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
        command.Parameters.AddWithValue("key", key);
        command.Parameters.AddWithValue("body_hash", bodyHash);
        command.Parameters.Add("response", NpgsqlDbType.Jsonb).Value = JsonSerializer.Serialize(response, JsonOptions);
        await command.ExecuteNonQueryAsync(cancellationToken);
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

    private static async Task<bool> ReadIsTerminalAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid siteId,
        string waybillNo,
        CancellationToken cancellationToken)
    {
        // A separate probe rather than an extra ordinal on ReadProjectionAsync: that reader
        // is shared with ingest, and widening it would change a hot path for one caller.
        const string sql = "SELECT is_terminal FROM waybill_projections WHERE site_id = @site_id AND waybill_no = @waybill_no;";
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("site_id", siteId);
        command.Parameters.AddWithValue("waybill_no", waybillNo);
        var value = await command.ExecuteScalarAsync(cancellationToken);
        return value is bool terminal && terminal;
    }

    private static async Task<bool> HasTombstoneAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid siteId,
        string waybillNo,
        CancellationToken cancellationToken)
    {
        const string sql = "SELECT EXISTS (SELECT 1 FROM waybill_tombstones WHERE site_id = @site_id AND waybill_no = @waybill_no);";
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("site_id", siteId);
        command.Parameters.AddWithValue("waybill_no", waybillNo);
        return (bool)(await command.ExecuteScalarAsync(cancellationToken) ?? false);
    }

    private static async Task<long> ClearTerminalAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid siteId,
        string waybillNo,
        long sequence,
        DateTimeOffset updatedAt,
        CancellationToken cancellationToken)
    {
        // The only place terminal state is cleared. The version bump is what tells a station
        // its cached copy is stale; without it the change row would carry a version the
        // client already has and be discarded as a replay.
        const string sql = """
            UPDATE waybill_projections
               SET is_terminal = false,
                   terminal_at = NULL,
                   terminal_state_code = NULL,
                   last_change_seq = @sequence,
                   version = version + 1,
                   updated_at = @updated_at
             WHERE site_id = @site_id AND waybill_no = @waybill_no
            RETURNING version;
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("site_id", siteId);
        command.Parameters.AddWithValue("waybill_no", waybillNo);
        command.Parameters.AddWithValue("sequence", sequence);
        command.Parameters.AddWithValue("updated_at", updatedAt);
        var value = await command.ExecuteScalarAsync(cancellationToken);
        if (value is null or DBNull)
            throw new InvalidOperationException($"ClearTerminalAsync returned no row for waybill '{waybillNo}'.");
        return Convert.ToInt64(value, System.Globalization.CultureInfo.InvariantCulture);
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
}

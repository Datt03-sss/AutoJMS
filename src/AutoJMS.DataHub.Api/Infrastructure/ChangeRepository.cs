using System.Text.Json;
using AutoJMS.DataHub.Api.Domain;
using Npgsql;

namespace AutoJMS.DataHub.Api.Infrastructure;

public sealed class ChangeRepository(PostgresDataSource dataSource)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<(bool ResyncRequired, ChangePage? Page)> ReadChangesAsync(
        Guid siteId,
        long after,
        int limit,
        CancellationToken cancellationToken)
    {
        limit = Math.Clamp(limit, 1, 500);
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead, cancellationToken);
        await using (var timeout = new NpgsqlCommand("SET LOCAL statement_timeout = '30s';", connection, transaction))
            await timeout.ExecuteNonQueryAsync(cancellationToken);

        var watermark = await ReadWatermarkAsync(connection, transaction, siteId, cancellationToken);
        if (watermark is null)
        {
            await transaction.RollbackAsync(cancellationToken);
            throw new KeyNotFoundException("The site change counter has not been provisioned.");
        }
        var (prunedThrough, current) = watermark.Value;
        if (ChangeCursorWindow.RequiresResync(after, prunedThrough, current))
        {
            await transaction.RollbackAsync(cancellationToken);
            return (true, null);
        }

        const string sql = """
            SELECT change_seq, entity_type, entity_key, operation, change_at, body
              FROM dashboard_changes
             WHERE site_id = @site_id AND change_seq > @after
             ORDER BY change_seq
             LIMIT @limit;
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("site_id", siteId);
        command.Parameters.AddWithValue("after", after);
        command.Parameters.AddWithValue("limit", limit + 1);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var items = new List<DashboardChange>(limit + 1);
        while (await reader.ReadAsync(cancellationToken))
        {
            var body = ParseJson(reader.GetFieldValue<string>(5));
            items.Add(new DashboardChange(
                siteId,
                reader.GetInt64(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetFieldValue<DateTimeOffset>(4),
                body));
        }
        await reader.CloseAsync();

        var hasMore = items.Count > limit;
        if (hasMore) items.RemoveAt(items.Count - 1);
        var next = items.Count == 0 ? after : items[^1].ChangeSeq;
        await transaction.CommitAsync(cancellationToken);
        return (false, new ChangePage(siteId, after, items, hasMore, next));
    }

    public async Task<SnapshotResponse> ReadSnapshotAsync(Guid siteId, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead, cancellationToken);
        await using var timeout = new NpgsqlCommand("SET LOCAL statement_timeout = '30s';", connection, transaction);
        await timeout.ExecuteNonQueryAsync(cancellationToken);

        const string counterSql = "SELECT change_seq FROM site_change_counters WHERE site_id = @site_id;";
        await using var counterCommand = new NpgsqlCommand(counterSql, connection, transaction);
        counterCommand.Parameters.AddWithValue("site_id", siteId);
        var counterValue = await counterCommand.ExecuteScalarAsync(cancellationToken);
        if (counterValue is null or DBNull)
            throw new KeyNotFoundException("The site change counter has not been provisioned.");
        var snapshotSeq = Convert.ToInt64(counterValue, System.Globalization.CultureInfo.InvariantCulture);

        const string sql = """
            SELECT site_id, waybill_no,
                   state_code, state_name, state_status, state_kind, state_event_at, state_fingerprint, state_event_id, state_payload,
                   last_activity_code, last_activity_name, last_activity_status, last_activity_kind, last_activity_at,
                   last_activity_fingerprint, last_activity_event_id, last_activity_payload,
                   inventory_code, inventory_name, inventory_status, inventory_event_at, inventory_fingerprint, inventory_event_id, inventory_payload,
                   payload, reducer_version, version, updated_at
              FROM waybill_projections
             WHERE site_id = @site_id
             ORDER BY waybill_no;
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("site_id", siteId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var items = new List<ProjectionBody>();
        while (await reader.ReadAsync(cancellationToken))
            items.Add(ReadBody(reader));
        await reader.CloseAsync();
        await transaction.CommitAsync(cancellationToken);
        return new SnapshotResponse(siteId, snapshotSeq, items, items.Count, DateTimeOffset.UtcNow);
    }

    private static async Task<(long PrunedThrough, long Current)?> ReadWatermarkAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, Guid siteId, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT pruned_through_seq, change_seq
              FROM site_change_counters
             WHERE site_id = @site_id;
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("site_id", siteId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return null;
        return (reader.GetInt64(0), reader.GetInt64(1));
    }

    private static ProjectionBody ReadBody(NpgsqlDataReader reader)
    {
        var hasState = !reader.IsDBNull(6) && !reader.IsDBNull(7);
        var hasActivity = !reader.IsDBNull(14) && !reader.IsDBNull(15);
        var hasInventory = !reader.IsDBNull(21) && !reader.IsDBNull(22);
        var payload = hasState || hasActivity || hasInventory
            ? ParseJson(reader.GetFieldValue<string>(25))
            : (JsonElement?)null;
        return new ProjectionBody(
            SiteId: reader.GetGuid(0),
            WaybillNo: reader.GetString(1),
            StateCode: NullableInt(reader, 2),
            StateName: NullableString(reader, 3),
            StateKind: NullableString(reader, 5),
            StateStatus: NullableString(reader, 4),
            StateEventAt: NullableDate(reader, 6),
            StateFingerprint: NullableString(reader, 7),
            StateEventId: NullableLong(reader, 8),
            LastActivityCode: NullableInt(reader, 10),
            LastActivityName: NullableString(reader, 11),
            LastActivityKind: NullableString(reader, 13),
            LastActivityStatus: NullableString(reader, 12),
            LastActivityAt: NullableDate(reader, 14),
            LastActivityFingerprint: NullableString(reader, 15),
            LastActivityEventId: NullableLong(reader, 16),
            InventoryCode: NullableInt(reader, 18),
            InventoryName: NullableString(reader, 19),
            InventoryKind: hasInventory ? JmsEventKind.Inventory.ToWireValue() : null,
            InventoryStatus: NullableString(reader, 20),
            InventoryEventAt: NullableDate(reader, 21),
            InventoryFingerprint: NullableString(reader, 22),
            InventoryEventId: NullableLong(reader, 23),
            StatePayload: hasState ? ParseJson(reader.GetFieldValue<string>(9)) : null,
            ActivityPayload: hasActivity ? ParseJson(reader.GetFieldValue<string>(17)) : null,
            InventoryPayload: hasInventory ? ParseJson(reader.GetFieldValue<string>(24)) : null,
            Payload: payload,
            ReducerVersion: reader.GetInt32(26),
            Version: reader.GetInt64(27),
            UpdatedAt: reader.GetFieldValue<DateTimeOffset>(28));
    }

    private static JsonElement ParseJson(string value)
    {
        using var document = JsonDocument.Parse(string.IsNullOrWhiteSpace(value) ? "{}" : value);
        return document.RootElement.Clone();
    }

    private static int? NullableInt(NpgsqlDataReader reader, int ordinal) => reader.IsDBNull(ordinal) ? null : reader.GetInt32(ordinal);
    private static long? NullableLong(NpgsqlDataReader reader, int ordinal) => reader.IsDBNull(ordinal) ? null : reader.GetInt64(ordinal);
    private static string? NullableString(NpgsqlDataReader reader, int ordinal) => reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
    private static DateTimeOffset? NullableDate(NpgsqlDataReader reader, int ordinal) => reader.IsDBNull(ordinal) ? null : reader.GetFieldValue<DateTimeOffset>(ordinal);
}

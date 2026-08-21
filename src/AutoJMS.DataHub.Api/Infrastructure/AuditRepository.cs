using System.Text.Json;
using Npgsql;
using NpgsqlTypes;

namespace AutoJMS.DataHub.Api.Infrastructure;

/// <summary>
/// Small transactional audit writer. It deliberately accepts only a compact
/// server-created payload; raw license/JMS/device credentials never enter audit.
/// </summary>
public static class AuditRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static async Task AppendAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid? siteId,
        string actor,
        string action,
        object payload,
        CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO audit_logs (site_id, actor, action, payload)
            VALUES (@site_id, @actor, @action, @payload);
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.Add("site_id", NpgsqlDbType.Uuid).Value = siteId is null ? DBNull.Value : siteId.Value;
        command.Parameters.AddWithValue("actor", actor);
        command.Parameters.AddWithValue("action", action);
        command.Parameters.Add("payload", NpgsqlDbType.Jsonb).Value = JsonSerializer.Serialize(payload, JsonOptions);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}

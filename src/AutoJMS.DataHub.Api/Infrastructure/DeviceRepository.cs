using AutoJMS.DataHub.Api.Auth;
using Npgsql;

namespace AutoJMS.DataHub.Api.Infrastructure;

public sealed class DeviceRepository(PostgresDataSource dataSource)
{
    public async Task<bool> TouchActiveAsync(DeviceIdentity identity, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        const string sql = """
            UPDATE devices
               SET last_seen_at = now(), updated_at = now()
             WHERE id = @device_id
               AND status = 'active'
               AND token_version = @token_version
               AND site_id = @site_id;
            """;
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("device_id", identity.DeviceId);
        command.Parameters.AddWithValue("token_version", identity.TokenVersion);
        command.Parameters.AddWithValue("site_id", identity.SiteId);
        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }
}

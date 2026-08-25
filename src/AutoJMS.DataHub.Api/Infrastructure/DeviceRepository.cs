using AutoJMS.DataHub.Api.Auth;
using Npgsql;

namespace AutoJMS.DataHub.Api.Infrastructure;

public sealed class DeviceRepository(PostgresDataSource dataSource)
{
    /// <param name="credentialHash">
    /// <see cref="DeviceCredentialHash"/> of the presented bearer token. Required:
    /// without it the enrolled digest was decoration, and a compromised token signing
    /// key alone was enough to authenticate as any known device.
    /// </param>
    public async Task<bool> TouchActiveAsync(
        DeviceIdentity identity,
        string credentialHash,
        CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        // The digest is compared in SQL rather than with FixedTimeEquals in process.
        // There is no prefix to grind here: the compared value is HMAC(pepper, token),
        // so producing a chosen digest already requires the pepper. Comparing in the
        // predicate keeps this a single statement and cannot report a stale row.
        const string sql = """
            UPDATE devices
               SET last_seen_at = now(), updated_at = now()
             WHERE id = @device_id
               AND status = 'active'
               AND token_version = @token_version
               AND site_id = @site_id
               AND credential_hash = @credential_hash;
            """;
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("device_id", identity.DeviceId);
        command.Parameters.AddWithValue("token_version", identity.TokenVersion);
        command.Parameters.AddWithValue("site_id", identity.SiteId);
        command.Parameters.AddWithValue("credential_hash", credentialHash);
        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }
}

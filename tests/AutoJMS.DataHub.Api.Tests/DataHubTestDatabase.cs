using Npgsql;

namespace AutoJMS.DataHub.Api.Tests;

/// <summary>
/// Shared plumbing for the tests that run against a real PostgreSQL, named by
/// <see cref="RequiresDataHubDatabaseFactAttribute.ConnectionString"/>.
///
/// Enrollment now creates sites, and the database these tests point at is a developer's or
/// staging's rather than a throwaway — so a test that provisions one is responsible for
/// taking it away again.
/// </summary>
internal static class DataHubTestDatabase
{
    /// <summary>
    /// A site code no production licence would carry, unique per call so tests running side
    /// by side never contend for the same row.
    /// </summary>
    public static string NewSiteCode() => "ZZT" + Guid.NewGuid().ToString("N")[..9].ToUpperInvariant();

    public static async Task<Guid?> FindSiteIdAsync(string connectionString, string siteCode)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "SELECT id FROM sites WHERE upper(site_code) = upper(@site_code);", connection);
        command.Parameters.AddWithValue("site_code", siteCode);
        return await command.ExecuteScalarAsync() as Guid?;
    }

    public static async Task<long> CountAsync(string connectionString, string sql, Guid siteId)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("site_id", siteId);
        return Convert.ToInt64(await command.ExecuteScalarAsync(), System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Child rows first, in the one order PostgreSQL accepts: every foreign key into
    /// <c>sites</c> is NO ACTION, so a bare <c>DELETE FROM sites</c> is refused, and
    /// <c>site_fetch_leases.leader_device_id</c> is RESTRICT, so a device cannot go while a
    /// lease still names it. Separate statements rather than one multi-CTE delete, because
    /// RESTRICT is checked the moment the row goes rather than at end of statement.
    ///
    /// Best-effort on purpose: this runs from a finally block, where an exception would
    /// replace the assertion failure that actually matters with a cleanup failure.
    /// </summary>
    public static async Task TryDeleteSiteAsync(string connectionString, string siteCode)
    {
        try
        {
            var siteId = await FindSiteIdAsync(connectionString, siteCode);
            if (siteId is null) return;

            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync();
            await using var transaction = await connection.BeginTransactionAsync();
            foreach (var sql in DeleteOrder)
            {
                await using var command = new NpgsqlCommand(sql, connection, transaction);
                command.Parameters.AddWithValue("site_id", siteId.Value);
                await command.ExecuteNonQueryAsync();
            }
            await transaction.CommitAsync();
        }
        catch (NpgsqlException)
        {
        }
    }

    private static readonly string[] DeleteOrder =
    [
        "DELETE FROM site_fetch_leases WHERE site_id = @site_id;",
        "DELETE FROM site_change_counters WHERE site_id = @site_id;",
        "DELETE FROM audit_logs WHERE site_id = @site_id;",
        "DELETE FROM devices WHERE site_id = @site_id;",
        "DELETE FROM sites WHERE id = @site_id;"
    ];
}

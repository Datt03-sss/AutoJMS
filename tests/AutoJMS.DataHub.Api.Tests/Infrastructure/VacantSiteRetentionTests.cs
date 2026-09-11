using AutoJMS.DataHub.Api.Configuration;
using AutoJMS.DataHub.Api.Infrastructure;
using Npgsql;

namespace AutoJMS.DataHub.Api.Tests.Infrastructure;

/// <summary>
/// Database-backed tests for the vacant-site sweep — the half of enrollment's
/// auto-provisioning that takes junk away again.
///
/// The policy these install is global, so while one of them holds it every vacant site in
/// the database is in scope. That is why the interval is ten years and the seeded sites are
/// backdated twenty: no site anyone actually provisioned can qualify, so a test cannot
/// remove a row it did not create even when pointed at staging. Each test drops the policy
/// again in a finally block.
///
/// <see cref="RetentionRepository.RunOnceAsync"/> is called whole rather than reaching for
/// the sweep alone, because the sweep only runs as part of a pass and the ordering between
/// parts is part of what is under test. The other parts act on the policies
/// <c>003_seed_retention.sql</c> already seeds, which is what the deployed worker does every
/// interval anyway.
/// </summary>
public sealed class VacantSiteRetentionTests
{
    private static readonly TimeSpan PolicyAge = TimeSpan.FromDays(3650);
    private static readonly TimeSpan TwiceThePolicyAge = TimeSpan.FromDays(7300);

    [RequiresDataHubDatabaseFact]
    public async Task A_site_that_never_carried_data_is_swept_once_a_policy_names_sites()
    {
        var connectionString = RequiresDataHubDatabaseFactAttribute.ConnectionString!;
        var siteCode = DataHubTestDatabase.NewSiteCode();
        try
        {
            var siteId = await SeedVacantSiteAsync(connectionString, siteCode, TwiceThePolicyAge);
            await SetSitesPolicyAsync(connectionString, PolicyAge);

            var result = await RunRetentionAsync(connectionString);

            Assert.Equal(1, result.DeletedVacantSites);
            Assert.Null(await DataHubTestDatabase.FindSiteIdAsync(connectionString, siteCode));
            // The device goes with the site. Leaving it would violate its own foreign key,
            // so this asserts the delete order worked rather than that it was attempted.
            Assert.Equal(0L, await DataHubTestDatabase.CountAsync(
                connectionString, "SELECT count(*) FROM devices WHERE site_id = @site_id;", siteId));
            Assert.Equal(1L, await CountTraceAsync(connectionString, siteCode));
        }
        finally
        {
            await ClearSitesPolicyAsync(connectionString);
            await DataHubTestDatabase.TryDeleteSiteAsync(connectionString, siteCode);
            await ClearTraceAsync(connectionString, siteCode);
        }
    }

    [RequiresDataHubDatabaseFact]
    public async Task Nothing_is_swept_while_no_policy_names_sites()
    {
        // The default state of every deployment: 011_vacant_sites seeds no policy, so a site
        // old enough and empty enough to qualify still stays. Without this the sweep would be
        // one accidental migration away from deleting sites nobody asked it to touch.
        var connectionString = RequiresDataHubDatabaseFactAttribute.ConnectionString!;
        var siteCode = DataHubTestDatabase.NewSiteCode();
        try
        {
            var siteId = await SeedVacantSiteAsync(connectionString, siteCode, TwiceThePolicyAge);
            await ClearSitesPolicyAsync(connectionString);

            var result = await RunRetentionAsync(connectionString);

            Assert.Equal(0, result.DeletedVacantSites);
            Assert.Equal(siteId, await DataHubTestDatabase.FindSiteIdAsync(connectionString, siteCode));
        }
        finally
        {
            await DataHubTestDatabase.TryDeleteSiteAsync(connectionString, siteCode);
        }
    }

    [RequiresDataHubDatabaseFact]
    public async Task A_site_whose_change_counter_has_moved_is_never_swept()
    {
        // The clause that survives retention. Once this site's events, changes and
        // projections have aged away it looks exactly like a typo site to every other
        // check — and it is not one, because change_seq says something was published for
        // it and stations may still be syncing against that history.
        var connectionString = RequiresDataHubDatabaseFactAttribute.ConnectionString!;
        var siteCode = DataHubTestDatabase.NewSiteCode();
        try
        {
            var siteId = await SeedVacantSiteAsync(connectionString, siteCode, TwiceThePolicyAge);
            await ExecuteAsync(
                connectionString,
                "UPDATE site_change_counters SET change_seq = 1 WHERE site_id = @site_id;",
                command => command.Parameters.AddWithValue("site_id", siteId));
            await SetSitesPolicyAsync(connectionString, PolicyAge);

            var result = await RunRetentionAsync(connectionString);

            Assert.Equal(0, result.DeletedVacantSites);
            Assert.Equal(siteId, await DataHubTestDatabase.FindSiteIdAsync(connectionString, siteCode));
        }
        finally
        {
            await ClearSitesPolicyAsync(connectionString);
            await DataHubTestDatabase.TryDeleteSiteAsync(connectionString, siteCode);
        }
    }

    [RequiresDataHubDatabaseFact]
    public async Task A_site_whose_device_called_recently_is_never_swept()
    {
        // A station that enrolled long ago and has not scanned anything yet is still a
        // station: every authenticated request stamps devices.last_seen_at, so a site whose
        // device is calling is in use no matter how old the row is or how empty the site is.
        var connectionString = RequiresDataHubDatabaseFactAttribute.ConnectionString!;
        var siteCode = DataHubTestDatabase.NewSiteCode();
        try
        {
            var siteId = await SeedVacantSiteAsync(connectionString, siteCode, TwiceThePolicyAge);
            await ExecuteAsync(
                connectionString,
                "UPDATE devices SET last_seen_at = now() WHERE site_id = @site_id;",
                command => command.Parameters.AddWithValue("site_id", siteId));
            await SetSitesPolicyAsync(connectionString, PolicyAge);

            var result = await RunRetentionAsync(connectionString);

            Assert.Equal(0, result.DeletedVacantSites);
            Assert.Equal(siteId, await DataHubTestDatabase.FindSiteIdAsync(connectionString, siteCode));
        }
        finally
        {
            await ClearSitesPolicyAsync(connectionString);
            await DataHubTestDatabase.TryDeleteSiteAsync(connectionString, siteCode);
        }
    }

    /// <summary>
    /// A site in exactly the shape auto-provisioning leaves behind: created by
    /// <c>create_datahub_site</c>, carrying one device and nothing else. Both timestamps are
    /// pushed back by <paramref name="age"/> because the sweep tests each of them separately.
    /// </summary>
    private static async Task<Guid> SeedVacantSiteAsync(string connectionString, string siteCode, TimeSpan age)
    {
        var siteId = Guid.NewGuid();
        await ExecuteAsync(
            connectionString,
            "SELECT create_datahub_site(@site_id, @site_code);",
            command =>
            {
                command.Parameters.AddWithValue("site_id", siteId);
                command.Parameters.AddWithValue("site_code", siteCode);
            });
        await ExecuteAsync(
            connectionString,
            """
            UPDATE sites SET created_at = now() - @age WHERE id = @site_id;
            INSERT INTO devices (id, site_id, name, credential_hash, last_seen_at, created_at)
            VALUES (@device_id, @site_id, 'vacant-site-test', 'not-a-real-digest', now() - @age, now() - @age);
            """,
            command =>
            {
                command.Parameters.AddWithValue("site_id", siteId);
                command.Parameters.AddWithValue("device_id", Guid.NewGuid());
                command.Parameters.AddWithValue("age", age);
            });
        return siteId;
    }

    private static Task SetSitesPolicyAsync(string connectionString, TimeSpan deleteAfter) => ExecuteAsync(
        connectionString,
        """
        DELETE FROM retention_policies WHERE site_id IS NULL AND table_name = 'sites';
        INSERT INTO retention_policies (site_id, table_name, clock_column, delete_after)
        VALUES (NULL, 'sites', 'created_at', @delete_after);
        """,
        command => command.Parameters.AddWithValue("delete_after", deleteAfter));

    private static Task ClearSitesPolicyAsync(string connectionString) => ExecuteAsync(
        connectionString,
        "DELETE FROM retention_policies WHERE site_id IS NULL AND table_name = 'sites';",
        _ => { });

    private static Task ClearTraceAsync(string connectionString, string siteCode) => ExecuteAsync(
        connectionString,
        $"DELETE {TraceFilterFromSql}",
        command => command.Parameters.AddWithValue("site_code", siteCode));

    private static async Task<long> CountTraceAsync(string connectionString, string siteCode)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(TraceFilterSql, connection);
        command.Parameters.AddWithValue("site_code", siteCode);
        return Convert.ToInt64(await command.ExecuteScalarAsync(), System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// The deletion's own record. It carries a null site_id deliberately — the row has to
    /// outlive the site it names — so the site code in the payload is the only handle on it.
    /// Written once and prefixed by each caller, so the count and the cleanup cannot select
    /// different rows.
    /// </summary>
    private const string TraceFilterFromSql = """
        FROM audit_logs
         WHERE site_id IS NULL
           AND action = 'site.vacant_delete'
           AND payload->>'siteCode' = @site_code;
        """;

    private const string TraceFilterSql = $"SELECT count(*) {TraceFilterFromSql}";

    private static async Task<RetentionRunResult> RunRetentionAsync(string connectionString)
    {
        await using var dataSource = new PostgresDataSource(new DataHubRuntimeOptions
        {
            Channel = DataHubRuntimeOptions.AllowedStagingChannel,
            ConnectionString = connectionString,
            DeviceTokenSigningKey = new string('d', 32),
            EnrollmentPepper = new string('p', 32)
        });
        return await new RetentionRepository(dataSource)
            .RunOnceAsync(1000, TimeSpan.FromDays(30), CancellationToken.None);
    }

    private static async Task ExecuteAsync(string connectionString, string sql, Action<NpgsqlCommand> configure)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        configure(command);
        await command.ExecuteNonQueryAsync();
    }
}

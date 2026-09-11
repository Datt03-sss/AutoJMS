using AutoJMS.DataHub.Api.Auth;
using AutoJMS.DataHub.Api.Configuration;
using AutoJMS.DataHub.Api.Infrastructure;
using Microsoft.AspNetCore.Http;
using Npgsql;

namespace AutoJMS.DataHub.Api.Tests.Infrastructure;

/// <summary>
/// Database-backed, because what these pin lives in PostgreSQL and no fake reproduces it:
/// Npgsql allows one command in progress per connector, a unique index decides which of two
/// simultaneous stations creates a site, and an error inside a transaction aborts the whole
/// transaction unless a SAVEPOINT catches it. <see cref="PostgresDataSource"/> is sealed and
/// builds a real <c>NpgsqlDataSource</c>, so there is no seam to substitute either.
///
/// They carry <see cref="RequiresDataHubDatabaseFactAttribute"/> and skip without a database,
/// which makes them a local and staging guard rather than a build gate.
/// </summary>
public sealed class EnrollmentRepositoryTests
{
    [RequiresDataHubDatabaseFact]
    public async Task Enrolling_into_an_unprovisioned_site_provisions_it()
    {
        // A site code the signed assertion names is a site this caller is entitled to, so
        // the enrollment creates it instead of answering 404 and sending somebody to a psql
        // prompt. The three rows asserted below are what create_datahub_site() exists to
        // keep together: a site without its lease or its change counter is a site the fetch
        // leader and the change feed both fall over on.
        var connectionString = RequiresDataHubDatabaseFactAttribute.ConnectionString!;
        var options = TestOptions(connectionString);
        var siteCode = DataHubTestDatabase.NewSiteCode();
        try
        {
            await using var dataSource = new PostgresDataSource(options);
            var repository = new EnrollmentRepository(
                dataSource,
                new HmacDeviceTokenService(options, TimeProvider.System),
                options);

            var result = await repository.EnrollAsync(
                siteCode,
                "PC-01",
                "operator",
                LicenseFor(siteCode),
                CancellationToken.None);

            Assert.True(result.Succeeded);
            Assert.Equal(StatusCodes.Status201Created, result.StatusCode);
            Assert.Equal(siteCode, result.SiteCode);
            Assert.NotNull(result.SiteId);
            Assert.False(string.IsNullOrWhiteSpace(result.DeviceToken));

            Assert.Equal(result.SiteId, await DataHubTestDatabase.FindSiteIdAsync(connectionString, siteCode));
            Assert.Equal(1L, await DataHubTestDatabase.CountAsync(
                connectionString, "SELECT count(*) FROM site_fetch_leases WHERE site_id = @site_id;", result.SiteId!.Value));
            Assert.Equal(1L, await DataHubTestDatabase.CountAsync(
                connectionString, "SELECT count(*) FROM site_change_counters WHERE site_id = @site_id;", result.SiteId!.Value));

            // The only record that this site was never provisioned by a human.
            Assert.Equal(1L, await DataHubTestDatabase.CountAsync(
                connectionString,
                "SELECT count(*) FROM audit_logs WHERE site_id = @site_id AND action = 'site.auto_provision';",
                result.SiteId!.Value));
        }
        finally
        {
            await DataHubTestDatabase.TryDeleteSiteAsync(connectionString, siteCode);
        }
    }

    [RequiresDataHubDatabaseFact]
    public async Task Enrolling_into_a_site_the_assertion_does_not_name_creates_nothing()
    {
        // Auto-provision is only as safe as the scope check in front of it. EnrollmentEndpoints
        // refuses an unlicensed site code before the repository is reached, but the repository
        // is what writes to `sites` now, so the rule has to hold here too — otherwise the
        // security of the table depends on every future caller remembering to ask first.
        var connectionString = RequiresDataHubDatabaseFactAttribute.ConnectionString!;
        var options = TestOptions(connectionString);
        var licensedSiteCode = DataHubTestDatabase.NewSiteCode();
        var requestedSiteCode = DataHubTestDatabase.NewSiteCode();
        await using var dataSource = new PostgresDataSource(options);
        var repository = new EnrollmentRepository(
            dataSource,
            new HmacDeviceTokenService(options, TimeProvider.System),
            options);

        var result = await repository.EnrollAsync(
            requestedSiteCode,
            "PC-01",
            "operator",
            LicenseFor(licensedSiteCode),
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(StatusCodes.Status403Forbidden, result.StatusCode);
        Assert.Equal(ApiProblemCodes.SiteNotLicensed, result.ProblemCode);
        Assert.Null(await DataHubTestDatabase.FindSiteIdAsync(connectionString, requestedSiteCode));
    }

    [RequiresDataHubDatabaseFact]
    public async Task A_first_enrollment_that_loses_the_provisioning_race_joins_the_winning_site()
    {
        // Two stations of a brand-new site power on together. Neither SELECT ... FOR UPDATE
        // finds a row to lock, so both try to create the site and the unique index on
        // sites.site_code decides. The loser must join the winner's site, not fail.
        //
        // The race is staged rather than hoped for: a second connection creates the site and
        // holds the transaction open, so the enrollment under test is guaranteed to look up
        // nothing, then block inside create_datahub_site on the uncommitted row. Committing
        // releases it into the unique violation. Run as two racing tasks instead, the two
        // would usually just serialise and this would pass without ever reaching the branch.
        //
        // Removing the SAVEPOINT in ProvisionSiteAsync fails here and only here: PostgreSQL
        // aborts the transaction on the violation, and the re-read comes back 25P02.
        var connectionString = RequiresDataHubDatabaseFactAttribute.ConnectionString!;
        var options = TestOptions(connectionString);
        var siteCode = DataHubTestDatabase.NewSiteCode();
        var winnerSiteId = Guid.NewGuid();
        try
        {
            await using var blocker = new NpgsqlConnection(connectionString);
            await blocker.OpenAsync();
            await using var blockerTransaction = await blocker.BeginTransactionAsync();
            await using (var create = new NpgsqlCommand("SELECT create_datahub_site(@site_id, @site_code);", blocker, blockerTransaction))
            {
                create.Parameters.AddWithValue("site_id", winnerSiteId);
                create.Parameters.AddWithValue("site_code", siteCode);
                await create.ExecuteNonQueryAsync();
            }

            await using var dataSource = new PostgresDataSource(options);
            var repository = new EnrollmentRepository(
                dataSource,
                new HmacDeviceTokenService(options, TimeProvider.System),
                options);
            var enrollment = repository.EnrollAsync(
                siteCode,
                "PC-02",
                "operator",
                LicenseFor(siteCode),
                CancellationToken.None);

            Assert.True(
                await WaitForBlockedInsertAsync(connectionString),
                "The enrollment never blocked on the uncommitted site row, so the unique-violation branch was not reached.");
            await blockerTransaction.CommitAsync();

            var result = await enrollment;
            Assert.True(result.Succeeded);
            Assert.Equal(winnerSiteId, result.SiteId);
            Assert.Equal(siteCode, result.SiteCode);

            // The device landed on the winner's site, and the loser's own site.auto_provision
            // row went away with the savepoint — a rollback that undid the INSERT but left the
            // audit behind would claim this station provisioned a site it did not.
            Assert.Equal(1L, await DataHubTestDatabase.CountAsync(
                connectionString, "SELECT count(*) FROM devices WHERE site_id = @site_id;", winnerSiteId));
            Assert.Equal(0L, await DataHubTestDatabase.CountAsync(
                connectionString,
                "SELECT count(*) FROM audit_logs WHERE site_id = @site_id AND action = 'site.auto_provision';",
                winnerSiteId));
        }
        finally
        {
            await DataHubTestDatabase.TryDeleteSiteAsync(connectionString, siteCode);
        }
    }

    /// <summary>
    /// Polls until a backend is waiting on a lock, which is how the blocked INSERT shows up
    /// from outside. Without this the test would have to guess with a sleep, and a guess that
    /// is too short commits the winner before the enrollment has looked anything up — the
    /// enrollment then finds the site normally and the test passes without touching the
    /// branch it exists to cover. Bounded well inside the 30s statement_timeout the
    /// enrollment's own connection carries.
    /// </summary>
    private static async Task<bool> WaitForBlockedInsertAsync(string connectionString)
    {
        const string sql = """
            SELECT count(*)
              FROM pg_stat_activity
             WHERE wait_event_type = 'Lock'
               AND datname = current_database()
               AND pid <> pg_backend_pid();
            """;
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < deadline)
        {
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand(sql, connection);
            if (Convert.ToInt64(await command.ExecuteScalarAsync(), System.Globalization.CultureInfo.InvariantCulture) > 0)
                return true;
            await Task.Delay(50);
        }
        return false;
    }

    private static DataHubRuntimeOptions TestOptions(string connectionString) => new()
    {
        Channel = DataHubRuntimeOptions.AllowedStagingChannel,
        ConnectionString = connectionString,
        // Both are length-checked before the query runs; enrollment reports 503 on a short
        // pepper, which would fail the status assertions above for the wrong reason.
        DeviceTokenSigningKey = new string('d', 32),
        EnrollmentPepper = new string('p', 32)
    };

    private static LicenseAssertionIdentity LicenseFor(string siteCode) => new(
        DataHubRuntimeOptions.AllowedStagingChannel,
        new HashSet<string>(StringComparer.Ordinal) { siteCode },
        DateTimeOffset.UtcNow.AddHours(1),
        null,
        1,
        1);
}

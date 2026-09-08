using AutoJMS.DataHub.Api.Auth;
using AutoJMS.DataHub.Api.Configuration;
using AutoJMS.DataHub.Api.Infrastructure;
using Microsoft.AspNetCore.Http;

namespace AutoJMS.DataHub.Api.Tests.Infrastructure;

/// <summary>
/// Database-backed, because the defect guarded here lives in Npgsql's connector state and
/// no fake reproduces it: one command may be in progress per connector, so a rollback
/// issued while a reader is still open throws <c>NpgsqlOperationInProgressException</c>
/// instead of rolling back. <see cref="PostgresDataSource"/> is sealed and builds a real
/// <c>NpgsqlDataSource</c>, so there is no seam to substitute either.
///
/// Point <c>DATAHUB_TEST_CONNECTION_STRING</c> at a migrated DataHub database to run
/// these; without it they skip. CI skips them — no workflow provisions PostgreSQL — so
/// this is a local and staging guard, not a build gate.
/// </summary>
public sealed class EnrollmentRepositoryTests
{
    private const string ConnectionStringVariable = "DATAHUB_TEST_CONNECTION_STRING";

    /// <summary>
    /// xunit 2.x decides skipping at discovery, so the reason is set in the constructor —
    /// there is no <c>Assert.Skip</c> to call from the test body on this version.
    /// </summary>
    private sealed class RequiresDataHubDatabaseFactAttribute : FactAttribute
    {
        public RequiresDataHubDatabaseFactAttribute()
        {
            if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(ConnectionStringVariable)))
                Skip = $"{ConnectionStringVariable} is not set, so there is no database to enroll against.";
        }
    }

    [RequiresDataHubDatabaseFact]
    public async Task Enrolling_into_an_unprovisioned_site_reports_not_found()
    {
        // The site lookup's reader is scoped to the whole method, so this branch used to
        // reach RollbackAsync with it still open and throw. Nothing mapped that exception
        // to a status, so callers saw 503 SERVICE_UNAVAILABLE instead of the 404 the line
        // below it has always intended. Asserting the returned result — rather than an
        // HTTP status — puts the assertion on the branch itself: before the fix this test
        // does not observe a 503, it observes the throw.
        var connectionString = Environment.GetEnvironmentVariable(ConnectionStringVariable)!;
        var options = TestOptions(connectionString);
        await using var dataSource = new PostgresDataSource(options);
        var repository = new EnrollmentRepository(
            dataSource,
            new HmacDeviceTokenService(options, TimeProvider.System),
            options);
        var siteCode = "NOSUCH" + Guid.NewGuid().ToString("N")[..10].ToUpperInvariant();

        var result = await repository.EnrollAsync(
            siteCode,
            "PC-01",
            "operator",
            LicenseFor(siteCode),
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(StatusCodes.Status404NotFound, result.StatusCode);
        Assert.Equal(ApiProblemCodes.NotFound, result.ProblemCode);
    }

    private static DataHubRuntimeOptions TestOptions(string connectionString) => new()
    {
        Channel = DataHubRuntimeOptions.AllowedStagingChannel,
        ConnectionString = connectionString,
        // Both are length-checked before the query runs; enrollment reports 503 on a short
        // pepper, which would pass the status assertion above for the wrong reason.
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

using AutoJMS.DataHub.Api.Auth;
using AutoJMS.DataHub.Api.Configuration;
using AutoJMS.DataHub.Api.Infrastructure;
using Microsoft.AspNetCore.Http;

namespace AutoJMS.DataHub.Api.Tests.Infrastructure;

/// <summary>
/// Database-backed tests for <see cref="ReopenRepository"/>. Paths that require seeded
/// waybill projections or migrations 007–009 (replay, no-op, tombstone) are deliberately
/// uncovered until the backup-verification gate is passed. The one case that needs neither
/// is the unprovisioned-site 404, which exercises the transaction entry, the site-existence
/// probe, and the rollback — before the idempotency reserve and before any projection read.
/// </summary>
public sealed class ReopenRepositoryTests
{
    [RequiresDataHubDatabaseFact]
    public async Task Reopening_a_waybill_at_an_unprovisioned_site_returns_not_found()
    {
        var options = TestOptions(RequiresDataHubDatabaseFactAttribute.ConnectionString!);
        await using var dataSource = new PostgresDataSource(options);
        var repository = new ReopenRepository(dataSource, TimeProvider.System);
        var siteId = Guid.NewGuid();
        var idempotencyKey = "reopen-test-" + Guid.NewGuid().ToString("N")[..8];

        var result = await repository.ReopenAsync(
            siteId,
            "JMS-NOSUCH-" + Guid.NewGuid().ToString("N")[..8],
            idempotencyKey,
            "test-actor",
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(StatusCodes.Status404NotFound, result.StatusCode);
        Assert.Equal(ApiProblemCodes.NotFound, result.ProblemCode);
    }

    private static DataHubRuntimeOptions TestOptions(string connectionString) => new()
    {
        Channel = DataHubRuntimeOptions.AllowedStagingChannel,
        ConnectionString = connectionString,
        DeviceTokenSigningKey = new string('d', 32),
        EnrollmentPepper = new string('p', 32)
    };
}

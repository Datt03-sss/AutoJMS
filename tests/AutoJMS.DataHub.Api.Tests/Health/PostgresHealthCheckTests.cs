using AutoJMS.DataHub.Api.Health;
using AutoJMS.DataHub.Api.Infrastructure;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace AutoJMS.DataHub.Api.Tests.Health;

public sealed class PostgresHealthCheckTests
{
    [Fact]
    public async Task Check_is_unhealthy_when_database_probe_fails()
    {
        var check = new PostgresHealthCheck(new FailingDatabaseProbe());

        var result = await check.CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(HealthStatus.Unhealthy, result.Status);
        Assert.Equal("PostgreSQL is unavailable.", result.Description);
    }

    private sealed class FailingDatabaseProbe : IDataHubDatabaseProbe
    {
        public Task<bool> CanConnectAsync(CancellationToken cancellationToken) => Task.FromResult(false);
    }
}

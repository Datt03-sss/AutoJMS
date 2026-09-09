using AutoJMS.DataHub.Api.Configuration;
using AutoJMS.DataHub.Api.Health;
using AutoJMS.DataHub.Api.Infrastructure;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Npgsql;

namespace AutoJMS.DataHub.Api.Tests.Health;

/// <summary>
/// The status this check reports is a traffic decision. <c>/health/ready</c> maps Degraded
/// to 200 and Unhealthy to 503, and docker-compose gates the caddy service on it — so
/// choosing Unhealthy here would take a host that serves every read and write correctly out
/// of rotation over a retention policy an operator can fix while it runs.
/// </summary>
public sealed class IngestHorizonHealthCheckTests
{
    private sealed class StubReader(IReadOnlyList<RetentionDeletePolicy>? policies, Exception? failure = null)
        : IRetentionPolicyReader
    {
        public Task<IReadOnlyList<RetentionDeletePolicy>> ReadEventDeletePoliciesAsync(CancellationToken cancellationToken)
            => failure is null
                ? Task.FromResult(policies!)
                : Task.FromException<IReadOnlyList<RetentionDeletePolicy>>(failure);
    }

    private static readonly DataHubRuntimeOptions Options = new() { IngestHorizon = TimeSpan.FromDays(45) };

    [Fact]
    public async Task A_retention_longer_than_the_horizon_is_healthy()
    {
        var check = new IngestHorizonHealthCheck(Options, new StubReader([new RetentionDeletePolicy(null, TimeSpan.FromDays(60))]));

        var result = await check.CheckHealthAsync(new HealthCheckContext(), CancellationToken.None);

        Assert.Equal(HealthStatus.Healthy, result.Status);
    }

    [Fact]
    public async Task A_violating_policy_is_degraded_and_names_the_policy()
    {
        var check = new IngestHorizonHealthCheck(Options, new StubReader([new RetentionDeletePolicy(null, TimeSpan.FromDays(30))]));

        var result = await check.CheckHealthAsync(new HealthCheckContext(), CancellationToken.None);

        Assert.Equal(HealthStatus.Degraded, result.Status);
        Assert.Contains("global", result.Description!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_unreadable_database_is_not_this_check_s_report_to_make()
    {
        // PostgresHealthCheck already answers "can we reach the database", and it is tagged
        // ready too. Reporting it twice would double-count one outage; reporting it as a
        // horizon violation would send the operator to the wrong setting entirely.
        var check = new IngestHorizonHealthCheck(Options, new StubReader(null, new NpgsqlException("connection refused")));

        var result = await check.CheckHealthAsync(new HealthCheckContext(), CancellationToken.None);

        Assert.Equal(HealthStatus.Healthy, result.Status);
    }

    [Fact]
    public async Task An_unconfigured_data_source_is_not_a_violation_either()
    {
        // PostgresDataSource throws InvalidOperationException when no connection string was
        // supplied, which is the shape of a test host and of a misconfigured one. The
        // configuration check owns that report.
        var check = new IngestHorizonHealthCheck(Options, new StubReader(null, new InvalidOperationException("not configured")));

        var result = await check.CheckHealthAsync(new HealthCheckContext(), CancellationToken.None);

        Assert.Equal(HealthStatus.Healthy, result.Status);
    }
}

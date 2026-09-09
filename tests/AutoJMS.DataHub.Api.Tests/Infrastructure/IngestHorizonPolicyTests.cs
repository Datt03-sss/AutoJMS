using AutoJMS.DataHub.Api.Configuration;
using AutoJMS.DataHub.Api.Infrastructure;

namespace AutoJMS.DataHub.Api.Tests.Infrastructure;

/// <summary>
/// The horizon decision is extracted to a pure type for one reason: it is the only part of
/// the ingest guardrail that can be wrong in a way tests can catch without a database.
/// Every case below fixes <c>now</c> explicitly — a test that read the real clock would be
/// asserting against the thing under test.
/// </summary>
public sealed class IngestHorizonPolicyTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 9, 12, 0, 0, TimeSpan.Zero);
    private static readonly IngestHorizonPolicy Policy = new(TimeSpan.FromDays(45), TimeSpan.FromSeconds(900));

    [Fact]
    public void A_scan_time_inside_the_window_is_accepted()
    {
        Assert.Null(Policy.FindViolation(Now.AddDays(-44), Now));
    }

    [Fact]
    public void The_present_moment_is_accepted()
    {
        Assert.Null(Policy.FindViolation(Now, Now));
    }

    [Theory]
    [InlineData(45)]  // exactly at the horizon
    [InlineData(0)]   // and the trivial case, to pin that the boundary is inclusive
    public void The_horizon_boundary_itself_is_accepted(int daysOld)
    {
        // Inclusive on purpose. A device that batches a full 45 days and posts at the
        // boundary is the normal backlog case, not a clock fault, and an exclusive bound
        // would reject it on a round number for no reason an operator could act on.
        Assert.Null(Policy.FindViolation(Now.AddDays(-daysOld), Now));
    }

    [Fact]
    public void A_scan_time_older_than_the_horizon_is_rejected()
    {
        var violation = Policy.FindViolation(Now.AddDays(-45).AddSeconds(-1), Now);

        Assert.NotNull(violation);
        Assert.Contains("older than", violation, StringComparison.Ordinal);
    }

    [Fact]
    public void A_scan_time_inside_the_future_skew_is_accepted()
    {
        // A station clock running fast is ordinary; 15 minutes of it is signed as
        // acceptable by OD-2. Rejecting it would drop real scans.
        Assert.Null(Policy.FindViolation(Now.AddSeconds(900), Now));
    }

    [Fact]
    public void A_scan_time_beyond_the_future_skew_is_rejected()
    {
        var violation = Policy.FindViolation(Now.AddSeconds(901), Now);

        Assert.NotNull(violation);
        Assert.Contains("ahead of", violation, StringComparison.Ordinal);
    }

    [Fact]
    public void Comparison_is_in_utc_regardless_of_the_supplied_offset()
    {
        // The parser hands back UTC, but the type must not depend on that: the same instant
        // written with a +07:00 offset has to decide identically, or a Vietnam-local value
        // would be judged seven hours out.
        var sameInstantInVietnam = Now.AddDays(-1).ToOffset(TimeSpan.FromHours(7));

        Assert.Null(Policy.FindViolation(sameInstantInVietnam, Now));
    }

    [Fact]
    public void From_options_carries_the_configured_values()
    {
        var options = new DataHubRuntimeOptions
        {
            IngestHorizon = TimeSpan.FromDays(10),
            IngestFutureSkew = TimeSpan.FromSeconds(30)
        };

        var policy = IngestHorizonPolicy.From(options);

        Assert.Equal(TimeSpan.FromDays(10), policy.Horizon);
        Assert.Equal(TimeSpan.FromSeconds(30), policy.FutureSkew);
    }
}

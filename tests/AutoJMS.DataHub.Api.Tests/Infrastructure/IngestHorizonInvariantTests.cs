using AutoJMS.DataHub.Api.Infrastructure;

namespace AutoJMS.DataHub.Api.Tests.Infrastructure;

/// <summary>
/// <c>ingest_horizon &lt; event_retention</c> is the one relation the horizon exists to
/// preserve, and it cannot be checked from configuration alone: retention lives in
/// <c>retention_policies</c> rows, per site, with a global fallback, and it is nullable.
/// These cases pin what counts as a violation before either caller is written.
/// </summary>
public sealed class IngestHorizonInvariantTests
{
    private static readonly TimeSpan Horizon = TimeSpan.FromDays(45);

    [Fact]
    public void No_policies_is_no_violation()
    {
        // An empty table means nothing deletes events, so no horizon can outlive retention.
        Assert.Empty(IngestHorizonInvariant.Violations([], Horizon));
    }

    [Fact]
    public void A_null_delete_after_is_not_a_violation()
    {
        // NULL is "never delete". That satisfies the invariant for any horizon, and
        // reporting it would make an intentionally archival deployment permanently Degraded.
        var policies = new[] { new RetentionDeletePolicy(null, null) };

        Assert.Empty(IngestHorizonInvariant.Violations(policies, Horizon));
    }

    [Fact]
    public void A_retention_longer_than_the_horizon_is_not_a_violation()
    {
        var policies = new[] { new RetentionDeletePolicy(null, TimeSpan.FromDays(60)) };

        Assert.Empty(IngestHorizonInvariant.Violations(policies, Horizon));
    }

    [Fact]
    public void A_retention_equal_to_the_horizon_is_a_violation()
    {
        // Equality is not safe. At the same value the horizon admits an event on the day
        // the retention pass deletes its dedupe row, and which one runs first is a race.
        var policies = new[] { new RetentionDeletePolicy(null, TimeSpan.FromDays(45)) };

        Assert.Single(IngestHorizonInvariant.Violations(policies, Horizon));
    }

    [Fact]
    public void A_retention_shorter_than_the_horizon_is_a_violation()
    {
        var policies = new[] { new RetentionDeletePolicy(null, TimeSpan.FromDays(30)) };
        var violations = IngestHorizonInvariant.Violations(policies, Horizon);

        Assert.Single(violations);
        Assert.Contains("global", violations[0], StringComparison.Ordinal);
    }

    [Fact]
    public void A_per_site_violation_names_the_site()
    {
        // A startup refusal has to say which policy to fix. "A policy is wrong" against a
        // fleet of sites is not an actionable message.
        var siteId = Guid.Parse("11111111-2222-3333-4444-555555555555");
        var policies = new[] { new RetentionDeletePolicy(siteId, TimeSpan.FromDays(10)) };
        var violations = IngestHorizonInvariant.Violations(policies, Horizon);

        Assert.Single(violations);
        Assert.Contains(siteId.ToString("D"), violations[0], StringComparison.Ordinal);
    }

    [Fact]
    public void Every_violating_policy_is_reported_not_only_the_first()
    {
        // The operator fixes them in one pass or comes back for each reboot.
        var policies = new[]
        {
            new RetentionDeletePolicy(null, TimeSpan.FromDays(30)),
            new RetentionDeletePolicy(Guid.NewGuid(), TimeSpan.FromDays(7)),
            new RetentionDeletePolicy(Guid.NewGuid(), TimeSpan.FromDays(90))
        };

        Assert.Equal(2, IngestHorizonInvariant.Violations(policies, Horizon).Count);
    }
}

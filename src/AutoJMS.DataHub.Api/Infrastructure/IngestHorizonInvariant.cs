using System.Globalization;

namespace AutoJMS.DataHub.Api.Infrastructure;

/// <summary>
/// One <c>retention_policies</c> row's delete clock, reduced to what the invariant needs.
/// A null <see cref="SiteId"/> is the global fallback row; a null
/// <see cref="DeleteAfter"/> means the table is never purged.
/// </summary>
public sealed record RetentionDeletePolicy(Guid? SiteId, TimeSpan? DeleteAfter);

/// <summary>
/// Checks <c>ingest_horizon &lt; event_retention</c> against the retention policies that
/// actually exist.
///
/// The static 59-day maximum on <see cref="Configuration.DataHubRuntimeOptions.IngestHorizon"/>
/// only defends the seeded 60-day global policy. Retention is per site with a global
/// fallback and is editable at runtime, so a per-site row inserted an hour after boot can
/// break the relation without any configuration changing — which is why this is evaluated
/// at startup AND on every readiness poll rather than once.
/// </summary>
public static class IngestHorizonInvariant
{
    public static IReadOnlyList<string> Violations(
        IEnumerable<RetentionDeletePolicy> policies,
        TimeSpan horizon)
        => policies
            .Where(policy => policy.DeleteAfter is { } deleteAfter && deleteAfter <= horizon)
            .Select(policy => Describe(policy, horizon))
            .ToArray();

    private static string Describe(RetentionDeletePolicy policy, TimeSpan horizon)
    {
        var scope = policy.SiteId is { } siteId
            ? string.Create(CultureInfo.InvariantCulture, $"site {siteId:D}")
            : "the global policy";

        return string.Create(
            CultureInfo.InvariantCulture,
            $"{scope} deletes waybill_scan_events after {policy.DeleteAfter!.Value.TotalDays:0.##} days, "
            + $"which is not longer than the {horizon.TotalDays:0.##}-day ingest horizon");
    }
}

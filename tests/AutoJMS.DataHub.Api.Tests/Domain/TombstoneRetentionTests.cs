using AutoJMS.DataHub.Api.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace AutoJMS.DataHub.Api.Tests.Domain;

/// <summary>
/// The one number that decides whether a station ever learns a waybill was deleted.
///
/// Retention publishes a <c>delete</c> tombstone into <c>dashboard_changes</c> before it
/// removes a projection, and the tombstone is the only record of the removal — the snapshot
/// says what exists, never what went away. A station offline for longer than this window
/// reconnects to find the notice already pruned and keeps the row in local SQLite forever,
/// which is the bug the tombstone exists to fix. So this is asserted on the resolved options
/// rather than left to a live retention pass: by the time a pass runs, a bad value has
/// already been read.
/// </summary>
public sealed class TombstoneRetentionTests
{
    private sealed class StubEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Production";
        public string ApplicationName { get; set; } = "AutoJMS.DataHub.Api";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private static DataHubRuntimeOptions Resolve(string? configuredDays)
    {
        var settings = new Dictionary<string, string?>
        {
            ["ConnectionStrings:DataHub"] = "Host=postgres;Database=datahub;Username=datahub;Password=test"
        };
        if (configuredDays is not null)
            settings["DATAHUB_TOMBSTONE_RETENTION_DAYS"] = configuredDays;

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        return DataHubRuntimeOptions.FromConfiguration(configuration, new StubEnvironment());
    }

    [Fact]
    public void An_unset_window_defaults_to_ninety_days()
    {
        // The far end of the 30–90 day requirement, not the near end: the default has to
        // cover the longest offline window a site can plausibly have, because the operator
        // who would have raised it is the one who never set the variable.
        Assert.Equal(TimeSpan.FromDays(90), Resolve(null).TombstoneRetention);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-30")]
    [InlineData("1")]
    [InlineData("14")]
    public void A_window_below_the_floor_is_clamped_rather_than_honoured(string configured)
    {
        // Zero is the value that inverts the safeguard: read literally it prunes every
        // tombstone on the next pass, so a projection would be deleted and the notice of
        // the deletion destroyed in the same 15-minute cycle. 14 is here because it is the
        // seeded dashboard_changes clock, and matching it would make tombstones expire on
        // the same schedule as the ordinary changes they must outlive.
        Assert.Equal(
            TimeSpan.FromDays(DataHubRuntimeOptions.MinimumTombstoneRetentionDays),
            Resolve(configured).TombstoneRetention);
    }

    [Theory]
    [InlineData("100000")]
    [InlineData("366")]
    public void A_window_above_the_ceiling_is_clamped(string configured)
    {
        // The ceiling exists because pruning removes only a contiguous prefix of a site's
        // feed: a live tombstone pins every later change behind it, so an unbounded window
        // means a site's change history is never pruned at all.
        Assert.Equal(
            TimeSpan.FromDays(DataHubRuntimeOptions.MaximumTombstoneRetentionDays),
            Resolve(configured).TombstoneRetention);
    }

    [Fact]
    public void An_operator_window_inside_the_range_is_honoured()
    {
        Assert.Equal(TimeSpan.FromDays(45), Resolve("45").TombstoneRetention);
    }

    [Fact]
    public void An_unparseable_window_falls_back_to_the_default_instead_of_zero()
    {
        // A typo must not be read as "expire immediately". int.TryParse failing is not a
        // signal about how long deletions should be remembered.
        Assert.Equal(TimeSpan.FromDays(DataHubRuntimeOptions.DefaultTombstoneRetentionDays), Resolve("ninety").TombstoneRetention);
    }

    [Fact]
    public void The_floor_stays_at_or_above_the_thirty_day_requirement()
    {
        // Pinned as a requirement, not as an implementation detail: the deployment plan
        // specifies a 30–90 day tombstone window, and lowering this constant would silently
        // narrow it for every site at once.
        Assert.True(DataHubRuntimeOptions.MinimumTombstoneRetentionDays >= 30);
        Assert.InRange(DataHubRuntimeOptions.DefaultTombstoneRetentionDays, 30, 90);
        Assert.True(DataHubRuntimeOptions.MaximumTombstoneRetentionDays >= DataHubRuntimeOptions.DefaultTombstoneRetentionDays);
    }
}

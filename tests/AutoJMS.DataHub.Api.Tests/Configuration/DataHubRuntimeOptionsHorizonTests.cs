using AutoJMS.DataHub.Api.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace AutoJMS.DataHub.Api.Tests.Configuration;

/// <summary>
/// The bounds are the point. 45 days is not a preference — it is the value that keeps
/// <c>ingest_horizon &lt; event_retention</c> true against the seeded 60-day event policy,
/// so a configuration that clamps somewhere else silently breaks the invariant the startup
/// check exists to defend.
/// </summary>
public sealed class DataHubRuntimeOptionsHorizonTests
{
    [Fact]
    public void The_horizon_defaults_to_the_signed_forty_five_days()
    {
        Assert.Equal(TimeSpan.FromDays(45), Build().IngestHorizon);
    }

    [Fact]
    public void The_future_skew_defaults_to_the_signed_fifteen_minutes()
    {
        Assert.Equal(TimeSpan.FromSeconds(900), Build().IngestFutureSkew);
    }

    [Fact]
    public void A_configured_horizon_is_honoured()
    {
        Assert.Equal(TimeSpan.FromDays(30), Build(("DATAHUB_INGEST_HORIZON_DAYS", "30")).IngestHorizon);
    }

    [Fact]
    public void A_horizon_at_or_above_the_sixty_day_event_retention_is_clamped_to_fifty_nine()
    {
        // The upper bound is the invariant expressed as a number: at 60 the horizon admits
        // events the retention pass is already deleting, so ingest would accept scans whose
        // dedupe history no longer exists and re-accept them forever.
        Assert.Equal(TimeSpan.FromDays(59), Build(("DATAHUB_INGEST_HORIZON_DAYS", "60")).IngestHorizon);
        Assert.Equal(TimeSpan.FromDays(59), Build(("DATAHUB_INGEST_HORIZON_DAYS", "3650")).IngestHorizon);
    }

    [Fact]
    public void A_zero_or_negative_horizon_is_clamped_to_one_day()
    {
        Assert.Equal(TimeSpan.FromDays(1), Build(("DATAHUB_INGEST_HORIZON_DAYS", "0")).IngestHorizon);
        Assert.Equal(TimeSpan.FromDays(1), Build(("DATAHUB_INGEST_HORIZON_DAYS", "-5")).IngestHorizon);
    }

    [Fact]
    public void An_unparseable_horizon_falls_back_to_the_default_rather_than_to_a_bound()
    {
        // Distinct from clamping: "abc" is not a number to clamp, and answering 1 day would
        // turn a typo into a silent near-total rejection of the fleet's backlog.
        Assert.Equal(TimeSpan.FromDays(45), Build(("DATAHUB_INGEST_HORIZON_DAYS", "abc")).IngestHorizon);
    }

    [Fact]
    public void The_future_skew_is_clamped_between_zero_and_one_hour()
    {
        Assert.Equal(TimeSpan.Zero, Build(("DATAHUB_INGEST_FUTURE_SKEW_SECONDS", "-1")).IngestFutureSkew);
        Assert.Equal(TimeSpan.FromSeconds(3600), Build(("DATAHUB_INGEST_FUTURE_SKEW_SECONDS", "99999")).IngestFutureSkew);
    }

    [Fact]
    public void A_zero_future_skew_is_a_legal_choice()
    {
        // Zero means "no clock tolerance", which an operator running NTP everywhere may
        // legitimately want. It must not be clamped up to the default.
        Assert.Equal(TimeSpan.Zero, Build(("DATAHUB_INGEST_FUTURE_SKEW_SECONDS", "0")).IngestFutureSkew);
    }

    private static DataHubRuntimeOptions Build(params (string Key, string Value)[] settings)
    {
        var values = new Dictionary<string, string?>
        {
            ["ConnectionStrings:DataHub"] = "Host=postgres;Database=datahub;Username=datahub;Password=test"
        };
        foreach (var (key, value) in settings)
            values[key] = value;

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        return DataHubRuntimeOptions.FromConfiguration(configuration, new StubEnvironment());
    }

    // A copy rather than a reference: the equivalent stub in TombstoneRetentionTests is a
    // private nested class, and widening it would mean editing a passing test file for the
    // convenience of a new one.
    private sealed class StubEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Production";
        public string ApplicationName { get; set; } = "AutoJMS.DataHub.Api";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}

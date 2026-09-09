using System.Globalization;
using AutoJMS.DataHub.Api.Configuration;

namespace AutoJMS.DataHub.Api.Infrastructure;

/// <summary>
/// Decides whether one scan time is close enough to now to be ingested.
///
/// A pure record with the clock passed in, because this is the whole guardrail: the
/// repository does nothing but call it and turn a non-null answer into a 422. Keeping the
/// decision here is what lets the boundary cases be tested without a database or a clock
/// hack.
/// </summary>
public sealed record IngestHorizonPolicy(TimeSpan Horizon, TimeSpan FutureSkew)
{
    /// <summary>
    /// Distinct from VALIDATION_FAILED on purpose. Both are 422, but an operator fixes them
    /// differently: this one means a device's clock or backlog is outside the window, and
    /// the remedy is on the device or in the horizon setting, not in the payload. No client
    /// change is needed — unknown problem codes are already handled generically.
    /// </summary>
    public const string ProblemCode = "INGEST_HORIZON_VIOLATION";

    public static IngestHorizonPolicy From(DataHubRuntimeOptions options)
        => new(options.IngestHorizon, options.IngestFutureSkew);

    /// <summary>
    /// Null when the scan time is acceptable; otherwise the detail an operator reads in the
    /// problem response. Both bounds are inclusive: a batch that lands exactly on the
    /// horizon is an ordinary backlog, not a fault.
    /// </summary>
    public string? FindViolation(DateTimeOffset scanTimeUtc, DateTimeOffset nowUtc)
    {
        var scan = scanTimeUtc.ToUniversalTime();
        var now = nowUtc.ToUniversalTime();

        if (scan < now - Horizon)
            return string.Create(
                CultureInfo.InvariantCulture,
                $"scanTime {scan:O} is older than the {Horizon.TotalDays:0.##}-day ingest horizon.");

        if (scan > now + FutureSkew)
            return string.Create(
                CultureInfo.InvariantCulture,
                $"scanTime {scan:O} is more than {FutureSkew.TotalSeconds:0} seconds ahead of server time.");

        return null;
    }
}

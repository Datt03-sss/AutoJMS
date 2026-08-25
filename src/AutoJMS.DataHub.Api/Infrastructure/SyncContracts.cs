using System.Text.Json;
using System.Text.Json.Serialization;
using AutoJMS.DataHub.Api.Domain;

namespace AutoJMS.DataHub.Api.Infrastructure;

public sealed record DashboardChange(
    Guid SiteId,
    long ChangeSeq,
    string EntityType,
    string EntityKey,
    string Operation,
    DateTimeOffset ChangeAt,
    JsonElement Body);

public sealed record ChangePage(
    Guid SiteId,
    long After,
    IReadOnlyList<DashboardChange> Items,
    bool HasMore,
    long NextAfter);

public sealed record SnapshotResponse(
    Guid SiteId,
    [property: JsonPropertyName("snapshot_seq")]
    long SnapshotSeq,
    IReadOnlyList<ProjectionBody> Items,
    int ItemCount,
    DateTimeOffset GeneratedAt,
    /// <summary>
    /// True when the site holds more projections than the requested limit, so
    /// <see cref="Items"/> is a prefix ordered by waybill number rather than the whole
    /// state. A caller that adopts <see cref="SnapshotSeq"/> as its cursor after a
    /// truncated snapshot has silently lost the remainder: the missing waybills
    /// reappear only when they next change. Bounding the query without saying so
    /// would trade a server-side memory risk for a client-side correctness bug.
    /// </summary>
    bool Truncated = false);

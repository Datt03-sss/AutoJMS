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
    DateTimeOffset GeneratedAt);

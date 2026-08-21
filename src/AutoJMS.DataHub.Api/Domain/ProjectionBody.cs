using System.Text.Json;

namespace AutoJMS.DataHub.Api.Domain;

public sealed record ProjectionBody(
    Guid SiteId,
    string WaybillNo,
    int? StateCode,
    string? StateName,
    string? StateKind,
    string? StateStatus,
    DateTimeOffset? StateEventAt,
    string? StateFingerprint,
    long? StateEventId,
    int? LastActivityCode,
    string? LastActivityName,
    string? LastActivityKind,
    string? LastActivityStatus,
    DateTimeOffset? LastActivityAt,
    string? LastActivityFingerprint,
    long? LastActivityEventId,
    int? InventoryCode,
    string? InventoryName,
    string? InventoryKind,
    string? InventoryStatus,
    DateTimeOffset? InventoryEventAt,
    string? InventoryFingerprint,
    long? InventoryEventId,
    JsonElement? StatePayload,
    JsonElement? ActivityPayload,
    JsonElement? InventoryPayload,
    JsonElement? Payload,
    int ReducerVersion,
    long Version,
    DateTimeOffset UpdatedAt)
{
    public static ProjectionBody From(WaybillProjection projection, DateTimeOffset updatedAt)
        => new(
            projection.SiteId,
            projection.WaybillNo,
            projection.CurrentState?.Code,
            projection.CurrentState?.Name,
            projection.CurrentState?.Kind.ToWireValue(),
            projection.CurrentState?.Status,
            projection.CurrentState?.EventOccurredAt,
            projection.CurrentState?.EventFingerprint,
            projection.CurrentState?.EventId,
            projection.LatestActivity?.Code,
            projection.LatestActivity?.Name,
            projection.LatestActivity?.Kind.ToWireValue(),
            projection.LatestActivity?.Status,
            projection.LatestActivity?.EventOccurredAt,
            projection.LatestActivity?.EventFingerprint,
            projection.LatestActivity?.EventId,
            projection.Inventory?.Code,
            projection.Inventory?.Name,
            projection.Inventory?.Kind.ToWireValue(),
            projection.Inventory?.Status,
            projection.Inventory?.EventOccurredAt,
            projection.Inventory?.EventFingerprint,
            projection.Inventory?.EventId,
            ClonePayload(projection.CurrentState?.Payload),
            ClonePayload(projection.LatestActivity?.Payload),
            ClonePayload(projection.Inventory?.Payload),
            LatestPayload(projection),
            projection.ReducerVersion,
            projection.Version,
            updatedAt);

    private static JsonElement? LatestPayload(WaybillProjection projection)
        => (projection.LatestActivity?.Payload ?? projection.CurrentState?.Payload ?? projection.Inventory?.Payload) is { } payload
            ? payload.Clone()
            : null;

    private static JsonElement? ClonePayload(JsonElement? payload)
        => payload is { } value ? value.Clone() : null;
}

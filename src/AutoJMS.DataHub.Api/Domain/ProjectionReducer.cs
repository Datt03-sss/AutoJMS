using System.Text.Json;

namespace AutoJMS.DataHub.Api.Domain;

public sealed record JmsEvent
{
    public Guid SiteId { get; init; }
    public string WaybillNo { get; init; } = "";
    public DateTimeOffset EventOccurredAt { get; init; }
    public string EventFingerprint { get; init; } = "";
    public int? Code { get; init; }
    public string? Name { get; init; }
    public string? Status { get; init; }
    public JmsEventKind? Kind { get; init; }
    public JsonElement? Payload { get; init; }
    public long? EventId { get; init; }
}

public sealed record ProjectionSlot(
    JmsEventKind Kind,
    int? Code,
    string? Name,
    string? Status,
    DateTimeOffset EventOccurredAt,
    string EventFingerprint,
    JsonElement? Payload,
    long? EventId);

public sealed record WaybillProjection(
    Guid SiteId,
    string WaybillNo,
    ProjectionSlot? CurrentState,
    ProjectionSlot? LatestActivity,
    ProjectionSlot? Inventory,
    int ReducerVersion,
    long Version)
{
    public static WaybillProjection Empty(Guid siteId, string waybillNo, int reducerVersion = 1)
        => new(siteId, waybillNo, null, null, null, reducerVersion, 0);
}

public sealed class ProjectionReducer
{
    private readonly JmsEventPolicyCatalog _policies;

    public ProjectionReducer(JmsEventPolicyCatalog policies)
    {
        _policies = policies ?? throw new ArgumentNullException(nameof(policies));
    }

    public WaybillProjection Reduce(WaybillProjection? current, JmsEvent @event)
        => Reduce(current, @event, _policies);

    public WaybillProjection Reduce(
        WaybillProjection? current,
        JmsEvent @event,
        JmsEventPolicyCatalog policies)
    {
        ArgumentNullException.ThrowIfNull(@event);
        ArgumentNullException.ThrowIfNull(policies);
        ValidateEvent(@event);

        var projection = current ?? WaybillProjection.Empty(@event.SiteId, @event.WaybillNo, policies.DefaultVersion);
        if (projection.SiteId != @event.SiteId || !string.Equals(projection.WaybillNo, @event.WaybillNo, StringComparison.Ordinal))
            throw new ArgumentException("Event and projection tenant keys must match.", nameof(@event));

        var kind = @event.Kind ?? policies.Resolve(@event.Code, projection.ReducerVersion);
        var slot = new ProjectionSlot(
            kind,
            @event.Code,
            @event.Name,
            @event.Status,
            @event.EventOccurredAt.ToUniversalTime(),
            @event.EventFingerprint.Trim(),
            ProjectionPayloadCompactor.Compact(@event.Payload),
            @event.EventId);

        var state = projection.CurrentState;
        var activity = projection.LatestActivity;
        var inventory = projection.Inventory;
        var changed = false;

        if (IsWinner(slot, activity))
        {
            activity = slot;
            changed = true;
        }

        if (kind == JmsEventKind.StateTransition && IsWinner(slot, state))
        {
            state = slot;
            changed = true;
        }

        if (kind == JmsEventKind.Inventory && IsWinner(slot, inventory))
        {
            inventory = slot;
            changed = true;
        }

        return changed
            ? projection with
            {
                CurrentState = state,
                LatestActivity = activity,
                Inventory = inventory,
                Version = projection.Version + 1
            }
            : projection;
    }

    public WaybillProjection Reduce(WaybillProjection? current, JmsObservation observation)
    {
        ArgumentNullException.ThrowIfNull(observation);
        var occurredAt = ScanTimeParser.ParseRequired(observation.ScanTime);
        var fingerprint = EventFingerprintV1.Compute(observation, occurredAt);
        var version = current?.ReducerVersion ?? _policies.DefaultVersion;
        var kind = _policies.Resolve(observation.Code, version);
        return Reduce(current, new JmsEvent
        {
            SiteId = observation.SiteId,
            WaybillNo = observation.WaybillNo,
            EventOccurredAt = occurredAt,
            EventFingerprint = fingerprint,
            Code = observation.Code,
            Name = observation.ScanTypeName,
            Status = observation.Status,
            Kind = kind,
            Payload = observation.Payload
        });
    }

    private static bool IsWinner(ProjectionSlot candidate, ProjectionSlot? existing)
    {
        if (existing is null) return true;
        var timeComparison = candidate.EventOccurredAt.CompareTo(existing.EventOccurredAt);
        if (timeComparison != 0) return timeComparison > 0;
        return string.CompareOrdinal(candidate.EventFingerprint, existing.EventFingerprint) > 0;
    }

    private static void ValidateEvent(JmsEvent @event)
    {
        if (@event.SiteId == Guid.Empty) throw new ArgumentException("SiteId is required.", nameof(@event));
        if (string.IsNullOrWhiteSpace(@event.WaybillNo)) throw new ArgumentException("WaybillNo is required.", nameof(@event));
        if (string.IsNullOrWhiteSpace(@event.EventFingerprint)) throw new ArgumentException("EventFingerprint is required.", nameof(@event));
    }

    private static JsonElement? Clone(JsonElement? value)
        => value is { } element ? element.Clone() : null;
}

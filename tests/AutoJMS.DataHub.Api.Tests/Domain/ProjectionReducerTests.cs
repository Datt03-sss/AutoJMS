using System.Text.Json;
using AutoJMS.DataHub.Api.Domain;

namespace AutoJMS.DataHub.Api.Tests.Domain;

public sealed class ProjectionReducerTests
{
    private static readonly Guid SiteId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    [Fact]
    public void Inventory_updates_inventory_and_activity_but_does_not_overwrite_state()
    {
        var reducer = new ProjectionReducer(JmsEventPolicyCatalog.Default);
        var stateEvent = Event("862229607222", "2026-08-17T11:24:22Z", 110, "state", "v1:state");
        var inventoryEvent = Event("862229607222", "2026-08-17T12:07:32Z", 98, "inventory", "v1:inventory");

        var afterState = reducer.Reduce(null, stateEvent);
        var result = reducer.Reduce(afterState, inventoryEvent);

        Assert.Equal(110, result.CurrentState!.Code);
        Assert.Equal("state_transition", result.CurrentState.Kind.ToWireValue());
        Assert.Equal(98, result.Inventory!.Code);
        Assert.Equal(98, result.LatestActivity!.Code);
    }

    [Fact]
    public void Unknown_code_defaults_to_activity_without_touching_state_or_inventory()
    {
        var reducer = new ProjectionReducer(JmsEventPolicyCatalog.Default);
        var state = reducer.Reduce(null, Event("862229607222", "2026-08-17T11:24:22Z", 110, "state", "v1:state"));

        var result = reducer.Reduce(state, Event("862229607222", "2026-08-17T12:30:00Z", 999, "unknown", "v1:unknown"));

        Assert.Equal(110, result.CurrentState!.Code);
        Assert.Null(result.Inventory);
        Assert.Equal(999, result.LatestActivity!.Code);
        Assert.Equal(JmsEventKind.Activity, result.LatestActivity.Kind);
    }

    [Fact]
    public void An_older_event_does_not_replace_a_slot_winner()
    {
        var reducer = new ProjectionReducer(JmsEventPolicyCatalog.Default);
        var newest = Event("862229607222", "2026-08-17T12:00:00Z", 110, "new", "v1:new");
        var older = Event("862229607222", "2026-08-17T11:00:00Z", 110, "old", "v1:old");

        var afterNewest = reducer.Reduce(null, newest);
        var result = reducer.Reduce(afterNewest, older);

        Assert.Equal("new", result.CurrentState!.Name);
        Assert.Equal("new", result.LatestActivity!.Name);
        Assert.Equal(1, result.Version);
    }

    [Fact]
    public void Equal_time_uses_fingerprint_as_deterministic_tiebreaker()
    {
        var reducer = new ProjectionReducer(JmsEventPolicyCatalog.Default);
        var first = Event("862229607222", "2026-08-17T12:00:00Z", 110, "first", "v1:a");
        var second = Event("862229607222", "2026-08-17T12:00:00Z", 110, "second", "v1:b");

        var afterFirst = reducer.Reduce(null, first);
        var result = reducer.Reduce(afterFirst, second);

        Assert.Equal("second", result.CurrentState!.Name);
        Assert.Equal(2, result.Version);
    }

    [Fact]
    public void Policy_catalog_is_configurable_and_unknown_codes_still_default_to_activity()
    {
        var catalog = new JmsEventPolicyCatalog(new[]
        {
            new JmsEventPolicy(7, 501, JmsEventKind.StateTransition)
        });
        var reducer = new ProjectionReducer(catalog);

        var result = reducer.Reduce(null, Event("862229607222", "2026-08-17T12:00:00Z", 501, "custom", "v1:custom"));

        Assert.Equal(JmsEventKind.StateTransition, result.CurrentState!.Kind);
        Assert.Null(result.Inventory);
        Assert.Equal(7, result.ReducerVersion);
    }

    [Fact]
    public void Projection_payload_keeps_hot_fields_without_copying_verbose_jms_text()
    {
        var reducer = new ProjectionReducer(JmsEventPolicyCatalog.Default);
        var result = reducer.Reduce(null, new JmsEvent
        {
            SiteId = SiteId,
            WaybillNo = "862229607222",
            EventOccurredAt = DateTimeOffset.Parse("2026-08-17T12:00:00Z"),
            EventFingerprint = "v1:compact",
            Code = 110,
            Name = "Quet kien van de",
            Status = "van-chuyen",
            Payload = JsonSerializer.SerializeToElement(new
            {
                remark1 = "Nguoi mua hen lai",
                scanNetworkName = "Can Giuoc 3",
                waybillTrackingContent = new string('x', 50000),
                trackTemplate = "verbose-template"
            })
        });

        var payload = result.CurrentState!.Payload!.Value;
        Assert.Equal("Nguoi mua hen lai", payload.GetProperty("remark1").GetString());
        Assert.Equal("Can Giuoc 3", payload.GetProperty("scanNetworkName").GetString());
        Assert.False(payload.TryGetProperty("waybillTrackingContent", out _));
        Assert.False(payload.TryGetProperty("trackTemplate", out _));
        Assert.True(payload.GetRawText().Length < 4096);
    }

    private static JmsEvent Event(string waybillNo, string occurredAt, int code, string name, string fingerprint)
        => new()
        {
            SiteId = SiteId,
            WaybillNo = waybillNo,
            EventOccurredAt = DateTimeOffset.Parse(occurredAt),
            EventFingerprint = fingerprint,
            Code = code,
            Name = name,
            Payload = JsonSerializer.SerializeToElement(new { name })
        };
}

using System.Text.Json;
using AutoJMS.DataHub.Api.Domain;

namespace AutoJMS.DataHub.Api.Tests.Domain;

public sealed class ProjectionBodyTests
{
    [Fact]
    public void Keeps_state_activity_and_inventory_payloads_independent()
    {
        var siteId = Guid.Parse("33333333-3333-3333-3333-333333333333");
        var state = Slot(JmsEventKind.StateTransition, 110, "problem", "van-chuyen", "v1:state", new { remark1 = "Nguoi mua hen lai" });
        var activity = Slot(JmsEventKind.Inventory, 98, "inventory", null, "v1:activity", new { remark2 = "1" });
        var projection = new WaybillProjection(siteId, "862229607222", state, activity, activity, 1, 2);

        var body = ProjectionBody.From(projection, DateTimeOffset.Parse("2026-08-17T12:08:00Z"));

        Assert.Equal("van-chuyen", body.StateStatus);
        Assert.Equal("Nguoi mua hen lai", body.StatePayload!.Value.GetProperty("remark1").GetString());
        Assert.Equal("1", body.ActivityPayload!.Value.GetProperty("remark2").GetString());
        Assert.Equal("1", body.InventoryPayload!.Value.GetProperty("remark2").GetString());
        Assert.Equal("1", body.Payload!.Value.GetProperty("remark2").GetString());
    }

    private static ProjectionSlot Slot(
        JmsEventKind kind,
        int code,
        string name,
        string? status,
        string fingerprint,
        object payload)
        => new(
            kind,
            code,
            name,
            status,
            DateTimeOffset.Parse("2026-08-17T12:07:32Z"),
            fingerprint,
            JsonSerializer.SerializeToElement(payload),
            code);
}

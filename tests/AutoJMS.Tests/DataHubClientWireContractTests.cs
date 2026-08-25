using AutoJMS.Data;
using Newtonsoft.Json.Linq;
using Xunit;

namespace AutoJMS.Tests;

/// <summary>
/// The DataHub API rejects unmapped JSON members outright (JsonUnmappedMemberHandling.Disallow)
/// and only accepts two scanTime shapes, so a mapping slip turns every push into a 400. These
/// tests pin the projection of a local row onto the JmsObservation contract.
/// </summary>
public class DataHubClientWireContractTests
{
    [Fact]
    public void ToIngestItem_maps_snake_case_rows_onto_the_observation_contract()
    {
        var row = new JObject
        {
            ["waybill_no"] = "886123456789",
            ["thoi_gian_thao_tac"] = "2026-08-23 14:05:09",
            ["current_status"] = "Đang giao",
            ["last_action"] = "Phát hàng",
            ["last_site_code"] = "272C03",
            ["employee_code"] = "EMP01",
            ["risk_level"] = "HIGH"
        };

        var item = DataHubClient.ToIngestItem(row);

        Assert.NotNull(item);
        Assert.Equal("886123456789", item.Value<string>("waybillNo"));
        Assert.Equal("2026-08-23 14:05:09", item.Value<string>("scanTime"));
        Assert.Equal("Đang giao", item.Value<string>("status"));
        Assert.Equal("Phát hàng", item.Value<string>("scanTypeName"));
        Assert.Equal("272C03", item.Value<string>("scanNetworkCode"));
        Assert.Equal("EMP01", item.Value<string>("scanByCode"));

        // Everything the contract has no member for must survive inside payload, never at the root.
        var payload = item.Value<JObject>("payload");
        Assert.NotNull(payload);
        Assert.Equal("HIGH", payload.Value<string>("risk_level"));
        Assert.Null(item["risk_level"]);
    }

    [Fact]
    public void ToIngestItem_emits_only_contract_members_at_the_root()
    {
        var row = new JObject
        {
            ["waybillNo"] = "886000000001",
            ["scanTime"] = "2026-08-23 09:00:00",
            ["unmapped_local_column"] = "x"
        };

        var item = DataHubClient.ToIngestItem(row);

        var allowed = new[]
        {
            "waybillNo", "scanTime", "payload", "status", "scanTypeName",
            "scanNetworkCode", "scanByCode", "packageNumber", "taskCode", "code"
        };
        foreach (var property in item.Properties())
            Assert.Contains(property.Name, allowed);
    }

    [Fact]
    public void ToIngestItem_drops_rows_without_a_waybill_or_scan_time()
    {
        Assert.Null(DataHubClient.ToIngestItem(new JObject { ["scanTime"] = "2026-08-23 09:00:00" }));
        Assert.Null(DataHubClient.ToIngestItem(new JObject { ["waybillNo"] = "886000000002" }));
        Assert.Null(DataHubClient.ToIngestItem(null));
    }

    [Fact]
    public void ToIngestItem_keeps_a_numeric_code_but_ignores_a_non_numeric_one()
    {
        var withCode = DataHubClient.ToIngestItem(new JObject
        {
            ["waybillNo"] = "886000000003",
            ["scanTime"] = "2026-08-23 09:00:00",
            ["code"] = 130
        });

        Assert.Equal(130, withCode.Value<int>("code"));
    }

    [Theory]
    // The server reads a naive timestamp as Asia/Ho_Chi_Minh, which is exactly what JMS produced,
    // so it must be passed through byte-for-byte rather than reinterpreted.
    [InlineData("2026-08-23 14:05:09", "2026-08-23 14:05:09")]
    [InlineData("  2026-08-23 14:05:09  ", "2026-08-23 14:05:09")]
    // An offset-bearing value is converted to UTC so the VPS is left with nothing to guess.
    [InlineData("2026-08-23T14:05:09+07:00", "2026-08-23T07:05:09.0000000Z")]
    [InlineData("2026-08-23T07:05:09Z", "2026-08-23T07:05:09.0000000Z")]
    public void TryNormalizeScanTime_emits_a_shape_the_server_accepts(string raw, string expected)
    {
        Assert.True(DataHubClient.TryNormalizeScanTime(raw, out var normalized));
        Assert.Equal(expected, normalized);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("hôm nay")]
    public void TryNormalizeScanTime_refuses_values_that_would_fail_the_whole_batch(string raw)
    {
        Assert.False(DataHubClient.TryNormalizeScanTime(raw, out var normalized));
        Assert.Null(normalized);
    }

    [Fact]
    public void TryNormalizeScanTime_rewrites_other_parsable_forms_into_the_naive_format()
    {
        Assert.True(DataHubClient.TryNormalizeScanTime("2026-08-23T14:05:09", out var normalized));
        Assert.Equal("2026-08-23 14:05:09", normalized);
    }

    // ── Read projections ──────────────────────────────────────────────────
    //
    // ToWaybill (snapshot reader) and ToWaybillRow (change feed / resync) read the SAME server
    // projection and write the SAME local columns, so they must agree. They did not: ToWaybill
    // preferred stateName/stateEventAt and ToWaybillRow preferred lastActivityName/lastActivityAt,
    // which disagree for any waybill whose newest activity has not yet advanced its state.

    /// <summary>A projection whose activity and state deliberately disagree on all three fields.</summary>
    private static JObject DivergentProjection() => new()
    {
        ["waybillNo"] = "886900000001",
        ["stateName"] = "Đang trung chuyển",
        ["stateStatus"] = "IN_TRANSIT",
        ["stateEventAt"] = "2026-08-23T02:00:00+00:00",
        ["lastActivityName"] = "Phát hàng",
        ["lastActivityStatus"] = "DELIVERING",
        ["lastActivityAt"] = "2026-08-23T05:30:00+00:00"
    };

    [Fact]
    public void Both_readers_prefer_the_latest_activity_over_the_state()
    {
        var model = DataHubClient.ToWaybill(DivergentProjection());
        var row = DataHubClient.ToWaybillRow(DivergentProjection());

        Assert.Equal("Phát hàng", model.ThaoTacCuoi);
        Assert.Equal("Phát hàng", row.Value<string>("thao_tac_cuoi"));
        Assert.Equal(model.ThaoTacCuoi, row.Value<string>("thao_tac_cuoi"));
        Assert.Equal(model.TrangThaiHienTai, row.Value<string>("trang_thai_hien_tai"));
    }

    [Fact]
    public void Both_readers_emit_jms_local_naive_time_not_the_servers_offset_form()
    {
        var model = DataHubClient.ToWaybill(DivergentProjection());
        var row = DataHubClient.ToWaybillRow(DivergentProjection());

        // 05:30Z is 12:30 in Asia/Ho_Chi_Minh. The local columns are compared against SQLite's
        // datetime(), so an offset-bearing value here shifts every merge comparison by 7 hours.
        Assert.Equal("2026-08-23 12:30:00", model.ThoiGianThaoTac);
        Assert.Equal("2026-08-23 12:30:00", row.Value<string>("thoi_gian_thao_tac"));
    }

    [Fact]
    public void Both_readers_fall_back_to_the_state_when_no_activity_is_present()
    {
        var projection = new JObject
        {
            ["waybillNo"] = "886900000002",
            ["stateName"] = "Đang trung chuyển",
            ["stateStatus"] = "IN_TRANSIT",
            ["stateEventAt"] = "2026-08-23T02:00:00+00:00"
        };

        var model = DataHubClient.ToWaybill((JObject)projection.DeepClone());
        var row = DataHubClient.ToWaybillRow((JObject)projection.DeepClone());

        Assert.Equal("Đang trung chuyển", model.ThaoTacCuoi);
        Assert.Equal("Đang trung chuyển", row.Value<string>("thao_tac_cuoi"));
        Assert.Equal("2026-08-23 09:00:00", model.ThoiGianThaoTac);
        Assert.Equal("2026-08-23 09:00:00", row.Value<string>("thoi_gian_thao_tac"));
    }

    [Fact]
    public void An_embedded_payload_still_wins_over_the_projections_own_fields()
    {
        var projection = DivergentProjection();
        // PascalCase because that is what ToObservation puts on the wire: it serialises the model
        // itself (JObject.FromObject), so the round trip binds back onto the same properties.
        projection["payload"] = new JObject
        {
            ["WaybillNo"] = "886900000001",
            ["ThaoTacCuoi"] = "Ghi chú tại chỗ"
        };

        // ToWaybill hydrates the model from payload first and only fills the gaps, so a value the
        // pushing leader supplied must not be overwritten by the reducer's view.
        Assert.Equal("Ghi chú tại chỗ", DataHubClient.ToWaybill(projection).ThaoTacCuoi);
    }

    [Fact]
    public void A_snake_case_payload_key_does_not_bind_and_the_projection_fills_the_gap()
    {
        // WaybillDbModel carries no [JsonProperty] aliases, so Newtonsoft binds by property name
        // only. A hand-written snake_case payload silently fails to bind — the value is not lost
        // (the projection's own field fills in) but it is also not honoured, which is worth
        // knowing before anyone hand-authors a payload the way ToWaybillRow writes its columns.
        var projection = DivergentProjection();
        projection["payload"] = new JObject { ["thao_tac_cuoi"] = "Ghi chú tại chỗ" };

        Assert.Equal("Phát hàng", DataHubClient.ToWaybill(projection).ThaoTacCuoi);
    }
}

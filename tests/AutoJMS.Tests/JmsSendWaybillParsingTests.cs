using System.Text.Json;
using Xunit;

namespace AutoJMS.Tests;

/// <summary>
/// Ghim tên trường trong phản hồi thật của màn "Quản lý vận đơn gửi" (tab con "In Reverse").
/// <para>
/// Các JSON dưới đây cắt ra từ response thật của <c>sysStaff/selectAll</c> và
/// <c>omsWaybill/shippingWaybillList</c>. Giữ nguyên cả những trường "gần giống" đã
/// từng làm đọc nhầm (<c>dispatchCode</c> null trong khi giá trị thật nằm ở
/// <c>terminalDispatchCode</c>; KHÔNG hề có <c>printCount</c>, số lần in là
/// <c>printsNumber</c>) — đó mới là phần khiến bài test này có giá trị.
/// </para>
/// </summary>
public sealed class JmsSendWaybillParsingTests
{
    // ── sysStaff/selectAll: data.records[], mã ở "code", tên ở "name" ──────────────

    private const string StaffResponse = """
    {
        "code": 1,
        "msg": "Yêu cầu thành công",
        "data": {
            "records": [
                {
                    "id": 1098868,
                    "name": "Đinh Thị Chinh",
                    "enName": null,
                    "cnName": null,
                    "code": "01989714",
                    "sort": 0,
                    "levelType": null,
                    "mobile": "+84334951707",
                    "networkId": 5165,
                    "adCode": "01989714"
                }
            ],
            "total": 1,
            "size": 20,
            "current": 1,
            "searchCount": true,
            "pages": 1
        },
        "fail": false,
        "succ": true
    }
    """;

    [Fact]
    public void ParseStaff_DocDungMaVaTen_TuDataRecords()
    {
        var staff = JmsSendWaybillService.ParseStaff(StaffResponse);

        var one = Assert.Single(staff);
        Assert.Equal("01989714", one.Code);
        Assert.Equal("Đinh Thị Chinh", one.Name);
        // Đây là chuỗi hiển thị trong danh sách thả xuống khi người dùng chọn nhân viên.
        Assert.Equal("Đinh Thị Chinh (01989714)", one.Display);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("khong-phai-json")]
    [InlineData("{\"code\":1,\"data\":null}")]
    [InlineData("{\"code\":1,\"data\":{\"records\":[]}}")]
    public void ParseStaff_BodyHongHoacRong_TraDanhSachRong(string body)
    {
        Assert.Empty(JmsSendWaybillService.ParseStaff(body));
    }

    [Fact]
    public void ParseStaff_BanGhiThieuCaMaLanTen_ThiBoQua()
    {
        const string json = """
        {"code":1,"data":{"records":[{"id":1},{"name":"Có tên","code":"01"}]}}
        """;

        var one = Assert.Single(JmsSendWaybillService.ParseStaff(json));
        Assert.Equal("01", one.Code);
    }

    // ── shippingWaybillList: data[] phẳng, không bọc trong "records" ───────────────

    private const string WaybillRecord = """
    {
        "id": "969111714052311352",
        "waybillNo": "801113227096",
        "customerName": "KHÁCH LẺ-CN-THÁI NGUYÊN",
        "collectStaffCode": "01989714",
        "collectStaffName": "Đinh Thị Chinh",
        "collectTime": "2026-09-19 11:42:50",
        "inputTime": "2026-09-19 11:42:57",
        "goodsTypeName": "THƯ TỪ",
        "goodsName": "tài liệu",
        "senderName": "Cty TNHH tổ chức sự kiện New Star",
        "senderDetailedAddress": "168A, đường Nguyễn Thăng Bình",
        "senderAreaName": "Phường Bắc Cường-214TPL01",
        "pickNetworkCode": "214A02",
        "pickFinanceCode": "208001",
        "dispatchCode": null,
        "dispatchNetworkCode": "024E15",
        "terminalDispatchCode": "478-L024E15-012",
        "printsNumber": 2
    }
    """;

    [Fact]
    public void MapRow_DocDungTatCaCotCuaLuoiInReverse()
    {
        using var doc = JsonDocument.Parse(WaybillRecord);

        var row = JmsSendWaybillService.MapRow(doc.RootElement);

        Assert.NotNull(row);
        Assert.Equal("801113227096", row.WaybillNo);
        Assert.Equal("Đinh Thị Chinh", row.NhanVienNhanHang);
        Assert.Equal("168A, đường Nguyễn Thăng Bình", row.DiaChiLayHang);
        Assert.Equal("Cty TNHH tổ chức sự kiện New Star", row.TenNguoiGui);
        Assert.Equal("2026-09-19 11:42:50", row.ThoiGianNhanHang);
        Assert.Equal("tài liệu", row.NoiDungHangHoa);
        // Số lần in nằm ở "printsNumber" — KHÔNG phải "printCount"/"printNum"/"printTimes".
        Assert.Equal(2, row.PrintCount);
        // Mã đoạn phải lấy ở "terminalDispatchCode", không phải "dispatchCode" (đang null).
        Assert.Equal("478-L024E15-012", row.PrintSenderNetworkCode);
    }

    [Fact]
    public void MapRow_KhongCoWaybillNo_TraNull()
    {
        using var doc = JsonDocument.Parse("""{"collectStaffName":"Ai đó","printsNumber":9}""");

        Assert.Null(JmsSendWaybillService.MapRow(doc.RootElement));
    }

    // ── 2.4 waybillCenterPrint trả data LÀ OBJECT, không phải chuỗi URL ────────────

    [Fact]
    public void PhanHoiCenterPrint_UrlPdfNamODataPdfFullPath()
    {
        // Ghim lại hình dạng response của waybillCenterPrint: printWaybill (3 tab con kia)
        // trả data là chuỗi URL, còn endpoint này trả object — chỉ pdfFullPath mới tải được,
        // pdfRelativePath không phải URL. ParsePrintWaybillResponse phải đọc được cả hai dạng.
        const string json = """
        {
            "code": 1,
            "msg": "Yêu cầu thành công",
            "data": {
                "requestTraceId": "07ad80fa0f39734d",
                "pdfRelativePath": "osb30del/lpt/20260919/my-lpt-api-openhtml2pdf-3ddabd.pdf",
                "pdfFullPath": "https://pro-jmsvn-file.jtexpress.vn/osb30del/lpt/20260919/my-lpt-api-openhtml2pdf-3ddabd.pdf?Expires=1789815171"
            },
            "succ": true,
            "fail": false
        }
        """;

        using var doc = JsonDocument.Parse(json);
        var data = doc.RootElement.GetProperty("data");

        Assert.Equal(JsonValueKind.Object, data.ValueKind);
        Assert.StartsWith("https://", data.GetProperty("pdfFullPath").GetString());
        Assert.False(data.GetProperty("pdfRelativePath").GetString()!.StartsWith("http"));
    }

    // ── Payload in: đúng 3 khoá như cURL của giao diện JMS ─────────────────────────

    [Fact]
    public void BuildCenterPrintPayload_DungPrintMode2VaCountryId1()
    {
        string payload = JmsSendWaybillService.BuildCenterPrintPayload(
            new[] { "801113227096", "842617152633" });

        using var doc = JsonDocument.Parse(payload);
        var root = doc.RootElement;

        Assert.Equal(2, root.GetProperty("printMode").GetInt32());
        Assert.Equal("1", root.GetProperty("countryId").GetString());
        Assert.Equal(
            new[] { "801113227096", "842617152633" },
            root.GetProperty("waybillNos").EnumerateArray().Select(x => x.GetString()).ToArray());
    }
}

using System.Text.Json;
using System.Text.RegularExpressions;
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

    // ── Body multipart phải giống hệt trình duyệt ──────────────────────────────────

    [Fact]
    public async Task BuildListForm_BocNhayTenTruongVaKhongBocNhayBoundary()
    {
        using var form = JmsSendWaybillService.BuildListForm(
            1, "01989714",
            new DateTime(2026, 9, 19, 0, 0, 0),
            new DateTime(2026, 9, 19, 23, 59, 59),
            "", "208001");

        string contentType = form.Headers.ContentType!.ToString();
        string body = await form.ReadAsStringAsync();

        // .NET mặc định sinh boundary="..." và name=current (không nháy) — ngược hẳn với
        // trình duyệt. Backend JMS bóc form đúng RFC 7578 nên sẽ BỎ QUA mọi trường có tên
        // không bọc nháy, rồi trả code:1 kèm danh sách rỗng mà không báo lỗi gì.
        Assert.DoesNotContain("boundary=\"", contentType);
        Assert.DoesNotContain("name=current", body);

        // ĐÚNG 10 trường, ĐÚNG thứ tự của cURL thật — không có searchTimeType.
        string[] expected =
        {
            "current", "size", "pickFinanceCode", "collectStaffCode",
            "timeStart", "timeEnd", "waybillNos", "customerCodes",
            "inputTimeStart", "inputTimeEnd"
        };
        Assert.Equal(
            expected,
            Regex.Matches(body, "name=\"([^\"]+)\"").Select(m => m.Groups[1].Value).ToArray());

        // Chốt bằng SỐ: ảnh DevTools của request thật (cùng đúng bộ tham số này, boundary
        // cũng dài 38 ký tự) ghi Content-Length: 1114. Thừa một trường là lệch ngay — chẳng
        // hạn searchTimeType=1 đẩy lên 1216. Đây là thứ duy nhất bắt được "thừa trường",
        // vì JMS nhận trường lạ rồi trả code:1 với danh sách rỗng mà không kêu.
        Assert.Equal(1114, body.Length);

        // Phần text thường không kèm Content-Type, y như trình duyệt.
        Assert.DoesNotContain("Content-Type: text/plain", body);
        Assert.Contains("\r\n\r\n208001\r\n", body);
        Assert.Contains("\r\n\r\n01989714\r\n", body);
        Assert.Contains("\r\n\r\n1\r\n", body);
        Assert.Contains("2026-09-19 00:00:00", body);
        Assert.Contains("2026-09-19 23:59:59", body);
    }

    [Fact]
    public void RouterNameList_PercentEncodeCaDauPhanCap()
    {
        // cURL thật gửi %3E chứ không phải ">" trần, và mọi hằng RouterNameList khác trong
        // repo cũng vậy. Bản cũ để ">" trần — header đi ra khác giao diện JMS một cách
        // không ai nhìn thấy, vì phân hệ này không báo lỗi bao giờ.
        Assert.DoesNotContain(">", JmsSendWaybillService.CenterPrintRouterNameList);
        Assert.Equal(2, Regex.Matches(JmsSendWaybillService.CenterPrintRouterNameList, "%3E").Count);
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

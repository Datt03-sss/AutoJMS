using Xunit;

namespace AutoJMS.Tests;

/// <summary>
/// Kiểm chứng cách đọc phản hồi của thao tác "Lưu và thêm mới" (đăng ký chuyển hoàn).
/// <para>
/// Quy ước nghiệp vụ: đăng ký hợp lệ trả về <c>succ: true</c>, thất bại trả về
/// <c>succ: false</c> / <c>fail: true</c>. Cờ <c>succ</c> được ƯU TIÊN; chuỗi
/// "Thao tác thành công" + <c>code=1</c> chỉ là DỰ PHÒNG khi response thiếu cờ.
/// </para>
/// </summary>
public sealed class DkchSaveResponseTests
{
    // ── Cờ succ là chuẩn ưu tiên ───────────────────────────────────────────────────

    [Theory]
    [InlineData("{\"succ\":true,\"msg\":\"1:Thao tác thành công\",\"code\":1}")]
    [InlineData("{\"succ\":\"true\",\"msg\":\"OK\"}")]
    [InlineData("{\"succ\":true,\"msg\":\"Đăng ký thành công\"}")] // không cần khớp chuỗi mẫu
    public void SuccTrue_ThiLaThanhCong(string json)
    {
        Assert.True(WebViewAutomation.IsSaveSuccessResponse(json));
        Assert.False(WebViewAutomation.IsFailureResponse(json));
    }

    [Theory]
    [InlineData("{\"succ\":false,\"msg\":\"Chưa đủ ca phát 2/3\",\"code\":137099001}")]
    [InlineData("{\"succ\":\"false\",\"msg\":\"Vận đơn không tồn tại\"}")]
    [InlineData("{\"fail\":true,\"msg\":\"Đã đăng ký chuyển hoàn\"}")]
    public void SuccFalseHoacFailTrue_ThiLaThatBai(string json)
    {
        Assert.True(WebViewAutomation.IsFailureResponse(json));
        Assert.False(WebViewAutomation.IsSaveSuccessResponse(json));
    }

    [Fact]
    public void SuccTrue_NhungFailTrue_ThiVanCoiLaThatBai()
    {
        // Phản hồi tự mâu thuẫn → ưu tiên an toàn, coi là thất bại (không MarkSaved).
        const string json = "{\"succ\":true,\"fail\":true,\"msg\":\"Lỗi\"}";
        Assert.True(WebViewAutomation.IsFailureResponse(json));
        Assert.False(WebViewAutomation.IsSaveSuccessResponse(json));
    }

    // ── Dự phòng khi response thiếu hẳn cờ succ ───────────────────────────────────

    [Fact]
    public void KhongCoSucc_NhungCoThongDiepThanhCongVaCode1_ThiLaThanhCong()
    {
        const string json = "{\"code\":1,\"msg\":\"1:Thao tác thành công\"}";
        Assert.True(WebViewAutomation.IsSaveSuccessResponse(json));
    }

    [Fact]
    public void KhongCoSucc_CodeLaMaLoiDaiChua1_ThiKhongDuocCoiLaThanhCong()
    {
        // "code":137043004 không được khớp thành code = 1.
        const string json = "{\"code\":137043004,\"msg\":\"Thao tác thành công\"}";
        Assert.False(WebViewAutomation.IsSaveSuccessResponse(json));
    }

    [Fact]
    public void KhongCoSucc_KhongCoThongDiepMau_ThiKhongXacMinhDuoc()
    {
        const string json = "{\"code\":1,\"msg\":\"Đã ghi nhận\"}";
        Assert.False(WebViewAutomation.IsSaveSuccessResponse(json));
        Assert.False(WebViewAutomation.IsFailureResponse(json));
    }

    // ── Lọc response: không được nhặt nhầm response của request khác ───────────────

    [Fact]
    public void ChapNhanPhongBiKetQuaNhoGonCuaThaoTac()
    {
        Assert.True(WebViewAutomation.LooksLikeActionEnvelope(
            "https://jms.example.com/operatingplatform/returnAndForward/save",
            "{\"succ\":true,\"msg\":\"1:Thao tác thành công\"}"));
    }

    [Theory]
    [InlineData("https://jms.example.com/operatingplatform/podTracking/inner/query/keywordList")]
    [InlineData("https://jms.example.com/x/keywordList")]
    public void TuChoiResponseTraCuuHanhTrinh(string uri)
    {
        Assert.False(WebViewAutomation.LooksLikeActionEnvelope(
            uri, "{\"succ\":true,\"msg\":\"1:Thao tác thành công\"}"));
    }

    [Fact]
    public void TuChoiResponseQuaDai_VuotKhuonPhongBiKetQua()
    {
        string huge = "{\"succ\":true,\"msg\":\"1:Thao tác thành công\",\"data\":\""
                      + new string('x', 5000) + "\"}";
        Assert.False(WebViewAutomation.LooksLikeActionEnvelope("https://jms.example.com/save", huge));
    }

    [Fact]
    public void TuChoiResponseKhongCoSuccVaKhongCoMsg()
    {
        Assert.False(WebViewAutomation.LooksLikeActionEnvelope(
            "https://jms.example.com/save", "{\"total\":12,\"list\":[]}"));
    }

    // ── Bóc thông điệp để hiển thị nguyên văn lên ô kết quả ───────────────────────

    [Theory]
    [InlineData("{\"msg\":\"1:Thao tác thành công\"}", "Thao tác thành công")]
    [InlineData("{\"msg\":\"999006328:Chưa đủ ca phát 2/3\"}", "Chưa đủ ca phát 2/3")]
    [InlineData("{\"msg\":\"Đã ghi nhận dữ liệu đăng ký chuyển hoàn\"}", "Đã ghi nhận dữ liệu đăng ký chuyển hoàn")]
    [InlineData("{\"code\":1}", "")]
    public void BocMsg_BoTienToMaLoi(string json, string expected)
    {
        Assert.Equal(expected, WebViewAutomation.ExtractMessage(json));
    }

    [Theory]
    [InlineData("{\"code\":1,\"msg\":\"x\"}", "1")]
    [InlineData("{\"code\":\"999006082\",\"msg\":\"x\"}", "999006082")]
    [InlineData("{\"msg\":\"x\"}", "")]
    public void BocCode(string json, string expected)
    {
        Assert.Equal(expected, WebViewAutomation.ExtractCode(json));
    }

    // ── Lỗi 999010051 "chưa đủ ca phát" — mẫu thật từ JMS ─────────────────────────

    /// <summary>Response thật khi đơn giao lại chưa đủ số ca phát (出仓次数).</summary>
    private const string NotEnoughDispatchJson =
        "{\"code\":999010051,\"succ\":false," +
        "\"msg\":\"999010051:此单的出仓次数不满足登记条件，出仓次数：1/2\"}";

    [Fact]
    public void ChuaDuCaPhat_LaThatBaiXacDinh()
    {
        Assert.True(WebViewAutomation.IsFailureResponse(NotEnoughDispatchJson));
        Assert.False(WebViewAutomation.IsSaveSuccessResponse(NotEnoughDispatchJson));
        Assert.True(WebViewAutomation.IsKnownBusinessRejection(NotEnoughDispatchJson));
    }

    /// <summary>Response thật khi đơn MỚI chưa đủ số ca kiện vấn đề (问题件次数).</summary>
    private const string NotEnoughProblemScanJson =
        "{\"code\":999010052,\"succ\":false," +
        "\"msg\":\"999010052:此单的问题件次数不满足登记条件，问题件次数：1/3\"}";

    [Fact]
    public void ChuaDuCaPhat_DichSangTiengVietKemTiSoCaPhat()
    {
        Assert.Equal("Đơn phát lại chưa đủ ca (1/2)",
            WebViewAutomation.TranslateJmsMessage(NotEnoughDispatchJson));
    }

    [Fact]
    public void ChuaDuCaKienVanDe_LaLoiNghiepVuVaDichRieng()
    {
        // 问题件次数 (đơn mới) phải ra câu KHÁC 出仓次数 (đơn giao lại).
        Assert.True(WebViewAutomation.IsKnownBusinessRejection(NotEnoughProblemScanJson));
        Assert.Equal("Đơn mới chưa đủ ca (1/3)",
            WebViewAutomation.TranslateJmsMessage(NotEnoughProblemScanJson));
    }

    [Theory]
    [InlineData("{\"succ\":false,\"msg\":\"999010051:此单的出仓次数不满足登记条件，出仓次数：1/2\"}", "1/2")]
    [InlineData("{\"succ\":false,\"msg\":\"999010052:此单的问题件次数不满足登记条件，问题件次数：1/3\"}", "1/3")]
    [InlineData("{\"succ\":false,\"msg\":\"出仓次数: 2 / 3\"}", "2/3")]
    [InlineData("{\"succ\":true,\"msg\":\"1:Thao tác thành công\"}", "")]
    public void BocTiSoCaPhat(string json, string expected)
    {
        Assert.Equal(expected, WebViewAutomation.ExtractRatio(json));
    }

    [Fact]
    public void ChuaDuCaPhat_GiuNguyenVanDeDoiChieu()
    {
        Assert.Equal("999010051:此单的出仓次数不满足登记条件，出仓次数：1/2",
            WebViewAutomation.ExtractRawMessage(NotEnoughDispatchJson));
        Assert.Equal("999010051", WebViewAutomation.ExtractErrorCode(NotEnoughDispatchJson));
    }

    [Fact]
    public void ChuaDuCaPhat_KhongCoTiSo_ThiVanDichDuoc()
    {
        const string json = "{\"code\":999010051,\"succ\":false,\"msg\":\"999010051:此单的出仓次数不满足登记条件\"}";
        Assert.Equal("Đơn phát lại chưa đủ ca", WebViewAutomation.TranslateJmsMessage(json));
    }

    [Fact]
    public void MaLoiNhoiCaChuoiVaoTruongCode_VanBocDuocSoVaKhongBiHieuLaDoiMode()
    {
        // Phòng trường hợp JMS trả code kiểu "999010051:此单…": phần số vẫn phải bóc đúng,
        // và IsKnownBusinessRejection phải nhận ra để KHÔNG đổi mode rồi bấm Lưu lần 2.
        const string json = "{\"code\":\"999010051:此单的出仓次数不满足登记条件，出仓次数：1/2\",\"succ\":false}";
        Assert.Equal("999010051", WebViewAutomation.ExtractErrorCode(json));
        Assert.True(WebViewAutomation.IsKnownBusinessRejection(json));
    }

    [Theory]
    [InlineData("{\"succ\":false,\"code\":999006328,\"msg\":\"999006328:Chưa có đơn\"}")]
    [InlineData("{\"succ\":false,\"code\":137043004,\"msg\":\"137043004:Sai mode\"}")]
    public void LoiDoiMode_KhongBiXemLaLoiNghiepVuDaBiet(string json)
    {
        Assert.True(WebViewAutomation.IsFailureResponse(json));
        Assert.False(WebViewAutomation.IsKnownBusinessRejection(json));
    }

    [Fact]
    public void ResponseThanhCong_KhongBaoGioLaLoiNghiepVu()
    {
        Assert.False(WebViewAutomation.IsKnownBusinessRejection(
            "{\"succ\":true,\"code\":1,\"msg\":\"1:Thao tác thành công\"}"));
    }

    // ── Chịu được JSON có BOM / không chuẩn (capture thực tế có BOM đầu file) ──────

    [Fact]
    public void ChiuDuocJsonCoBOM()
    {
        string json = "﻿{\"succ\":true,\"msg\":\"1:Thao tác thành công\",\"code\":1}";
        Assert.True(WebViewAutomation.IsSaveSuccessResponse(json));
        Assert.Equal("Thao tác thành công", WebViewAutomation.ExtractMessage(json));
    }
}

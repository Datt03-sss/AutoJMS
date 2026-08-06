using System;
using System.Collections.Generic;
using Xunit;

namespace AutoJMS.Tests;

/// <summary>
/// Kiểm chứng luật nghiệp vụ DKCH đọc từ lịch sử hành trình vận chuyển:
/// <list type="number">
/// <item>CHỈ CHẶN khi sau "Giao lại hàng" đã có "Quét phát hàng" mà chưa có "Quét kiện vấn đề"
/// phía sau. Mọi trạng thái còn lại đều được "Đăng ký chuyển hoàn".</item>
/// <item>Mỗi chu kỳ chỉ đăng ký chuyển hoàn đúng 1 lần (chống spam).</item>
/// </list>
/// </summary>
public sealed class DkchJourneyAnalyzerTests
{
    private static WaybillDetail Ev(string scanTypeName, int hour) => new WaybillDetail
    {
        scanTypeName = scanTypeName,
        uploadTime = $"2026-07-29 {hour:00}:00:00",
        scanTime = $"2026-07-29 {hour:00}:00:00",
        scanByName = "TEST"
    };

    // ── Luồng chuẩn: được đăng ký đúng 1 lần ────────────────────────────────────────

    [Fact]
    public void VeKho_QuetPhat_KienVanDe_ChuaDangKy_ThiDuocDangKy()
    {
        var d = DkchJourneyAnalyzer.Analyze(new List<WaybillDetail>
        {
            Ev("卸车到件", 1), Ev("出仓扫描", 2), Ev("问题件扫描", 3)
        });
        Assert.Equal(DkchAction.Register, d.Action);
        Assert.True(d.ShouldRegister);
    }

    [Fact]
    public void ChiVeKho_ChuaPhatLanNao_VanDangKyBatBuoc1Lan()
    {
        var d = DkchJourneyAnalyzer.Analyze(new List<WaybillDetail> { Ev("卸车到件", 1) });
        Assert.Equal(DkchAction.Register, d.Action);
    }

    // ── Chống spam: đã đăng ký thì bỏ qua ──────────────────────────────────────────

    [Fact]
    public void DaCoDangKyChuyenHoan_ThiBoQua_KhongDangKyTrung()
    {
        var d = DkchJourneyAnalyzer.Analyze(new List<WaybillDetail>
        {
            Ev("卸车到件", 1), Ev("出仓扫描", 2), Ev("问题件扫描", 3), Ev("退件登记", 4)
        });
        Assert.Equal(DkchAction.SkipAlreadyRegistered, d.Action);
        Assert.True(d.AlreadyRegisteredThisCycle);
        Assert.Equal(1, d.RegisterCount);

        // KHÔNG còn tự bỏ qua: đơn đã đăng ký vẫn được thử Lưu 1 lần, JMS sẽ tự từ chối và
        // app đổi sang "Chuyển hoàn lần 2". Chỉ 2 trạng thái CHẶN mới không bấm Lưu.
        Assert.False(d.IsBlocked);
        Assert.True(d.ShouldRegister);
    }

    [Fact]
    public void DaDangKyLan2_ThiBoQua()
    {
        var d = DkchJourneyAnalyzer.Analyze(new List<WaybillDetail>
        {
            Ev("退件登记", 1), Ev("重派", 2), Ev("出仓扫描", 3), Ev("问题件扫描", 4), Ev("再次登记", 5)
        });
        Assert.Equal(DkchAction.SkipAlreadyRegistered, d.Action);
        Assert.Equal(2, d.RegisterCount);
    }

    // ── Luật CHẶN của chủ sở hữu: "Phát lại có quét phát chưa kiện" ────────────────

    [Fact]
    public void PhatLai_CoQuetPhat_ChuaCoKienVanDe_ThiCHAN()
    {
        var d = DkchJourneyAnalyzer.Analyze(new List<WaybillDetail>
        {
            Ev("退件登记", 1), Ev("重派", 2), Ev("出仓扫描", 3)
        });
        Assert.Equal(DkchAction.BlockedPendingProblemScan, d.Action);
        Assert.True(d.IsBlocked);
        Assert.False(d.ShouldRegister);
        Assert.False(d.SelfDispatchViolation);
    }

    [Fact]
    public void CaPhatDauTien_ChuaKien_ThiVanDangKy_DeJmsTuBaoThieuCa()
    {
        // Xuống kiện -> Quét phát hàng, chưa kiện, CHƯA từng giao lại: app vẫn bấm Lưu,
        // JMS sẽ tự trả 问题件次数 nếu chưa đủ ca.
        var d = DkchJourneyAnalyzer.Analyze(new List<WaybillDetail>
        {
            Ev("卸车到件", 1), Ev("出仓扫描", 2)
        });
        Assert.Equal(DkchAction.Register, d.Action);
        Assert.Equal(0, d.RedeliverCount);
    }

    [Fact]
    public void SauKhiBiChan_CoKienVanDeMoi_ThiDuocDangKyLai()
    {
        var d = DkchJourneyAnalyzer.Analyze(new List<WaybillDetail>
        {
            Ev("退件登记", 1), Ev("重派", 2), Ev("出仓扫描", 3), Ev("问题件扫描", 4)
        });
        Assert.Equal(DkchAction.Register, d.Action);
    }

    [Fact]
    public void HaiChuKyDayDu_ChuKyMoiChuaKien_ThiCHAN()
    {
        var d = DkchJourneyAnalyzer.Analyze(new List<WaybillDetail>
        {
            Ev("出仓扫描", 1), Ev("问题件扫描", 2), Ev("退件登记", 3), Ev("重派", 4), Ev("出仓扫描", 5)
        });
        Assert.Equal(DkchAction.BlockedPendingProblemScan, d.Action);
    }

    // ── Nhãn tiếng Việt (API có thể trả về bản đã dịch) ────────────────────────────

    [Theory]
    [InlineData("Xuống hàng kiện đến", "Quét phát hàng", "Quét kiện vấn đề", DkchAction.Register)]
    [InlineData("Quét phát hàng", "Quét kiện vấn đề", "Đăng ký chuyển hoàn", DkchAction.SkipAlreadyRegistered)]
    [InlineData("Đăng ký chuyển hoàn", "Giao lại hàng", "Quét phát hàng", DkchAction.BlockedPendingProblemScan)]
    public void NhanTiengViet_ChoKetQuaGiongNhanTiengTrung(string a, string b, string c, DkchAction expected)
    {
        var d = DkchJourneyAnalyzer.Analyze(new List<WaybillDetail> { Ev(a, 1), Ev(b, 2), Ev(c, 3) });
        Assert.Equal(expected, d.Action);
    }

    // ── Không được nhận nhầm các nhãn "chuyển hoàn" khác thành "đã đăng ký" ────────

    [Theory]
    [InlineData("退件扫描")]   // In đơn chuyển hoàn
    [InlineData("退件确认")]   // Xác nhận chuyển hoàn
    [InlineData("退件签收")]   // Ký nhận chuyển hoàn
    public void CacNhanChuyenHoanKhac_KhongDuocTinhLaDaDangKy(string nhan)
    {
        var d = DkchJourneyAnalyzer.Analyze(new List<WaybillDetail>
        {
            Ev("出仓扫描", 2), Ev("问题件扫描", 3), Ev(nhan, 4)
        });
        Assert.Equal(DkchAction.Register, d.Action);
        Assert.Equal(0, d.RegisterCount);
    }

    // ── Bền vững với thứ tự mảng và thao tác nhiễu ─────────────────────────────────

    [Fact]
    public void KhongPhuThuocThuTuMangTraVe()
    {
        // Cùng dữ liệu nhưng đảo ngược thứ tự (API có thể trả mới→cũ hoặc cũ→mới).
        var moiTruocCuSau = new List<WaybillDetail> { Ev("出仓扫描", 3), Ev("重派", 2), Ev("退件登记", 1) };
        var cuTruocMoiSau = new List<WaybillDetail> { Ev("退件登记", 1), Ev("重派", 2), Ev("出仓扫描", 3) };

        Assert.Equal(
            DkchJourneyAnalyzer.Analyze(cuTruocMoiSau).Action,
            DkchJourneyAnalyzer.Analyze(moiTruocCuSau).Action);
        Assert.Equal(DkchAction.BlockedPendingProblemScan, DkchJourneyAnalyzer.Analyze(moiTruocCuSau).Action);
    }

    [Fact]
    public void ThaoTacNhieu_KhongLamLechKetQua()
    {
        var d = DkchJourneyAnalyzer.Analyze(new List<WaybillDetail>
        {
            Ev("出仓扫描", 2), Ev("库存盘点", 3), Ev("派件电联", 4), Ev("问题件扫描", 5)
        });
        Assert.Equal(DkchAction.Register, d.Action);
    }

    [Fact]
    public void NhieuSauPhatLai_VanPhaiChan()
    {
        var d = DkchJourneyAnalyzer.Analyze(new List<WaybillDetail>
        {
            Ev("重派", 1), Ev("出仓扫描", 2), Ev("库存盘点", 3), Ev("Lịch sử cuộc gọi", 4)
        });
        Assert.Equal(DkchAction.BlockedPendingProblemScan, d.Action);
    }

    // ── Luật chặn CHỈ áp dụng khi có "Giao lại hàng" trước "Quét phát hàng" ────────

    [Fact]
    public void QuetPhatDauTien_ChuaTungGiaoLaiHang_ThiVanDangKy()
    {
        // Ca phát đầu tiên, chưa có "Giao lại hàng" → không thuộc diện chặn.
        var d = DkchJourneyAnalyzer.Analyze(new List<WaybillDetail>
        {
            Ev("卸车到件", 1), Ev("出仓扫描", 2)
        });
        Assert.Equal(DkchAction.Register, d.Action);
        Assert.True(d.ShouldRegister);
    }

    [Fact]
    public void NhieuSauQuetPhatDauTien_VanDangKy()
    {
        var d = DkchJourneyAnalyzer.Analyze(new List<WaybillDetail>
        {
            Ev("出仓扫描", 2), Ev("库存盘点", 3), Ev("Lịch sử cuộc gọi", 4)
        });
        Assert.Equal(DkchAction.Register, d.Action);
    }

    [Fact]
    public void GiaoLaiHang_ChuaQuetPhat_ThiVanDangKy()
    {
        // Có "Giao lại hàng" nhưng CHƯA có "Quét phát hàng" phía sau → chưa vào ca phát.
        var d = DkchJourneyAnalyzer.Analyze(new List<WaybillDetail>
        {
            Ev("问题件扫描", 1), Ev("重派", 2)
        });
        Assert.Equal(DkchAction.Register, d.Action);
    }

    [Fact]
    public void GiaoLaiHang_QuetPhat_RoiGiaoLaiHangLanNua_ThiVanCHAN()
    {
        // "Giao lại hàng" đứng sau lần "Quét phát hàng" cuối cùng không xoá được việc lần quét
        // phát đó vẫn chưa có "Quét kiện vấn đề" → phải chặn.
        var d = DkchJourneyAnalyzer.Analyze(new List<WaybillDetail>
        {
            Ev("重派", 1), Ev("出仓扫描", 2), Ev("重派", 3)
        });
        Assert.Equal(DkchAction.BlockedPendingProblemScan, d.Action);
    }

    [Fact]
    public void QuetPhatTruocGiaoLaiHang_ThiVanDangKy()
    {
        // "Quét phát hàng" xảy ra TRƯỚC "Giao lại hàng" → không phải trạng thái bị chặn.
        var d = DkchJourneyAnalyzer.Analyze(new List<WaybillDetail>
        {
            Ev("出仓扫描", 1), Ev("重派", 2)
        });
        Assert.Equal(DkchAction.Register, d.Action);
    }

    [Fact]
    public void ChanThiBaoDungThongDiepViPhamVaThaoTacCuoi()
    {
        var d = DkchJourneyAnalyzer.Analyze(new List<WaybillDetail>
        {
            Ev("出仓扫描", 1), Ev("问题件扫描", 2), Ev("退件登记", 3),
            Ev("重派", 4), Ev("出仓扫描", 5)
        });
        Assert.Equal(DkchAction.BlockedPendingProblemScan, d.Action);
        Assert.Equal(1, d.DeliveryAttemptCount);          // 1 ca phát sau lần đăng ký gần nhất

        // Chu kỳ phát lại này CHƯA từng "Quét kiện vấn đề" -> biến thể KHÔNG có "vi phạm tự ý".
        Assert.False(d.SelfDispatchViolation);
        Assert.Contains("Chưa quét kiện vấn đề, chặn đăng ký chuyển hoàn", d.Reason);
        Assert.Equal("⛔ Chưa quét kiện vấn đề, chặn đăng ký chuyển hoàn", d.ShortHeadline);
        Assert.DoesNotContain("vi phạm tự ý", d.ShortHeadline);

        // "chưa đủ ca" là lỗi của JMS, KHÔNG phải kết luận của analyzer.
        Assert.DoesNotContain("chưa đủ ca", d.Reason);

        Assert.Equal("出仓扫描", d.LastEventType);
        Assert.Contains("出仓扫描", d.LastActionLine);
    }

    [Fact]
    public void QuetPhatLan2SauKhiDaKien_ThiVanCHAN()
    {
        // Giao lại hàng → Quét phát hàng → Quét kiện vấn đề → Quét phát hàng:
        // lần quét phát cuối chưa được kiện → chặn.
        var d = DkchJourneyAnalyzer.Analyze(new List<WaybillDetail>
        {
            Ev("重派", 1), Ev("出仓扫描", 2), Ev("问题件扫描", 3), Ev("出仓扫描", 4)
        });
        Assert.Equal(DkchAction.BlockedPendingProblemScan, d.Action);
        Assert.False(d.ShouldRegister);
        Assert.Equal(2, d.DeliveryAttemptCount);

        // Đã kiện rồi vẫn tự ý phát thêm ca -> biến thể "vi phạm tự ý chuyển hoàn".
        Assert.True(d.SelfDispatchViolation);
        Assert.Equal("⛔ Chưa quét kiện vấn đề, vi phạm tự ý chuyển hoàn, chặn đăng ký chuyển hoàn",
            d.ShortHeadline);
    }

    [Fact]
    public void DuCaPhatVaDaKien_ThiVanDangKy_DeJmsTuQuyetSoCaPhat()
    {
        // Giao lại hàng → Quét phát → Quét kiện: analyzer cho đăng ký. Nếu JMS thấy chưa đủ
        // ca phát (999010051 … 出仓次数：1/2) thì server tự từ chối, ta không đoán ngưỡng.
        var d = DkchJourneyAnalyzer.Analyze(new List<WaybillDetail>
        {
            Ev("重派", 1), Ev("出仓扫描", 2), Ev("问题件扫描", 3)
        });
        Assert.Equal(DkchAction.Register, d.Action);
        Assert.Equal(1, d.DeliveryAttemptCount);
    }

    [Fact]
    public void DemCaPhatTheoChuKyHienTai_KhongDemCaChuKyCu()
    {
        var d = DkchJourneyAnalyzer.Analyze(new List<WaybillDetail>
        {
            Ev("出仓扫描", 1), Ev("出仓扫描", 2), Ev("问题件扫描", 3), Ev("退件登记", 4),
            Ev("重派", 5), Ev("出仓扫描", 6), Ev("问题件扫描", 7)
        });
        Assert.Equal(DkchAction.Register, d.Action);
        Assert.Equal(1, d.DeliveryAttemptCount);          // chỉ tính "出仓扫描" ở giờ 6
        Assert.Equal(1, d.RegisterCount);
    }

    // ── Không đủ dữ liệu thì không được đăng ký mù ─────────────────────────────────

    [Fact]
    public void KhongCoDuLieu_ThiKhongDangKy()
    {
        foreach (var d in new[] { DkchJourneyAnalyzer.Analyze(null),
                                  DkchJourneyAnalyzer.Analyze(new List<WaybillDetail>()) })
        {
            Assert.Equal(DkchAction.BlockedNoData, d.Action);
            // Không đọc được hành trình thì không biết đơn có thuộc diện chặn hay không
            // -> vẫn phải chặn, không đăng ký mù.
            Assert.True(d.IsBlocked);
            Assert.False(d.ShouldRegister);
        }
    }

    [Fact]
    public void ChiCoThaoTacNhieu_ThiKhongDangKy()
    {
        var d = DkchJourneyAnalyzer.Analyze(new List<WaybillDetail> { Ev("库存盘点", 1), Ev("派件电联", 2) });
        Assert.Equal(DkchAction.BlockedNoData, d.Action);
    }

    // ── Xác minh sau khi Lưu (dùng khi response bị timeout) ───────────────────────

    [Fact]
    public void RegistrationLanded_PhatHienDangKyMoiPhatSinh()
    {
        var before = DkchJourneyAnalyzer.Analyze(new List<WaybillDetail>
        {
            Ev("出仓扫描", 2), Ev("问题件扫描", 3)
        });
        var after = DkchJourneyAnalyzer.Analyze(new List<WaybillDetail>
        {
            Ev("出仓扫描", 2), Ev("问题件扫描", 3), Ev("退件登记", 4)
        });

        Assert.True(DkchJourneyAnalyzer.RegistrationLanded(before, after));
        Assert.False(DkchJourneyAnalyzer.RegistrationLanded(before, before));
    }

    [Fact]
    public void RegistrationLanded_KhongCoGiThayDoi_ThiTraVeFalse()
    {
        var snapshot = DkchJourneyAnalyzer.Analyze(new List<WaybillDetail>
        {
            Ev("出仓扫描", 2), Ev("问题件扫描", 3)
        });
        Assert.False(DkchJourneyAnalyzer.RegistrationLanded(snapshot, snapshot));
    }
}

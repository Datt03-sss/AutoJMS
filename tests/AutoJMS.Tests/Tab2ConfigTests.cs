using System.Collections.Generic;
using Xunit;

namespace AutoJMS.Tests;

/// <summary>
/// Kiểm chứng bộ chọn nội dung ô kết quả tabDKCH_result đọc từ modules/tab2config.json:
/// case đầu tiên khớp sẽ thắng, placeholder được thay đúng, không khớp thì dùng fallback.
/// </summary>
public sealed class Tab2ConfigTests
{
    private static Tab2Case Case(string id, Tab2Match match, string result, string act = "", bool enabled = true)
        => new Tab2Case { Id = id, Enabled = enabled, Match = match, Result = result, ActRecommend = act };

    /// <summary>Cấu hình rút gọn phản chiếu đúng thứ tự các case thật trong tab2config.json.</summary>
    private static Tab2Config Cfg() => new Tab2Config
    {
        ActionPrefix = "→ ",
        StatsLine = "Số lần đã ĐKCH: {registerCount}   ·   Số lần phát lại: {redeliverCount}",
        Cases = new List<Tab2Case>
        {
            Case("success-dkch2",
                new Tab2Match { Phase = "afterSave", Outcomes = { "success" }, Modes = { "DKCH2" } },
                "Đã đăng ký chuyển hoàn lần 2.", "Kiểm tra trạng thái duyệt chuyển hoàn."),
            Case("success-dkch1",
                new Tab2Match { Phase = "afterSave", Outcomes = { "success" } },
                "Đã đăng ký chuyển hoàn.", "Kiểm tồn kho."),
            Case("not-enough-problem-scan",
                new Tab2Match { Outcomes = { "failed" }, MsgContains = { "问题件次数" } },
                "Đơn mới chưa đủ ca ({ratio}).", "Phát lại ngày hôm sau."),
            Case("not-enough-dispatch-scan",
                new Tab2Match { Outcomes = { "failed" }, MsgContains = { "出仓次数" } },
                "Đơn phát lại chưa đủ ca ({ratio}).", "Phát lại ngày hôm sau."),
            Case("complaint",
                new Tab2Match { Outcomes = { "failed" } },
                "Đơn dính khiếu nại.", "Điền link giải trình đơn khiếu nại.", enabled: false),
            Case("late-problem-scan",
                new Tab2Match { Phase = "beforeSave", Outcomes = { "readyToRegister" },
                                ProblemScanAfter = "17:30", Registered = "no" },
                "Đơn kiện muộn sau giờ hành chính ( {problemScanTime} ).", "Phát lại ngày hôm sau."),
            Case("blocked-violation",
                new Tab2Match { Outcomes = { "blockedViolation" } },
                "Chưa quét kiện vấn đề, vi phạm tự ý chuyển hoàn, chặn đăng ký chuyển hoàn."),
            Case("blocked",
                new Tab2Match { Outcomes = { "blocked" } },
                "Chưa quét kiện vấn đề, chặn đăng ký chuyển hoàn."),
            Case("already-registered",
                new Tab2Match { Outcomes = { "skipped" } },
                "Đã ghi nhận đăng ký chuyển hoàn ( {registerCount} lần ) — vẫn thử đăng ký."),
            Case("signed-cpn",
                new Tab2Match { Phase = "beforeSave", Outcomes = { "readyToRegister" },
                                LastActionContains = { "Ký nhận CPN", "快件签收" } },
                "Đã ký nhận.", "Thực hiện in đơn hoàn 1 phần (nếu có thể)."),
            Case("dispatch-pending-problem-scan",
                new Tab2Match { Phase = "beforeSave", Outcomes = { "readyToRegister" },
                                LastActionContains = { "Quét phát hàng", "出仓扫描" } },
                "Chưa kiện vấn đề."),
            Case("before-save-default",
                new Tab2Match { Phase = "beforeSave", Outcomes = { "readyToRegister" } },
                "{lastAction}")
        }
    };

    private static DkchResultContext Ctx(string phase = "beforeSave", string outcome = "", string mode = "DKCH1")
        => new DkchResultContext { Phase = phase, Outcome = outcome, Mode = mode, Waybill = "862086847555" };

    // ── Hai loại "chưa đủ ca" phải ra hai câu khác nhau ────────────────────────────

    [Fact]
    public void DonMoi_ThieuCaKienVanDe_RaCauDonMoi()
    {
        var ctx = Ctx("afterSave", "failed");
        ctx.JmsRawMessage = "999010052:此单的问题件次数不满足登记条件，问题件次数：1/3";
        ctx.Ratio = "1/3";

        var t = Cfg().Resolve(ctx);
        Assert.Equal("not-enough-problem-scan", t.CaseId);
        Assert.Equal("Đơn mới chưa đủ ca (1/3).", t.Result);
        Assert.Equal("Phát lại ngày hôm sau.", t.ActRecommend);
    }

    [Fact]
    public void DonGiaoLai_ThieuCaPhat_RaCauDonGiaoLai()
    {
        var ctx = Ctx("afterSave", "failed");
        ctx.JmsRawMessage = "999010051:此单的出仓次数不满足登记条件，出仓次数：1/2";
        ctx.Ratio = "1/2";
        ctx.RegisterCount = 1;

        var t = Cfg().Resolve(ctx);
        Assert.Equal("not-enough-dispatch-scan", t.CaseId);
        Assert.Equal("Đơn phát lại chưa đủ ca (1/2).", t.Result);
    }

    // ── Thành công: đề xuất khác nhau theo lần đăng ký ─────────────────────────────

    [Fact]
    public void ThanhCongLanDau_DeXuatKiemTonKho()
    {
        var ctx = Ctx("afterSave", "success", "DKCH1");
        ctx.LastAction = "Đăng ký chuyển hoàn  •  2026-07-29 16:20:00";

        var t = Cfg().Resolve(ctx);
        Assert.Equal("success-dkch1", t.CaseId);
        Assert.Equal("Đã đăng ký chuyển hoàn.", t.Result);
        Assert.Equal("Kiểm tồn kho.", t.ActRecommend);
    }

    [Fact]
    public void ThanhCongLan2_DeXuatKiemTraDuyetChuyenHoan()
    {
        var ctx = Ctx("afterSave", "success", "DKCH2");
        ctx.LastAction = "Đăng ký chuyển hoàn lần 2  •  2026-07-29 16:22:00";

        var t = Cfg().Resolve(ctx);
        Assert.Equal("success-dkch2", t.CaseId);
        Assert.Equal("Kiểm tra trạng thái duyệt chuyển hoàn.", t.ActRecommend);
    }

    // ── Các case theo thao tác cuối cùng ──────────────────────────────────────────

    [Theory]
    [InlineData("Ký nhận CPN", "signed-cpn", "Đã ký nhận.")]
    [InlineData("快件签收", "signed-cpn", "Đã ký nhận.")]
    [InlineData("Quét phát hàng", "dispatch-pending-problem-scan", "Chưa kiện vấn đề.")]
    [InlineData("出仓扫描", "dispatch-pending-problem-scan", "Chưa kiện vấn đề.")]
    public void ThaoTacCuoi_ChonDungCase(string lastActionType, string expectedCase, string expectedResult)
    {
        var ctx = Ctx(outcome: "readyToRegister");
        ctx.LastActionType = lastActionType;

        var t = Cfg().Resolve(ctx);
        Assert.Equal(expectedCase, t.CaseId);
        Assert.Equal(expectedResult, t.Result);
    }

    [Fact]
    public void BiChan_ThangCaCaseTheoThaoTacCuoi()
    {
        // Đơn bị chặn có thao tác cuối là "Quét phát hàng" — case blocked phải thắng vì đứng trên.
        var ctx = Ctx(outcome: "blocked");
        ctx.LastActionType = "Quét phát hàng";

        var t = Cfg().Resolve(ctx);
        Assert.Equal("blocked", t.CaseId);
        Assert.Equal("Chưa quét kiện vấn đề, chặn đăng ký chuyển hoàn.", t.Result);
        Assert.Equal("", t.ActRecommend);
    }

    [Fact]
    public void ViPhamTuYPhatThemCa_RaCauNangHon()
    {
        var ctx = Ctx(outcome: "blockedViolation");
        ctx.LastActionType = "Quét phát hàng";

        var t = Cfg().Resolve(ctx);
        Assert.Equal("blocked-violation", t.CaseId);
        Assert.Equal("Chưa quét kiện vấn đề, vi phạm tự ý chuyển hoàn, chặn đăng ký chuyển hoàn.", t.Result);
        Assert.Equal("", t.ActRecommend);
    }

    [Fact]
    public void DaGhiNhanDKCH_HienThaoTacCuoi()
    {
        var ctx = Ctx(outcome: "skipped");
        ctx.RegisterCount = 1;
        ctx.LastAction = "Đăng ký chuyển hoàn  •  2026-07-29 16:30:00";
        ctx.LastActionType = "Đăng ký chuyển hoàn";

        var t = Cfg().Resolve(ctx);
        Assert.Equal("already-registered", t.CaseId);
        Assert.Equal("Đã ghi nhận đăng ký chuyển hoàn ( 1 lần ) — vẫn thử đăng ký.", t.Result);
    }

    // ── enabled=false thì bỏ qua hoàn toàn ────────────────────────────────────────

    [Fact]
    public void CaseTatEnabled_KhongDuocDung()
    {
        // "complaint" khớp outcome=failed nhưng enabled=false → phải rơi xuống fallback.
        var ctx = Ctx("afterSave", "failed");
        ctx.JmsRawMessage = "123456:某个未知错误";
        ctx.JmsMessage = "某个未知错误";

        var t = Cfg().Resolve(ctx);
        Assert.Equal("fallback", t.CaseId);
        Assert.Equal("某个未知错误", t.Result);
        Assert.Equal("", t.ActRecommend);
    }

    // ── problemScanAfter: lọc theo giờ hành chính ─────────────────────────────────

    [Fact]
    public void KienMuonSauGioHanhChinh_ThiKhop()
    {
        var ctx = Ctx(outcome: "readyToRegister");
        ctx.ProblemScanTime = "2026-07-29 18:05:00";     // sau 17:30
        ctx.LastActionType = "Quét kiện vấn đề";

        var t = Cfg().Resolve(ctx);
        Assert.Equal("late-problem-scan", t.CaseId);
        Assert.Contains("2026-07-29 18:05:00", t.Result);
    }

    [Fact]
    public void KienTrongGioHanhChinh_ThiKhongKhop()
    {
        var ctx = Ctx(outcome: "readyToRegister");
        ctx.ProblemScanTime = "2026-07-29 16:01:13";     // trước 17:30
        ctx.LastAction = "Quét kiện vấn đề  •  2026-07-29 16:01:13";
        ctx.LastActionType = "Quét kiện vấn đề";

        var t = Cfg().Resolve(ctx);
        Assert.Equal("before-save-default", t.CaseId);
        Assert.Equal("Quét kiện vấn đề  •  2026-07-29 16:01:13", t.Result);
    }

    [Fact]
    public void DaDangKyRoi_ThiKhongApDungLuatKienMuon()
    {
        // late-problem-scan có registered='no'; đơn đã ĐKCH phải bỏ qua case này.
        var ctx = Ctx(outcome: "readyToRegister");
        ctx.ProblemScanTime = "2026-07-29 18:05:00";
        ctx.RegisterCount = 1;
        ctx.LastAction = "Quét kiện vấn đề  •  2026-07-29 18:05:00";
        ctx.LastActionType = "Quét kiện vấn đề";

        var t = Cfg().Resolve(ctx);
        Assert.Equal("before-save-default", t.CaseId);
    }

    // ── Placeholder ──────────────────────────────────────────────────────────────

    [Fact]
    public void ThayDuMoiPlaceholder()
    {
        var cfg = new Tab2Config
        {
            Cases = new List<Tab2Case>
            {
                Case("all", new Tab2Match(),
                    "{waybill}|{ratio}|{code}|{mode}|{registerCount}|{redeliverCount}|{dispatchCount}",
                    "{problemScanReason}|{errorMessage}|{rawMessage}")
            }
        };

        var ctx = new DkchResultContext
        {
            Waybill = "862086847555", Ratio = "1/2", JmsCode = "999010051", Mode = "DKCH2",
            RegisterCount = 1, RedeliverCount = 2, DispatchCount = 3,
            ProblemScanReason = "Không liên lạc được", ErrorMessage = "boom", JmsRawMessage = "raw-msg"
        };

        var t = cfg.Resolve(ctx);
        Assert.Equal("862086847555|1/2|999010051|DKCH2|1|2|3", t.Result);
        Assert.Equal("Không liên lạc được|boom|raw-msg", t.ActRecommend);
    }

    // ── statsLine: 2 con số trên CÙNG 1 dòng, chỉ khi có lịch sử hành trình ───────

    [Fact]
    public void CoLichSuHanhTrinh_ThiVeDongThongKeGopChung()
    {
        var ctx = Ctx(outcome: "skipped");
        ctx.HasJourney = true;
        ctx.RegisterCount = 1;
        ctx.RedeliverCount = 2;
        ctx.LastAction = "Đăng ký chuyển hoàn  •  2026-07-29 16:30:00";

        var t = Cfg().Resolve(ctx);
        Assert.Equal("Số lần đã ĐKCH: 1   ·   Số lần phát lại: 2", t.Stats);
        Assert.DoesNotContain("\n", t.Stats);          // phải nằm trên 1 dòng
    }

    [Fact]
    public void ChuaDocDuocLichSu_ThiKhongVeDongThongKe()
    {
        // HasJourney=false (vd lỗi thao tác, không có dữ liệu) -> tránh hiện "0 · 0" gây hiểu nhầm.
        var ctx = Ctx("afterSave", "error");
        ctx.ErrorMessage = "Không tìm thấy nút Lưu.";

        var t = Cfg().Resolve(ctx);
        Assert.Equal("", t.Stats);
    }

    [Fact]
    public void KhongCoCaseNao_ThiDungFallbackMacDinh()
    {
        var cfg = new Tab2Config();                       // rỗng
        var ctx = Ctx("afterSave", "failed");
        ctx.JmsMessage = "Lỗi gì đó";

        var t = cfg.Resolve(ctx);
        Assert.Equal("fallback", t.CaseId);
        Assert.Equal("Lỗi gì đó", t.Result);
    }

    [Fact]
    public void CauHinhMacDinh_LuonCoCaseChoThanhCongVaBiChan()
    {
        var cfg = Tab2Config.Default();

        var success = cfg.Resolve(new DkchResultContext
        {
            Phase = "afterSave", Outcome = "success", LastAction = "Đăng ký chuyển hoàn"
        });
        // Dòng 1 của ô kết quả là thao tác cuối cùng, nên result mặc định là câu thông báo,
        // KHÔNG phải {lastAction} (sẽ bị lặp với dòng 1).
        Assert.Equal("Đã đăng ký chuyển hoàn.", success.Result);
        Assert.Equal("Kiểm tồn kho.", success.ActRecommend);

        var blocked = cfg.Resolve(new DkchResultContext { Phase = "beforeSave", Outcome = "blocked" });
        Assert.Equal("Chưa quét kiện vấn đề, chặn đăng ký chuyển hoàn.", blocked.Result);
    }
}

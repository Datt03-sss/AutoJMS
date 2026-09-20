using System;
using System.Linq;
using Xunit;

namespace AutoJMS.Tests;

/// <summary>
/// Lịch sử in của tab In Reverse. Đây là nơi DUY NHẤT biết về những lượt in đi đường bản xem
/// trước: chúng chạy <c>printMode=1</c> nên <c>printsNumber</c> của JMS không hề nhúc nhích.
/// Đọc sai là cột "Số bản in" nói dối, dọn sai là mất luôn lịch sử của ca đang làm.
/// </summary>
public sealed class ReversePrintLedgerTests
{
    private static readonly DateTime Now = new(2026, 9, 20, 14, 32, 11);

    // ── định dạng dòng ────────────────────────────────────────────────────────────

    [Fact]
    public void FormatLine_DungDungCauOwnerChot()
    {
        Assert.Equal(
            "2026-09-20 14:32:11\t845123456789\tđã in 4 lần",
            ReversePrintLedger.FormatLine(Now, "845123456789", 4));
    }

    [Fact]
    public void FormatLineRoiDocLai_TraLaiDungSoBanIn()
    {
        var counts = ReversePrintLedger.CountByWaybill(new[]
        {
            ReversePrintLedger.FormatLine(Now, "845000000001", 3),
            ReversePrintLedger.FormatLine(Now, "845000000002", 12),
        });

        Assert.Equal(3, counts["845000000001"]);
        Assert.Equal(12, counts["845000000002"]);
    }

    // ── đọc số bản in ─────────────────────────────────────────────────────────────

    [Fact]
    public void CountByWaybill_LaySoLONNHATChuKhongPhaiSoDONG()
    {
        // n của mỗi dòng đã là tổng — nó tính cả những lượt JMS in trước khi app thấy mã này.
        // Đếm số dòng thì một mã JMS đã in 3 lần rồi app in thêm 1 sẽ ra 1 thay vì 4.
        var counts = ReversePrintLedger.CountByWaybill(new[]
        {
            ReversePrintLedger.FormatLine(Now.AddMinutes(-2), "845123456789", 4),
            ReversePrintLedger.FormatLine(Now, "845123456789", 5),
        });

        Assert.Equal(5, counts["845123456789"]);
    }

    [Theory]
    [InlineData("2026-09-20 14:32:11\t845123456789")]        // thiếu cột chữ
    [InlineData("2026-09-20 14:32:11\t\tđã in 4 lần")]       // thiếu mã
    [InlineData("2026-09-20 14:32:11\t845123456789\tđã in")] // không có số nào
    [InlineData("rác")]
    public void CountByWaybill_DongHongThiBoQuaChuKhongNem(string line)
    {
        Assert.Empty(ReversePrintLedger.CountByWaybill(new[] { line }));
    }

    // ── dọn theo ngày ─────────────────────────────────────────────────────────────

    [Fact]
    public void Prune_BoDongQuaHanVaGiuDongConHan()
    {
        DateTime cutoff = Now.Date.AddDays(-7);
        var kept = ReversePrintLedger.Prune(new[]
        {
            ReversePrintLedger.FormatLine(Now.AddDays(-8), "845000000001", 1),
            ReversePrintLedger.FormatLine(Now.AddDays(-7), "845000000002", 1),
            ReversePrintLedger.FormatLine(Now, "845000000003", 1),
        }, cutoff);

        // Đúng mốc thì GIỮ — so theo ngày, không theo giờ, nên dòng lúc 14h của ngày thứ bảy
        // không được rơi ra chỉ vì lượt dọn chạy lúc 15h.
        Assert.Equal(
            new[] { "845000000002", "845000000003" },
            ReversePrintLedger.CountByWaybill(kept).Keys.OrderBy(k => k, StringComparer.Ordinal));
    }

    [Fact]
    public void Prune_BoDongChuThichVaDongRong()
    {
        var kept = ReversePrintLedger.Prune(new[]
        {
            "# Lịch sử in của tab IN ĐƠN > In Reverse — app tự ghi, đừng sửa tay.",
            "",
            ReversePrintLedger.FormatLine(Now, "845123456789", 2),
        }, Now.Date.AddDays(-7));

        Assert.Equal(new[] { ReversePrintLedger.FormatLine(Now, "845123456789", 2) }, kept);
    }

    [Fact]
    public void Prune_DongKhongDocDuocGioThiBoDi()
    {
        // Không có mốc thời gian thì không bao giờ dọn được — để lại là file lớn mãi.
        Assert.Empty(ReversePrintLedger.Prune(
            new[] { "hôm qua\t845123456789\tđã in 2 lần" }, Now.Date.AddDays(-7)));
    }
}

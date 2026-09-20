using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace AutoJMS.Tests;

/// <summary>
/// Sổ lượt in của tab In Reverse. Sổ này là thứ DUY NHẤT biết về lượt in thứ 4 và thứ 5:
/// hai lượt đó đi đường <c>printMode=1</c> nên JMS không đếm, và nếu sổ đọc sai thì hoặc
/// người dùng bị chặn sớm, hoặc in vượt trần mà không ai hay.
/// </summary>
public sealed class ReversePrintLedgerTests
{
    private static readonly DateTime Now = new(2026, 9, 20, 14, 32, 11);

    // ── đọc ───────────────────────────────────────────────────────────────────────

    [Fact]
    public void Parse_BoQuaChuThichVaDongTieuDe()
    {
        // Format() ghi ba dòng "#" rồi tới dòng tiêu đề; Parse() phải đọc lại được chính nó
        // mà không đếm nhầm "waybillNo" thành một vận đơn.
        var rows = ReversePrintLedger.Parse(new[]
        {
            "# Sổ lượt in của tab IN ĐƠN > In Reverse — app tự ghi, đừng sửa tay.",
            "waybillNo\tprintCount\tlastPrintedAt",
            "",
            "845123456789\t4\t2026-09-20 14:32:11",
        });

        Assert.Equal(new[] { "845123456789" }, rows.Keys);
        Assert.Equal(4, rows["845123456789"].Count);
        Assert.Equal(Now, rows["845123456789"].At);
    }

    [Theory]
    [InlineData("845123456789")]                 // thiếu hẳn cột số
    [InlineData("845123456789\t")]               // cột số rỗng
    [InlineData("845123456789\tbốn")]            // cột số không phải số
    [InlineData("\t4\t2026-09-20 14:32:11")]     // thiếu mã
    public void Parse_DongHongThiBoQuaChuKhongNem(string line)
    {
        Assert.Empty(ReversePrintLedger.Parse(new[] { line }));
    }

    [Fact]
    public void Parse_KhongDocDuocNgayThiVanGiuSoBanIn()
    {
        // Ngày chỉ dùng để dọn dòng cũ. Mất ngày mà vứt luôn cả dòng là mở lại hai lượt in.
        var rows = ReversePrintLedger.Parse(new[] { "845123456789\t5\trác" });

        Assert.Equal(5, rows["845123456789"].Count);
        Assert.Equal(default, rows["845123456789"].At);
    }

    // ── ghi ───────────────────────────────────────────────────────────────────────

    [Fact]
    public void FormatRoiParse_TraLaiDungSoBanInVaThoiDiem()
    {
        var before = new Dictionary<string, (int Count, DateTime At)>(StringComparer.OrdinalIgnoreCase)
        {
            ["845000000002"] = (5, Now),
            ["845000000001"] = (3, Now.AddHours(-2)),
        };

        var after = ReversePrintLedger.Parse(ReversePrintLedger.Format(before));

        Assert.Equal(before, after);
    }

    [Fact]
    public void Format_XepTheoMaVaGiuDungBaCot()
    {
        var lines = ReversePrintLedger.Format(new Dictionary<string, (int, DateTime)>
        {
            ["845000000002"] = (5, Now),
            ["845000000001"] = (3, Now),
        }).ToList();

        var data = lines.Where(l => !l.StartsWith("#") && !l.StartsWith("waybillNo")).ToList();
        Assert.Equal(
            new[] { "845000000001\t3\t2026-09-20 14:32:11", "845000000002\t5\t2026-09-20 14:32:11" },
            data);
    }

    // ── chọn đường in ─────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void NextPrintMode_TrongBaLuotDauThiDiDuongInThat(int printedSoFar)
    {
        // Ba lượt đầu phải để JMS đếm: đi tắt ngay từ lượt một là mất luôn mốc đối chiếu với
        // printsNumber mà JMS trả về ở lượt tra sau.
        Assert.Equal(
            JmsSendWaybillService.CenterPrintModePrint,
            ReversePrintLedger.NextPrintMode(printedSoFar));
    }

    [Theory]
    [InlineData(3)]
    [InlineData(4)]
    public void NextPrintMode_HetLuotJmsThiDiDuongXemTruoc(int printedSoFar)
    {
        Assert.Equal(
            JmsSendWaybillService.CenterPrintModePreview,
            ReversePrintLedger.NextPrintMode(printedSoFar));
    }

    [Fact]
    public void GioiHan_LaBaLuotJmsCongHaiLuotXemTruoc()
    {
        // Chốt bằng số: đổi MaxPrints mà quên JmsPrintLimit là lặng lẽ cho in thêm/bớt lượt.
        Assert.Equal(3, ReversePrintLedger.JmsPrintLimit);
        Assert.Equal(5, ReversePrintLedger.MaxPrints);
    }
}

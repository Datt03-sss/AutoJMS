using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace AutoJMS.Tests;

/// <summary>
/// Hai lớp lọc đứng giữa <c>sysStaff/selectAll</c> và ô "Tên nhân viên" của tab In Reverse:
/// khớp đúng chữ đã gõ, rồi bỏ người đã nghỉ. JMS không làm giúp cái nào — nó khớp LIKE và
/// không đánh dấu người đã nghỉ việc.
/// </summary>
public sealed class StaffFilterTests
{
    private static JmsSendWaybillService.StaffInfo S(string code, string name) =>
        new() { Code = code, Name = name };

    private static readonly IReadOnlyList<JmsSendWaybillService.StaffInfo> Sample = new[]
    {
        S("01", "Thàn Văn Đạt"),
        S("02", "Nguyễn Văn Thành"),
        S("03", "Lê Thàng"),
        S("04", "Đinh Thị Chinh"),
    };

    // ── khớp đúng chữ đã gõ ───────────────────────────────────────────────────────

    [Fact]
    public void KeepExactName_GoMotTieng_ChiLayTenCoTrondungTiengDo()
    {
        // Đây là ca Owner nêu: "Thàn" là tiền tố của cả "Thành" lẫn "Thàng" nên JMS trả về
        // cả ba. Chỉ người tên đúng "Thàn" được giữ lại.
        var kept = JmsSendWaybillService.KeepExactName(Sample, "Thàn ");

        var one = Assert.Single(kept);
        Assert.Equal("Thàn Văn Đạt", one.Name);
        Assert.Equal("01", one.Code);
    }

    [Fact]
    public void KeepExactName_GoDoMotTieng_ThiChuaAiKhop()
    {
        // Hệ quả cố ý của luật "trùng trọn tiếng": gõ dở thì chưa gợi ý ai.
        Assert.Empty(JmsSendWaybillService.KeepExactName(Sample, "Thà"));
    }

    [Fact]
    public void KeepExactName_NhieuTieng_PhaiCoDuCaHai()
    {
        Assert.Equal("Đinh Thị Chinh",
            Assert.Single(JmsSendWaybillService.KeepExactName(Sample, "chinh đinh")).Name);
        Assert.Empty(JmsSendWaybillService.KeepExactName(Sample, "Đinh Chinh Thàn"));
    }

    [Theory]
    [InlineData("Than")]   // mất dấu
    [InlineData("Thân")]   // sai dấu
    public void KeepExactName_SaiDau_ThiKhongKhop(string query)
    {
        Assert.Empty(JmsSendWaybillService.KeepExactName(Sample, query));
    }

    [Fact]
    public void KeepExactName_ORong_ThiGiuNguyenDanhSach()
    {
        Assert.Equal(Sample.Count, JmsSendWaybillService.KeepExactName(Sample, "   ").Count);
    }

    // ── danh sách nhân viên đang làm việc ─────────────────────────────────────────

    [Fact]
    public void KeepActive_FileRong_ThiKhongLoc()
    {
        // Trạng thái lúc vừa tạo file: chưa bổ sung ai thì mọi người vẫn hiện ra.
        var lines = new[] { "# chỉ có chú thích", "", "   " };

        Assert.Equal(Sample.Count, ActiveStaffRoster.KeepActive(Sample, lines).Count);
    }

    [Fact]
    public void KeepActive_NhanCaMa_CaTen_LanCapMaTen()
    {
        var lines = new[]
        {
            "01",                      // chỉ mã
            "Nguyễn Văn Thành",        // chỉ tên
            "04|Đinh Thị Chinh",       // cả hai
            "# 03 đã nghỉ",            // chú thích, không tính
        };

        var kept = ActiveStaffRoster.KeepActive(Sample, lines);

        Assert.Equal(new[] { "01", "02", "04" }, kept.Select(s => s.Code));
    }

    [Fact]
    public void KeepActive_ChuThichCuoiDong_KhongDinhVaoGiaTri()
    {
        var lines = new[] { "01   # Đạt, tổ 2" };

        Assert.Equal("01", Assert.Single(ActiveStaffRoster.KeepActive(Sample, lines)).Code);
    }
}

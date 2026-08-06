using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace AutoJMS.Tests;

/// <summary>
/// Kiểm chứng phần cấu hình phụ thuộc NGÔN NGỮ của trang JMS: nhãn dropdown "Loại đơn", nút Lưu,
/// tiêu đề panel cần thu gọn, và từ khoá nhận diện thông điệp lỗi.
/// <para>
/// Yêu cầu cốt lõi: một bản build phải chạy được với CẢ tiếng Việt VÀ tiếng Trung, và khi JMS đổi
/// nhãn thì chỉ cần sửa <c>modules/tab2config.json</c> — không build lại app.
/// </para>
/// </summary>
public sealed class Tab2ConfigI18nTests
{
    // ── Dropdown "Loại đơn" / 申请类型 ─────────────────────────────────────────────

    [Fact]
    public void MacDinh_DropdownCoDuCaTiengVietVaTiengTrung()
    {
        var cfg = new Tab2Config();   // chưa có cấu hình -> phải rơi về mặc định trong code

        var dkch1 = cfg.DropdownOptionsFor("DKCH1");
        Assert.Contains("Từ chối", dkch1);        // nhãn hiện tại
        Assert.Contains("Chuyển hoàn", dkch1);    // nhãn cũ — bản build vẫn chạy được ở cả hai phía
        Assert.Contains("退回", dkch1);            // tiếng Trung

        var dkch2 = cfg.DropdownOptionsFor("DKCH2");
        Assert.Contains("Chuyển hoàn lần 2", dkch2);
        Assert.Contains("二次退件", dkch2);
    }

    [Theory]
    [InlineData("DKCH1")]
    [InlineData("dkch1")]
    [InlineData("Dkch1")]
    public void KhoaModeKhongPhanBietChuHoaChuThuong(string key)
    {
        Assert.Contains("退回", new Tab2Config().DropdownOptionsFor(key));
    }

    [Fact]
    public void ModeLaKhongBiet_ThiCoiLaDkch1()
    {
        var cfg = new Tab2Config();
        Assert.Equal(cfg.DropdownOptionsFor("DKCH1"), cfg.DropdownOptionsFor("gì đó lạ"));
    }

    [Fact]
    public void CauHinhDeTruoc_MacDinhDeSau_GiuThuTuUuTien()
    {
        var cfg = new Tab2Config
        {
            DropdownOptions = new Dictionary<string, List<string>>
            {
                ["DKCH1"] = new List<string> { "Nhãn JMS mới" }
            }
        };

        var list = cfg.DropdownOptionsFor("DKCH1");
        Assert.Equal("Nhãn JMS mới", list[0]);
    }

    [Fact]
    public void CauHinhRong_ThiVanDungMacDinh_KhongTraVeDanhSachRong()
    {
        var cfg = new Tab2Config
        {
            DropdownOptions = new Dictionary<string, List<string>>
            {
                ["DKCH1"] = new List<string> { "", "   ", null }
            }
        };

        var list = cfg.DropdownOptionsFor("DKCH1");
        Assert.NotEmpty(list);
        Assert.Contains("退回", list);
    }

    // ── Nút Lưu / 保存并新增 ───────────────────────────────────────────────────────

    [Fact]
    public void NutLuu_CoDuCaHaiNgonNgu()
    {
        var titles = new Tab2Config().SaveButtonTitleList();
        Assert.Contains("Lưu và thêm mới", titles);
        Assert.Contains("保存并新增", titles);
    }

    [Fact]
    public void NutLuu_ThemNhanMoi_KhongLamMatNhanCu()
    {
        // Cấu hình được GỘP với mặc định — người dùng thêm chuỗi mới mà không vô tình
        // làm mất các chuỗi đang hoạt động.
        var cfg = new Tab2Config { SaveButtonTitles = new List<string> { "Save and add" } };

        var titles = cfg.SaveButtonTitleList();
        Assert.Equal("Save and add", titles[0]);
        Assert.Contains("Lưu và thêm mới", titles);
        Assert.Contains("保存并新增", titles);
    }

    [Fact]
    public void KhongTrungLapKhiCauHinhGhiLaiNhanDaCoTrongMacDinh()
    {
        var cfg = new Tab2Config { SaveButtonTitles = new List<string> { "保存并新增" } };

        var titles = cfg.SaveButtonTitleList();
        Assert.Single(titles.Where(x => x == "保存并新增"));
    }

    // ── Panel cần thu gọn ────────────────────────────────────────────────────────

    [Fact]
    public void PanelThuGon_CoDuCaHaiNgonNgu()
    {
        var headers = new Tab2Config().CollapseHeaderList();
        Assert.Contains("Thông tin người gửi", headers);
        Assert.Contains("hóa đơn gốc", headers);
        Assert.Contains("原单收寄件人信息", headers);
        Assert.Contains("新单收寄件人信息", headers);
    }

    // ── Từ khoá thông điệp lỗi ───────────────────────────────────────────────────

    [Fact]
    public void TuKhoaThongDiep_CoDuCaHaiNgonNgu()
    {
        var cfg = new Tab2Config();

        var needDkch1 = cfg.MessageKeys("needDkch1");
        Assert.Contains("Chưa có", needDkch1);
        Assert.Contains("二次退件", needDkch1);

        var noData = cfg.MessageKeys("noData");
        Assert.Contains("Vận đơn không tồn tại", noData);
        Assert.Contains("运单不存在", noData);
    }

    [Fact]
    public void NhomTuKhoaKhongTonTai_ThiTraVeDanhSachRong()
    {
        Assert.Empty(new Tab2Config().MessageKeys("nhóm không có"));
        Assert.Empty(new Tab2Config().MessageKeys(null));
    }

    [Fact]
    public void ThemTuKhoaChoNgonNguKhac_VanGiuTuKhoaMacDinh()
    {
        var cfg = new Tab2Config
        {
            JmsMessages = new Dictionary<string, List<string>>
            {
                ["noData"] = new List<string> { "waybill not found" }
            }
        };

        var noData = cfg.MessageKeys("noData");
        Assert.Equal("waybill not found", noData[0]);
        Assert.Contains("运单不存在", noData);
        Assert.Contains("Vận đơn không tồn tại", noData);
    }

    // ── Chuỗi "chưa đủ ca phát" nhận theo mã nghiệp vụ, không theo ngôn ngữ UI ───

    [Theory]
    [InlineData("{\"succ\":false,\"msg\":\"999010051:此单的出仓次数不满足登记条件，出仓次数：1/2\"}", "1/2")]
    [InlineData("{\"succ\":false,\"msg\":\"999010052:此单的问题件次数不满足登记条件，问题件次数：1/3\"}", "1/3")]
    public void ChuaDuCaPhat_NhanDienDuocKhiJmsChayTiengTrung(string json, string ratio)
    {
        Assert.True(WebViewAutomation.IsKnownBusinessRejection(json));
        Assert.Equal(ratio, WebViewAutomation.ExtractRatio(json));
    }
}

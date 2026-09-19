using System.IO;
using Xunit;
using AutoJMS.FullStack.LocalDb;

namespace AutoJMS.Tests;

// ApplyLicenseMiddleCode ghi xuống cả AppConfig (secure file) lẫn SettingsManager
// (AutoJMS.json). Constructor chụp giá trị gốc, Dispose() khôi phục đầy đủ — kể cả
// khi assertion giữa chừng thất bại — để config thật của máy không bị clobber.
[Collection("SiteContext")]
public sealed class SiteIsolationTests : IDisposable
{
    private readonly string _origActionSiteCode;
    private readonly string _origMiddleCode;
    private readonly List<string> _origMiddleCodeAliases;
    private readonly List<string> _origSiteNameAliases;

    public SiteIsolationTests()
    {
        // AppConfig.SaveCurrent() ghi vào AppPaths.SecureDir; tạo thư mục trước để
        // tránh DirectoryNotFoundException khi chạy từ thư mục binary test sạch.
        Directory.CreateDirectory(AppPaths.SecureDir);

        _origActionSiteCode = AppConfig.Current.ActionSiteCode;
        var settings = SettingsManager.Load();
        // Chụp bản sao — không phải reference — để code test không thể mutate chúng.
        _origMiddleCode = settings.MiddleCode;
        _origMiddleCodeAliases = new List<string>(settings.MiddleCodeAliases);
        _origSiteNameAliases = new List<string>(settings.SiteNameAliases);
    }

    public void Dispose()
    {
        AppConfig.Current.ActionSiteCode = _origActionSiteCode;
        AppConfig.SaveCurrent();
        var settings = SettingsManager.Load();
        settings.MiddleCode = _origMiddleCode;
        settings.MiddleCodeAliases = _origMiddleCodeAliases;
        settings.SiteNameAliases = _origSiteNameAliases;
        SettingsManager.Save(settings);
        SiteContextProvider.InvalidateCache();
    }

    // Xác minh: chuyển trạm → aliases và tên học được từ trạm cũ bị xoá sạch.
    // Test này sẽ fail nếu ApplyLicenseMiddleCode không reset MiddleCodeAliases/SiteNameAliases
    // khi oldCode != newCode.
    [Fact]
    public void StationChange_ResetsAliasesAndNames()
    {
        SiteContextProvider.ApplyLicenseMiddleCode("214A02");
        // Học tên trạm từ dữ liệu J&T (IsHomeStation persist tên mới vào AutoJMS.json).
        Assert.True(SiteContextProvider.IsHomeStation("214A02", "Bưu cục Kim Tân"));

        SiteContextProvider.ApplyLicenseMiddleCode("214A03");

        var settings = SettingsManager.Load();
        Assert.Equal("214A03", settings.MiddleCode);
        Assert.Equal(new List<string> { "214A03" }, settings.MiddleCodeAliases);
        Assert.Empty(settings.SiteNameAliases);
        Assert.False(SiteContextProvider.IsHomeStation("214A02", "Bưu cục Kim Tân"));
        Assert.True(SiteContextProvider.IsHomeStation("214A03", ""));
    }

    // Xác minh: apply lại cùng code (mô phỏng khởi động lại app) không xoá tên đã học.
    // Test này sẽ fail nếu code else-branch trong ApplyLicenseMiddleCode xoá SiteNameAliases.
    [Fact]
    public void ReApplySameCode_KeepsLearnedNames()
    {
        SiteContextProvider.ApplyLicenseMiddleCode("214A02");
        Assert.True(SiteContextProvider.IsHomeStation("214A02", "Bưu cục Kim Tân"));

        // Gọi lại cùng code — IsHomeStation đã persist "Bưu cục Kim Tân" vào disk,
        // nên apply cùng code không được reset SiteNameAliases.
        SiteContextProvider.ApplyLicenseMiddleCode("214A02");

        var settings = SettingsManager.Load();
        Assert.Contains("Bưu cục Kim Tân", settings.SiteNameAliases);
    }

    // Xác minh: đường dẫn database phân vùng theo mã bưu cục.
    // Test này sẽ fail nếu FullStackLocalDbPaths.GetDatabasePath không dùng SiteContextProvider.Get()
    // để xác định thư mục, hoặc nếu GetDatabasePath dùng "default" cho mọi mã.
    [Fact]
    public void DatabasePath_PartitionedBySiteCode()
    {
        SiteContextProvider.ApplyLicenseMiddleCode("214A02");
        var path214A02 = new FullStackDbConnectionFactory().DatabasePath;

        SiteContextProvider.ApplyLicenseMiddleCode("214A03");
        var path214A03 = new FullStackDbConnectionFactory().DatabasePath;

        Assert.Contains("214A02", path214A02);
        Assert.Contains("214A03", path214A03);
        Assert.NotEqual(path214A02, path214A03);

        SiteContextProvider.ApplyLicenseMiddleCode("");
        var pathEmpty = new FullStackDbConnectionFactory().DatabasePath;
        Assert.Contains("default", pathEmpty);
    }
}

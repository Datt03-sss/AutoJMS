using System;
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

    // Xac minh: ma buu cuc "." khong ra duong dan thu muc chia se goc (FullStack\<filename>).
    // Path.GetInvalidFileNameChars() khong co '.'; neu khong co Trim('.') thi "." se thanh
    // FullStack\.\journey_history.db — Path.GetFullPath thu gon "." thanh FullStack\journey_history.db
    // nen so sanh raw string la vo nghia; can GetFullPath ca hai phia moi bat duoc lo hong.
    [Fact]
    public void DotSiteCode_DoesNotResolveToLegacySharedFolder()
    {
        SiteContextProvider.ApplyLicenseMiddleCode(".");
        var path = FullStackLocalDbPaths.GetDatabasePath("journey_history.db");
        var legacyPath = System.IO.Path.Combine(AppPaths.UserDataDir, "FullStack", "journey_history.db");
        Assert.NotEqual(
            System.IO.Path.GetFullPath(legacyPath),
            System.IO.Path.GetFullPath(path),
            StringComparer.OrdinalIgnoreCase);
    }

    // Xac minh: ma buu cuc ".." khong thoat ra ngoai thu muc FullStack (path traversal).
    // ".." se tao FullStack\..\journey_history.db = AppData\journey_history.db neu khong co Trim('.').
    [Fact]
    public void DotDotSiteCode_DoesNotEscapeFullStackFolder()
    {
        SiteContextProvider.ApplyLicenseMiddleCode("..");
        var path = FullStackLocalDbPaths.GetDatabasePath("journey_history.db");
        var escapedPath = System.IO.Path.Combine(AppPaths.UserDataDir, "journey_history.db");
        Assert.NotEqual(
            System.IO.Path.GetFullPath(escapedPath),
            System.IO.Path.GetFullPath(path),
            StringComparer.OrdinalIgnoreCase);
    }

    // Xac minh: file cu duoc chuyen sang duong dan moi khi file dich chua ton tai.
    // Test se fail neu MigrateLegacyIfNeeded khong goi File.Move hoac khong tao thu muc dich.
    [Fact]
    public void LegacyMigration_MovesFile_WhenTargetAbsent()
    {
        SiteContextProvider.ApplyLicenseMiddleCode("214A02");

        var tempRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        try
        {
            // Tao file cu tai duong dan legacy (khong co thu muc buu cuc)
            string legacyDir = Path.Combine(tempRoot, "FullStack");
            Directory.CreateDirectory(legacyDir);
            string legacyDb = Path.Combine(legacyDir, "journey_history.db");
            File.WriteAllText(legacyDb, "db-content");
            File.WriteAllText(legacyDb + "-wal", "wal-content");

            string targetDb = Path.Combine(tempRoot, "FullStack", "214A02", "journey_history.db");

            FullStackLocalDbPaths.MigrateLegacyIfNeeded(targetDb, tempRoot);

            Assert.True(File.Exists(targetDb), "File cu phai duoc chuyen sang duong dan theo buu cuc");
            Assert.True(File.Exists(targetDb + "-wal"), "WAL phai duoc chuyen cung voi DB chinh");
            Assert.False(File.Exists(legacyDb), "File cu phai bien mat sau khi chuyen thanh cong");
            Assert.False(File.Exists(legacyDb + "-wal"), "WAL cu phai bien mat sau khi chuyen");
        }
        finally
        {
            if (Directory.Exists(tempRoot)) Directory.Delete(tempRoot, recursive: true);
        }
    }

    // Xac minh: file cu KHONG bi chuyen khi file dich da ton tai.
    // Test se fail neu MigrateLegacyIfNeeded ghi de len file dich hien co.
    [Fact]
    public void LegacyMigration_DoesNotMoveFile_WhenTargetExists()
    {
        SiteContextProvider.ApplyLicenseMiddleCode("214A02");

        var tempRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        try
        {
            string legacyDir = Path.Combine(tempRoot, "FullStack");
            Directory.CreateDirectory(legacyDir);
            string legacyDb = Path.Combine(legacyDir, "journey_history.db");
            File.WriteAllText(legacyDb, "legacy");

            string targetDir = Path.Combine(tempRoot, "FullStack", "214A02");
            Directory.CreateDirectory(targetDir);
            string targetDb = Path.Combine(targetDir, "journey_history.db");
            File.WriteAllText(targetDb, "new-data");

            FullStackLocalDbPaths.MigrateLegacyIfNeeded(targetDb, tempRoot);

            // Ca hai file phai giu nguyen — file dich khong duoc bi ghi de
            Assert.Equal("legacy", File.ReadAllText(legacyDb));
            Assert.Equal("new-data", File.ReadAllText(targetDb));
        }
        finally
        {
            if (Directory.Exists(tempRoot)) Directory.Delete(tempRoot, recursive: true);
        }
    }

    // Xac minh: file cu KHONG bi chuyen khi siteCode rong (thu muc "default").
    // Test se fail neu MigrateLegacyIfNeeded bo qua kiem tra siteCode va chuyen vao "default".
    [Fact]
    public void LegacyMigration_DoesNotMoveFile_WhenSiteCodeBlank()
    {
        // Xoa siteCode de SiteContextProvider.Get() tra ve ""
        AppConfig.Current.ActionSiteCode = "";
        AppConfig.SaveCurrent();
        var s = SettingsManager.Load();
        s.MiddleCode = "";
        SettingsManager.Save(s);
        SiteContextProvider.InvalidateCache();

        var tempRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        try
        {
            string legacyDir = Path.Combine(tempRoot, "FullStack");
            Directory.CreateDirectory(legacyDir);
            string legacyDb = Path.Combine(legacyDir, "journey_history.db");
            File.WriteAllText(legacyDb, "legacy");

            string targetDb = Path.Combine(tempRoot, "FullStack", "default", "journey_history.db");

            FullStackLocalDbPaths.MigrateLegacyIfNeeded(targetDb, tempRoot);

            // File cu phai giu nguyen — khong duoc chuyen vao thu muc "default"
            Assert.True(File.Exists(legacyDb));
            Assert.False(File.Exists(targetDb));
        }
        finally
        {
            if (Directory.Exists(tempRoot)) Directory.Delete(tempRoot, recursive: true);
        }
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

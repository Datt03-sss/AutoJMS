#nullable enable
using System;
using System.IO;

namespace AutoJMS
{
    /// <summary>
    /// Centralized path management for AutoJMS.
    ///
    /// Layout (default install dir = {localappdata}\AutoJMS, or any path chosen during setup):
    ///
    ///   {InstallRoot}\
    ///     ├── current\        ← Velopack-managed app binaries
    ///     │                     (AppContext.BaseDirectory points here)
    ///     ├── packages\       ← Velopack .nupkg cache
    ///     ├── AutoJMS.exe     ← Velopack stub launcher
    ///     └── AppData\        ← all user/runtime data lives here
    ///           ├── logs\debug.log
    ///           ├── secure\
    ///           ├── cache\
    ///           ├── Downloads\
    ///           └── AutoJMS.json
    ///
    /// Why AppData lives next to (not inside) `current\`:
    ///   Velopack DELETES `current\` on every update. Putting data outside
    ///   `current\` keeps it intact across updates while staying inside the
    ///   user-chosen install directory (no scattering to %LocalAppData%).
    /// </summary>
    public static class AppPaths
    {
        /// <summary>The folder containing AutoJMS.exe at runtime (Velopack: ...\current).</summary>
        public static string InstallDir => AppContext.BaseDirectory;

        /// <summary>
        /// The user-chosen install root (parent of `current`).
        /// E.g. D:\AutoJMS when AppContext.BaseDirectory = D:\AutoJMS\current.
        /// Falls back to <see cref="InstallDir"/> when not running in a Velopack layout.
        /// </summary>
        public static string InstallRoot { get; } = ResolveInstallRoot();

        /// <summary>Root for all writable user data.</summary>
        public static string UserDataDir { get; } = Path.Combine(InstallRoot, "AppData");

        public static string SecureDir => Path.Combine(UserDataDir, "secure");
        public static string SecretsDir => Path.Combine(UserDataDir, "secrets");
        public static string CacheDir => Path.Combine(UserDataDir, "cache");
        public static string LogsDir => Path.Combine(UserDataDir, "logs");
        public static string DownloadsDir => Path.Combine(UserDataDir, "Downloads");
        public static string BrowserDataDir => Path.Combine(UserDataDir, "BrowserData");
        public static string ZaloProfileDir => Path.Combine(UserDataDir, "ZaloProfile");
        /// <summary>Parent of the "modules" folder. Module code appends "\modules".</summary>
        public static string ModulesCacheDir => UserDataDir;

        public static string AutoJmsJson => Path.Combine(UserDataDir, "AutoJMS.json");
        /// <summary>Danh sách nhân viên đang làm việc, do chủ bưu cục tự bổ sung.</summary>
        public static string ActiveStaffFile => Path.Combine(UserDataDir, "nhan-vien-hoat-dong.txt");
        public static string UserSettingsFile => AutoJmsJson;
        public static string GoogleServiceAccountJson => Path.Combine(UserDataDir, "service_account.json");
        public static string EncryptedGoogleServiceAccount => Path.Combine(SecretsDir, "service_account.sec");
        public static string SecureFile => Path.Combine(SecureDir, "AutoJMS.secure");
        public static string SecureTempFile => Path.Combine(SecureDir, "AutoJMS.secure.tmp");
        public static string ConfigEncFile => Path.Combine(SecureDir, "AutoJMS.config.enc");
        public static string LicenseDatFile => Path.Combine(SecureDir, "license.dat");
        public static string RuntimeConfigCache => Path.Combine(CacheDir, "runtime-config.cache");
        public static string RuntimePolicyCache => Path.Combine(CacheDir, "runtime-policy.cache");

        /// <summary>Real-time debug log written by AppLogger.</summary>
        public static string DebugLogFile => Path.Combine(LogsDir, "debug.log");

        /// <summary>Read-only resource shipped with the app (under InstallDir).</summary>
        public static string InstallResource(string relativePath) =>
            Path.Combine(InstallDir, relativePath);

        public static void EnsureDirectories()
        {
            Directory.CreateDirectory(UserDataDir);
            Directory.CreateDirectory(SecureDir);
            Directory.CreateDirectory(SecretsDir);
            Directory.CreateDirectory(CacheDir);
            Directory.CreateDirectory(LogsDir);
            Directory.CreateDirectory(DownloadsDir);
            Directory.CreateDirectory(BrowserDataDir);
            Directory.CreateDirectory(Path.Combine(ModulesCacheDir, "modules"));
        }

        /// <summary>
        /// Ghi đè file văn bản mà không để lại file rỗng khi máy tắt cứng giữa chừng.
        ///
        /// File.WriteAllText ghi thẳng lên file đích: NTFS cập nhật kích thước mới vào MFT
        /// ngay lập tức còn nội dung thì còn nằm trong page cache. Mất điện đúng lúc đó thì
        /// lần khởi động sau file dựng lại ĐÚNG kích thước nhưng toàn byte 0 — đã xảy ra
        /// thật với license.dat (373 byte, cả 373 đều 0x00), làm app mất hẳn bản quyền đã
        /// kích hoạt. Ghi ra file tạm rồi đổi chỗ thì file đích không bao giờ ở trạng thái
        /// dở dang: hỏng thì cùng lắm mất bản ghi MỚI, bản cũ vẫn dùng được.
        ///
        /// Flush(flushToDisk: true) là bắt buộc chứ không phải cho chắc: thiếu nó thì dữ
        /// liệu của chính file tạm cũng đang nằm trong page cache, đổi chỗ xong vẫn zero
        /// được y như cũ — chỉ khác là zero một file mới toanh thay vì file cũ.
        ///
        /// File.Replace được NTFS ghi nhật ký nên bản thân bước đổi chỗ là nguyên tử, nhưng
        /// nó đòi file đích phải tồn tại sẵn; lần ghi đầu tiên phải rơi về File.Move.
        /// </summary>
        public static void AtomicWriteAllText(string path, string content)
        {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            var temp = path + ".tmp";
            using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
            using (var writer = new StreamWriter(stream))
            {
                writer.Write(content);
                writer.Flush();              // StreamWriter -> FileStream
                stream.Flush(flushToDisk: true); // FileStream -> đĩa thật
            }

            if (File.Exists(path)) File.Replace(temp, path, null);
            else File.Move(temp, path);
        }

        /// <summary>
        /// First-run migration: copy bundled read-only data (modules, AutoJMS.json template)
        /// from the install dir into the writable UserData dir.
        /// </summary>
        public static void MigrateBundledDataIfNeeded()
        {
            try
            {
                var jsonTemplate = InstallResource("AutoJMS.json");
                if (!File.Exists(AutoJmsJson) && File.Exists(jsonTemplate))
                    File.Copy(jsonTemplate, AutoJmsJson, overwrite: false);

                var sourceModules = InstallResource("modules");
                var targetModulesDir = Path.Combine(ModulesCacheDir, "modules");
                var targetCache = Path.Combine(targetModulesDir, "modules-cache.json");
                if (Directory.Exists(sourceModules) && !File.Exists(targetCache))
                    CopyDirectoryRecursive(sourceModules, targetModulesDir);
            }
            catch (Exception ex)
            {
                AppLogger.Warning($"AppPaths: bundled data migration failed: {ex.Message}");
            }
        }

        // ── internals ────────────────────────────────────────

        private static string ResolveInstallRoot()
        {
            try
            {
                var baseDir = new DirectoryInfo(AppContext.BaseDirectory.TrimEnd(
                    Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));

                // When packaged by Velopack the layout is {root}\current\AutoJMS.exe.
                // Detect that and return {root}; otherwise return BaseDirectory itself.
                if (baseDir.Parent != null &&
                    string.Equals(baseDir.Name, "current", StringComparison.OrdinalIgnoreCase))
                {
                    return baseDir.Parent.FullName;
                }
                return baseDir.FullName;
            }
            catch
            {
                return AppContext.BaseDirectory;
            }
        }

        private static void CopyDirectoryRecursive(string sourceDir, string targetDir)
        {
            Directory.CreateDirectory(targetDir);
            foreach (var file in Directory.GetFiles(sourceDir))
            {
                var dest = Path.Combine(targetDir, Path.GetFileName(file));
                if (!File.Exists(dest)) File.Copy(file, dest, overwrite: false);
            }
            foreach (var dir in Directory.GetDirectories(sourceDir))
                CopyDirectoryRecursive(dir, Path.Combine(targetDir, Path.GetFileName(dir)));
        }
    }
}

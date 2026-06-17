#nullable enable
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;
using AutoJMS.ModuleSystem;
using AutoJMS.Diagnostics.AppCapture;
using Velopack;

namespace AutoJMS
{
    static class Program
    {
        [DllImport("user32.dll")] private static extern bool SetProcessDPIAware();
        [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
        [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);
        [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)] private static extern bool CheckRemoteDebuggerPresent(IntPtr hProcess, ref bool isDebuggerPresent);

        private const int SW_RESTORE = 9;
        private const string MUTEX_NAME = "Global\\AutoJMS_SingleInstance_Commercial";
        private static readonly string cacheFilePath = AppPaths.LicenseDatFile;

        public static string HWID { get; private set; } = "";
        public static string ExecutableHash { get; private set; } = "";
        private static readonly CancellationTokenSource AppCts = new CancellationTokenSource();

        // NEW: Global service instances (nullable — only set after successful license verify)
        public static SupabaseManifestService SupabaseManifest { get; private set; } = null!;
        public static RuntimeConfigService RuntimeConfig { get; private set; } = null!;
        public static IntegrityService Integrity { get; private set; } = null!;
        public static MajorUpdateService MajorUpdateServiceInstance { get; private set; } = null!;
        public static SmallUpdateService SmallUpdate { get; private set; } = null!;
        public static UserSettingsService UserSettings { get; private set; } = null!;
        public static RuntimePolicyDocument RuntimePolicy { get; private set; } = null!;

        /// <summary>Update channel from license ("stable" or "beta"). Used by VelopackUpdateService.</summary>
        public static string UpdateChannel { get; private set; } = "stable";

        [STAThread]
        static void Main()
        {
            VelopackApp.Build().Run();

            // Bring up writable dirs + logger ASAP so any subsequent failure
            // is captured in debug.log (single source of truth for diagnostics).
            AppPaths.EnsureDirectories();
            AppLogger.Info("─────────────────────────────────────────────");
            AppLogger.Info($"AutoJMS starting. InstallRoot={AppPaths.InstallRoot}, BaseDir={AppPaths.InstallDir}");
            AppLogger.Info($"AppData={AppPaths.UserDataDir}, LogFile={AppLogger.LogFile}");
            try
            {
                var captureSettings = SettingsManager.Load();
                AppCaptureManager.Instance.Configure(AppCaptureOptions.FromRuntime(captureSettings, Environment.GetCommandLineArgs()));
                AppCaptureManager.Instance.StartAsync(AppCts.Token).GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                AppLogger.Warning($"[AppCapture] startup skipped: {ex.Message}");
            }

#if !DEBUG
            if (Debugger.IsAttached || IsDebuggerPresent())
            {
                AppLogger.Warning("Debugger detected at startup — exiting per anti-debug policy.");
                Environment.Exit(0);
            }
#endif

            try
            {
                AppPaths.MigrateBundledDataIfNeeded();
            }
            catch (Exception ex)
            {
                AppLogger.Error("AppPaths.MigrateBundledDataIfNeeded failed", ex);
            }

            Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            HWID = GetHWID();
            ExecutableHash = HashVerifier.ComputeDllHash();

            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += (sender, e) =>
            {
                if (e.Exception is System.Collections.Generic.KeyNotFoundException &&
                    e.Exception.StackTrace != null &&
                    e.Exception.StackTrace.Contains("KeyboardToolTipStateMachine"))
                    return;

                AppLogger.Error("Lỗi hệ thống UI chưa được xử lý", e.Exception);
                MessageBox.Show($"Lỗi hệ thống:\n{e.Exception.Message}", "Lỗi", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            };

            Application.ApplicationExit += (s, e) =>
            {
                AppCts.Cancel();
                try
                {
                    AppCaptureManager.Instance.StopAsync(CancellationToken.None).GetAwaiter().GetResult();
                }
                catch { }
            };

            bool createdNew;
            using (Mutex mutex = new Mutex(true, MUTEX_NAME, out createdNew))
            {
                if (!createdNew)
                {
                    BringExistingInstanceToFront();
                    return;
                }

                if (Environment.OSVersion.Version.Major >= 6) SetProcessDPIAware();

                string myHwid = HWID;
                try { AppConfig.LoadBootstrap(myHwid); } catch { }

                // ==========================================
                // KHỞI ĐỘNG OFFLINE-FIRST THÔNG MINH
                // ==========================================
                string? savedKey = ReadLocalCache(myHwid);
                bool isAuthorized = false;
                string activeToken = "";
                string activeKey = savedKey ?? "";
                string sessionTier = "BASE";
                System.Action<VerifyResult> applyLicensePolicy = (result) =>
                {
                    if (result == null) return;
                    sessionTier = result.Tier ?? "BASE";
                    AutoJMS.ModuleSystem.SupabaseModuleProvider.AutoUpdateEnabled = result.AutoUpdate;
                    AutoJMS.ModuleSystem.SupabaseModuleProvider.SilentUpdateEnabled = result.SilentUpdate;
                    AutoJMS.ModuleSystem.SupabaseModuleProvider.SkipHashCheck = result.SkipHashCheck;
                };

                if (!string.IsNullOrEmpty(savedKey))
                {
                    AppLogger.Info("Found saved license key — attempting offline-first verification.");
                    bool online = NetworkInterface.GetIsNetworkAvailable();
                    AppLogger.Info($"Network available: {online}");

                    if (online)
                    {
                        try
                        {
                            using var verifyCts = new CancellationTokenSource(TimeSpan.FromSeconds(12));
                            var checkResult = Task.Run(() =>
                                LicenseApiService.VerifyLicenseSecureAsync(savedKey, myHwid, verifyCts.Token))
                                .GetAwaiter().GetResult();

                            if (checkResult.Success && !string.IsNullOrEmpty(checkResult.Token))
                            {
                                activeToken = checkResult.Token;
                                activeKey = savedKey;
                                applyLicensePolicy(checkResult);
                                isAuthorized = true;
                                SaveLocalCache(savedKey, myHwid);

                                // Initialize new services from license response
                                InitializeServicesFromLicense(checkResult);
                                sessionTier = RuntimePolicy?.Tier ?? sessionTier;
                            }
                            else
                            {
                                if (checkResult.AllowOfflineCacheFallback)
                                {
                                    isAuthorized = true;
                                    activeToken = "";
                                    activeKey = savedKey;
                                    AppLogger.Warning($"Không xác thực online được ({checkResult.Message}); dùng license cache local. failureKind={checkResult.FailureKind}");
                                }
                                else
                                {
                                    DeleteLocalCache();
                                    AppLogger.Error($"Key bị từ chối: {checkResult.Message}; failureKind={checkResult.FailureKind}");
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            isAuthorized = true;
                            activeToken = "";
                            activeKey = savedKey;
                            AppLogger.Error("Mất kết nối trong lúc xác thực.", ex);
                        }
                    }
                    else
                    {
                        isAuthorized = true;
                        activeToken = "";
                        AppLogger.Warning("Khởi động offline với key đã lưu.");
                    }
                }

                // ==========================================
                // YÊU CẦU NHẬP KEY (NẾU CHƯA CÓ HOẶC BỊ THU HỒI)
                // ==========================================
                while (!isAuthorized)
                {
                    string userInputKey = "";
                    AppLogger.Action("Showing license input dialog (frmLogin).");
                    try
                    {
                        using (frmLogin frm = new frmLogin(myHwid))
                        {
                            var dr = frm.ShowDialog();
                            AppLogger.Info($"frmLogin closed with DialogResult={dr}.");
                            if (dr == DialogResult.OK) userInputKey = frm.EnteredKey;
                            else
                            {
                                AppLogger.Info("User cancelled license dialog — exiting.");
                                return;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        AppLogger.Fatal("frmLogin threw an unhandled exception", ex);
                        MessageBox.Show($"Lỗi mở màn hình đăng nhập:\n{ex.Message}",
                            "Lỗi", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        return;
                    }

                    AppLogger.Action("Verifying license key with server (online required).");

                    // Khi nhập key mới, bắt buộc online
                    var activateResult = Task.Run(() =>
                        LicenseApiService.VerifyLicenseSecureAsync(userInputKey, myHwid))
                        .GetAwaiter().GetResult();

                    AppLogger.Info($"License verify result: success={activateResult.Success}, message={activateResult.Message}");
                    if (!activateResult.Success)
                        AppLogger.Info($"License verify failureKind={activateResult.FailureKind}");

                    if (activateResult.Success && !string.IsNullOrEmpty(activateResult.Token))
                    {
                        MessageBox.Show(activateResult.Message, "Thành công", MessageBoxButtons.OK, MessageBoxIcon.Information);
                        activeToken = activateResult.Token;
                        activeKey = userInputKey;
                        applyLicensePolicy(activateResult);
                        SaveLocalCache(userInputKey, myHwid);
                        isAuthorized = true;

                        // Initialize new services from license response
                        InitializeServicesFromLicense(activateResult);
                        sessionTier = RuntimePolicy?.Tier ?? sessionTier;
                    }
                    else
                    {
                        MessageBox.Show(activateResult.Message, "Từ chối", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                }

                // ==========================================
                // KHỞI ĐỘNG TỐI THIỂU: không init Google Sheet/Database ở startup
                // ==========================================
                var networkMonitor = new uiControlService();
                _ = networkMonitor.StartAsync(AppCts.Token);

                var heartbeat = new LicenseApiService.HeartbeatSupervisor(
                    activeKey,
                    myHwid,
                    activeToken,
                    token => { /* LicenseApiService updates the runtime token internally. */ },
                    warning => { AppLogger.Info(warning); }
                );
                _ = heartbeat.StartAsync(AppCts.Token);

                // ==========================================
                // KHỞI ĐỘNG MODULE SYSTEM (OFFLINE-FIRST)
                // ==========================================
                try
                {
                    ModuleStartup.Registry.RegisterBuiltIn();
                    var loadTask = ModuleStartup.Registry.LoadFromActivePointerAsync(AppCts.Token);
                    loadTask.GetAwaiter().GetResult();
                }
                catch (Exception ex)
                {
                    AppLogger.Error("Module system init failed, using built-in only", ex);
                }

                // Background sync (non-blocking) — Supabase + Firebase Storage
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await ModuleStartup.SyncAsync(AppCts.Token);
                    }
                    catch (Exception ex)
                    {
                        AppLogger.Warning($"Background module sync: {ex.Message}");
                    }
                }, AppCts.Token);

                // Version-aware hash integrity check (non-blocking)
                _ = Task.Run(async () =>
                {
                    try
                    {
                        var result = await HashVerifier.VerifyAgainstManifestAsync(AppCts.Token);
                        if (result == false)
                            AppLogger.Error("INTEGRITY FAILURE: local AutoJMS.dll hash does not match manifest. Possible tamper or unregistered update.");
                        else if (result == true)
                            AppLogger.Info("Integrity check passed");
                    }
                    catch (Exception ex)
                    {
                        AppLogger.Warning($"Integrity check unavailable: {ex.Message}");
                    }
                }, AppCts.Token);

                Application.Run(new Main(sessionTier));
            }
        }

        // ─── Initialize new services from license response ────────────────────

        private static void InitializeServicesFromLicense(VerifyResult result)
        {
            try
            {
                UpdateChannel = result.UpdateChannel ?? "stable";
                UserSettings ??= new UserSettingsService();

                if (!string.IsNullOrWhiteSpace(result.MiddleCode))
                {
                    SiteContextProvider.ApplyLicenseMiddleCode(result.MiddleCode);
                }

                string supabaseUrl = result.SupabaseBaseUrl;
                var manifests = result.Manifests;
                var releases = result.Releases;

                if (!string.IsNullOrWhiteSpace(supabaseUrl) && manifests != null)
                {
                    SupabaseDbService.Configure(result.SupabaseProjectUrl, result.SupabaseAnonKey);
                    SupabaseManifest = new SupabaseManifestService(supabaseUrl, manifests);
                    RuntimeConfig = new RuntimeConfigService();
                    RuntimeConfig.LoadCachedOrDefault();
                    RuntimePolicy = new SupabaseRuntimePolicyService(SupabaseManifest)
                        .FetchPolicyAsync(result.Tier, GetAppVersion(), AppCts.Token)
                        .GetAwaiter()
                        .GetResult();
                    RuntimePolicyApplier.ApplyToSettings(RuntimePolicy, UserSettings.Current);
                    UserSettings.Save();

                    bool skipHashCheck = result.SkipHashCheck;
                    if (RuntimePolicy?.ModulePolicy != null)
                    {
                        AutoJMS.ModuleSystem.SupabaseModuleProvider.AutoUpdateEnabled = RuntimePolicy.ModulePolicy.AutoUpdate;
                        AutoJMS.ModuleSystem.SupabaseModuleProvider.SilentUpdateEnabled = RuntimePolicy.ModulePolicy.SilentUpdate;
                    }

                    Integrity = new IntegrityService(SupabaseManifest, skipHashCheck);
                    MajorUpdateServiceInstance = new MajorUpdateService(SupabaseManifest, releases, result.UpdateChannel ?? "stable");
                    SmallUpdate = new SmallUpdateService(SupabaseManifest, RuntimeConfig);

                    if (result.SkipHashCheck)
                        SmallUpdate.SetSkipSignatureCheck(true);

                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            await SmallUpdate.CheckAndApplyAsync(AppCts.Token);
                        }
                        catch (Exception ex)
                        {
                            AppLogger.Warning($"Small update failed (non-fatal): {ex.Message}");
                        }
                    }, AppCts.Token);

                    AppLogger.Info($"Services initialized: tier={result.Tier}, channel={result.UpdateChannel}, supabase={supabaseUrl.Substring(0, Math.Min(40, supabaseUrl.Length))}...");
                }
                else
                {
                    AppLogger.Warning("License response missing Supabase config — services not initialized");
                }
            }
            catch (Exception ex)
            {
                AppLogger.Error("Failed to initialize services from license", ex);
            }
        }

        private static string GetAppVersion()
        {
            try
            {
                return typeof(Program).Assembly.GetName().Version?.ToString() ?? "";
            }
            catch
            {
                return "";
            }
        }

        // --- CÁC HÀM TIỆN ÍCH ---
        private static bool IsDebuggerPresent()
        {
            try
            {
                bool isDebuggerPresent = false;
                CheckRemoteDebuggerPresent(Process.GetCurrentProcess().Handle, ref isDebuggerPresent);
                return isDebuggerPresent;
            }
            catch { return false; }
        }

        private static void BringExistingInstanceToFront()
        {
            var current = Process.GetCurrentProcess();
            foreach (var p in Process.GetProcessesByName(current.ProcessName))
            {
                if (p.Id != current.Id && p.MainWindowHandle != IntPtr.Zero)
                {
                    ShowWindow(p.MainWindowHandle, SW_RESTORE);
                    SetForegroundWindow(p.MainWindowHandle);
                    break;
                }
            }
        }

        // --- LOCAL CACHE DPAPI ---
        public static void SaveLocalCache(string licenseKey, string hwid)
        {
            try
            {
                string rawData = $"{licenseKey}||{hwid}";
                string encryptedData = SecureConfigCrypto.ProtectString(rawData, BuildCacheSecret(hwid));
                File.WriteAllText(cacheFilePath, encryptedData);
            }
            catch { }
        }

        private static string? ReadLocalCache(string currentHwid)
        {
            if (!File.Exists(cacheFilePath)) return null;
            try
            {
                string encryptedData = File.ReadAllText(cacheFilePath);
                string rawData = SecureConfigCrypto.UnprotectString(encryptedData, BuildCacheSecret(currentHwid));
                string[] parts = rawData.Split(new[] { "||" }, StringSplitOptions.None);
                if (parts.Length == 2 && parts[1] == currentHwid) return parts[0];
                return null;
            }
            catch { return null; }
        }

        public static void DeleteLocalCache() { try { if (File.Exists(cacheFilePath)) File.Delete(cacheFilePath); } catch { } }

        private static string BuildCacheSecret(string hwid) => $"{Environment.MachineName}|{Environment.UserName}|{hwid}|AutoJMS";

        // --- HWID TAM GIÁC VÀNG ---
        private static string GetHWID()
        {
            string smbiosUUID = GetSystemUUID();
            string physicalDisk = GetPhysicalDiskSerial();
            string machineGuid = GetMachineGuid();
            return ComputeSha256($"{smbiosUUID}-{physicalDisk}-{machineGuid}");
        }

        private static string GetSystemUUID()
        {
            try
            {
                using (var searcher = new ManagementObjectSearcher("SELECT UUID FROM Win32_ComputerSystemProduct"))
                {
                    foreach (ManagementObject mObject in searcher.Get())
                    {
                        string uuid = mObject["UUID"]?.ToString()?.Trim() ?? "";
                        if (!string.IsNullOrEmpty(uuid) && uuid != "FFFFFFFF-FFFF-FFFF-FFFF-FFFFFFFFFFFF") return uuid;
                    }
                }
            }
            catch { }
            return "UNKNOWN_UUID";
        }

        private static string GetPhysicalDiskSerial()
        {
            try
            {
                using (var searcher = new ManagementObjectSearcher("SELECT SerialNumber FROM Win32_DiskDrive WHERE MediaType='Fixed hard disk media'"))
                {
                    foreach (ManagementObject mObject in searcher.Get())
                    {
                        string serial = mObject["SerialNumber"]?.ToString()?.Trim() ?? "";
                        if (!string.IsNullOrEmpty(serial)) return serial.Replace(" ", "").Replace(".", "");
                    }
                }
            }
            catch { }
            return "UNKNOWN_PHYSICAL_DISK";
        }

        private static string GetMachineGuid()
        {
            try
            {
                using (RegistryKey localMachineX64View = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
                {
                    using (RegistryKey? rk = localMachineX64View.OpenSubKey(@"SOFTWARE\Microsoft\Cryptography"))
                    {
                        if (rk != null)
                        {
                            object? guid = rk.GetValue("MachineGuid");
                            if (guid != null) return guid.ToString() ?? "";
                        }
                    }
                }
            }
            catch { }
            return "UNKNOWN_GUID";
        }

        private static string ComputeSha256(string rawData)
        {
            using (var sha256 = SHA256.Create())
            {
                byte[] hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(rawData));
                return BitConverter.ToString(hash).Replace("-", "").ToLower();
            }
        }
    }

    // ==========================================
    // MÃ HÓA NỘI BỘ (Giữ nguyên)
    // ==========================================
    [System.Reflection.Obfuscation(Exclude = true, ApplyToMembers = true)]
    internal static class SecureConfigCrypto
    {
        private const int SaltSize = 16;
        private const int IvSize = 16;
        private const string AlgorithmName = "AES-CBC-HMACSHA256-MD5-SHA256";

        [System.Reflection.Obfuscation(Exclude = true, ApplyToMembers = true)]
        public sealed class ProtectedPayload
        {
            [JsonPropertyName("version")] public int Version { get; set; } = 1;
            [JsonPropertyName("algorithm")] public string Algorithm { get; set; } = AlgorithmName;
            [JsonPropertyName("salt")] public string Salt { get; set; } = "";
            [JsonPropertyName("iv")] public string IV { get; set; } = "";
            [JsonPropertyName("cipherText")] public string CipherText { get; set; } = "";
            [JsonPropertyName("hash")] public string Hash { get; set; } = "";
        }

        public static string ProtectString(string plaintext, string secret)
        {
            if (plaintext == null) throw new ArgumentNullException(nameof(plaintext));
            if (string.IsNullOrWhiteSpace(secret)) throw new ArgumentException("Secret is required.", nameof(secret));

            byte[] salt = RandomNumberGenerator.GetBytes(SaltSize);
            byte[] iv = RandomNumberGenerator.GetBytes(IvSize);
            byte[] plainBytes = Encoding.UTF8.GetBytes(plaintext);

            byte[] cipherBytes;
            using (Aes aes = Aes.Create())
            {
                aes.Key = DeriveKey(secret, salt, "aes");
                aes.IV = iv;
                aes.Mode = CipherMode.CBC;
                aes.Padding = PaddingMode.PKCS7;

                using ICryptoTransform encryptor = aes.CreateEncryptor();
                cipherBytes = encryptor.TransformFinalBlock(plainBytes, 0, plainBytes.Length);
            }

            byte[] mac = ComputeHash(secret, salt, iv, cipherBytes);
            var payload = new ProtectedPayload
            {
                Salt = Convert.ToBase64String(salt),
                IV = Convert.ToBase64String(iv),
                CipherText = Convert.ToBase64String(cipherBytes),
                Hash = Convert.ToBase64String(mac)
            };

            return JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });
        }

        public static string UnprotectString(string protectedJson, string secret)
        {
            if (string.IsNullOrWhiteSpace(protectedJson)) throw new ArgumentException("Protected payload is required.", nameof(protectedJson));
            if (string.IsNullOrWhiteSpace(secret)) throw new ArgumentException("Secret is required.", nameof(secret));

            ProtectedPayload? payload = JsonSerializer.Deserialize<ProtectedPayload>(protectedJson, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                ReadCommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true
            });
            if (payload == null || payload.Version != 1 || !string.Equals(payload.Algorithm, AlgorithmName, StringComparison.Ordinal))
                throw new InvalidDataException("Định dạng config mã hóa không hợp lệ.");

            byte[] salt = Convert.FromBase64String(payload.Salt);
            byte[] iv = Convert.FromBase64String(payload.IV);
            byte[] cipherBytes = Convert.FromBase64String(payload.CipherText);
            byte[] expectedHash = Convert.FromBase64String(payload.Hash);
            byte[] actualHash = ComputeHash(secret, salt, iv, cipherBytes);

            if (!CryptographicOperations.FixedTimeEquals(expectedHash, actualHash))
                throw new CryptographicException("Config mã hóa không còn hợp lệ hoặc sai khóa giải mã.");

            using Aes aes = Aes.Create();
            aes.Key = DeriveKey(secret, salt, "aes");
            aes.IV = iv;
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.PKCS7;

            using ICryptoTransform decryptor = aes.CreateDecryptor();
            byte[] plainBytes = decryptor.TransformFinalBlock(cipherBytes, 0, cipherBytes.Length);
            return Encoding.UTF8.GetString(plainBytes);
        }

        private static byte[] ComputeHash(string secret, byte[] salt, byte[] iv, byte[] cipherBytes)
        {
            byte[] macKey = DeriveKey(secret, salt, "sha");
            using var hmac = new HMACSHA256(macKey);
            byte[] header = Encoding.UTF8.GetBytes(AlgorithmName);
            byte[] data = Combine(header, salt, iv, cipherBytes);
            return hmac.ComputeHash(data);
        }

        private static byte[] DeriveKey(string secret, byte[] salt, string purpose)
        {
            byte[] secretBytes = Encoding.UTF8.GetBytes(secret);
            byte[] purposeBytes = Encoding.UTF8.GetBytes(purpose);

            using MD5 md5 = MD5.Create();
            byte[] md5Hash = md5.ComputeHash(secretBytes);

            using SHA256 sha256 = SHA256.Create();
            return sha256.ComputeHash(Combine(purposeBytes, md5Hash, secretBytes, salt));
        }

        private static byte[] Combine(params byte[][] arrays)
        {
            byte[] result = new byte[arrays.Sum(a => a.Length)];
            int offset = 0;
            foreach (byte[] array in arrays)
            {
                Buffer.BlockCopy(array, 0, result, offset, array.Length);
                offset += array.Length;
            }
            return result;
        }
    }
}

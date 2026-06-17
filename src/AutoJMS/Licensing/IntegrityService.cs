using System;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace AutoJMS
{
    public class IntegrityService
    {
        private readonly SupabaseManifestService _manifestService;
        private bool _skipHashCheck;

        public IntegrityService(SupabaseManifestService manifestService, bool skipHashCheck = false)
        {
            _manifestService = manifestService;
            _skipHashCheck = skipHashCheck;
        }

        public void SetSkipHashCheck(bool skip) => _skipHashCheck = skip;

        public static string ComputeDllHash()
        {
            try
            {
                string dllPath = Path.Combine(AppPaths.InstallDir, "AutoJMS.dll");
                if (!File.Exists(dllPath))
                    dllPath = Assembly.GetExecutingAssembly().Location;
                using var sha256 = SHA256.Create();
                using var stream = new FileStream(dllPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                byte[] hash = sha256.ComputeHash(stream);
                return BitConverter.ToString(hash).Replace("-", "").ToLower();
            }
            catch
            {
                return "HASH_ERROR";
            }
        }

        public async Task<bool?> VerifyAgainstManifestAsync(CancellationToken ct = default)
        {
            if (_skipHashCheck)
            {
                AppLogger.Info("IntegrityService: hash check skipped (dev mode)");
                return true;
            }

            try
            {
                string currentVersion = AppVersion.Current;

                var manifest = await _manifestService.FetchHashManifestAsync(ct);
                if (manifest?.Versions == null || manifest.Versions.Count == 0)
                {
                    AppLogger.Warning("IntegrityService: hash manifest unavailable");
                    return null;
                }

                string expectedHash = null;
                if (manifest.Versions.TryGetValue(currentVersion, out var versionEntry) &&
                    versionEntry.Files != null &&
                    versionEntry.Files.TryGetValue("AutoJMS.dll", out var hash))
                {
                    expectedHash = hash;
                }

                if (string.IsNullOrEmpty(expectedHash))
                {
                    AppLogger.Warning($"IntegrityService: no hash entry for version {currentVersion}");
                    return null;
                }

                string localHash = ComputeDllHash();
                if (localHash == "HASH_ERROR") return null;

                bool match = string.Equals(localHash, expectedHash, StringComparison.OrdinalIgnoreCase);
                if (!match)
                    AppLogger.Error($"INTEGRITY FAIL: expected {expectedHash}, got {localHash}");
                else
                    AppLogger.Info("IntegrityService: hash check passed");

                return match;
            }
            catch (Exception ex)
            {
                AppLogger.Warning($"IntegrityService: verification unavailable. {ex.Message}");
                return null;
            }
        }
    }
}

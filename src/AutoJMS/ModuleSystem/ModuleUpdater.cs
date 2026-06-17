using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AutoJMS.Diagnostics.AppCapture;

namespace AutoJMS.ModuleSystem
{
    public class ModuleUpdater
    {
        private readonly string _baseUrl;
        private readonly string _modulesDir;
        private readonly string _stagingDir;
        private readonly HttpClient _http;
        private const int MaxRetries = 3;

        public ModuleUpdater(string serverBaseUrl)
        {
            _baseUrl = serverBaseUrl?.TrimEnd('/') ?? "";
            _modulesDir = Path.Combine(AppPaths.ModulesCacheDir, "modules");
            _stagingDir = Path.Combine(_modulesDir, ".staging");
            _http = new HttpClient(new AppHttpCaptureHandler(new HttpClientHandler(), "ModuleUpdater")) { Timeout = TimeSpan.FromSeconds(30) };
            Directory.CreateDirectory(_modulesDir);
        }

        // ─── 2-tier Manifest Fetch ────────────────────────────

        public async Task<AppManifest> FetchAppManifestAsync(CancellationToken ct = default)
        {
            for (int attempt = 1; attempt <= MaxRetries; attempt++)
            {
                try
                {
                    var url = $"{_baseUrl}/app-manifest.json";
                    var json = await _http.GetStringAsync(url, ct);
                    return JsonSerializer.Deserialize<AppManifest>(json, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true,
                        ReadCommentHandling = JsonCommentHandling.Skip,
                        AllowTrailingCommas = true
                    }) ?? new AppManifest();
                }
                catch (Exception ex) when (attempt < MaxRetries)
                {
                    AppLogger.Warning($"Fetch app-manifest attempt {attempt} failed: {ex.Message}");
                    await Task.Delay(1000 * attempt, ct);
                }
            }
            return null;
        }

        public async Task<ModulesManifest> FetchModulesManifestAsync(string url, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(url)) return null;

            for (int attempt = 1; attempt <= MaxRetries; attempt++)
            {
                try
                {
                    var json = await _http.GetStringAsync(url, ct);
                    return ModulesManifest.FromJson(json);
                }
                catch (Exception ex) when (attempt < MaxRetries)
                {
                    AppLogger.Warning($"Fetch modules manifest attempt {attempt} failed: {ex.Message}");
                    await Task.Delay(1000 * attempt, ct);
                }
            }
            return null;
        }

        // ─── Download ─────────────────────────────────────────

        public async Task<bool> DownloadModuleAsync(ModuleEntry entry, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(entry.File))
            {
                AppLogger.Warning($"Module {entry.Name} has no file specified");
                return false;
            }

            try
            {
                var stagingPath = GetStagingPath(entry.Name, entry.File);
                Directory.CreateDirectory(Path.GetDirectoryName(stagingPath));

                var fileUrl = entry.File.StartsWith("http") ? entry.File : $"{_baseUrl}/{entry.File.TrimStart('/')}";
                var response = await _http.GetAsync(fileUrl, HttpCompletionOption.ResponseHeadersRead, ct);
                response.EnsureSuccessStatusCode();

                using var stream = await response.Content.ReadAsStreamAsync(ct);
                using var fileStream = new FileStream(stagingPath, FileMode.Create, FileAccess.Write, FileShare.None);
                await stream.CopyToAsync(fileStream, ct);

                if (!VerifyHash(stagingPath, entry.Sha256))
                {
                    AppLogger.Error($"SHA256 mismatch for {entry.Name}");
                    TryDelete(stagingPath);
                    return false;
                }

                if (!VerifySignature(stagingPath, entry.Signature))
                {
                    AppLogger.Error($"RSA signature mismatch for {entry.Name}");
                    TryDelete(stagingPath);
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                AppLogger.Error($"Download failed for {entry.Name}", ex);
                TryDelete(GetStagingPath(entry.Name, entry.File));
                return false;
            }
        }

        // ─── Activation (versioned folder) ────────────────────

        public bool ActivateModule(ModuleEntry entry, ActiveModules active)
        {
            var stagingFile = GetStagingPath(entry.Name, entry.File);
            if (!File.Exists(stagingFile))
            {
                AppLogger.Warning($"Cannot activate {entry.Name}: staging file not found");
                return false;
            }

            try
            {
                var versionDir = Path.Combine(_modulesDir, entry.Name, entry.Version);
                Directory.CreateDirectory(versionDir);
                var targetPath = Path.Combine(versionDir, entry.File);

                File.Copy(stagingFile, targetPath, overwrite: true);

                if (!VerifyHash(targetPath, entry.Sha256))
                {
                    AppLogger.Error($"Post-activate verify failed for {entry.Name}");
                    TryDelete(targetPath);
                    return false;
                }

                active.SetActiveVersion(entry.Name, entry.Version);
                TryDelete(stagingFile);
                return true;
            }
            catch (Exception ex)
            {
                AppLogger.Error($"Activate failed for {entry.Name}", ex);
                return false;
            }
        }

        // ─── Verification ─────────────────────────────────────

        public bool VerifyHash(string filePath, string expectedSha256)
        {
            if (string.IsNullOrWhiteSpace(expectedSha256)) return true;
            if (!File.Exists(filePath)) return false;

            try
            {
                using var sha256 = SHA256.Create();
                using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
                var actual = BitConverter.ToString(sha256.ComputeHash(stream)).Replace("-", "").ToLower();
                return string.Equals(actual, expectedSha256, StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        public bool VerifySignature(string filePath, string expectedSignature)
        {
            if (string.IsNullOrWhiteSpace(expectedSignature)) return true;

            try
            {
                using var rsa = RSA.Create();
                rsa.FromXmlString(EmbeddedPublicKey);
                var fileBytes = File.ReadAllBytes(filePath);
                using var sha256 = SHA256.Create();
                var hash = sha256.ComputeHash(fileBytes);
                var sigBytes = Convert.FromBase64String(expectedSignature);
                return rsa.VerifyHash(hash, sigBytes, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            }
            catch
            {
                return false;
            }
        }

        private static readonly string EmbeddedPublicKey = @"<RSAKeyValue><Modulus>placeholder</Modulus><Exponent>AQAB</Exponent></RSAKeyValue>";

        // ─── Dependency Resolution ────────────────────────────

        public bool CheckDependencies(ModuleEntry entry, Dictionary<string, string> availableVersions)
        {
            foreach (var constraint in entry.Requires)
            {
                var parsed = VersionRange.Parse(constraint);
                if (parsed == null)
                {
                    AppLogger.Warning($"Invalid dependency constraint: {constraint}");
                    return false;
                }

                var (name, range) = parsed.Value;
                if (!availableVersions.TryGetValue(name, out var installedVersion))
                {
                    AppLogger.Error($"Missing dependency: {constraint}");
                    return false;
                }

                if (!range.IsSatisfiedBy(installedVersion))
                {
                    AppLogger.Error($"Unsatisfied dependency: {constraint}, installed={installedVersion}");
                    return false;
                }
            }
            return true;
        }

        // ─── Sync Orchestrator ────────────────────────────────

        public async Task SyncAsync(CancellationToken ct = default)
        {
            // 1. Load local state
            var localApp = AppManifest.LoadLocal();
            var active = ActiveModules.LoadLocal();
            var localModules = LoadLocalModulesCache();

            // 2. Check TTL
            if (!localApp.IsTtlExpired() && localModules != null && localModules.Modules.Count > 0)
            {
                AppLogger.Info($"App manifest TTL valid, using cache ({localModules.Modules.Count} modules)");
                return;
            }

            // 3. Fetch remote app-manifest
            var remoteApp = await FetchAppManifestAsync(ct);
            if (remoteApp == null)
            {
                AppLogger.Warning("Cannot reach module server, using local cache");
                return;
            }

            // 4. Validate minCoreVersion
            if (!IsCoreVersionCompatible(remoteApp.MinCoreVersion))
            {
                AppLogger.Error($"App requires core >= {remoteApp.MinCoreVersion}, current app too old");
                return;
            }

            // 5. Fetch remote modules manifest
            var manifestUrl = ResolveModulesUrl(remoteApp.ModulesManifestUrl);
            var remoteModules = await FetchModulesManifestAsync(manifestUrl, ct);
            if (remoteModules == null)
            {
                AppLogger.Warning("Cannot fetch modules manifest, using local cache");
                return;
            }

            // 6. Compute delta
            var delta = remoteModules.ComputeDelta(localModules ?? new ModulesManifest());
            if (delta.Count == 0)
            {
                AppLogger.Info("All modules up to date");
                remoteApp.LastChecked = DateTime.UtcNow;
                remoteApp.SaveLocal();
                return;
            }

            AppLogger.Info($"Delta: {delta.Count} module(s) need update");

            // 7. Build version map for dependency checking
            var versionMap = GetVersionMap(active, localModules, remoteModules);

            // 8. Process delta (respect dependency order)
            foreach (var entry in delta.OrderBy(e => e.Requires.Count))
            {
                if (string.IsNullOrWhiteSpace(entry.File) && !entry.Required)
                {
                    AppLogger.Info($"Skipping {entry.Name}: no file and not required");
                    continue;
                }

                if (!CheckDependencies(entry, versionMap))
                {
                    AppLogger.Error($"Dependency check failed for {entry.Name}, skipping");
                    continue;
                }

                var ok = await DownloadModuleAsync(entry, ct);
                if (ok)
                {
                    if (ActivateModule(entry, active))
                    {
                        versionMap[entry.Name] = entry.Version;
                        AppLogger.Info($"Updated {entry.Name} → v{entry.Version}");
                    }
                }
                else if (entry.Required)
                {
                    AppLogger.Error($"Required module {entry.Name} failed to update");
                }
            }

            // 9. Save state
            remoteApp.LastChecked = DateTime.UtcNow;
            remoteApp.SaveLocal();
            active.SaveLocal();
            SaveLocalModulesCache(remoteModules);
        }

        // ─── Helpers ──────────────────────────────────────────

        private string GetStagingPath(string moduleName, string fileName)
        {
            return Path.Combine(_stagingDir, moduleName, fileName);
        }

        private string ResolveModulesUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return $"{_baseUrl}/modules.json";
            return url.StartsWith("http") ? url : $"{_baseUrl}/{url.TrimStart('/')}";
        }

        private static bool IsCoreVersionCompatible(string minCoreVersion)
        {
            if (string.IsNullOrWhiteSpace(minCoreVersion)) return true;
            if (!System.Version.TryParse(minCoreVersion, out var minVer)) return true;
            var appVer = System.Version.TryParse(AppManifest.LoadLocal().AppVersion, out var av) ? av : new System.Version(0, 0);
            return appVer >= minVer;
        }

        private static Dictionary<string, string> GetVersionMap(
            ActiveModules active, ModulesManifest local, ModulesManifest remote)
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var m in (local?.Modules ?? new()))
                map[m.Name] = m.Version;
            foreach (var m in (remote?.Modules ?? new()))
                map[m.Name] = m.Version;
            foreach (var kv in active.Modules)
                map[kv.Key] = kv.Value;
            return map;
        }

        private static ModulesManifest LoadLocalModulesCache()
        {
            try
            {
                var path = Path.Combine(AppPaths.ModulesCacheDir, "modules", "modules-cache.json");
                if (File.Exists(path))
                    return ModulesManifest.FromJson(File.ReadAllText(path));
            }
            catch { }
            return null;
        }

        private static void SaveLocalModulesCache(ModulesManifest manifest)
        {
            try
            {
                var path = Path.Combine(AppPaths.ModulesCacheDir, "modules", "modules-cache.json");
                var dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                File.WriteAllText(path, manifest.ToJson());
            }
            catch (Exception ex)
            {
                AppLogger.Error("Failed to save modules cache", ex);
            }
        }

        private static void TryDelete(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); } catch { }
        }
    }
}

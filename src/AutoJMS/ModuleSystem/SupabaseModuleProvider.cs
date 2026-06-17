using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AutoJMS.Diagnostics.AppCapture;

namespace AutoJMS.ModuleSystem
{
    public class SupabaseModuleProvider
    {
        // License-based policy overrides (set from Program.cs after verification)
        public static bool AutoUpdateEnabled = true;
        public static bool SilentUpdateEnabled = true;
        public static bool SkipHashCheck = false;

        private readonly string _storageBase;
        private readonly string _modulesDir;
        private readonly HttpClient _http;
        private const int MaxRetries = 3;

        private static readonly string SupabaseStorageUrl =
            "https://bnsnnrlwfzxemmizknwy.supabase.co/storage/v1/object/public/autojms-modules";

        public SupabaseModuleProvider()
        {
            _storageBase = SupabaseStorageUrl;
            _modulesDir = Path.Combine(AppPaths.ModulesCacheDir, "modules");
            _http = new HttpClient(new AppHttpCaptureHandler(new HttpClientHandler(), "SupabaseModuleProvider")) { Timeout = TimeSpan.FromSeconds(30) };
            Directory.CreateDirectory(_modulesDir);
        }

        // ─── Fetch JSON from Supabase Storage ────────────────

        private async Task<T> FetchJsonAsync<T>(string relativePath, CancellationToken ct = default)
            where T : new()
        {
            var url = $"{_storageBase}/{relativePath.TrimStart('/')}";
            for (int attempt = 1; attempt <= MaxRetries; attempt++)
            {
                try
                {
                    var json = await _http.GetStringAsync(url, ct);
                    return JsonSerializer.Deserialize<T>(json, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true,
                        ReadCommentHandling = JsonCommentHandling.Skip,
                        AllowTrailingCommas = true
                    }) ?? new T();
                }
                catch (Exception ex) when (attempt < MaxRetries)
                {
                    AppLogger.Warning($"Fetch {relativePath} attempt {attempt} failed: {ex.Message}");
                    await Task.Delay(1000 * attempt, ct);
                }
            }
            return new T();
        }

        public async Task<AppManifest> FetchAppManifestAsync(CancellationToken ct = default)
        {
            return await FetchJsonAsync<AppManifest>("manifest/app-manifest.json", ct);
        }

        public async Task<ModulesManifest> FetchModulesManifestAsync(string url = null, CancellationToken ct = default)
        {
            if (!string.IsNullOrWhiteSpace(url))
                return await FetchJsonAsync<ModulesManifest>(url, ct);
            return await FetchJsonAsync<ModulesManifest>("modules/modules.json", ct);
        }

        // ─── Download file from Supabase Storage ─────────────

        public async Task<bool> DownloadFileAsync(string moduleName, string remotePath, string sha256, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(remotePath))
            {
                AppLogger.Warning($"Module {moduleName} has no remote path");
                return false;
            }

            var fileName = Path.GetFileName(remotePath);
            var stagingPath = GetStagingPath(moduleName, fileName);

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(stagingPath));
                var url = remotePath.StartsWith("http") ? remotePath : $"{_storageBase}/{remotePath.TrimStart('/')}";

                for (int attempt = 1; attempt <= MaxRetries; attempt++)
                {
                    try
                    {
                        var response = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
                        response.EnsureSuccessStatusCode();

                        using var stream = await response.Content.ReadAsStreamAsync(ct);
                        using var fileStream = new FileStream(stagingPath, FileMode.Create, FileAccess.Write, FileShare.None);
                        await stream.CopyToAsync(fileStream, ct);

                        if (!VerifyHash(stagingPath, sha256))
                        {
                            AppLogger.Error($"SHA256 mismatch for {moduleName}");
                            TryDelete(stagingPath);
                            return false;
                        }
                        return true;
                    }
                    catch (Exception ex) when (attempt < MaxRetries && !ct.IsCancellationRequested)
                    {
                        AppLogger.Warning($"Download {moduleName} attempt {attempt} failed: {ex.Message}");
                        await Task.Delay(1000 * attempt, ct);
                    }
                }
                return false;
            }
            catch (Exception ex)
            {
                AppLogger.Error($"Download failed for {moduleName}", ex);
                TryDelete(stagingPath);
                return false;
            }
        }

        // ─── Activate into versioned folder ──────────────────

        public bool ActivateModule(string moduleName, string version, string remotePath, string sha256, ActiveModules active)
        {
            var fileName = Path.GetFileName(remotePath);
            var stagingPath = GetStagingPath(moduleName, fileName);
            if (!File.Exists(stagingPath))
            {
                AppLogger.Warning($"Cannot activate {moduleName}: staging file not found");
                return false;
            }

            try
            {
                var versionDir = Path.Combine(_modulesDir, moduleName, version);
                Directory.CreateDirectory(versionDir);
                var targetPath = Path.Combine(versionDir, fileName);
                File.Copy(stagingPath, targetPath, overwrite: true);

                if (!VerifyHash(targetPath, sha256))
                {
                    AppLogger.Error($"Post-activate verify failed for {moduleName}");
                    TryDelete(targetPath);
                    return false;
                }

                active.SetActiveVersion(moduleName, version);
                TryDelete(stagingPath);
                return true;
            }
            catch (Exception ex)
            {
                AppLogger.Error($"Activate failed for {moduleName}", ex);
                return false;
            }
        }

        // ─── Sync orchestrator ──────────────────────────────

        public async Task SyncAsync(CancellationToken ct = default)
        {
            var localApp = AppManifest.LoadLocal();
            var active = ActiveModules.LoadLocal();
            var localCache = LoadLocalCache();

            var remoteApp = await FetchAppManifestAsync(ct);
            if (remoteApp == null || string.IsNullOrWhiteSpace(remoteApp.AppVersion))
            {
                AppLogger.Info("Cannot reach Supabase Storage, using local cache");
                return;
            }

            if (!IsCoreVersionCompatible(remoteApp.MinCoreVersion))
            {
                AppLogger.Error($"App requires core >= {remoteApp.MinCoreVersion}, current too old");
                return;
            }

            var remoteModules = await FetchModulesManifestAsync(remoteApp.ModulesManifestUrl, ct);
            if (remoteModules?.Modules == null || remoteModules.Modules.Count == 0)
            {
                AppLogger.Warning("No modules in remote manifest");
                return;
            }

            var versionMap = remoteModules.Modules.ToDictionary(m => m.Name, m => m.Version, StringComparer.OrdinalIgnoreCase);

            // Compute delta
            var delta = remoteModules.Modules
                .Where(rm => IsNewer(rm, localCache))
                .OrderBy(rm => rm.Requires?.Count ?? 0)
                .ToList();

            if (delta.Count == 0)
            {
                AppLogger.Info("All modules up to date");
                localApp.LastChecked = DateTime.UtcNow;
                localApp.SaveLocal();
                SaveLocalCache(remoteModules.Modules);
                return;
            }

            AppLogger.Info($"Delta: {delta.Count} module(s) need update");

            foreach (var entry in delta)
            {
                if (!CheckDependencies(entry, versionMap))
                {
                    AppLogger.Error($"Dependency check failed for {entry.Name}, skipping");
                    continue;
                }

                var ok = await DownloadFileAsync(entry.Name, entry.File, entry.Sha256, ct);
                if (ok)
                {
                    if (ActivateModule(entry.Name, entry.Version, entry.File, entry.Sha256, active))
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

            localApp.LastChecked = DateTime.UtcNow;
            localApp.SaveLocal();
            active.SaveLocal();
            SaveLocalCache(remoteModules.Modules);
        }

        // ─── Helpers ────────────────────────────────────────

        private string GetStagingPath(string moduleName, string fileName)
        {
            return Path.Combine(_modulesDir, ".staging", moduleName, fileName);
        }

        private static bool IsNewer(ModuleEntry remote, List<ModuleEntry> local)
        {
            var localMod = local?.FirstOrDefault(m =>
                string.Equals(m.Name, remote.Name, StringComparison.OrdinalIgnoreCase));
            if (localMod == null) return true;
            if (!string.Equals(localMod.Version, remote.Version, StringComparison.Ordinal)) return true;
            return !string.Equals(localMod.Sha256, remote.Sha256, StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsCoreVersionCompatible(string minCoreVersion)
        {
            if (string.IsNullOrWhiteSpace(minCoreVersion)) return true;
            if (!Version.TryParse(minCoreVersion, out var minVer)) return true;
            var appVer = Version.TryParse(AppManifest.LoadLocal().AppVersion, out var av) ? av : new Version(0, 0);
            return appVer >= minVer;
        }

        private static bool CheckDependencies(ModuleEntry module, Dictionary<string, string> available)
        {
            if (module.Requires == null || module.Requires.Count == 0) return true;
            foreach (var constraint in module.Requires)
            {
                var parsed = VersionRange.Parse(constraint);
                if (parsed == null)
                {
                    AppLogger.Warning($"Invalid dependency constraint: {constraint}");
                    return false;
                }
                var (name, range) = parsed.Value;
                if (!available.TryGetValue(name, out var installedVersion))
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

        private static bool VerifyHash(string filePath, string expectedSha256)
        {
            if (SkipHashCheck) return true;
            if (string.IsNullOrWhiteSpace(expectedSha256)) return true;
            if (!File.Exists(filePath)) return false;
            try
            {
                using var sha256 = SHA256.Create();
                using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
                var actual = BitConverter.ToString(sha256.ComputeHash(stream)).Replace("-", "").ToLower();
                return string.Equals(actual, expectedSha256, StringComparison.OrdinalIgnoreCase);
            }
            catch { return false; }
        }

        private static List<ModuleEntry> LoadLocalCache()
        {
            try
            {
                var path = Path.Combine(AppPaths.ModulesCacheDir, "modules", "modules-cache.json");
                if (File.Exists(path))
                {
                    var doc = JsonDocument.Parse(File.ReadAllText(path));
                    if (doc.RootElement.TryGetProperty("modules", out var arr))
                    {
                        var entries = new List<ModuleEntry>();
                        foreach (var el in arr.EnumerateArray())
                        {
                            entries.Add(new ModuleEntry
                            {
                                Name = el.TryGetProperty("name", out var n) ? n.GetString() : "",
                                Version = el.TryGetProperty("version", out var v) ? v.GetString() : "",
                                File = el.TryGetProperty("file", out var f) ? f.GetString() : "",
                                Sha256 = el.TryGetProperty("sha256", out var s) ? s.GetString() : "",
                                Required = el.TryGetProperty("required", out var r) && r.GetBoolean()
                            });
                        }
                        return entries;
                    }
                }
            }
            catch { }
            return new List<ModuleEntry>();
        }

        private static void SaveLocalCache(List<ModuleEntry> modules)
        {
            try
            {
                var cacheEntries = modules.Select(m => new
                {
                    name = m.Name,
                    version = m.Version,
                    file = Path.GetFileName(m.File ?? m.Name),
                    sha256 = m.Sha256,
                    signature = m.Signature,
                    requires = m.Requires,
                    required = m.Required
                }).ToList();
                var obj = new { modules = cacheEntries };
                var json = JsonSerializer.Serialize(obj, new JsonSerializerOptions
                {
                    WriteIndented = true,
                    PropertyNameCaseInsensitive = true
                });
                var path = Path.Combine(AppPaths.ModulesCacheDir, "modules", "modules-cache.json");
                var dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                File.WriteAllText(path, json);
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

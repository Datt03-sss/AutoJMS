using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AutoJMS.ModuleSystem
{
    public class AppManifest
    {
        [JsonPropertyName("appVersion")] public string AppVersion { get; set; } = "1.0.0";
        [JsonPropertyName("manifestVersion")] public string ManifestVersion { get; set; } = "2026.05.25";
        [JsonPropertyName("minCoreVersion")] public string MinCoreVersion { get; set; } = "1.0.0";
        [JsonPropertyName("modulesManifestUrl")] public string ModulesManifestUrl { get; set; } = "";
        [JsonPropertyName("lastChecked")] public DateTime LastChecked { get; set; }

        private static readonly string LocalPath = Path.Combine(AppPaths.ModulesCacheDir, "modules", "app-manifest.json");
        private static readonly JsonSerializerOptions JsonOpts = new()
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        };

        public static AppManifest LoadLocal()
        {
            try
            {
                if (File.Exists(LocalPath))
                {
                    var json = File.ReadAllText(LocalPath);
                    return JsonSerializer.Deserialize<AppManifest>(json, JsonOpts) ?? new AppManifest();
                }
            }
            catch (Exception ex)
            {
                AppLogger.Warning($"AppManifest load failed: {ex.Message}");
            }
            return new AppManifest();
        }

        public void SaveLocal()
        {
            try
            {
                var dir = Path.GetDirectoryName(LocalPath);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                var json = JsonSerializer.Serialize(this, JsonOpts);
                var tmp = LocalPath + ".tmp";
                File.WriteAllText(tmp, json);
                if (File.Exists(LocalPath)) File.Delete(LocalPath);
                File.Move(tmp, LocalPath);
            }
            catch (Exception ex)
            {
                AppLogger.Error($"AppManifest save failed", ex);
            }
        }

        public bool IsTtlExpired(int ttlMinutes = 30)
        {
            return DateTime.UtcNow - LastChecked > TimeSpan.FromMinutes(ttlMinutes);
        }
    }
}

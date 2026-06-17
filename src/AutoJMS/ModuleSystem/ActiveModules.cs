using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AutoJMS.ModuleSystem
{
    public class ActiveModules
    {
        [JsonPropertyName("modules")] public Dictionary<string, string> Modules { get; set; } = new(StringComparer.OrdinalIgnoreCase);

        private static readonly string LocalPath = Path.Combine(AppPaths.ModulesCacheDir, "modules", "active_modules.json");
        private static readonly JsonSerializerOptions JsonOpts = new()
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        };

        public static ActiveModules LoadLocal()
        {
            try
            {
                if (File.Exists(LocalPath))
                {
                    var json = File.ReadAllText(LocalPath);
                    return JsonSerializer.Deserialize<ActiveModules>(json, JsonOpts) ?? new ActiveModules();
                }
            }
            catch (Exception ex)
            {
                AppLogger.Warning($"ActiveModules load failed: {ex.Message}");
            }
            return new ActiveModules();
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
                AppLogger.Error($"ActiveModules save failed", ex);
            }
        }

        public string GetActiveVersion(string moduleName)
        {
            return Modules.TryGetValue(moduleName, out var v) ? v : null;
        }

        public void SetActiveVersion(string moduleName, string version)
        {
            Modules[moduleName] = version;
        }

        public string GetModuleDir(string moduleName)
        {
            var version = GetActiveVersion(moduleName);
            if (string.IsNullOrWhiteSpace(version)) return null;
            return Path.Combine(AppPaths.ModulesCacheDir, "modules", moduleName, version);
        }

        public string GetModuleFilePath(string moduleName, string fileName)
        {
            var dir = GetModuleDir(moduleName);
            if (dir == null) return null;
            var path = Path.Combine(dir, fileName);
            return File.Exists(path) ? path : null;
        }
    }
}

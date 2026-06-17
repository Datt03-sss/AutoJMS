using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AutoJMS.ModuleSystem
{
    public class ModuleEntry
    {
        [JsonPropertyName("name")] public string Name { get; set; }
        [JsonPropertyName("version")] public string Version { get; set; }
        [JsonPropertyName("file")] public string File { get; set; }
        [JsonPropertyName("sha256")] public string Sha256 { get; set; }
        [JsonPropertyName("signature")] public string Signature { get; set; }
        [JsonPropertyName("requires")] public List<string> Requires { get; set; } = new();
        [JsonPropertyName("required")] public bool Required { get; set; }
    }

    public class ModulesManifest
    {
        [JsonPropertyName("manifestVersion")] public string ManifestVersion { get; set; } = "1.0";
        [JsonPropertyName("appVersion")] public string AppVersion { get; set; }
        [JsonPropertyName("modules")] public List<ModuleEntry> Modules { get; set; } = new();

        private static readonly JsonSerializerOptions JsonOpts = new()
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        };

        public static ModulesManifest FromJson(string json)
        {
            return JsonSerializer.Deserialize<ModulesManifest>(json, JsonOpts) ?? new ModulesManifest();
        }

        public string ToJson()
        {
            return JsonSerializer.Serialize(this, JsonOpts);
        }

        public ModuleEntry GetModule(string name)
        {
            return Modules.Find(m => string.Equals(m.Name, name, StringComparison.OrdinalIgnoreCase));
        }

        public void Merge(ModulesManifest remote)
        {
            foreach (var remoteEntry in remote.Modules)
            {
                var idx = Modules.FindIndex(m =>
                    string.Equals(m.Name, remoteEntry.Name, StringComparison.OrdinalIgnoreCase));
                if (idx >= 0)
                    Modules[idx] = remoteEntry;
                else
                    Modules.Add(remoteEntry);
            }
        }

        public List<ModuleEntry> ComputeDelta(ModulesManifest current)
        {
            var delta = new List<ModuleEntry>();
            foreach (var entry in Modules)
            {
                var local = current.GetModule(entry.Name);
                if (local == null ||
                    !string.Equals(local.Version, entry.Version, StringComparison.Ordinal) ||
                    !string.Equals(local.Sha256, entry.Sha256, StringComparison.OrdinalIgnoreCase))
                {
                    if (!string.IsNullOrWhiteSpace(entry.File) || entry.Required)
                        delta.Add(entry);
                }
            }
            return delta;
        }
    }
}

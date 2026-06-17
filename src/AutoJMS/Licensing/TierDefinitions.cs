using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AutoJMS;

public class TierDefinitions
{
    [JsonPropertyName("schemaVersion")]
    public int SchemaVersion { get; set; } = 1;

    [JsonPropertyName("updatedAt")]
    public string UpdatedAt { get; set; }

    [JsonPropertyName("tiers")]
    public Dictionary<string, TierConfig> Tiers { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public TierConfig GetTier(string tier)
    {
        if (Tiers == null || Tiers.Count == 0) return TierConfig.DefaultBase;

        string key = tier?.ToUpperInvariant() ?? "BASE";
        if (Tiers.TryGetValue(key, out var config))
        {
            if (!string.IsNullOrWhiteSpace(config.Inherits) &&
                Tiers.TryGetValue(config.Inherits, out var parent))
            {
                return MergeWithParent(parent, config);
            }
            return config;
        }

        return Tiers.TryGetValue("BASE", out var baseCfg) ? baseCfg : TierConfig.DefaultBase;
    }

    public bool HasForm(string tier, string formName)
    {
        var config = GetTier(tier);
        return config.Forms?.Any(f =>
            string.Equals(f.Name, formName, StringComparison.OrdinalIgnoreCase)) == true;
    }

    public List<TierFormDefinition> GetForms(string tier)
    {
        var config = GetTier(tier);
        return config.Forms ?? new List<TierFormDefinition>();
    }

    private static TierConfig MergeWithParent(TierConfig parent, TierConfig child)
    {
        return new TierConfig
        {
            Inherits = null,
            Tabs = child.Tabs?.Count > 0 ? child.Tabs : parent.Tabs,
            Forms = child.Forms?.Count > 0 ? child.Forms : parent.Forms,
            Modules = child.Modules?.Count > 0 ? child.Modules : parent.Modules
        };
    }

    public static TierDefinitions FromJson(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new TierDefinitions();
        try
        {
            return JsonSerializer.Deserialize<TierDefinitions>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                ReadCommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true
            }) ?? new TierDefinitions();
        }
        catch (Exception ex)
        {
            AppLogger.Warning($"TierDefinitions: parse failed: {ex.Message}");
            return new TierDefinitions();
        }
    }

    public static TierDefinitions LoadFromFile()
    {
        try
        {
            var path = System.IO.Path.Combine(AppPaths.InstallDir, "tier-definitions.json");
            if (System.IO.File.Exists(path))
            {
                var json = System.IO.File.ReadAllText(path);
                return FromJson(json);
            }
        }
        catch { }
        return new TierDefinitions();
    }
}

public class TierConfig
{
    [JsonPropertyName("inherits")]
    public string Inherits { get; set; }

    [JsonPropertyName("tabs")]
    public List<string> Tabs { get; set; } = new();

    [JsonPropertyName("forms")]
    public List<TierFormDefinition> Forms { get; set; } = new();

    [JsonPropertyName("modules")]
    public List<string> Modules { get; set; } = new();

    [JsonIgnore]
    public List<string> BackgroundForms =>
        Forms?.Where(f => string.Equals(f.Type, "VISIBLE_FORM", StringComparison.OrdinalIgnoreCase))
              .Select(f => f.Name)
              .ToList() ?? new List<string>();

    public static TierConfig DefaultBase => new()
    {
        Tabs = new List<string> { "HOME", "DKCH", "TRACKING", "PRINT", "ABOUT" },
        Forms = new List<TierFormDefinition>(),
        Modules = new List<string>()
    };
}

public class TierFormDefinition
{
    [JsonPropertyName("name")]
    public string Name { get; set; }

    [JsonPropertyName("type")]
    public string Type { get; set; } = "VISIBLE_FORM";

    [JsonPropertyName("launch")]
    public string Launch { get; set; } = "AFTER_MAINFORM_SHOWN";

    [JsonPropertyName("fetchApiAfterAuthToken")]
    public bool FetchApiAfterAuthToken { get; set; } = true;
}

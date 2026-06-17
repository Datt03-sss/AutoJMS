using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AutoJMS;

public sealed class RuntimePolicyDocument
{
    [JsonPropertyName("schemaVersion")]
    public int SchemaVersion { get; set; } = 1;

    [JsonPropertyName("updatedAt")]
    public string UpdatedAt { get; set; } = "";

    [JsonPropertyName("tier")]
    public string Tier { get; set; } = "BASE";

    [JsonPropertyName("features")]
    public Dictionary<string, JsonElement> Features { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    [JsonPropertyName("print")]
    public RuntimePrintPolicy Print { get; set; } = new();

    [JsonPropertyName("googleSheets")]
    public RuntimeGoogleSheetsPolicy GoogleSheets { get; set; } = new();

    [JsonPropertyName("fullStack")]
    public RuntimeFullStackPolicy FullStack { get; set; } = new();

    [JsonPropertyName("modulePolicy")]
    public ModulePolicyObj ModulePolicy { get; set; } = new();

    [JsonPropertyName("debugCapture")]
    public RuntimeDebugCapturePolicy DebugCapture { get; set; } = new();

    [JsonIgnore]
    public string Source { get; set; } = "unknown";

    public bool GetFeatureBool(string key, bool defaultValue)
    {
        if (Features == null || !Features.TryGetValue(key, out var value))
            return defaultValue;

        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.String when bool.TryParse(value.GetString(), out bool parsed) => parsed,
            _ => defaultValue
        };
    }

    public string GetFeatureString(string key, string defaultValue)
    {
        if (Features == null || !Features.TryGetValue(key, out var value))
            return defaultValue;

        return value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? defaultValue
            : value.ToString();
    }

    public static RuntimePolicyDocument SafeDefault(string tier = "BASE", string source = "safe-default")
    {
        bool isUltra = string.Equals(tier, "ULTRA", StringComparison.OrdinalIgnoreCase);
        var policy = new RuntimePolicyDocument
        {
            Tier = isUltra ? "ULTRA" : "BASE",
            Source = source,
            Features = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase)
        };

        policy.SetFeature("tabs.home", true);
        policy.SetFeature("tabs.dkch", true);
        policy.SetFeature("tabs.tracking", true);
        policy.SetFeature("tabs.print", true);
        policy.SetFeature("tabs.about", true);
        policy.SetFeature("forms.fullStackOperation", false);
        policy.SetFeature("fullStack.backgroundSync", false);
        policy.SetFeature("googleSheets.enabled", true);
        policy.SetFeature("googleSheets.provider", "TokenBroker");
        policy.SetFeature("print.defaultAutoPrint", true);
        policy.SetFeature("print.enablePrinterPreflight", true);
        policy.SetFeature("debugCapture.enabled", false);

        policy.FullStack.Enabled = false;
        policy.FullStack.BackgroundSync = false;
        policy.FullStack.Launch = "DISABLED";
        policy.GoogleSheets.Provider = "TokenBroker";
        return policy;
    }

    private void SetFeature(string key, bool value)
    {
        Features[key] = JsonSerializer.SerializeToElement(value);
    }

    private void SetFeature(string key, string value)
    {
        Features[key] = JsonSerializer.SerializeToElement(value ?? "");
    }
}

public sealed class RuntimePrintPolicy
{
    [JsonPropertyName("defaultAutoPrint")]
    public bool? DefaultAutoPrint { get; set; }

    [JsonPropertyName("enablePrinterPreflight")]
    public bool? EnablePrinterPreflight { get; set; }

    [JsonPropertyName("maxReprintCount")]
    public int? MaxReprintCount { get; set; }

    [JsonPropertyName("paperWidthInch")]
    public decimal? PaperWidthInch { get; set; }

    [JsonPropertyName("paperHeightInch")]
    public decimal? PaperHeightInch { get; set; }

    [JsonPropertyName("blockWhenQueueBusy")]
    public bool? BlockWhenQueueBusy { get; set; }

    [JsonPropertyName("blockWhenQueueHasErrorJob")]
    public bool? BlockWhenQueueHasErrorJob { get; set; }

    [JsonPropertyName("blockWhenPrinterPaused")]
    public bool? BlockWhenPrinterPaused { get; set; }

    [JsonPropertyName("blockWhenPrinterOffline")]
    public bool? BlockWhenPrinterOffline { get; set; }
}

public sealed class RuntimeGoogleSheetsPolicy
{
    [JsonPropertyName("enabled")]
    public bool? Enabled { get; set; }

    [JsonPropertyName("provider")]
    public string Provider { get; set; } = "TokenBroker";

    [JsonPropertyName("allowLegacyLocalFallback")]
    public bool? AllowLegacyLocalFallback { get; set; }

    [JsonPropertyName("archiveLocalSecretAfterSuccess")]
    public bool? ArchiveLocalSecretAfterSuccess { get; set; }

    [JsonPropertyName("deleteLocalSecretAfterBrokerSuccess")]
    public bool? DeleteLocalSecretAfterBrokerSuccess { get; set; }

    [JsonPropertyName("tokenRefreshSkewMinutes")]
    public int? TokenRefreshSkewMinutes { get; set; }
}

public sealed class RuntimeFullStackPolicy
{
    [JsonPropertyName("enabled")]
    public bool? Enabled { get; set; }

    [JsonPropertyName("launch")]
    public string Launch { get; set; } = "DISABLED";

    [JsonPropertyName("backgroundSync")]
    public bool? BackgroundSync { get; set; }

    [JsonPropertyName("localDbEnabled")]
    public bool? LocalDbEnabled { get; set; }
}

public sealed class RuntimeDebugCapturePolicy
{
    [JsonPropertyName("enabled")]
    public bool? Enabled { get; set; }

    [JsonPropertyName("slowApiThresholdMs")]
    public int? SlowApiThresholdMs { get; set; }
}

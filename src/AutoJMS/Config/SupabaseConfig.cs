using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace AutoJMS
{
    public class SupabaseManifestUrls
    {
        [JsonPropertyName("appManifest")]
        public string AppManifest { get; set; } = "manifest/app-manifest.json";

        [JsonPropertyName("versionLatest")]
        public string VersionLatest { get; set; } = "manifest/version-latest.json";

        [JsonPropertyName("hashManifest")]
        public string HashManifest { get; set; } = "manifest/hash-manifest.json";

        [JsonPropertyName("selectorUpdateManifest")]
        public string SelectorUpdateManifest { get; set; } = "selector-updates/selector-update-manifest.json";

        [JsonPropertyName("tierDefinitions")]
        public string TierDefinitions { get; set; } = "manifest/tier-definitions.json";

        [JsonPropertyName("publicConfig")]
        public string PublicConfig { get; set; } = "configs/public-config.json";

        [JsonPropertyName("runtimePolicy")]
        public string RuntimePolicy { get; set; } = "configs/runtime-policy.json";

        [JsonPropertyName("featurePolicy")]
        public string FeaturePolicy { get; set; } = "manifest/feature-policy.json";

        [JsonPropertyName("googleSheetsPolicy")]
        public string GoogleSheetsPolicy { get; set; } = "manifest/google-sheets-policy.json";

        [JsonPropertyName("printPolicy")]
        public string PrintPolicy { get; set; } = "manifest/print-policy.json";

        [JsonPropertyName("fullStackPolicy")]
        public string FullStackPolicy { get; set; } = "manifest/fullstack-policy.json";

        [JsonPropertyName("debugCapturePolicy")]
        public string DebugCapturePolicy { get; set; } = "manifest/debug-capture-policy.json";
    }

    public class SupabaseReleasesConfig
    {
        [JsonPropertyName("stableFeed")]
        public string StableFeed { get; set; }

        [JsonPropertyName("betaFeed")]
        public string BetaFeed { get; set; }
    }

    public class SupabaseLicenseConfig
    {
        [JsonPropertyName("baseUrl")]
        public string BaseUrl { get; set; }

        [JsonPropertyName("projectUrl")]
        public string ProjectUrl { get; set; }

        [JsonPropertyName("anonKey")]
        public string AnonKey { get; set; }

        [JsonPropertyName("manifests")]
        public SupabaseManifestUrls Manifests { get; set; } = new();

        [JsonPropertyName("releases")]
        public SupabaseReleasesConfig Releases { get; set; } = new();
    }

    public class LicenseSubObject
    {
        [JsonPropertyName("status")]
        public string Status { get; set; } = "active";

        [JsonPropertyName("tier")]
        public string Tier { get; set; } = "BASE";

        [JsonPropertyName("skipHashCheck")]
        public bool SkipHashCheck { get; set; } = false;

        [JsonPropertyName("modulePolicy")]
        public ModulePolicyObj ModulePolicy { get; set; } = new();
    }

    public class ModulePolicyObj
    {
        [JsonPropertyName("autoUpdate")]
        public bool AutoUpdate { get; set; } = false;

        [JsonPropertyName("silentUpdate")]
        public bool SilentUpdate { get; set; } = false;

        [JsonPropertyName("applyOnNextStartup")]
        public bool ApplyOnNextStartup { get; set; } = true;
    }

    public class IntegritySubObject
    {
        [JsonPropertyName("skipHashCheck")]
        public bool SkipHashCheck { get; set; } = false;

        [JsonPropertyName("mode")]
        public string Mode { get; set; } = "HASH_ONLY";
    }

    public class CfgSubObject
    {
        [JsonPropertyName("dataSpreadsheetId")]
        public string DataSpreadsheetId { get; set; }

        [JsonPropertyName("updateChannel")]
        public string UpdateChannel { get; set; } = "stable";

        [JsonPropertyName("supabase")]
        public SupabaseLicenseConfig Supabase { get; set; }
    }

    public class VersionLatest
    {
        [JsonPropertyName("schemaVersion")]
        public int SchemaVersion { get; set; } = 1;

        [JsonPropertyName("updatedAt")]
        public string UpdatedAt { get; set; }

        [JsonPropertyName("channels")]
        public Dictionary<string, VersionChannel> Channels { get; set; } = new();

        public VersionChannel GetChannel(string channel = "stable")
        {
            if (Channels == null || Channels.Count == 0) return null;
            if (Channels.TryGetValue(channel ?? "stable", out var ch)) return ch;
            if (Channels.TryGetValue("stable", out var stable)) return stable;
            return null;
        }
    }

    public class VersionChannel
    {
        [JsonPropertyName("version")]
        public string Version { get; set; }

        [JsonPropertyName("displayVersion")]
        public string DisplayVersion { get; set; }

        [JsonPropertyName("internalBuild")]
        public string InternalBuild { get; set; }

        [JsonPropertyName("velopackChannel")]
        public string VelopackChannel { get; set; }

        [JsonPropertyName("mandatory")]
        public bool Mandatory { get; set; } = false;

        [JsonPropertyName("manualOnly")]
        public bool ManualOnly { get; set; } = true;

        // ── Distribution provider ──────────────────────────────────────────
        // "github"  → binaries live on GitHub Releases (read via Velopack GithubSource)
        // "supabase"/null → legacy Supabase Storage feed (velopackFeedUrl)
        [JsonPropertyName("provider")]
        public string Provider { get; set; }

        [JsonPropertyName("githubRepo")]
        public string GithubRepo { get; set; }

        [JsonPropertyName("githubRepoUrl")]
        public string GithubRepoUrl { get; set; }

        [JsonPropertyName("tag")]
        public string Tag { get; set; }

        [JsonPropertyName("releaseTag")]
        public string ReleaseTag
        {
            get => Tag;
            set => Tag = value;
        }

        [JsonPropertyName("prerelease")]
        public bool Prerelease { get; set; } = false;

        // Legacy Supabase Storage feed (kept for backward compatibility).
        [JsonPropertyName("velopackFeedUrl")]
        public string VelopackFeedUrl { get; set; }

        [JsonPropertyName("setupUrl")]
        public string SetupUrl { get; set; }

        [JsonPropertyName("velopackSetupUrl")]
        public string VelopackSetupUrl { get; set; }

        [JsonPropertyName("releaseNotesUrl")]
        public string ReleaseNotesUrl { get; set; }

        [JsonPropertyName("releaseNotes")]
        public string ReleaseNotes { get; set; }

        /// <summary>True when this channel's binaries are hosted on GitHub Releases.</summary>
        [JsonIgnore]
        public bool IsGithubProvider =>
            string.Equals(Provider, "github", StringComparison.OrdinalIgnoreCase);
    }

    public class SelectorUpdateManifest
    {
        [JsonPropertyName("schemaVersion")]
        public int SchemaVersion { get; set; } = 1;

        [JsonPropertyName("updatedAt")]
        public string UpdatedAt { get; set; }

        [JsonPropertyName("version")]
        public string Version { get; set; }

        [JsonPropertyName("channel")]
        public string Channel { get; set; } = "stable";

        [JsonPropertyName("autoApply")]
        public bool AutoApply { get; set; } = false;

        [JsonPropertyName("manualOnly")]
        public bool ManualOnly { get; set; } = true;

        [JsonPropertyName("applyMode")]
        public string ApplyMode { get; set; } = "next-startup";

        [JsonPropertyName("files")]
        public SelectorUpdateFiles Files { get; set; } = new();
    }

    public class SelectorUpdateFiles
    {
        [JsonPropertyName("runtimeConfig")]
        public SelectorUpdateFileEntry RuntimeConfig { get; set; } = new();

        [JsonPropertyName("selectorConfig")]
        public SelectorUpdateFileEntry SelectorConfig { get; set; } = new();

        [JsonPropertyName("signature")]
        public SelectorUpdateFileEntry Signature { get; set; } = new();
    }

    public class SelectorUpdateFileEntry
    {
        [JsonPropertyName("path")]
        public string Path { get; set; }

        [JsonPropertyName("sha256")]
        public string Sha256 { get; set; }
    }
}

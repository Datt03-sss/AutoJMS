using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace AutoJMS;

public sealed class SupabaseRuntimePolicyService
{
    private static readonly string CacheFilePath = AppPaths.RuntimePolicyCache;
    private static readonly string CacheSecret = $"{Environment.MachineName}|{Environment.UserName}|RuntimePolicy";
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        WriteIndented = true
    };

    private readonly SupabaseManifestService _manifestService;

    public SupabaseRuntimePolicyService(SupabaseManifestService manifestService)
    {
        _manifestService = manifestService ?? throw new ArgumentNullException(nameof(manifestService));
    }

    public async Task<RuntimePolicyDocument> FetchPolicyAsync(
        string tier,
        string appVersion,
        CancellationToken cancellationToken = default)
    {
        string normalizedTier = NormalizeTier(tier);

        var fromSupabase = await TryFetchFromSupabaseAsync(normalizedTier, cancellationToken)
            .ConfigureAwait(false);
        if (fromSupabase != null)
        {
            fromSupabase.Source = "supabase";
            SaveCache(fromSupabase);
            AppLogger.Info($"[Policy] source=supabase tier={fromSupabase.Tier} appVersion={appVersion}");
            return fromSupabase;
        }

        var cached = LoadCachedPolicy(normalizedTier);
        if (cached != null)
        {
            cached.Source = "cache";
            AppLogger.Warning($"[Policy] source=cache tier={cached.Tier}");
            return cached;
        }

        var safe = RuntimePolicyDocument.SafeDefault("BASE", "safe-default");
        AppLogger.Warning("[Policy] source=safe-default tier=BASE");
        return safe;
    }

    private async Task<RuntimePolicyDocument> TryFetchFromSupabaseAsync(
        string tier,
        CancellationToken cancellationToken)
    {
        string[] paths =
        {
            $"configs/runtime-policy.{tier.ToLowerInvariant()}.json",
            _manifestService.Urls?.RuntimePolicy,
            $"manifest/feature-policy.{tier.ToLowerInvariant()}.json",
            _manifestService.Urls?.FeaturePolicy,
            "configs/runtime-policy.json",
            "manifest/feature-policy.json"
        };

        foreach (var path in paths)
        {
            if (string.IsNullOrWhiteSpace(path))
                continue;

            var json = await _manifestService.FetchStringAsync(path, cancellationToken)
                .ConfigureAwait(false);
            var policy = TryParsePolicy(json, tier, path);
            if (policy != null)
                return policy;
        }

        return null;
    }

    private static RuntimePolicyDocument TryParsePolicy(string json, string tier, string sourcePath)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;

        try
        {
            var policy = JsonSerializer.Deserialize<RuntimePolicyDocument>(json, JsonOpts);
            if (policy == null)
                return null;

            policy.Tier = string.IsNullOrWhiteSpace(policy.Tier)
                ? NormalizeTier(tier)
                : NormalizeTier(policy.Tier);
            policy.Source = sourcePath;
            return policy;
        }
        catch (Exception ex)
        {
            AppLogger.Warning($"[Policy] parse failed path={sourcePath} error={ex.Message}");
            return null;
        }
    }

    private static RuntimePolicyDocument LoadCachedPolicy(string tier)
    {
        try
        {
            if (!File.Exists(CacheFilePath))
                return null;

            var encrypted = File.ReadAllText(CacheFilePath);
            var json = SecureConfigCrypto.UnprotectString(encrypted, CacheSecret);
            var policy = JsonSerializer.Deserialize<RuntimePolicyDocument>(json, JsonOpts);
            if (policy == null)
                return null;

            if (!string.Equals(policy.Tier, tier, StringComparison.OrdinalIgnoreCase))
                AppLogger.Warning($"[Policy] cached tier={policy.Tier} differs requested={tier}; using cached policy conservatively");

            return policy;
        }
        catch (Exception ex)
        {
            AppLogger.Warning($"[Policy] cache load failed: {ex.Message}");
            return null;
        }
    }

    private static void SaveCache(RuntimePolicyDocument policy)
    {
        try
        {
            Directory.CreateDirectory(AppPaths.CacheDir);
            string json = JsonSerializer.Serialize(policy, JsonOpts);
            string encrypted = SecureConfigCrypto.ProtectString(json, CacheSecret);
            string tmp = CacheFilePath + ".tmp";
            File.WriteAllText(tmp, encrypted);
            if (File.Exists(CacheFilePath))
                File.Delete(CacheFilePath);
            File.Move(tmp, CacheFilePath);
        }
        catch (Exception ex)
        {
            AppLogger.Warning($"[Policy] cache save failed: {ex.Message}");
        }
    }

    private static string NormalizeTier(string tier)
    {
        string normalized = (tier ?? "BASE").Trim().ToUpperInvariant();
        return string.IsNullOrWhiteSpace(normalized) ? "BASE" : normalized;
    }
}

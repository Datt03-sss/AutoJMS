using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace AutoJMS;

public sealed class VpsRuntimePolicyService
{
    private static readonly string CacheFilePath = AppPaths.RuntimePolicyCache;
    private static readonly string CacheSecret = $"{Environment.MachineName}|{Environment.UserName}|RuntimePolicy";

    /// <summary>
    /// Trần thời gian cho TOÀN BỘ vòng dò policy trên DataHub, không phải cho mỗi
    /// đường.
    ///
    /// Trước đây không có trần nào: <see cref="TryFetchFromDataHubAsync"/> đi tuần tự
    /// 6 đường, mà HttpClient của VpsManifestService đặt Timeout = 15 s
    /// (Updates/VpsManifestService.cs:29). Với một VPS "còn sống nhưng treo" — DNS
    /// blackhole, firewall drop gói, Caddy không trả lời — đó là 6 × 15 = 90 giây, và
    /// lời gọi này chạy đồng bộ bằng .GetAwaiter().GetResult() trên luồng UI
    /// (Program.cs:357), nên 90 giây đó là 90 giây app đứng hình trước khi form đầu
    /// tiên hiện ra.
    ///
    /// 8 giây, không phải 3: trần này rút ngắn thời gian NHƯỜNG cho fallback, và
    /// fallback cuối cùng là SafeDefault("BASE") — nó tắt FullStack, background sync,
    /// inventory sync và database tracking, kể cả cho một license ULTRA hợp lệ, mà
    /// không ghi một dòng error nào. Nghĩa là hạ trần quá tay sẽ đổi "app treo 90 s"
    /// lấy "khách ULTRA thỉnh thoảng mất tính năng" — một lỗi rẻ hơn nhiều về mặt
    /// hiển thị nhưng đắt hơn nhiều về mặt hậu quả. Một VPS khoẻ trả manifest dưới
    /// 1 s, nên 8 s vẫn đủ cho cả 6 đường đi trọn vẹn ở trạng thái bình thường; nó
    /// chỉ cắt đúng trường hợp bệnh lý. Chỉ nên hạ tiếp xuống 3–5 s SAU KHI
    /// SafeDefault thôi hạ cấp tier (xem TIER_LICENSE_AUDIT_REPORT.vi.md §1.4).
    /// </summary>
    private static readonly TimeSpan FetchBudget = TimeSpan.FromSeconds(8);
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        WriteIndented = true
    };

    private readonly VpsManifestService _manifestService;

    public VpsRuntimePolicyService(VpsManifestService manifestService)
    {
        _manifestService = manifestService ?? throw new ArgumentNullException(nameof(manifestService));
    }

    public async Task<RuntimePolicyDocument> FetchPolicyAsync(
        string tier,
        string appVersion,
        CancellationToken cancellationToken = default)
    {
        string normalizedTier = NormalizeTier(tier);

        // Ngân sách chỉ bao vòng dò mạng. Cache và safe-default nằm NGOÀI nó — hai
        // đường đó đọc đĩa hoặc dựng object trong bộ nhớ, huỷ chúng theo đồng hồ
        // mạng là biến một sự cố mạng thành một sự cố khởi động.
        RuntimePolicyDocument fromDataHub;
        using (var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
        {
            budget.CancelAfter(FetchBudget);
            fromDataHub = await TryFetchFromDataHubAsync(normalizedTier, budget.Token)
                .ConfigureAwait(false);
        }

        if (fromDataHub != null)
        {
            fromDataHub.Source = "datahub";
            SaveCache(fromDataHub);
            AppLogger.Info($"[Policy] source=datahub tier={fromDataHub.Tier} appVersion={appVersion}");
            return fromDataHub;
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

    private async Task<RuntimePolicyDocument> TryFetchFromDataHubAsync(
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

            // Phải tự kiểm ngân sách ở đây. VpsManifestService.FetchStringAsync bắt
            // MỌI exception — kể cả OperationCanceledException — rồi trả null, nên
            // token bị huỷ không nổi lên tới đây được. Không có chốt này thì hết
            // ngân sách vẫn chạy nốt 6 vòng, ghi 6 dòng warning, và trong log nó
            // trông y hệt "cả 6 đường đều 404" — tức là chẩn đoán sai hẳn nguyên
            // nhân, từ "VPS chậm/treo" thành "chưa publish seed".
            if (cancellationToken.IsCancellationRequested)
            {
                AppLogger.Warning(
                    $"[Policy] hết ngân sách {FetchBudget.TotalSeconds:0}s khi dò DataHub " +
                    $"(dừng trước path={path}) — chuyển sang cache/safe-default");
                return null;
            }

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

            // Document không được tự nhận nó thuộc tier khác với tier đang yêu cầu.
            // Trước đây tier trong file được tin vô điều kiện, nên một file policy
            // khai tier=ULTRA (kể cả file dùng chung tải về cho máy BASE) sẽ đi tiếp
            // vào TierRuntimePolicy và nâng quyền. Từ chối luôn ở đây.
            if (!string.IsNullOrWhiteSpace(policy.Tier)
                && !string.Equals(NormalizeTier(policy.Tier), NormalizeTier(tier), StringComparison.OrdinalIgnoreCase))
            {
                AppLogger.Warning(
                    $"[Policy] từ chối path={sourcePath}: document khai tier={policy.Tier} " +
                    $"nhưng đang yêu cầu tier={tier}");
                return null;
            }

            // Tier rỗng = file dùng chung, đóng dấu tier đang yêu cầu.
            policy.Tier = NormalizeTier(tier);
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

            // Cache của tier khác thì BỎ, không dùng "một cách bảo thủ". File cache
            // dùng chung một đường dẫn cho mọi tier, nên nếu máy từng chạy ULTRA rồi
            // đổi sang license BASE, cache ULTRA cũ vẫn còn đó. Trả về null để rơi
            // xuống SafeDefault(BASE) thay vì mang policy của tier khác đi tiếp.
            if (!string.Equals(policy.Tier, tier, StringComparison.OrdinalIgnoreCase))
            {
                AppLogger.Warning(
                    $"[Policy] bỏ cache: tier={policy.Tier} khác tier đang yêu cầu={tier}");
                return null;
            }

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

#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace AutoJMS;

public interface ISiteContextProvider
{
    SiteContext Current { get; }
    Task RefreshAsync(CancellationToken cancellationToken);
}

public sealed class SiteContext
{
    public string MiddleCode { get; init; } = "";
    public IReadOnlyList<string> MiddleCodeAliases { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> Segment2Candidates { get; init; } = Array.Empty<string>();
    public bool AllowSegment2Match { get; init; }
    public string Source { get; init; } = "";
    public bool IsValid => !string.IsNullOrWhiteSpace(MiddleCode);
}

public sealed class JourneyEventForValidation
{
    public int EventIndex { get; init; }
    public string ScanNetworkCode { get; init; } = "";
    public string NextNetworkCode { get; init; } = "";
    public string SiteCode { get; init; } = "";
    public string WaybillTrackingContent { get; init; } = "";
    public string RawEventJson { get; init; } = "";
}

public interface IMiddleCodeMatcher
{
    SiteMatchResult MatchJourney(
        string middleCode,
        IReadOnlyList<string> aliases,
        IReadOnlyList<string> segment2Candidates,
        bool allowSegment2,
        IReadOnlyList<JourneyEventForValidation> events);
}

public sealed class SiteMatchResult
{
    public bool IsMatched { get; init; }
    public string MatchType { get; init; } = "NO_MATCH";
    public string MatchedValue { get; init; } = "";
    public string MatchedField { get; init; } = "";
    public int EventIndex { get; init; } = -1;
}

public sealed class SiteContextProvider : ISiteContextProvider
{
    private static readonly object Sync = new();
    // Get() là đường nóng: Dashboard, sync service và ArrivalMonitor gọi nó mỗi nhịp
    // refresh. Getter `Current` bên dưới đọc (và có khi ghi) AutoJMS.json ở MỖI lần
    // truy cập — không được để đường nóng đi qua đó.
    private static string? _cachedMiddleCode;
    // IsHomeStation() chạy trong vòng lặp duyệt hành trình của từng vận đơn — cache
    // cả mã lẫn tên đã học để nó không đọc AutoJMS.json mỗi lần so sánh.
    private static List<string>? _homeCodes;
    private static List<string>? _siteNames;
    private static bool _promptedThisSession;
    private SiteContext _current;

    public SiteContextProvider()
    {
        _current = BuildContext(persistRuntimeToLocal: true);
    }

    public SiteContext Current
    {
        get
        {
            lock (Sync)
            {
                _current = BuildContext(persistRuntimeToLocal: true);
                return _current;
            }
        }
    }

    public Task RefreshAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (Sync)
        {
            _current = BuildContext(persistRuntimeToLocal: true);
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Mã bưu cục (middleCode) đã chuẩn hoá, hoặc "" nếu chưa cấu hình.
    /// Nguồn sự thật duy nhất cho toàn bộ app. Chỉ chạm đĩa ở lần gọi đầu tiên.
    /// </summary>
    public static string Get()
    {
        lock (Sync)
        {
            if (_cachedMiddleCode != null) return _cachedMiddleCode;

            string runtime = NormalizeCode(AppConfig.Current.ActionSiteCode);
            if (runtime.Length == 0 || runtime == "0000")
            {
                // AutoJMS.json giữ lại middleCode của lần verify gần nhất. Đọc lại ở đây
                // chỉ để khỏi lấy lại một giá trị cố định — không phải để chạy offline.
                runtime = NormalizeCode(SettingsManager.Load().MiddleCode);
            }

            _cachedMiddleCode = runtime == "0000" ? "" : runtime;
            return _cachedMiddleCode;
        }
    }

    public static void InvalidateCache()
    {
        lock (Sync)
        {
            _cachedMiddleCode = null;
            _homeCodes = null;
            _siteNames = null;
            _promptedThisSession = false;
        }
    }

    /// <summary>
    /// Scan event này có diễn ra tại bưu cục của mình không? Dùng cho mọi chỗ cần
    /// cắt hành trình tại "lần về kho gần nhất".
    /// So theo MÃ trước — đó là nguồn chắc chắn. Khi mã khớp thì tên đi kèm chính là
    /// tên trạm mình, nên học luôn để sau này nhận ra cả những event J&T chỉ trả tên.
    /// Chưa cấu hình mã bưu cục thì trả false: thà không cắt còn hơn cắt theo trạm người khác.
    /// </summary>
    public static bool IsHomeStation(string? networkCode, string? networkName)
    {
        string home = Get();
        if (home.Length == 0) return false;

        string code = NormalizeCode(networkCode);
        string name = (networkName ?? "").Trim();

        lock (Sync)
        {
            _homeCodes ??= SettingsManager.Load().MiddleCodeAliases
                .Append(home)
                .Select(NormalizeCode)
                .Where(x => x.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (code.Length > 0 && _homeCodes.Contains(code, StringComparer.OrdinalIgnoreCase))
            {
                LearnSiteName(name);
                return true;
            }

            _siteNames ??= SettingsManager.Load().SiteNameAliases.ToList();
            return name.Length > 0
                && _siteNames.Any(n => name.Contains(n, StringComparison.OrdinalIgnoreCase));
        }
    }

    /// <summary>Ghi nhớ tên trạm nhà học được từ dữ liệu J&amp;T. Gọi khi ĐANG giữ <see cref="Sync"/>.</summary>
    private static void LearnSiteName(string name)
    {
        if (name.Length == 0) return;

        _siteNames ??= SettingsManager.Load().SiteNameAliases.ToList();
        if (_siteNames.Contains(name, StringComparer.OrdinalIgnoreCase)) return;

        // Chỉ chạm đĩa khi thật sự có tên mới — thực tế là một, hai lần mỗi máy.
        _siteNames.Add(name);
        var settings = SettingsManager.Load();
        settings.SiteNameAliases = _siteNames.ToList();
        SettingsManager.Save(settings);
        AppLogger.Info($"[SiteContext] hoc ten buu cuc tu du lieu J&T: {name}");
    }

    /// <summary>
    /// Như <see cref="Get"/> nhưng khi chưa cấu hình thì hỏi người dùng rồi lưu lại.
    /// Chỉ hỏi MỘT lần mỗi phiên — các vòng lặp refresh nền không được spam dialog.
    /// Trả về "" nếu user huỷ hoặc không có form chủ để làm owner cho modal.
    /// </summary>
    public static string Require(Form? owner)
    {
        string current = Get();
        if (current.Length > 0) return current;

        // Không có owner nghĩa là đang ở thread nền không có UI — mở modal ở đó
        // sẽ dựng message loop lạc chỗ. Thà bỏ API còn hơn.
        if (owner == null || owner.IsDisposed) return "";

        lock (Sync)
        {
            if (_promptedThisSession) return "";
            _promptedThisSession = true;
        }

        // AInputDialog.Ask trả null khi bấm Huỷ. checkEmpty/maxLength của UIInputDialog
        // bỏ đi được: NormalizeCode ngay dưới đã loại chuỗi rỗng và cắt về đúng dạng mã.
        string? entered = null;
        void Prompt() => entered = AutoJMS.UI.DesignSystem.AInputDialog.Ask(owner,
            "Chưa xác định được mã bưu cục từ license. Vui lòng nhập mã bưu cục (Middle Code):",
            "Mã bưu cục");

        if (owner.InvokeRequired) owner.Invoke((Action)Prompt);
        else Prompt();

        if (entered == null) return "";

        string normalized = NormalizeCode(entered);
        if (normalized.Length == 0 || normalized == "0000") return "";

        // Ghi vào CẢ AppConfig lẫn AutoJMS.json để lần chạy sau có sẵn, khỏi hỏi lại.
        ApplyLicenseMiddleCode(normalized);
        AppLogger.Info($"[SiteContext] middleCode nhap tay source=dialog value={normalized}");
        return normalized;
    }

    public static void ApplyLicenseMiddleCode(string? middleCode)
    {
        string normalized = NormalizeCode(middleCode);
        AppConfig.Current.ActionSiteCode = normalized;
        AppConfig.SaveCurrent();

        var settings = SettingsManager.Load();
        string oldCode = NormalizeCode(settings.MiddleCode);
        bool isStationChange = !string.Equals(oldCode, normalized, StringComparison.OrdinalIgnoreCase);

        settings.MiddleCode = normalized;
        if (isStationChange)
        {
            settings.MiddleCodeAliases = normalized.Length == 0 ? new List<string>() : new List<string> { normalized };
            settings.SiteNameAliases = new List<string>();
            AppLogger.Info($"[SiteContext] doi tram '{oldCode}' -> '{normalized}', reset MiddleCodeAliases va SiteNameAliases");
        }
        else
        {
            settings.MiddleCodeAliases = normalized.Length == 0
                ? new List<string>()
                : DistinctCodes(settings.MiddleCodeAliases.Append(normalized)).ToList();
        }
        SettingsManager.Save(settings);
        InvalidateCache(); // xoa cache sau khi ca hai kho da nhat quan

        AppLogger.Info($"[SiteContext] license middleCode saved source=license value={(string.IsNullOrEmpty(normalized) ? "<empty>" : normalized)}");
    }

    private static SiteContext BuildContext(bool persistRuntimeToLocal)
    {
        var settings = SettingsManager.Load();
        string runtimeMiddleCode = NormalizeCode(AppConfig.Current.ActionSiteCode);
        string settingsMiddleCode = NormalizeCode(settings.MiddleCode);

        string middleCode = FirstNonEmpty(runtimeMiddleCode, settingsMiddleCode);
        string source = !string.IsNullOrWhiteSpace(runtimeMiddleCode)
            ? "runtime/session"
            : (!string.IsNullOrWhiteSpace(settingsMiddleCode) ? "AutoJMS.json" : "none");

        var aliases = DistinctCodes(settings.MiddleCodeAliases.Append(middleCode)).ToList();
        var segments = BuildSegment2Candidates(middleCode, settings.MiddleCodeSegment2, aliases);

        if (persistRuntimeToLocal && !string.IsNullOrWhiteSpace(runtimeMiddleCode)
            && !string.Equals(settingsMiddleCode, runtimeMiddleCode, StringComparison.OrdinalIgnoreCase))
        {
            settings.MiddleCode = runtimeMiddleCode;
            settings.MiddleCodeAliases = aliases;
            SettingsManager.Save(settings);
        }

        return new SiteContext
        {
            MiddleCode = middleCode,
            MiddleCodeAliases = aliases,
            Segment2Candidates = segments,
            AllowSegment2Match = settings.AllowMiddleCodeSegment2Match,
            Source = source
        };
    }

    private static IReadOnlyList<string> BuildSegment2Candidates(string middleCode, string configuredSegment2, IEnumerable<string> aliases)
    {
        var values = new List<string>();
        values.Add(NormalizeCode(configuredSegment2));

        foreach (var alias in aliases)
        {
            string normalizedAlias = NormalizeCode(alias);
            string derived = DeriveSegment2Candidate(normalizedAlias);
            if (!string.IsNullOrWhiteSpace(derived))
                values.Add(derived);
        }

        string derivedFromMiddle = DeriveSegment2Candidate(middleCode);
        if (!string.IsNullOrWhiteSpace(derivedFromMiddle))
            values.Add(derivedFromMiddle);

        return DistinctCodes(values).ToList();
    }

    private static string DeriveSegment2Candidate(string middleCode)
    {
        string value = NormalizeCode(middleCode);
        if (value.Length < 4) return "";

        var match = Regex.Match(value, @"^\d+([A-Z]\d{2,})$", RegexOptions.IgnoreCase);
        return match.Success ? match.Groups[1].Value.ToUpperInvariant() : "";
    }

    private static IEnumerable<string> DistinctCodes(IEnumerable<string> values)
    {
        return values
            .Select(NormalizeCode)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase);
    }

    private static string FirstNonEmpty(params string[] values)
        => values.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x)) ?? "";

    private static string NormalizeCode(string? value)
        => (value ?? "").Trim().ToUpperInvariant();
}

public sealed class MiddleCodeMatcher : IMiddleCodeMatcher
{
    public SiteMatchResult MatchJourney(
        string middleCode,
        IReadOnlyList<string> aliases,
        IReadOnlyList<string> segment2Candidates,
        bool allowSegment2,
        IReadOnlyList<JourneyEventForValidation> events)
    {
        var fullCandidates = BuildFullCandidates(middleCode, aliases);
        for (int i = 0; i < events.Count; i++)
        {
            var item = events[i];
            foreach (var candidate in fullCandidates)
            {
                bool isAlias = !string.Equals(candidate, middleCode, StringComparison.OrdinalIgnoreCase);

                var exact = MatchExactCodeFields(item, candidate, isAlias);
                if (exact.IsMatched) return exact;

                if (ContainsToken(item.WaybillTrackingContent, candidate))
                {
                    return new SiteMatchResult
                    {
                        IsMatched = true,
                        MatchType = isAlias ? "ALIAS_MATCH" : "EXACT_CONTENT_TOKEN",
                        MatchedValue = candidate,
                        MatchedField = "waybillTrackingContent",
                        EventIndex = item.EventIndex
                    };
                }

                if (ContainsToken(item.RawEventJson, candidate))
                {
                    return new SiteMatchResult
                    {
                        IsMatched = true,
                        MatchType = isAlias ? "ALIAS_MATCH" : "EXACT_CONTENT_TOKEN",
                        MatchedValue = candidate,
                        MatchedField = "raw_event_json",
                        EventIndex = item.EventIndex
                    };
                }
            }
        }

        if (!allowSegment2)
            return new SiteMatchResult();

        var segments = (segment2Candidates ?? Array.Empty<string>())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim().ToUpperInvariant())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var segment in segments)
        {
            for (int i = 0; i < events.Count; i++)
            {
                var item = events[i];
                var codeField = MatchSegmentCodeFields(item, segment);
                if (codeField.IsMatched) return codeField;

                if (ContainsToken(item.WaybillTrackingContent, segment))
                {
                    return new SiteMatchResult
                    {
                        IsMatched = true,
                        MatchType = "SEGMENT2_CONTENT_TOKEN",
                        MatchedValue = segment,
                        MatchedField = "waybillTrackingContent",
                        EventIndex = item.EventIndex
                    };
                }
            }
        }

        return new SiteMatchResult();
    }

    private static List<string> BuildFullCandidates(string middleCode, IReadOnlyList<string> aliases)
    {
        return (aliases ?? Array.Empty<string>())
            .Append(middleCode)
            .Select(x => (x ?? "").Trim().ToUpperInvariant())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static SiteMatchResult MatchExactCodeFields(JourneyEventForValidation item, string candidate, bool isAlias)
    {
        if (EqualsCode(item.ScanNetworkCode, candidate))
            return Matched(isAlias ? "ALIAS_MATCH" : "EXACT_SCAN_NETWORK_CODE", candidate, "scanNetworkCode", item.EventIndex);
        if (EqualsCode(item.NextNetworkCode, candidate))
            return Matched(isAlias ? "ALIAS_MATCH" : "EXACT_NEXT_NETWORK_CODE", candidate, "nextNetworkCode", item.EventIndex);
        if (EqualsCode(item.SiteCode, candidate))
            return Matched(isAlias ? "ALIAS_MATCH" : "EXACT_SITE_CODE", candidate, "siteCode", item.EventIndex);
        return new SiteMatchResult();
    }

    private static SiteMatchResult MatchSegmentCodeFields(JourneyEventForValidation item, string segment)
    {
        if (SegmentMatchesCodeField(item.ScanNetworkCode, segment))
            return Matched("SEGMENT2_CODE_FIELD", segment, "scanNetworkCode", item.EventIndex);
        if (SegmentMatchesCodeField(item.NextNetworkCode, segment))
            return Matched("SEGMENT2_CODE_FIELD", segment, "nextNetworkCode", item.EventIndex);
        if (SegmentMatchesCodeField(item.SiteCode, segment))
            return Matched("SEGMENT2_CODE_FIELD", segment, "siteCode", item.EventIndex);
        return new SiteMatchResult();
    }

    private static SiteMatchResult Matched(string type, string value, string field, int eventIndex)
    {
        return new SiteMatchResult
        {
            IsMatched = true,
            MatchType = type,
            MatchedValue = value,
            MatchedField = field,
            EventIndex = eventIndex
        };
    }

    private static bool EqualsCode(string value, string code)
        => string.Equals((value ?? "").Trim(), code, StringComparison.OrdinalIgnoreCase);

    private static bool SegmentMatchesCodeField(string value, string segment)
    {
        string code = (value ?? "").Trim().ToUpperInvariant();
        string token = (segment ?? "").Trim().ToUpperInvariant();
        if (code.Length == 0 || token.Length == 0) return false;
        if (string.Equals(code, token, StringComparison.OrdinalIgnoreCase)) return true;
        return token.Any(char.IsLetter) && code.EndsWith(token, StringComparison.OrdinalIgnoreCase);
    }

    private static bool ContainsToken(string text, string token)
    {
        if (string.IsNullOrWhiteSpace(text) || string.IsNullOrWhiteSpace(token)) return false;

        int index = 0;
        while (index < text.Length)
        {
            int found = text.IndexOf(token, index, StringComparison.OrdinalIgnoreCase);
            if (found < 0) return false;

            int before = found - 1;
            int after = found + token.Length;
            bool leftOk = before < 0 || !char.IsLetterOrDigit(text[before]);
            bool rightOk = after >= text.Length || !char.IsLetterOrDigit(text[after]);
            if (leftOk && rightOk) return true;
            index = found + token.Length;
        }

        return false;
    }
}

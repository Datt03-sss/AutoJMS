#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

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

    public static void ApplyLicenseMiddleCode(string? middleCode)
    {
        string normalized = NormalizeCode(middleCode);
        AppConfig.Current.ActionSiteCode = normalized;
        AppConfig.SaveCurrent();

        var settings = SettingsManager.Load();
        settings.MiddleCode = normalized;
        settings.MiddleCodeAliases = normalized.Length == 0
            ? new List<string>()
            : DistinctCodes(settings.MiddleCodeAliases.Append(normalized)).ToList();
        SettingsManager.Save(settings);

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

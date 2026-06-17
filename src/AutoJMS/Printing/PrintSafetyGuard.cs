#nullable enable
using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace AutoJMS;

public interface IPrintSafetyGuard
{
    PrintSafetyResult ValidateBeforePrint(
        string inputWaybillNo,
        string middleCode,
        TrackingRow? trackingResult);

    PrintSafetyResult ValidateBeforePrint(
        string inputWaybillNo,
        SiteContext siteContext,
        TrackingRow? trackingResult);
}

public sealed class PrintSafetyResult
{
    public bool CanPrint { get; set; }
    public string InputWaybillNo { get; set; } = "";
    public string TrackingWaybillNo { get; set; } = "";
    public string WaybillNo { get; set; } = "";
    public string MiddleCode { get; set; } = "";
    public string MaDoan2 { get; set; } = "";
    public string MatchedScanNetworkCode { get; set; } = "";
    public DateTime? MatchedScanTime { get; set; }
    public int MatchedEventIndex { get; set; } = -1;
    public string ReasonCode { get; set; } = "";
    public string UserMessage { get; set; } = "";
    public int EventCount { get; set; }
    public bool HasMiddleCodeInJourney { get; set; }
    public DateTime CheckedAt { get; set; } = DateTime.Now;
    public string TrackingStatus { get; set; } = "";
    public string RawJsonHash { get; set; } = "";
    public string MiddleCodeSource { get; set; } = "";
    public string Segment2Candidates { get; set; } = "";
    public string MatchType { get; set; } = "";
    public string MatchedValue { get; set; } = "";
    public string MatchedField { get; set; } = "";
}

public sealed class ScanNetworkCodeMatch
{
    public bool IsMatched { get; init; }
    public string MatchedScanNetworkCode { get; init; } = "";
    public DateTime? MatchedScanTime { get; init; }
    public int MatchedEventIndex { get; init; } = -1;
    public int EventCount { get; init; }
}

public sealed class PrintSafetyGuard : IPrintSafetyGuard
{
    private const string WrongOfficeMessage =
        "Đang in sai bưu cục, chuyển sang trang in truyền thống hoặc tắt AutoPrint.";

    public PrintSafetyResult ValidateBeforePrint(
        string inputWaybillNo,
        string middleCode,
        TrackingRow? trackingResult)
    {
        return ValidateBeforePrint(
            inputWaybillNo,
            new SiteContext
            {
                MiddleCode = middleCode ?? "",
                MiddleCodeAliases = Array.Empty<string>(),
                Segment2Candidates = Array.Empty<string>(),
                AllowSegment2Match = false,
                Source = "legacy"
            },
            trackingResult);
    }

    public PrintSafetyResult ValidateBeforePrint(
        string inputWaybillNo,
        SiteContext siteContext,
        TrackingRow? trackingResult)
    {
        siteContext ??= new SiteContext { Source = "none" };

        string normalizedInput = NormalizeWaybill(inputWaybillNo);
        string trackingWaybill = NormalizeTrackingWaybillNo(normalizedInput);
        string normalizedMiddleCode = NormalizeCode(siteContext.MiddleCode);
        string maDoan2 = NormalizeCode(trackingResult?.MaDoan2);
        string middleCodeSource = string.IsNullOrWhiteSpace(siteContext.Source) ? "none" : siteContext.Source;
        string trackingStatus = FirstNonEmpty(
            trackingResult?.ThaoTacCuoi,
            trackingResult?.TrangThaiHienTai,
            trackingResult?.RebackStatus);

        PrintSafetyResult result;
        if (string.IsNullOrWhiteSpace(normalizedInput))
        {
            result = Block("EMPTY_WAYBILL", normalizedInput, trackingWaybill, normalizedMiddleCode, maDoan2, "Mã vận đơn trống. Không in.", trackingStatus, middleCodeSource);
        }
        else if (normalizedInput.Length < 6 || normalizedInput.Length > 30)
        {
            result = Block("INVALID_WAYBILL", normalizedInput, trackingWaybill, normalizedMiddleCode, maDoan2, "Mã vận đơn không hợp lệ. Không in.", trackingStatus, middleCodeSource);
        }
        else if (IsMissing(normalizedMiddleCode))
        {
            result = Block(
                "MIDDLE_CODE_MISSING_CONFIG",
                normalizedInput,
                trackingWaybill,
                normalizedMiddleCode,
                maDoan2,
                "Không thể xác minh bưu cục trước khi in.",
                trackingStatus,
                middleCodeSource);
        }
        else if (trackingResult == null)
        {
            result = Block("TRACKING_FAILED", normalizedInput, trackingWaybill, normalizedMiddleCode, maDoan2, "Không lấy được dữ liệu tracking. Không in.", trackingStatus, middleCodeSource);
        }
        else if (!MatchesWaybill(trackingResult.WaybillNo, trackingWaybill))
        {
            result = Block("WAYBILL_MISMATCH", normalizedInput, trackingWaybill, normalizedMiddleCode, maDoan2, "Dữ liệu tracking không khớp mã vận đơn hiện tại. Không in.", trackingStatus, middleCodeSource);
        }
        else if (trackingResult.SafetyEvents == null || trackingResult.SafetyEvents.Count == 0)
        {
            result = Block("JOURNEY_EMPTY", normalizedInput, trackingWaybill, normalizedMiddleCode, maDoan2, "Tracking không có dữ liệu hành trình. Không in.", trackingStatus, middleCodeSource);
        }
        else
        {
            var scanNetworkMatch = FindScanNetworkCodeMatch(trackingResult, normalizedMiddleCode);
            if (scanNetworkMatch.IsMatched)
            {
                result = AllowScanNetworkCode(
                    normalizedInput,
                    trackingWaybill,
                    normalizedMiddleCode,
                    maDoan2,
                    scanNetworkMatch,
                    trackingStatus,
                    middleCodeSource,
                    trackingResult);
            }
            else if (!IsMissing(maDoan2) && string.Equals(maDoan2, normalizedMiddleCode, StringComparison.OrdinalIgnoreCase))
            {
                result = new PrintSafetyResult
                {
                    CanPrint = true,
                    InputWaybillNo = normalizedInput,
                    TrackingWaybillNo = trackingWaybill,
                    WaybillNo = normalizedInput,
                    MiddleCode = normalizedMiddleCode,
                    MaDoan2 = maDoan2,
                    ReasonCode = "MADOAN2_MATCHED",
                    UserMessage = "OK",
                    EventCount = scanNetworkMatch.EventCount,
                    HasMiddleCodeInJourney = true,
                    CheckedAt = DateTime.Now,
                    TrackingStatus = trackingStatus,
                    MiddleCodeSource = middleCodeSource,
                    MatchType = "MADOAN2_MATCHED",
                    MatchedValue = maDoan2,
                    MatchedField = nameof(TrackingRow.MaDoan2),
                    RawJsonHash = ComputeTrackingHash(trackingResult)
                };
            }
            else if (IsMissing(maDoan2))
            {
                result = Block(
                    "MADOAN2_MISSING_IN_TRACKING",
                    normalizedInput,
                    trackingWaybill,
                    normalizedMiddleCode,
                    maDoan2,
                    WrongOfficeMessage,
                    trackingStatus,
                    middleCodeSource,
                    eventCount: scanNetworkMatch.EventCount);
            }
            else
            {
                result = Block(
                    "MADOAN2_MISMATCH",
                    normalizedInput,
                    trackingWaybill,
                    normalizedMiddleCode,
                    maDoan2,
                    WrongOfficeMessage,
                    trackingStatus,
                    middleCodeSource,
                    eventCount: scanNetworkMatch.EventCount);
            }
        }

        _ = Task.Run(() => WriteAudit(result));
        AppLogger.Info(
            $"[PrintSafety] input={result.InputWaybillNo} tracking={result.TrackingWaybillNo} middleCode={Display(result.MiddleCode)} " +
            $"maDoan2={Display(result.MaDoan2)} scanNetworkCode={Display(result.MatchedScanNetworkCode)} eventIndex={result.MatchedEventIndex} " +
            $"canPrint={result.CanPrint} reason={result.ReasonCode} source={result.MiddleCodeSource}");
        return result;
    }

    private static PrintSafetyResult Block(
        string reasonCode,
        string inputWaybillNo,
        string trackingWaybillNo,
        string middleCode,
        string maDoan2,
        string message,
        string trackingStatus,
        string middleCodeSource,
        int eventCount = 0)
    {
        return new PrintSafetyResult
        {
            CanPrint = false,
            InputWaybillNo = inputWaybillNo ?? "",
            TrackingWaybillNo = trackingWaybillNo ?? "",
            WaybillNo = inputWaybillNo ?? "",
            MiddleCode = middleCode ?? "",
            MaDoan2 = maDoan2 ?? "",
            ReasonCode = reasonCode,
            UserMessage = message,
            EventCount = eventCount,
            HasMiddleCodeInJourney = false,
            CheckedAt = DateTime.Now,
            TrackingStatus = trackingStatus ?? "",
            MiddleCodeSource = middleCodeSource ?? "",
            MatchedEventIndex = -1,
            MatchType = "NO_MATCH",
            RawJsonHash = ""
        };
    }

    private static PrintSafetyResult AllowScanNetworkCode(
        string inputWaybillNo,
        string trackingWaybillNo,
        string middleCode,
        string maDoan2,
        ScanNetworkCodeMatch match,
        string trackingStatus,
        string middleCodeSource,
        TrackingRow trackingResult)
    {
        return new PrintSafetyResult
        {
            CanPrint = true,
            InputWaybillNo = inputWaybillNo,
            TrackingWaybillNo = trackingWaybillNo,
            WaybillNo = inputWaybillNo,
            MiddleCode = middleCode,
            MaDoan2 = maDoan2,
            MatchedScanNetworkCode = NormalizeCode(match.MatchedScanNetworkCode),
            MatchedScanTime = match.MatchedScanTime,
            MatchedEventIndex = match.MatchedEventIndex,
            ReasonCode = "SCAN_NETWORK_CODE_MATCHED",
            UserMessage = "OK",
            EventCount = match.EventCount,
            HasMiddleCodeInJourney = true,
            CheckedAt = DateTime.Now,
            TrackingStatus = trackingStatus,
            MiddleCodeSource = middleCodeSource,
            MatchType = "SCAN_NETWORK_CODE_MATCHED",
            MatchedValue = NormalizeCode(match.MatchedScanNetworkCode),
            MatchedField = nameof(TrackingSafetyEvent.ScanNetworkCode),
            RawJsonHash = ComputeTrackingHash(trackingResult)
        };
    }

    private static void WriteAudit(PrintSafetyResult result)
    {
        try
        {
            Directory.CreateDirectory(AppPaths.LogsDir);
            string path = Path.Combine(AppPaths.LogsDir, "print-safety-audit.log");
            string action = result.CanPrint ? "PRINT_ALLOWED" : "PRINT_BLOCKED";
            string line =
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}\t{result.InputWaybillNo}\t{result.TrackingWaybillNo}\t{result.MiddleCode}\t{result.MaDoan2}\t" +
                $"{result.MatchedScanNetworkCode}\t{FormatScanTime(result.MatchedScanTime)}\t{result.MatchedEventIndex}\t{action}\t{result.ReasonCode}\t{result.EventCount}\t{result.TrackingStatus}\t{result.RawJsonHash}\t" +
                $"{result.MiddleCodeSource}\t{result.MatchType}\t{result.MatchedValue}\t{result.MatchedField}{Environment.NewLine}";
            File.AppendAllText(path, line);
        }
        catch (Exception ex)
        {
            AppLogger.Warning($"[PrintSafety] audit write failed: {ex.Message}");
        }
    }

    private static bool MatchesWaybill(string? marker, string waybill)
        => string.Equals(NormalizeBaseWaybill(marker), NormalizeBaseWaybill(waybill), StringComparison.OrdinalIgnoreCase);

    private static string NormalizeWaybill(string? value)
        => (value ?? "").Trim().ToUpperInvariant();

    private static string NormalizeCode(string? value)
        => (value ?? "").Trim().ToUpperInvariant();

    private static string NormalizeBaseWaybill(string? value)
    {
        string normalized = NormalizeWaybill(value);
        int hyphen = normalized.IndexOf('-');
        return hyphen > 0 ? normalized[..hyphen] : normalized;
    }

    private static string NormalizeTrackingWaybillNo(string? input)
        => NormalizeBaseWaybill(input);

    private static bool IsMissing(string? value)
    {
        string normalized = NormalizeCode(value);
        return string.IsNullOrWhiteSpace(normalized)
               || normalized == "-"
               || normalized == "--"
               || normalized == " "
               || normalized == "NULL";
    }

    private static string FirstNonEmpty(params string?[] values)
        => values.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x))?.Trim() ?? "";

    private static string ComputeTrackingHash(TrackingRow? row)
    {
        if (row == null) return "";
        string value = string.Join("|",
            row.WaybillNo,
            row.MaDoan2,
            row.MaDoanFull,
            row.ThaoTacCuoi,
            row.TrangThaiHienTai,
            row.ThoiGianThaoTac,
            string.Join(";", row.SafetyEvents?.Select(e => $"{e.ScanTime}:{e.ScanNetworkCode}") ?? Enumerable.Empty<string>()));
        byte[] bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static ScanNetworkCodeMatch FindScanNetworkCodeMatch(TrackingRow row, string middleCode)
    {
        var events = (row.SafetyEvents ?? new System.Collections.Generic.List<TrackingSafetyEvent>())
            .Select((eventRow, originalIndex) => new
            {
                Event = eventRow,
                OriginalIndex = originalIndex,
                ParsedTime = ParseScanTime(eventRow.ScanTime)
            })
            .OrderByDescending(x => x.ParsedTime ?? DateTime.MinValue)
            .ToList();

        var match = events.FirstOrDefault(x =>
            string.Equals(NormalizeCode(x.Event.ScanNetworkCode), middleCode, StringComparison.OrdinalIgnoreCase));
        if (match == null)
        {
            return new ScanNetworkCodeMatch
            {
                IsMatched = false,
                EventCount = events.Count
            };
        }

        return new ScanNetworkCodeMatch
        {
            IsMatched = true,
            MatchedScanNetworkCode = NormalizeCode(match.Event.ScanNetworkCode),
            MatchedScanTime = match.ParsedTime,
            MatchedEventIndex = match.OriginalIndex,
            EventCount = events.Count
        };
    }

    private static DateTime? ParseScanTime(string? value)
    {
        if (DateTime.TryParse(value, out var parsed)) return parsed;
        return null;
    }

    private static string FormatScanTime(DateTime? value)
        => value?.ToString("yyyy-MM-dd HH:mm:ss") ?? "";

    private static string Display(string? value)
        => string.IsNullOrWhiteSpace(value) ? "<empty>" : value.Trim();
}

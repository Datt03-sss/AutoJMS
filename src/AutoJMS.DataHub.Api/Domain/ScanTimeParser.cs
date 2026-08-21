using System.Globalization;
using System.Text.RegularExpressions;

namespace AutoJMS.DataHub.Api.Domain;

/// <summary>
/// Parses the timestamp supplied by JMS without consulting the host timezone or
/// the current clock. Naive JMS values are local Vietnam time and are normalized
/// to UTC before they enter the database/reducer.
/// </summary>
public static partial class ScanTimeParser
{
    public const string InvalidScanTimeCode = "INVALID_SCAN_TIME";
    private static readonly TimeSpan VietnamOffset = TimeSpan.FromHours(7);

    public static ScanTimeParseResult Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return ScanTimeParseResult.Invalid("scanTime is required.");

        var candidate = value.Trim();
        if (DateTime.TryParseExact(
                candidate,
                "yyyy-MM-dd HH:mm:ss",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var naive))
        {
            var unspecified = DateTime.SpecifyKind(naive, DateTimeKind.Unspecified);
            return ScanTimeParseResult.Valid(new DateTimeOffset(unspecified, VietnamOffset).ToUniversalTime());
        }

        // DateTimeOffset's permissive parser treats an offsetless ISO value as
        // local time. Require an explicit Z/offset first so that behavior cannot
        // accidentally depend on the VPS timezone.
        if (HasExplicitOffset(candidate)
            && DateTimeOffset.TryParse(
                candidate,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.RoundtripKind,
                out var offsetValue))
        {
            return ScanTimeParseResult.Valid(offsetValue.ToUniversalTime());
        }

        return ScanTimeParseResult.Invalid("scanTime must be yyyy-MM-dd HH:mm:ss in Asia/Ho_Chi_Minh or ISO-8601 with Z/offset.");
    }

    public static bool TryParse(string? value, out DateTimeOffset utcValue)
    {
        var result = Parse(value);
        utcValue = result.UtcValue ?? default;
        return result.Success;
    }

    public static DateTimeOffset ParseRequired(string value)
    {
        var result = Parse(value);
        if (!result.Success)
            throw new FormatException(result.ErrorMessage);
        return result.UtcValue!.Value;
    }

    private static bool HasExplicitOffset(string value)
        => ExplicitOffsetRegex().IsMatch(value);

    [GeneratedRegex("(?:[zZ]|[+-]\\d{2}:?\\d{2})$")]
    private static partial Regex ExplicitOffsetRegex();
}

public sealed record ScanTimeParseResult(
    bool Success,
    DateTimeOffset? UtcValue,
    string? ErrorCode,
    string? ErrorMessage)
{
    public static ScanTimeParseResult Valid(DateTimeOffset value)
        => new(true, value.ToUniversalTime(), null, null);

    public static ScanTimeParseResult Invalid(string message)
        => new(false, null, ScanTimeParser.InvalidScanTimeCode, message);
}

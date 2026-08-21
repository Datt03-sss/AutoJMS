using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace AutoJMS.DataHub.Api.Domain;

public static class EventFingerprintV1
{
    public const string VersionPrefix = "v1:";

    public static string Compute(JmsObservation observation)
    {
        ArgumentNullException.ThrowIfNull(observation);
        var occurredAt = ScanTimeParser.ParseRequired(observation.ScanTime);
        return Compute(observation, occurredAt);
    }

    public static string Compute(JmsObservation observation, DateTimeOffset eventOccurredAt)
    {
        ArgumentNullException.ThrowIfNull(observation);
        if (observation.SiteId == Guid.Empty) throw new ArgumentException("SiteId is required.", nameof(observation));
        if (string.IsNullOrWhiteSpace(observation.WaybillNo)) throw new ArgumentException("WaybillNo is required.", nameof(observation));

        var fields = new List<string>(19)
        {
            observation.SiteId.ToString("D", CultureInfo.InvariantCulture),
            Normalize(observation.WaybillNo),
            eventOccurredAt.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
            FormatCode(observation.Code),
            Normalize(observation.Status),
            Normalize(observation.ScanTypeName),
            Normalize(observation.ScanNetworkCode),
            Normalize(observation.ScanByCode),
            Normalize(observation.PackageNumber),
            Normalize(observation.TaskCode)
        };

        for (var i = 1; i <= 9; i++)
            fields.Add(Normalize(observation.GetRemark(i)));

        var canonical = BuildLengthPrefixed(fields);
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(canonical));
        return VersionPrefix + Convert.ToHexString(digest).ToLowerInvariant();
    }

    public static string BuildCanonical(JmsObservation observation)
    {
        ArgumentNullException.ThrowIfNull(observation);
        var occurredAt = ScanTimeParser.ParseRequired(observation.ScanTime);
        return BuildCanonical(observation, occurredAt);
    }

    public static string BuildCanonical(JmsObservation observation, DateTimeOffset eventOccurredAt)
    {
        var values = new List<string>(19)
        {
            observation.SiteId.ToString("D", CultureInfo.InvariantCulture),
            Normalize(observation.WaybillNo),
            eventOccurredAt.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
            FormatCode(observation.Code),
            Normalize(observation.Status),
            Normalize(observation.ScanTypeName),
            Normalize(observation.ScanNetworkCode),
            Normalize(observation.ScanByCode),
            Normalize(observation.PackageNumber),
            Normalize(observation.TaskCode)
        };
        for (var i = 1; i <= 9; i++) values.Add(Normalize(observation.GetRemark(i)));
        return BuildLengthPrefixed(values);
    }

    private static string BuildLengthPrefixed(IEnumerable<string> fields)
        => string.Join("|", fields.Select(field => $"{field.Length.ToString(CultureInfo.InvariantCulture)}:{field}"));

    private static string Normalize(string? value) => value?.Trim() ?? "";

    private static string FormatCode(int? value)
        => value?.ToString(CultureInfo.InvariantCulture) ?? "";
}

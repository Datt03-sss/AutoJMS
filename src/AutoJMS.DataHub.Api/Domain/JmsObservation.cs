using System.Text.Json;

namespace AutoJMS.DataHub.Api.Domain;

/// <summary>
/// Normalized JMS observation fields used by fingerprinting and reduction. The
/// raw JMS envelope may be retained in <see cref="Payload"/>, but it is not part
/// of the business ordering key.
/// </summary>
public sealed record JmsObservation
{
    public Guid SiteId { get; init; }
    public string WaybillNo { get; init; } = "";
    public string ScanTime { get; init; } = "";
    public int? Code { get; init; }
    public string? Status { get; init; }
    public string? ScanTypeName { get; init; }
    public string? ScanNetworkCode { get; init; }
    public string? ScanByCode { get; init; }
    public string? PackageNumber { get; init; }
    public string? TaskCode { get; init; }
    public string? Remark1 { get; init; }
    public string? Remark2 { get; init; }
    public string? Remark3 { get; init; }
    public string? Remark4 { get; init; }
    public string? Remark5 { get; init; }
    public string? Remark6 { get; init; }
    public string? Remark7 { get; init; }
    public string? Remark8 { get; init; }
    public string? Remark9 { get; init; }
    public JsonElement? Payload { get; init; }

    public string? GetRemark(int number) => number switch
    {
        1 => Remark1,
        2 => Remark2,
        3 => Remark3,
        4 => Remark4,
        5 => Remark5,
        6 => Remark6,
        7 => Remark7,
        8 => Remark8,
        9 => Remark9,
        _ => throw new ArgumentOutOfRangeException(nameof(number))
    };
}

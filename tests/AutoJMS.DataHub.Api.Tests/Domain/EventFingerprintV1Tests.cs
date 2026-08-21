using System.Text.Json;
using AutoJMS.DataHub.Api.Domain;

namespace AutoJMS.DataHub.Api.Tests.Domain;

public sealed class EventFingerprintV1Tests
{
    private static readonly Guid SiteId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    [Fact]
    public void Fingerprint_has_v1_prefix_and_is_stable_for_same_business_fields()
    {
        var first = CreateObservation(uploadTime: "2026-08-17 19:00:00");
        var second = CreateObservation(uploadTime: "2026-08-17 20:00:00");

        var firstFingerprint = EventFingerprintV1.Compute(first);
        var secondFingerprint = EventFingerprintV1.Compute(second);

        Assert.StartsWith("v1:", firstFingerprint, StringComparison.Ordinal);
        Assert.Equal(firstFingerprint, secondFingerprint);
    }

    [Fact]
    public void Upload_time_and_other_raw_payload_fields_are_excluded()
    {
        var first = CreateObservation(uploadTime: "2026-08-17 19:00:00", payloadExtra: "one");
        var second = CreateObservation(uploadTime: "2026-08-17 20:00:00", payloadExtra: "two");

        Assert.Equal(EventFingerprintV1.Compute(first), EventFingerprintV1.Compute(second));
    }

    [Fact]
    public void A_remark_change_changes_the_fingerprint()
    {
        var first = CreateObservation();
        var second = CreateObservation() with { Remark1 = "different" };

        Assert.NotEqual(EventFingerprintV1.Compute(first), EventFingerprintV1.Compute(second));
    }

    private static JmsObservation CreateObservation(string uploadTime = "2026-08-17 19:00:00", string payloadExtra = "one")
        => new()
        {
            SiteId = SiteId,
            WaybillNo = "862229607222",
            ScanTime = "2026-08-17 18:24:22",
            Code = 110,
            Status = "运送中",
            ScanTypeName = "Quét kiện vấn đề",
            ScanNetworkCode = "272C03",
            ScanByCode = "PT272C03035",
            PackageNumber = "B357812596",
            TaskCode = "PTWTJ06",
            Remark1 = "Người mua hẹn lại ngày nhận",
            Remark2 = "1",
            Payload = JsonSerializer.SerializeToElement(new { uploadTime, payloadExtra })
        };
}

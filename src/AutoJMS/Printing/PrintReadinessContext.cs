using System;

namespace AutoJMS;

public sealed class PrintReadinessContext
{
    public string InputWaybillNo { get; init; } = "";
    public string TrackingWaybillNo { get; init; } = "";
    public DateTime VerifiedAt { get; init; }
    public PrintSafetyResult SafetyResult { get; init; } = default!;
    public PrintStatusSnapshot StatusSnapshot { get; init; }

    public bool IsSameWaybill(string inputWaybillNo, string trackingWaybillNo)
    {
        return string.Equals(InputWaybillNo, inputWaybillNo, StringComparison.OrdinalIgnoreCase)
            && string.Equals(TrackingWaybillNo, trackingWaybillNo, StringComparison.OrdinalIgnoreCase);
    }

    public bool IsFresh(TimeSpan ttl)
    {
        return DateTime.Now - VerifiedAt <= ttl;
    }

    public bool CanPrintFast(string inputWaybillNo, string trackingWaybillNo, TimeSpan ttl)
    {
        return IsSameWaybill(inputWaybillNo, trackingWaybillNo)
            && IsFresh(ttl)
            && SafetyResult?.CanPrint == true;
    }
}

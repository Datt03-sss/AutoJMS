using System;

namespace AutoJMS;

public enum PrintStatusRefreshReason
{
    Search,
    BeforePrint,
    AfterPrint
}

public sealed class PrintStatusSnapshot
{
    public string InputWaybillNo { get; set; } = "";
    public string WaybillNo
    {
        get => InputWaybillNo;
        set => InputWaybillNo = value ?? "";
    }

    public string TrackingWaybillNo { get; set; } = "";
    public string CurrentStatusName { get; set; } = "--";
    public DateTime? LastScanTime { get; set; }
    public string LastScanNetworkCode { get; set; } = "--";
    public string LastTrackingContent { get; set; } = "--";
    public string MaDoan2 { get; set; } = "";
    public string ApprovalStatusName { get; set; } = "--";
    public string SenderNetworkCodeAfterPrint { get; set; } = "--";
    public int PrintCount { get; set; }
    public string ApplyStaffName { get; set; } = "--";
    public DateTime RefreshedAt { get; set; } = DateTime.Now;
    public string Source { get; set; } = "FreshApi";
}

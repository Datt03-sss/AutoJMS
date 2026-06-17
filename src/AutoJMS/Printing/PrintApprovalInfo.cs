using System;

namespace AutoJMS;

public sealed class PrintApprovalInfo
{
    public string WaybillNo { get; init; } = "";
    public string StatusName { get; init; } = "";
    public string SenderNetworkCode { get; init; } = "";
    public int? PrintCount { get; init; }
    public string ApplyStaffName { get; init; } = "";
    public DateTime FetchedAt { get; init; } = DateTime.Now;

    public string PrintCountText => PrintCount.HasValue ? PrintCount.Value.ToString() : "-";
}

using System.Collections.Generic;

namespace AutoJMS.FullStack.UI.ThoiHieu
{
    public sealed class ThoiHieuEmployeeRow
    {
        public int Stt { get; set; }
        public string SupervisorName { get; set; } = "";
        public string SiteCode { get; set; } = "214A02";
        public string EmployeeCode { get; set; } = "";
        public string EmployeeName { get; set; } = "";
        public int DeliveryCount { get; set; }
        public int Required91 { get; set; }
        public int Required90 { get; set; }
        public int SignedCount { get; set; }
        public decimal SignedRate { get; set; }
        public Dictionary<int, int?> HourlySigned { get; set; } = new();
    }

    public sealed class ThoiHieuKpiSummary
    {
        public int NewOrders { get; set; }
        public int ScanTttcCount { get; set; }
        public int NotArrivedCount { get; set; }
        public int TransferCount { get; set; }
        public int NeedDeliveryCount { get; set; }
        public int NeedKpi91Count { get; set; }
        public int NeedKpi90Count { get; set; }
    }

    public sealed class ThoiHieuKpiSheetData
    {
        public string SiteCode { get; set; } = "214A02";
        public ThoiHieuKpiSummary Summary { get; set; } = new();
        public List<ThoiHieuEmployeeRow> Rows { get; set; } = new();
    }
}

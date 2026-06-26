using System.Collections.Generic;

namespace AutoJMS.FullStack.Models
{
    public sealed class InventoryFetchResult
    {
        public List<InventoryFetchItem> Items { get; set; } = new();
        public int TotalPages { get; set; }
        public int TotalRecords { get; set; }

        // Outcome classification (consumed by FullStackInventorySyncService).
        public bool Success { get; set; }
        public bool IsNoData { get; set; }
        public string ErrorCode { get; set; } = "";
        public string ErrorMessage { get; set; } = "";
        public string DetectedRecordsPath { get; set; } = "";
        public string DetectedTotalPath { get; set; } = "";
    }

    public sealed class InventoryFetchItem
    {
        public string WaybillNo { get; set; }
        public int PageNo { get; set; }
    }
}

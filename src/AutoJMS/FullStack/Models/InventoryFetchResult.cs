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

        // G0 inventory finalize integrity: true only when EVERY page was fetched OK and the
        // collected count covers the reported total. When false the run is INCOMPLETE and callers
        // MUST NOT mark unseen waybills as "left" (prevents mass-left on a single failed page).
        public bool IsComplete { get; set; }
        public int FailedPages { get; set; }
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

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

    // One Doris2 inventory record (take_ret_mon_detail_doris2). The endpoint returns 48 fields per
    // record; these are the ones the dashboard can use directly, so a freshly seen waybill shows COD,
    // destination and last-operation info WITHOUT waiting for a tracking call. Field names come from a
    // live capture of site 214A03 (tools/test_inventory_fetch.py --inspect) — note that the report uses
    // codMoney / lastopTime / sendScanTime / actualOperatingTime, not codFee / operateTime / sendTime.
    public sealed class InventoryFetchItem
    {
        public string WaybillNo { get; set; }
        public int PageNo { get; set; }

        // Consignment
        public string CustomerCode { get; set; }
        public string DestinationName { get; set; }
        public string CodMoney { get; set; }
        public string OrderSourceName { get; set; }

        // Last operation seen by the report
        public string LastOpTime { get; set; }
        public string LastOpSiteName { get; set; }
        public string ActualOperatingTime { get; set; }
        public string SendScanTime { get; set; }

        // Retention / detention monitoring — the point of this report
        public string RetType { get; set; }
        public string RetDuration { get; set; }
        public string SignTime { get; set; }
        public string BackApplyTime { get; set; }
        public string ProblemRegisterTypeName { get; set; }
        public string IsEnd { get; set; }
        public string IsCancel { get; set; }

        // Bookkeeping
        public string StatDate { get; set; }
        public string UpdateTime { get; set; }
    }
}

using System;

namespace AutoJMS.FullStack.Models
{
    public sealed class FullStackSyncResult
    {
        public long RunId { get; set; }
        public int TotalFetched { get; set; }
        public int TotalPages { get; set; }
        public int NewWaybills { get; set; }
        public int StillInInventory { get; set; }
        public int LeftInventory { get; set; }
        public DateTime StartedAt { get; set; }
        public DateTime FinishedAt { get; set; }

        // Outcome classification (consumed by FullStackOperation tabDash sync handler).
        public bool Success { get; set; }
        public bool IsNoData { get; set; }
        public string ErrorCode { get; set; } = "";
        public string ErrorMessage { get; set; } = "";
    }
}

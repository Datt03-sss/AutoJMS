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
    }
}

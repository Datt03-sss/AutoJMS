using System;

namespace AutoJMS.FullStack.Models
{
    public sealed class InventoryRun
    {
        public long Id { get; set; }
        public string ActionSiteCode { get; set; }
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
        public DateTime StartedAt { get; set; }
        public DateTime? FinishedAt { get; set; }
        public int TotalRecords { get; set; }
        public int TotalPages { get; set; }
        public string Status { get; set; }
        public string ErrorMessage { get; set; }
        public string Source { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}

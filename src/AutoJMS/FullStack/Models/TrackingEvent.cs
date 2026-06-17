using System;

namespace AutoJMS.FullStack.Models
{
    public sealed class TrackingEvent
    {
        public long Id { get; set; }
        public string WaybillNo { get; set; }
        public DateTime? EventTime { get; set; }
        public string Action { get; set; }
        public string Status { get; set; }
        public string SiteCode { get; set; }
        public string SiteName { get; set; }
        public string OperatorCode { get; set; }
        public string OperatorName { get; set; }
        public string RawJson { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}

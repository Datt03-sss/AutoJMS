using System;
using System.Collections.Generic;

namespace AutoJMS.FullStack.Models
{
    public sealed class FullStackOperationMetadata
    {
        public string WaybillNo { get; set; } = string.Empty;
        public bool IsChecked { get; set; }
        public DateTime? CheckedAt { get; set; }
        public string CheckedBy { get; set; } = string.Empty;
        public bool HasTask { get; set; }
        public bool HasOpenTask { get; set; }
        public bool IsEnriched { get; set; }
        public DateTime? EnrichedAt { get; set; }
        public int TrackingEventCount { get; set; }
    }

    public sealed class FullStackJourneyResult
    {
        public string WaybillNo { get; set; } = string.Empty;
        public bool Enriched { get; set; }
        public int TrackingEventCount { get; set; }
        public DateTime? EnrichedAt { get; set; }
        public string Message { get; set; } = string.Empty;
    }

    public sealed class FullStackOperationMetadataSnapshot
    {
        public IReadOnlyDictionary<string, FullStackOperationMetadata> Items { get; set; }
            = new Dictionary<string, FullStackOperationMetadata>(StringComparer.OrdinalIgnoreCase);
    }
}

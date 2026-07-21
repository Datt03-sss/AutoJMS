using System;

namespace AutoJMS.FullStack.Events
{
    /// <summary>
    /// Canonical event types for the event-sourcing-lite pipeline
    /// (docs/roadmap: event-sourcing-lite + ODS).
    /// </summary>
    public static class FullStackEventTypes
    {
        public const string TrackingObserved    = "TrackingObserved";
        public const string InventorySeen       = "InventorySeen";
        public const string InventoryLeft       = "InventoryLeft";
        public const string OrderDetailObserved = "OrderDetailObserved";
        public const string ManualNoteAdded     = "ManualNoteAdded";
        public const string CheckUpdated        = "CheckUpdated";
        public const string DispatchTaskUpdated = "DispatchTaskUpdated";
    }

    /// <summary>Event source origin (who observed/produced this event).</summary>
    public static class FullStackEventSources
    {
        public const string JmsTracking   = "JMS_TRACKING";
        public const string JmsInventory  = "JMS_INVENTORY";
        public const string JmsOrderDetail = "JMS_ORDER_DETAIL";
        public const string Manual        = "MANUAL";
        public const string Cloud         = "CLOUD";
    }

    /// <summary>
    /// Unified event envelope. Append-only; the projection (fs_waybills) is
    /// derived by folding events ordered by <see cref="EventTime"/>.
    ///
    /// Fingerprint is a SHA256 over the *semantic* fields only
    /// (waybill_no|event_type|event_time|payload) — it deliberately excludes
    /// observer metadata (source_client, observed_at, event_id) so two machines
    /// observing the same JMS state produce the same fingerprint and dedupe.
    /// </summary>
    public sealed class FullStackEvent
    {
        public string EventId { get; set; } = Guid.NewGuid().ToString("N");
        public string WaybillNo { get; set; }
        public string EventType { get; set; }
        public DateTime EventTime { get; set; }
        public string Source { get; set; }
        public string SourceClient { get; set; }
        public string Fingerprint { get; set; }
        public string Payload { get; set; } = "{}";
        public DateTime ObservedAt { get; set; } = DateTime.UtcNow;
        public int SchemaVersion { get; set; } = 1;
        // Server-assigned monotonic sequence (remote only; null until pushed/pulled).
        public long? Seq { get; set; }
    }
}

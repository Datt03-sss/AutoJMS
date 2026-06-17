#nullable enable
using System;
using System.Collections.Generic;

namespace AutoJMS.Diagnostics.AppCapture
{
    public sealed class AppCaptureEvent
    {
        public DateTimeOffset Ts { get; set; } = DateTimeOffset.Now;
        public long ElapsedMsFromStart { get; set; }
        public string Category { get; set; } = "";
        public string Source { get; set; } = "";
        public string EventName { get; set; } = "";
        public string CorrelationId { get; set; } = "";
        public string WaybillNo { get; set; } = "";
        public string Route { get; set; } = "";
        public long? DurationMs { get; set; }
        public Dictionary<string, object?> Data { get; set; } = new(StringComparer.OrdinalIgnoreCase);

        public static AppCaptureEvent Create(string category, string source, string eventName, object? data = null)
        {
            var evt = new AppCaptureEvent
            {
                Category = category ?? "",
                Source = source ?? "",
                EventName = eventName ?? ""
            };
            if (data != null)
                evt.Data["payload"] = data;
            return evt;
        }
    }
}


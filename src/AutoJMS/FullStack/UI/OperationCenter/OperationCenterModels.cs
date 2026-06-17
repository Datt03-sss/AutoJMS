using System;
using System.Collections.Generic;
using System.Drawing;

namespace AutoJMS.FullStack.UI.OperationCenter
{
    public sealed class OperationQueueItem
    {
        public string Key { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public int Count { get; set; }
        public Color AccentColor { get; set; } = Color.FromArgb(80, 100, 120);
        public bool Active { get; set; }
    }

    public sealed class OperationQueueSelectedEventArgs : EventArgs
    {
        public OperationQueueSelectedEventArgs(string key)
        {
            Key = key;
        }

        public string Key { get; }
    }

    public sealed class OperationWaybillDetail
    {
        public string WaybillNo { get; set; } = "-";
        public string State { get; set; } = "-";
        public string RiskLevel { get; set; } = "LOW";
        public int RiskScore { get; set; }
        public string SlaStatus { get; set; } = "-";
        public string AgeText { get; set; } = "-";
        public string LastAction { get; set; } = "-";
        public string LastActionTime { get; set; } = "-";
        public string Employee { get; set; } = "-";
        public string Site { get; set; } = "-";
        public string KvdReason { get; set; } = "-";
        public IReadOnlyList<string> RiskReasons { get; set; } = Array.Empty<string>();
        public IReadOnlyList<string> RecommendedActions { get; set; } = Array.Empty<string>();
        public IReadOnlyList<string> Notes { get; set; } = Array.Empty<string>();
        public IReadOnlyList<string> Tasks { get; set; } = Array.Empty<string>();
        public IReadOnlyList<string> Timeline { get; set; } = Array.Empty<string>();
    }

    public sealed class OperationDetailActionEventArgs : EventArgs
    {
        public OperationDetailActionEventArgs(string action, string waybillNo, string value)
        {
            Action = action;
            WaybillNo = waybillNo;
            Value = value;
        }

        public string Action { get; }
        public string WaybillNo { get; }
        public string Value { get; }
    }

    public sealed class OperationGridFilterEventArgs : EventArgs
    {
        public OperationGridFilterEventArgs(string key)
        {
            Key = key ?? string.Empty;
        }

        public string Key { get; }
    }
}

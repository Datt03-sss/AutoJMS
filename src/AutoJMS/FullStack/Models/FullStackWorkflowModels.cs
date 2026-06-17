using System;
using System.Collections.Generic;

namespace AutoJMS.FullStack.Models
{
    public sealed class FullStackNote
    {
        public long Id { get; set; }
        public string WaybillNo { get; set; } = string.Empty;
        public string Note { get; set; } = string.Empty;
        public string CreatedBy { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
    }

    public sealed class FullStackDispatchTask
    {
        public long Id { get; set; }
        public string WaybillNo { get; set; } = string.Empty;
        public string TaskType { get; set; } = string.Empty;
        public int Priority { get; set; }
        public string Status { get; set; } = string.Empty;
        public string AssignedTo { get; set; } = string.Empty;
        public DateTime? DueAt { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? CompletedAt { get; set; }
    }

    public sealed class FullStackWorkflowSnapshot
    {
        public IReadOnlyList<FullStackNote> Notes { get; set; } = Array.Empty<FullStackNote>();
        public IReadOnlyList<FullStackDispatchTask> Tasks { get; set; } = Array.Empty<FullStackDispatchTask>();
    }
}

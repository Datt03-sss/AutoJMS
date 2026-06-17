using AutoJMS.Data;
using System;
using System.Collections.Generic;

namespace AutoJMS.FullStack.UI
{
    public sealed class FullStackDashboardSnapshot
    {
        public List<WaybillDbModel> Rows { get; set; } = new();
        public DateTime? LastSyncAt { get; set; }
        public string DbPath { get; set; }
    }
}

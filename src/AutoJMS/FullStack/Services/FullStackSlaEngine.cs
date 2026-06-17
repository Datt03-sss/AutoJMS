using AutoJMS.Data;
using System;

namespace AutoJMS.FullStack.Services
{
    public sealed class FullStackSlaResult
    {
        public string Status { get; set; } = "UNKNOWN";
        public DateTime? Deadline { get; set; }
    }

    public sealed class FullStackSlaEngine
    {
        public FullStackSlaResult Evaluate(WaybillDbModel row)
        {
            DateTime? basis = TryParse(row?.ThoiGianNhanHang) ?? TryParse(row?.ThoiGianThaoTac);
            if (!basis.HasValue)
                return new FullStackSlaResult { Status = "UNKNOWN" };

            var deadline = basis.Value.AddHours(24);
            var diff = deadline - DateTime.Now;
            string status = diff.TotalHours < 0 ? "OVERDUE" : diff.TotalHours <= 4 ? "URGENT" : "OK";
            return new FullStackSlaResult { Status = status, Deadline = deadline };
        }

        private static DateTime? TryParse(string value) =>
            DateTime.TryParse(value, out var dt) ? dt : null;
    }
}

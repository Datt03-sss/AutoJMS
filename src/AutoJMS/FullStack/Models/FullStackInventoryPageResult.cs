using System;
using System.Collections.Generic;

namespace AutoJMS.FullStack.Models
{
    public sealed class FullStackInventoryPageResult
    {
        public bool Success { get; init; }
        public bool IsNoData { get; init; }
        public string ErrorCode { get; init; } = "";
        public string ErrorMessage { get; init; } = "";

        public int Page { get; init; }
        public int PageSize { get; init; }
        public int TotalRecords { get; init; }
        public int TotalPages { get; init; }

        public IReadOnlyList<string> WaybillNos { get; init; } = Array.Empty<string>();

        public string DetectedRecordsPath { get; init; } = "";
        public string DetectedTotalPath { get; init; } = "";
        public string RawJsonHash { get; init; } = "";
    }
}

using System;
using System.Collections.Generic;

namespace AutoJMS.FullStack.Models
{
    public sealed class WaybillJourneyRow
    {
        public int Stt { get; set; }
        public DateTime? ActionTime { get; set; }
        public DateTime? EventTime { get => ActionTime; set => ActionTime = value; }
        public string EventTimeText => ActionTime?.ToString("yyyy-MM-dd HH:mm:ss") ?? "-";
        public DateTime? UploadedAt { get; set; }
        public DateTime? UploadTime { get => UploadedAt; set => UploadedAt = value; }
        public string UploadTimeText => UploadedAt?.ToString("yyyy-MM-dd HH:mm:ss") ?? "-";
        public string ActionType { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string SiteInfo { get; set; } = string.Empty;
        public string ScanSite { get => SiteInfo; set => SiteInfo = value; }
        public string ContainerCode { get; set; } = string.Empty;
        public string BagNo { get => ContainerCode; set => ContainerCode = value; }
        public string ScanSource { get; set; } = string.Empty;
        public string Weight { get; set; } = string.Empty;
        public string ConvertedWeight { get; set; } = string.Empty;
        public string Volume { get => ConvertedWeight; set => ConvertedWeight = value; }
        public string VolumeWeight { get => ConvertedWeight; set => ConvertedWeight = value; }
        public string AttachmentText { get; set; } = string.Empty;
        public string Severity { get; set; } = string.Empty;
        public string SiteCode { get; set; } = string.Empty;
        public string SiteName { get; set; } = string.Empty;
        public string OperatorCode { get; set; } = string.Empty;
        public string OperatorName { get; set; } = string.Empty;
        public string PackageNumber { get; set; } = string.Empty;
        public string TaskCode { get; set; } = string.Empty;
        public int? ImgType { get; set; }
        public string RawJson { get; set; } = string.Empty;
        public string RawEventJson { get => RawJson; set => RawJson = value; }
    }

    public sealed class JourneyEventViewModel
    {
        public int Stt { get; set; }
        public DateTime? EventTime { get; set; }
        public DateTime? UploadTime { get; set; }
        public string EventTimeText => EventTime?.ToString("yyyy-MM-dd HH:mm:ss") ?? "-";
        public string UploadTimeText => UploadTime?.ToString("yyyy-MM-dd HH:mm:ss") ?? "-";
        public string ActionType { get; set; } = "-";
        public string Description { get; set; } = "-";
        public string ScanSite { get; set; } = "-";
        public string BagNo { get; set; } = "-";
        public string ScanSource { get; set; } = "-";
        public string Weight { get; set; } = "-";
        public string VolumeWeight { get; set; } = "-";
        public string AttachmentText { get; set; } = "-";
        public int? ImgType { get; set; }
        public string RawEventJson { get; set; } = string.Empty;
    }

    public sealed class WaybillJourneyViewModel
    {
        public string WaybillNo { get; set; } = string.Empty;
        public List<WaybillJourneyRow> Rows { get; set; } = new();
        public string MatchedKeyword { get; set; } = string.Empty;
        public int DataCount { get; set; }
        public string Source { get; set; } = string.Empty;
        public DateTime? FetchedAt { get; set; }
        public string ValidationWarning { get; set; } = string.Empty;
        public int? ResponseCode { get; set; }
        public string ResponseMessage { get; set; } = string.Empty;
    }

    public sealed class PodTrackingRawResponse
    {
        public string WaybillNo { get; set; } = string.Empty;
        public string RawJson { get; set; } = string.Empty;
        public int? HttpStatusCode { get; set; }
        public DateTime FetchedAt { get; set; }
    }

    public sealed class WaybillJourneyParseMetadata
    {
        public int? ResponseCode { get; set; }
        public string ResponseMessage { get; set; } = string.Empty;
        public int EventCount { get; set; }
        public int DataCount { get; set; }
        public string FirstEventSummary { get; set; } = string.Empty;
        public string LastEventSummary { get; set; } = string.Empty;
    }

    public sealed class WaybillJourneyRefreshResult
    {
        public bool Success { get; set; }
        public bool AuthExpired { get; set; }
        public string Message { get; set; } = string.Empty;
        public WaybillJourneyViewModel ViewModel { get; set; } = new();
        public string RawJson { get; set; } = string.Empty;
        public DateTime? FetchedAt { get; set; }
        public string Source { get; set; } = string.Empty;
        public bool ResponseMismatch { get; set; }
        public string ApiEndpoint { get; set; } = string.Empty;
        public int? HttpStatusCode { get; set; }
    }

    public sealed class FullStackTrackingJourneyResult
    {
        public bool Success { get; set; }
        public bool AuthExpired { get; set; }
        public bool IsWaybillMatched { get; set; } = true;
        public string Message { get; set; } = string.Empty;
        public string WaybillNo { get; set; } = string.Empty;
        public string RawJson { get; set; } = string.Empty;
        public string ApiEndpoint { get; set; } = string.Empty;
        public int? HttpStatusCode { get; set; }
        public DateTime FetchedAt { get; set; }
        public WaybillJourneyViewModel ViewModel { get; set; } = new();
        public int EventCount => ViewModel?.Rows?.Count ?? 0;
    }

    public sealed class WaybillJourneyRawCache
    {
        public string WaybillNo { get; set; } = string.Empty;
        public string RawJson { get; set; } = string.Empty;
        public int EventCount { get; set; }
        public DateTime? FetchedAt { get; set; }
        public DateTime? ExpiresAt { get; set; }
        public string SourceHash { get; set; } = string.Empty;
        public string LastError { get; set; } = string.Empty;
        public bool HasValue => !string.IsNullOrWhiteSpace(RawJson);
    }
}

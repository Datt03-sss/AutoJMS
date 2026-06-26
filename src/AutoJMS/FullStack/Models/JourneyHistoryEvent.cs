using System;

namespace AutoJMS.FullStack.Models
{
    /// <summary>
    /// One shipping-journey tracking event (from the podTracking keywordList
    /// response), mapped to the columns displayed in "Hành trình vận chuyển".
    /// </summary>
    public sealed class JourneyHistoryEvent
    {
        /// <summary>STT (sequence, newest = 1).</summary>
        public int Stt { get; set; }

        public string WaybillNo { get; set; } = string.Empty;

        /// <summary>Thời gian thao tác (response scanTime).</summary>
        public string ScanTime { get; set; } = string.Empty;

        /// <summary>Thời gian tải lên (response uploadTime).</summary>
        public string UploadTime { get; set; } = string.Empty;

        /// <summary>Loại thao tác (response scanTypeName).</summary>
        public string ScanTypeName { get; set; } = string.Empty;

        /// <summary>Mô tả lịch sử hành trình (response waybillTrackingContent).</summary>
        public string Description { get; set; } = string.Empty;

        /// <summary>Nguồn (response remark6 / source).</summary>
        public string Source { get; set; } = string.Empty;

        /// <summary>Trọng lượng (response weight).</summary>
        public string Weight { get; set; } = string.Empty;

        /// <summary>Raw JSON of this single event (as returned by the API).</summary>
        public string RawJson { get; set; } = string.Empty;

        public DateTime FetchedAt { get; set; }
    }
}

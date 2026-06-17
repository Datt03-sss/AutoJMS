using System;

namespace AutoJMS.FullStack.Models
{
    public sealed class FullStackWaybill
    {
        public string WaybillNo { get; set; }
        public DateTime? FirstSeenAt { get; set; }
        public DateTime? LastSeenAt { get; set; }
        public long? FirstInventoryRunId { get; set; }
        public long? LastInventoryRunId { get; set; }
        public bool IsInCurrentInventory { get; set; }
        public DateTime? LeftInventoryAt { get; set; }
        public string CurrentState { get; set; }
        public string CurrentStatus { get; set; }
        public string LastAction { get; set; }
        public DateTime? LastActionTime { get; set; }
        public string LastSiteCode { get; set; }
        public string LastSiteName { get; set; }
        public string EmployeeCode { get; set; }
        public string EmployeeName { get; set; }
        public string ReceiverName { get; set; }
        public string ReceiverPhoneMasked { get; set; }
        public double AgeHours { get; set; }
        public double DaysInInventory { get; set; }
        public int RiskScore { get; set; }
        public string RiskLevel { get; set; }
        public string RiskReasons { get; set; }
        public string SlaStatus { get; set; }
        public DateTime? SlaDeadline { get; set; }
        public DateTime? LastTrackAt { get; set; }
        public DateTime? NextTrackAt { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }

        public string TrangThaiHienTai { get; set; }
        public string ThaoTacCuoi { get; set; }
        public string ThoiGianThaoTac { get; set; }
        public string ThoiGianYeuCauPhatLai { get; set; }
        public string NhanVienKienVanDe { get; set; }
        public string NguyenNhanKienVanDe { get; set; }
        public string BuuCucThaoTac { get; set; }
        public string NguoiThaoTac { get; set; }
        public string DauChuyenHoan { get; set; }
        public string DiaChiNhanHang { get; set; }
        public string Phuong { get; set; }
        public string NoiDungHangHoa { get; set; }
        public string CODThucTe { get; set; }
        public string PTTT { get; set; }
        public string NhanVienNhanHang { get; set; }
        public string DiaChiLayHang { get; set; }
        public string ThoiGianNhanHang { get; set; }
        public string TenNguoiGui { get; set; }
        public string TrongLuong { get; set; }
        public string MaDoanFull { get; set; }
        public string MaDoan1 { get; set; }
        public string MaDoan2 { get; set; }
        public string MaDoan3 { get; set; }
        public string RebackStatus { get; set; }
        public string InHoanScanTime { get; set; }
        public int PrintCount { get; set; }
        public bool IsActive { get; set; } = true;
        public int TrackingIntervalMins { get; set; } = 30;
    }
}

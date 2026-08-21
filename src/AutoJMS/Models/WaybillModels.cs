using System;

namespace AutoJMS.Data;

public class WaybillDbModel
{
    public string WaybillNo { get; set; }
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
    public DateTime LastTrackedAt { get; set; }
    public DateTime NextTrackAt { get; set; }
}

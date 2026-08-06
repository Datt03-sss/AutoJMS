using System.Collections.Generic;
using System.Threading.Tasks;

namespace AutoJMS;

public interface ITrackingService
{
    Task SearchTrackingAsync(string waybillsText, bool updateMainGrid = true);
    void ClearData();
    void ExportToExcel();
    void ExportSpecial();
    List<TrackingRow> GetAllRows();
    Task<string> GetDKCHHistoryAsync(string waybill);

    /// <summary>
    /// Lấy chi tiết hành trình thô của một mã vận đơn để DKCH tự quyết định
    /// (xem <see cref="DkchJourneyAnalyzer"/>). Trả về null khi không gọi được API
    /// — khác với danh sách rỗng nghĩa là đơn không có hành trình.
    /// </summary>
    Task<List<WaybillDetail>> GetWaybillDetailsAsync(string waybill);

    /// <summary>Dựng đoạn text lịch sử hiển thị từ chi tiết đã có sẵn (không gọi lại API).</summary>
    string BuildDkchHistoryText(string waybill, List<WaybillDetail> details);
}

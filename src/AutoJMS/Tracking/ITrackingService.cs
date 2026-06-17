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
}

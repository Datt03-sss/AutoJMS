using AutoJMS.Data;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Linq;
using System.Threading.Tasks;

namespace AutoJMS
{
    public class LocalTrackingManager
    {
        public static LocalTrackingManager Instance { get; } = new LocalTrackingManager();

        public class DashboardRow : TrackingRow
        {
            public int SoLanNhac { get; set; } = 0;
            public string ThoiGianNhacGanNhat { get; set; } = "";
            public int SoLanDangKyChuyenHoan { get; set; } = 0;
            public int SoLanGiaoLaiHang { get; set; } = 0;
            public string NguonDonDat { get; set; } = "";
            public DateTime LastUpdate { get; set; }
        }

        private readonly List<DashboardRow> _virtualList = new List<DashboardRow>();
        private readonly DataTable _chatDataTable = new DataTable();

        public BindingSource ChatBindingSource { get; private set; }
        public DateTime LastTrackedTime { get; private set; } = DateTime.MinValue;

        private System.Threading.Timer _timer;
        private bool _isTracking = false;
        private List<string> _cachedWaybills = new List<string>();

        private LocalTrackingManager()
        {
            SetupDataTable();
            ChatBindingSource = new BindingSource { DataSource = _chatDataTable };
        }

        private void SetupDataTable()
        {
            _chatDataTable.Columns.Add("Mã vận đơn", typeof(string));
            _chatDataTable.Columns.Add("Tên nhân viên", typeof(string));
            _chatDataTable.Columns.Add("Trạng thái hiện tại", typeof(string));
            _chatDataTable.Columns.Add("Số lần nhắc", typeof(int));
        }

        public List<DashboardRow> GetVirtualList() => _virtualList;

        public void StartAutoTracking(int intervalMinutes = 5)
        {
            _timer?.Dispose();
            _timer = new System.Threading.Timer(async _ => await PerformIncrementalTrackingAsync(), null,
                TimeSpan.FromMinutes(intervalMinutes), TimeSpan.FromMinutes(intervalMinutes));
        }

        public void StopAutoTracking()
        {
            _timer?.Change(Timeout.Infinite, Timeout.Infinite);
        }

        public async Task PerformIncrementalTrackingAsync()
        {
            if (_isTracking || TrackingService == null) return;
            _isTracking = true;

            try
            {
                if (_cachedWaybills.Count == 0)
                    _cachedWaybills = ZaloChatService.GetWaybillsFromPhatLaiStatic();

                if (_cachedWaybills.Count == 0) return;

                string waybillText = string.Join("\n", _cachedWaybills);
                await TrackingService.SearchTrackingAsync(waybillText, updateMainGrid: false);

                var trackedRows = TrackingService.GetAllRows();

                _virtualList.Clear();

                foreach (var r in trackedRows)
                {
                    var newRow = new DashboardRow
                    {
                        WaybillNo = r.WaybillNo,
                        TrangThaiHienTai = r.TrangThaiHienTai ?? "",
                        ThaoTacCuoi = r.ThaoTacCuoi ?? "",
                        NguoiThaoTac = r.NhanVienKienVanDe ?? "",
                        ThoiGianThaoTac = r.ThoiGianThaoTac ?? "",
                        ThoiGianYeuCauPhatLai = r.ThoiGianYeuCauPhatLai ?? "",
                        LastUpdate = DateTime.Now
                    };
                    _virtualList.Add(newRow);
                }

                RefreshDataTable();
                LastTrackedTime = DateTime.Now;
                OnDataUpdated?.Invoke();
            }
            finally
            {
                _isTracking = false;
            }
        }

        // Thay đổi trong LocalTrackingManager.cs
        public void IncrementReminderCount(string waybillNo)
        {
            // CHỈNH SỬA: Dùng trực tiếp _virtualList thay vì GetAllVirtualRows()
            var item = _virtualList.FirstOrDefault(r => r.WaybillNo == waybillNo);

            if (item != null)
            {
                item.SoLanNhac++;
                item.ThoiGianNhacGanNhat = DateTime.Now.ToString("HH:mm:ss");

                RefreshDataTable();
                OnDataUpdated?.Invoke();
            }
        }
    
        
        private void RefreshDataTable()
        {
            _chatDataTable.Rows.Clear();
            foreach (var item in _virtualList)
            {
                _chatDataTable.Rows.Add(
                    item.WaybillNo,
                    item.NguoiThaoTac,
                    item.ThaoTacCuoi,
                    item.SoLanNhac
                );
            }
            ChatBindingSource.ResetBindings(false);
        }

        public event Action OnDataUpdated;

        public WaybillTrackingService TrackingService { get; set; }

    }
}
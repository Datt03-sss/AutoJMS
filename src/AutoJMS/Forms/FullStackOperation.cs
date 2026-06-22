using Sunny.UI;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using AutoJMS.Data;
using AutoJMS.FullStack.Models;
using AutoJMS.FullStack.Services;
using AutoJMS.FullStack.UI;
using AutoJMS.FullStack.UI.OperationCenter;
using AutoJMS.FullStack.UI.ThoiHieu;

namespace AutoJMS
{
    public partial class FullStackOperation : UIForm
    {
        private const string CHROME_USER_AGENT = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36";

        private List<WaybillDbModel> _cloudData = new();
        private List<WaybillDbModel> _lastDashSourceData = new();
        private List<WaybillDbModel> _lastChatSourceData = new();
        private List<string> _dashStatusCache = new();
        private List<string> _chatStatusCache = new();

        private ZaloChatService _zaloChatService;
        private bool _isZaloLoaded = false;
        private bool _isRefreshingStatusCombos = false;
        private System.Windows.Forms.Timer _autoRefreshTimer;
        private readonly CancellationTokenSource _cts = new();
        private TabPage _tabDetail;
        private readonly System.Windows.Forms.Timer _alertCheckTimer = new();
        private int _lastCriticalAlertCount = 0;
        private UILabel[] _detailLabels;
        private Sunny.UI.UIPanel _detailSlaCard;
        private UILabel _detailSlaValue;
        private UILabel _detailAgeValue;

        // Lifecycle guards for the view:
        private volatile bool _uiReady;
        private volatile bool _isClosing;

        // Dash enhancements
        private TextBox _dashSearchBox;
        private DateTimePicker _dashDateFrom;
        private DateTimePicker _dashDateTo;
        private UISymbolButton _dashExportBtn;
        private Label _dashFilterInfo;
        private List<int> _kpiHistory = new();
        private string _lastDataHash = string.Empty;
        private ToolTip _tooltip;
        private bool _isRealtimeStarted = false;
        private readonly FullStackDashboardService _fullStackDashboardService = new();
        private readonly FullStackWorkflowService _fullStackWorkflowService = new();
        private readonly FullStackExportService _fullStackExportService = new();
        private readonly IFullStackJourneyService _fullStackJourneyService = new FullStackJourneyService();
        private readonly IJourneyAttachmentService _journeyAttachmentService = new JourneyAttachmentService();
        private List<WaybillDbModel> _lastFilteredDashRows = new();
        private bool _isSyncRunning = false;
        private string _savedOperationFilter = string.Empty;
        private string _savedOperationSearch = string.Empty;
        private IReadOnlyDictionary<string, FullStackOperationMetadata> _operationMetadata = new Dictionary<string, FullStackOperationMetadata>(StringComparer.OrdinalIgnoreCase);
        private Action<string> _authTokenHandler;

        protected override bool ShowWithoutActivation => true;

        public FullStackOperation()
        {
            ConfigureFormShell();
            BuildUiInCode();
            WireCodeFirstEvents();

            _tooltip = new ToolTip();
            _tooltip.InitialDelay = 500;
            _tooltip.ReshowDelay = 200;
            _tooltip.AutoPopDelay = 10000;

            // Subscribe to auth — will activate runtime when token arrives
            _authTokenHandler = async token => await StartRealtimeRuntimeAsync();
            AuthStateService.Instance.TokenAcquired += _authTokenHandler;
        }

        public void ClearDashGridSelection()
        {
        }

        private async void FullStackOperation_Load(object sender, EventArgs e)
        {
            // STATE 1 — IDLE: UI only, no API calls, no realtime
            _selectedSource = "LOCAL";
            _selectedTimeInterval = "2 PHÚT";
            _selectedStatusSelect = "Tất cả tồn kho";
            _searchText = string.Empty;

            // UI (including _thoiHieuGrid) is now fully built — safe to update grids.
            _uiReady = true;
            AppLogger.Info("FullStack UI initialized");
            await InitializeLocalFullStackAsync();
            _ = CleanupJourneyDetailsCacheAsync();



            AppLogger.Info("FullStackOperation loaded (IDLE) — waiting for auth token");

            // If token was already set before form created, start ACTIVE now
            if (AuthStateService.Instance.IsAuthenticated)
                _ = StartRealtimeRuntimeAsync();
        }

        private async Task StartRealtimeRuntimeAsync()
        {
            if (_isRealtimeStarted) return;
            _isRealtimeStarted = true;

            // STATE 2 — ACTIVE: auth token available, start realtime runtime
            AppLogger.Info("FullStackOperation activating — token acquired");
            SetFullStackStatus("AuthToken sẵn sàng - local-first SQLite");

            _autoRefreshTimer = new System.Windows.Forms.Timer();
            _autoRefreshTimer.Interval = 2 * 60 * 1000;
            _autoRefreshTimer.Tick += async (s, ev) => await LoadDataAndRefreshViewsAsync();
            _autoRefreshTimer.Start();

            await LoadDataAndRefreshViewsAsync();
        }

        private void FullStackOperation_FormClosing(object sender, FormClosingEventArgs e)
        {
            // Stop background work first so no tick touches a disposing grid.
            _isClosing = true;
            _autoRefreshTimer?.Stop();
            _autoRefreshTimer?.Dispose();
            CancelCurrentJourneyLoad();
            _cts.Cancel();
            if (_authTokenHandler != null)
            {
                AuthStateService.Instance.TokenAcquired -= _authTokenHandler;
                _authTokenHandler = null;
            }
        }

        private async Task CleanupJourneyDetailsCacheAsync()
        {
            try
            {
                await _fullStackJourneyService.CleanupExpiredAsync(_cts.Token);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                AppLogger.Warning($"[FullStackJourney] cleanup failed: {ex.Message}");
            }
        }



        private async Task InitializeLocalFullStackAsync()
        {
            try
            {
                await _fullStackDashboardService.InitializeAsync(_cts.Token);
                SetFullStackStatus("SQLite local sẵn sàng");
                AppLogger.Info($"[FullStackOperation] local-first DB path={_fullStackDashboardService.DatabasePath}");
            }
            catch (Exception ex)
            {
                AppLogger.Error("[FullStackOperation] local DB init failed", ex);
                SetFullStackStatus("Lỗi khởi tạo SQLite local");
            }
        }

        private void UpdateLocalSnapshotStatus(FullStackDashboardSnapshot snapshot)
        {
            if (snapshot == null)
            {
                SetFullStackStatus("SQLite local chưa có dữ liệu");
                return;
            }

            string syncText = snapshot.LastSyncAt.HasValue
                ? $"Sync local: {snapshot.LastSyncAt.Value.ToLocalTime():HH:mm:ss dd/MM}"
                : "Chưa sync local";
            SetFullStackStatus($"{syncText} | DB: {Path.GetFileName(snapshot.DbPath)}");
        }

        private void SetFullStackStatus(string text)
        {
            _fullStackStatus = text;
        }

        private async Task LoadDataAndRefreshViewsAsync()
        {
            try
            {
                await _fullStackDashboardService.InitializeAsync(_cts.Token);
                FullStackDashboardSnapshot snapshot = await _fullStackDashboardService.LoadSnapshotAsync(_cts.Token);
                _cloudData = snapshot.Rows ?? new List<WaybillDbModel>();
                await RefreshOperationMetadataAsync();
                await RefreshDashViewAsync(_cts.Token);
                UpdateLocalSnapshotStatus(snapshot);
            }
            catch (Exception ex)
            {
                AppLogger.Error("LoadDataAndRefreshViewsAsync failed", ex);
                SetFullStackStatus("Lỗi tải SQLite local");
            }
        }

        private async Task SyncDataAsync()
        {
            if (_isSyncRunning) return;
            _isSyncRunning = true;
            try
            {
                var ct = _cts.Token;
                if (!JmsAuthStateService.HasToken && !AuthStateService.Instance.IsAuthenticated)
                {
                    SetFullStackStatus("Đang chờ đăng nhập / authToken");
                    MessageBox.Show("Đang chờ đăng nhập JMS / authToken. Vui lòng đăng nhập JMS trước khi đồng bộ tồn kho.", "FullStack local-first", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                SetFullStackStatus("Đang đồng bộ tồn kho 30 ngày...");
                var result = await _fullStackDashboardService.SyncInventoryAndRefreshTrackingAsync(ct);
                AppLogger.Info($"[FullStackOperation] manual sync finished runId={result.RunId}, fetched={result.TotalFetched}, new={result.NewWaybills}, left={result.LeftInventory}");
                SetFullStackStatus($"Sync xong: {result.TotalFetched:N0} đơn, mới {result.NewWaybills:N0}, rời tồn {result.LeftInventory:N0}");
                await LoadDataAndRefreshViewsAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Lỗi làm mới dữ liệu: " + ex.Message, "Lỗi", MessageBoxButtons.OK, MessageBoxIcon.Error);
                SetFullStackStatus("Sync failed");
            }
            finally
            {
                _isSyncRunning = false;
            }
        }



        private async Task RefreshDashViewAsync(CancellationToken ct)
        {
            string dataSourceOption = _selectedSource;
            List<WaybillDbModel> dashSourceData;

            if (dataSourceOption == "PHATLAI")
            {
                var phatLaiWaybills = await ZaloChatService.GetWaybillsFromPhatLaiAsync();
                var dbMap = _cloudData
                    .Where(x => !string.IsNullOrWhiteSpace(x.WaybillNo))
                    .GroupBy(x => x.WaybillNo, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

                dashSourceData = phatLaiWaybills
                    .Select(wb => dbMap.TryGetValue(wb, out var existing) ? existing : new WaybillDbModel { WaybillNo = wb })
                    .ToList();
            }
            else
            {
                dashSourceData = _cloudData.ToList();
            }

            _lastDashSourceData = dashSourceData;

            // Render Grids
            RefreshFilteredGrid();
        }

        private void RefreshFilteredGrid()
        {
            var filtered = ApplyDashFilter(_lastDashSourceData);
        }

        private string CalculateWarehouseAge(string thoiGianThaoTac, string thaoTacCuoi)
        {
            if (string.IsNullOrWhiteSpace(thoiGianThaoTac) || thoiGianThaoTac == "empty") return "N/A";

            // If delivered or fully returned, warehouse age stops or is N/A
            if (thaoTacCuoi != null && (thaoTacCuoi.Contains("Ký nhận") || thaoTacCuoi.Contains("Xác nhận chuyển hoàn thành công")))
                return "Đã hoàn thành";

            if (DateTime.TryParse(thoiGianThaoTac, out DateTime t))
            {
                var diff = DateTime.Now - t;
                if (diff.TotalDays >= 1) return $"{(int)diff.TotalDays} ngày {(int)diff.Hours} giờ";
                return $"{(int)diff.TotalHours} giờ {diff.Minutes} phút";
            }
            return "N/A";
        }

        private string CalculateSlaRemaining(string thoiGianNhanHang, out string warningLevel)
        {
            warningLevel = "Bình thường";
            if (string.IsNullOrWhiteSpace(thoiGianNhanHang) || thoiGianNhanHang == "empty") return "N/A";

            if (DateTime.TryParse(thoiGianNhanHang, out DateTime pickTime))
            {
                var deadline = pickTime.AddHours(24);
                var diff = deadline - DateTime.Now;
                if (diff.TotalHours < 0)
                {
                    warningLevel = "Nghiêm trọng";
                    var overdue = -diff;
                    if (overdue.TotalDays >= 1) return $"Trễ {(int)overdue.TotalDays} ngày";
                    return $"Trễ {(int)overdue.TotalHours} giờ";
                }
                else if (diff.TotalHours <= 4)
                {
                    warningLevel = "Cảnh báo";
                    return $"Gấp! Còn {(int)diff.TotalHours}h {diff.Minutes}m";
                }
                return $"Còn {(int)diff.TotalHours}h";
            }
            return "N/A";
        }

        private List<WaybillDbModel> ApplyDashFilter(List<WaybillDbModel> baseData)
        {
            string selected = GetSelectedDashStatus();
            List<WaybillDbModel> filtered = baseData;

            if (string.IsNullOrWhiteSpace(selected) || selected == "Tất cả" || selected == "Tất cả tồn kho")
            {
                filtered = baseData.OrderBy(x => x.WaybillNo).ToList();
            }
            else if (selected == "Cần xử lý ngay")
            {
                filtered = baseData.Where(IsNeedsAction).ToList();
            }
            else if (selected == "Đơn mới đến" || selected == "Đơn mới hôm nay")
            {
                filtered = baseData.Where(x => IsNewArrival(x)).ToList();
            }
            else if (selected == "Chưa quét phát")
            {
                filtered = baseData.Where(x => IsNotDispatched(x)).ToList();
            }
            else if (selected == "Giao thất bại")
            {
                filtered = baseData.Where(x => IsFailedDelivery(x)).ToList();
            }
            else if (selected == "Chờ hoàn")
            {
                filtered = baseData.Where(x => IsPendingReturn(x)).ToList();
            }
            else if (selected == "Tồn quá hạn (>24h)")
            {
                filtered = baseData.Where(x => GetWarehouseAgeDays(x.ThoiGianThaoTac) >= 1.0).ToList();
            }
            else if (selected == "Tồn quá hạn (>48h)")
            {
                filtered = baseData.Where(x => GetWarehouseAgeDays(x.ThoiGianThaoTac) >= 2.0).ToList();
            }
            else if (selected == "SLA sắp trễ (<4 giờ)")
            {
                filtered = baseData.Where(x => IsSlaUrgent(x.ThoiGianNhanHang)).ToList();
            }
            else if (selected == "Trễ SLA" || selected == "SLA quá hạn")
            {
                filtered = baseData.Where(x => IsSlaBreached(x.ThoiGianNhanHang)).ToList();
            }
            else if (selected == "Nguy cơ thất lạc")
            {
                filtered = baseData.Where(x => IsLostRisk(x)).ToList();
            }
            else if (selected == "Checked")
            {
                filtered = baseData.Where(IsCheckedWaybill).ToList();
            }
            else if (selected == "Has task")
            {
                filtered = baseData.Where(HasTaskWaybill).ToList();
            }
            else if (selected == "Enriched")
            {
                filtered = baseData.Where(IsEnrichedWaybill).ToList();
            }
            else if (selected.StartsWith("Hành trình đứng >") || selected.StartsWith("Dừng "))
            {
                int days = 1;
                if (selected.Contains("3 ngày")) days = 3;
                else if (selected.Contains("7 ngày")) days = 7;
                filtered = baseData.Where(x => GetWarehouseAgeDays(x.ThoiGianThaoTac) >= days && !(x.ThaoTacCuoi?.Contains("Ký nhận") == true)).ToList();
            }
            else
            {
                var multiStatuses = selected.Split(',').Select(s => s.Trim()).Where(s => s.Length > 0).ToHashSet(StringComparer.OrdinalIgnoreCase);
                filtered = baseData.Where(x => multiStatuses.Contains(x.ThaoTacCuoi ?? "")).OrderBy(x => x.WaybillNo).ToList();
            }

            // Apply search text filter
            string search = _searchText?.Trim()?.ToLower() ?? "";
            if (!string.IsNullOrEmpty(search))
            {
                filtered = filtered.Where(x =>
                    (x.WaybillNo?.ToLower().Contains(search) == true) ||
                    (x.NguoiThaoTac?.ToLower().Contains(search) == true) ||
                    (x.NhanVienNhanHang?.ToLower().Contains(search) == true) ||
                    (x.ThaoTacCuoi?.ToLower().Contains(search) == true)
                ).ToList();
            }



            UpdateDashGridDataSource(filtered);
            return filtered;
        }

        private bool IsNewArrival(WaybillDbModel row)
        {
            if (row.ThaoTacCuoi == null) return false;
            var finalStatus = row.ThaoTacCuoi;
            return finalStatus.Contains("Xuống hàng kiện đến") ||
                   finalStatus.Contains("Xuống kiện") ||
                   finalStatus.Contains("卸车到件") ||
                   finalStatus.Contains("到件");
        }

        private bool IsNotDispatched(WaybillDbModel row)
        {
            return IsNewArrival(row) &&
                   (row.ThaoTacCuoi == null ||
                    (!row.ThaoTacCuoi.Contains("Đang phát hàng") &&
                     !row.ThaoTacCuoi.Contains("Giao lại hàng") &&
                     !row.ThaoTacCuoi.Contains("Kiện vấn đề") &&
                     !row.ThaoTacCuoi.Contains("Ký nhận")));
        }

        private bool IsFailedDelivery(WaybillDbModel row)
        {
            if (row.ThaoTacCuoi == null) return false;
            return (row.ThaoTacCuoi.Contains("vấn đề") ||
                    row.ThaoTacCuoi.Contains("Kiện vấn đề") ||
                    !string.IsNullOrEmpty(row.NguyenNhanKienVanDe)) &&
                   !row.ThaoTacCuoi.Contains("Ký nhận") &&
                   !row.ThaoTacCuoi.Contains("Xác nhận chuyển hoàn");
        }

        private bool IsPendingReturn(WaybillDbModel row)
        {
            if (row.ThaoTacCuoi == null) return false;
            return (row.DauChuyenHoan == "Có" ||
                    row.ThaoTacCuoi.Contains("Yêu cầu trả hàng") ||
                    row.ThaoTacCuoi.Contains("In đơn chuyển hoàn") ||
                    row.ThaoTacCuoi.Contains("Xác nhận chuyển hoàn")) &&
                   !row.ThaoTacCuoi.Contains("Xác nhận chuyển hoàn thành công") &&
                   !row.ThaoTacCuoi.Contains("Đã trả lại cho người gửi");
        }

        private double GetWarehouseAgeDays(string thoiGianThaoTac)
        {
            if (string.IsNullOrWhiteSpace(thoiGianThaoTac) || thoiGianThaoTac == "empty") return 0;
            if (DateTime.TryParse(thoiGianThaoTac, out DateTime t))
            {
                return (DateTime.Now - t).TotalDays;
            }
            return 0;
        }

        private bool IsSlaUrgent(string thoiGianNhanHang)
        {
            if (string.IsNullOrWhiteSpace(thoiGianNhanHang) || thoiGianNhanHang == "empty") return false;
            if (DateTime.TryParse(thoiGianNhanHang, out DateTime pickTime))
            {
                var diff = pickTime.AddHours(24) - DateTime.Now;
                return diff.TotalHours > 0 && diff.TotalHours <= 4;
            }
            return false;
        }

        private bool IsSlaBreached(string thoiGianNhanHang)
        {
            if (string.IsNullOrWhiteSpace(thoiGianNhanHang) || thoiGianNhanHang == "empty") return false;
            if (DateTime.TryParse(thoiGianNhanHang, out DateTime pickTime))
            {
                var diff = pickTime.AddHours(24) - DateTime.Now;
                return diff.TotalHours < 0;
            }
            return false;
        }

        private bool IsLostRisk(WaybillDbModel row)
        {
            if (row.ThaoTacCuoi != null && (row.ThaoTacCuoi.Contains("Ký nhận") || row.ThaoTacCuoi.Contains("Xác nhận chuyển hoàn thành công")))
                return false;

            double days = GetWarehouseAgeDays(row.ThoiGianThaoTac);
            return days >= 3.0; // Stalled for more than 3 days
        }

        private bool IsNeedsAction(WaybillDbModel row)
        {
            if (row == null) return false;
            return IsNotDispatched(row)
                || IsFailedDelivery(row)
                || IsPendingReturn(row)
                || IsSlaBreached(row.ThoiGianNhanHang)
                || IsSlaUrgent(row.ThoiGianNhanHang)
                || GetWarehouseAgeDays(row.ThoiGianThaoTac) >= 2.0
                || IsLostRisk(row);
        }

        private bool IsCheckedWaybill(WaybillDbModel row)
        {
            return row != null
                && !string.IsNullOrWhiteSpace(row.WaybillNo)
                && _operationMetadata.TryGetValue(row.WaybillNo, out var metadata)
                && metadata.IsChecked;
        }

        private bool HasTaskWaybill(WaybillDbModel row)
        {
            return row != null
                && !string.IsNullOrWhiteSpace(row.WaybillNo)
                && _operationMetadata.TryGetValue(row.WaybillNo, out var metadata)
                && metadata.HasTask;
        }

        private bool IsEnrichedWaybill(WaybillDbModel row)
        {
            return row != null
                && !string.IsNullOrWhiteSpace(row.WaybillNo)
                && _operationMetadata.TryGetValue(row.WaybillNo, out var metadata)
                && metadata.IsEnriched;
        }

        private async Task RefreshOperationMetadataAsync()
        {
            try
            {
                var waybills = _cloudData.Select(x => x.WaybillNo).Where(x => !string.IsNullOrWhiteSpace(x)).ToList();
                var snapshot = await _fullStackWorkflowService.LoadOperationMetadataAsync(waybills, _cts.Token);
                _operationMetadata = snapshot.Items;
            }
            catch (Exception ex)
            {
                AppLogger.Warning($"Load operation metadata failed: {ex.Message}");
                _operationMetadata = new Dictionary<string, FullStackOperationMetadata>(StringComparer.OrdinalIgnoreCase);
            }
        }

        private string GetSelectedDashStatus()
        {
            return _selectedStatusSelect ?? string.Empty;
        }



        private bool IsCustomFilter(string status)
        {
            return status == "Tất cả tồn kho" || status == "Cần xử lý ngay" ||
                   status == "Đơn mới đến" || status == "Đơn mới hôm nay" ||
                   status == "Chưa quét phát" || status == "Giao thất bại" ||
                   status == "Checked" || status == "Has task" || status == "Enriched" ||
                   status == "Chờ hoàn" || status == "Tồn quá hạn (>24h)" || status == "Tồn quá hạn (>48h)" ||
                   status == "SLA sắp trễ (<4 giờ)" || status == "Trễ SLA" || status == "SLA quá hạn" ||
                   status == "Nguy cơ thất lạc" || status.StartsWith("Hành trình đứng >") || status.StartsWith("Dừng ");
        }

        private void UpdateDashGridDataSource(List<WaybillDbModel> data)
        {
            _lastFilteredDashRows = data?.ToList() ?? new List<WaybillDbModel>();
            PostStateToWebView2();
        }

        private async Task ExportOperationCurrentViewAsync(bool selectedOnly)
        {
            var rows = _lastFilteredDashRows.ToList();

            if (rows.Count == 0)
            {
                MessageBox.Show("Không có dữ liệu để xuất.", "Operation Center", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            string path = await _fullStackExportService.ExportWaybillsCsvAsync(rows, "operation-center", _cts.Token);
            SetFullStackStatus($"Đã xuất CSV: {Path.GetFileName(path)}");
            TryOpenPath(path);
        }

        private static void TryOpenPath(string path)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true });
            }
            catch
            {
                // Export completed; opening the file is only a convenience.
            }
        }
    }
}

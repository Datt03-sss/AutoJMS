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

        // Thoi Hieu tab
        private TabPage _tabThoiHieu;
        private ThoiHieuKpiSheetControl _thoiHieuKpiSheet;
        private ThoiHieuKpiSheetData _thoiHieuKpiSheetData = new();
        private Button _thoiHieuNormalViewButton;
        private Button _thoiHieuFitWidthButton;
        private Button _thoiHieuFitOnePageButton;
        private Button _thoiHieuExportImageButton;
        private Button _thoiHieuOpenExportFolderButton;
        private Label _thoiHieuStatusLabel;
        private string _lastThoiHieuExportPath = string.Empty;
        private Sunny.UI.UITableLayoutPanel _thoiHieuLayout;
        private Sunny.UI.UIDataGridView _thoiHieuGrid;
        private System.Windows.Forms.Timer _thoiHieuTimer;
        private List<ThoiHieuRow> _thoiHieuGridData = new();
        private Label _thoiHieuFooterLabel;
        private UIComboBox _thoiHieuShipperFilter;
        private TextBox _thoiHieuFilterText;

        // Lifecycle guards for the Thoi Hieu view:
        //  - _uiReady: set true once the UI (incl. _thoiHieuKpiSheet) is built.
        //  - _isClosing: set true on FormClosing so background ticks stop touching UI.
        //  - _pendingThoiHieuRows: data that arrived before the grid existed; replayed when ready.
        private volatile bool _uiReady;
        private volatile bool _isClosing;
        private List<ThoiHieuRow> _pendingThoiHieuRows;

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

        private async void FullStackOperation_Load(object sender, EventArgs e)
        {
            // STATE 1 — IDLE: UI only, no API calls, no realtime
            SetupGrids();
            InitializeEnhancedUI();
            SetupDashToolbar();
            tabDash_dataSource.SelectedIndex = 1;
            tabDash_timeUpdateData.Text = "2 PHÚT";

            // UI (including _thoiHieuGrid) is now fully built — safe to update grids.
            _uiReady = true;
            AppLogger.Info("FullStack UI initialized");
            await InitializeLocalFullStackAsync();
            _ = CleanupJourneyDetailsCacheAsync();

            // If data arrived before the UI was ready, apply it now.
            if (_pendingThoiHieuRows != null)
            {
                AppLogger.Info($"Applying cached ThoiHieu data after UI ready (count={_pendingThoiHieuRows.Count}).");
                SetThoiHieuData(_pendingThoiHieuRows);
                _pendingThoiHieuRows = null;
            }

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
            _thoiHieuTimer?.Stop();
            _thoiHieuTimer?.Dispose();
            _alertCheckTimer?.Stop();
            _alertCheckTimer?.Dispose();
            CancelCurrentJourneyLoad();
            _cts.Cancel();
            if (_authTokenHandler != null)
            {
                AuthStateService.Instance.TokenAcquired -= _authTokenHandler;
                _authTokenHandler = null;
            }
            if (_zaloChatService != null)
            {
                _zaloChatService.StopAutoReminder();
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

        private void SetupGrids()
        {
            ApplyStandardGridSettings(tabDash_dataGridView);
            ApplyStandardGridSettings(uiDataGridView2);
            ApplyStandardGridSettings(tabChat_dataGrid);
            
            // tabDash_dataGridView (tabPage3 - "Chuyển hoàn" / Dashboard)
            tabDash_dataGridView.AutoGenerateColumns = false;
            tabDash_dataGridView.Columns.Clear();
            tabDash_dataGridView.Columns.AddRange(new DataGridViewColumn[]
            {
                new DataGridViewTextBoxColumn { Name = "STT", HeaderText = "STT", Width = 45, ReadOnly = true, SortMode = DataGridViewColumnSortMode.NotSortable, Frozen = true },
                new DataGridViewTextBoxColumn { Name = "Mã vận đơn", HeaderText = "Mã vận đơn", DataPropertyName = "WaybillNo", Width = 140, SortMode = DataGridViewColumnSortMode.Programmatic, Frozen = true },
                new DataGridViewTextBoxColumn { Name = "Nhân viên xử lý cuối", HeaderText = "Nhân viên", DataPropertyName = "NguoiThaoTac", Width = 130, SortMode = DataGridViewColumnSortMode.Programmatic },
                new DataGridViewTextBoxColumn { Name = "Trạng thái hiện tại", HeaderText = "Trạng thái", DataPropertyName = "TrangThaiHienTai", Width = 140, SortMode = DataGridViewColumnSortMode.Programmatic },
                new DataGridViewTextBoxColumn { Name = "Loại quét kiện cuối", HeaderText = "Thao tác cuối", DataPropertyName = "ThaoTacCuoi", Width = 160, SortMode = DataGridViewColumnSortMode.Programmatic },
                new DataGridViewTextBoxColumn { Name = "Thời gian thao tác", HeaderText = "Thời gian", DataPropertyName = "ThoiGianThaoTac", Width = 150, SortMode = DataGridViewColumnSortMode.Programmatic },
                new DataGridViewTextBoxColumn { Name = "Nhân viên kiện vấn đề", HeaderText = "NV kiện vấn đề", DataPropertyName = "NhanVienKienVanDe", Width = 120, SortMode = DataGridViewColumnSortMode.Programmatic },
                new DataGridViewTextBoxColumn { Name = "Nguyên nhân kiện vấn đề", HeaderText = "Nguyên nhân KVD", DataPropertyName = "NguyenNhanKienVanDe", Width = 140, SortMode = DataGridViewColumnSortMode.Programmatic },
                new DataGridViewTextBoxColumn { Name = "Số lần nhắc", HeaderText = "Nhắc", DataPropertyName = "PrintCount", Width = 60, SortMode = DataGridViewColumnSortMode.Programmatic },
                new DataGridViewTextBoxColumn { Name = "Cập nhật lúc", HeaderText = "Cập nhật", DataPropertyName = "LastTrackedAt", Width = 140, SortMode = DataGridViewColumnSortMode.Programmatic }
            });
            tabDash_dataGridView.ColumnHeaderMouseClick += tabDash_dataGrid_ColumnHeaderMouseClick;

            // uiDataGridView2 (tabPage4 - "Thời hiệu")
            uiDataGridView2.AutoGenerateColumns = false;
            uiDataGridView2.Columns.Clear();
            uiDataGridView2.Columns.AddRange(new DataGridViewColumn[]
            {
                new DataGridViewTextBoxColumn { Name = "Mã vận đơn", HeaderText = "Mã vận đơn", DataPropertyName = "WaybillNo", SortMode = DataGridViewColumnSortMode.Programmatic },
                new DataGridViewTextBoxColumn { Name = "Thời gian nhận hàng", HeaderText = "Thời gian nhận hàng", DataPropertyName = "ThoiGianNhanHang", SortMode = DataGridViewColumnSortMode.Programmatic },
                new DataGridViewTextBoxColumn { Name = "Thời gian đến bưu cục", HeaderText = "Thời gian đến bưu cục", DataPropertyName = "ThoiGianThaoTac", SortMode = DataGridViewColumnSortMode.Programmatic },
                new DataGridViewTextBoxColumn { Name = "Thời gian tồn kho", HeaderText = "Thời gian tồn kho", DataPropertyName = "TonKhoDuration", SortMode = DataGridViewColumnSortMode.Programmatic },
                new DataGridViewTextBoxColumn { Name = "Hạn SLA còn lại", HeaderText = "Hạn SLA còn lại", DataPropertyName = "SlaRemaining", SortMode = DataGridViewColumnSortMode.Programmatic },
                new DataGridViewTextBoxColumn { Name = "Cảnh báo", HeaderText = "Cảnh báo", DataPropertyName = "LevelCanhBao", SortMode = DataGridViewColumnSortMode.Programmatic },
                new DataGridViewTextBoxColumn { Name = "Người thao tác cuối", HeaderText = "Người thao tác cuối", DataPropertyName = "NguoiThaoTac", SortMode = DataGridViewColumnSortMode.Programmatic },
                new DataGridViewTextBoxColumn { Name = "Trạng thái cuối", HeaderText = "Trạng thái cuối", DataPropertyName = "ThaoTacCuoi", SortMode = DataGridViewColumnSortMode.Programmatic }
            });
            uiDataGridView2.ColumnHeaderMouseClick += uiDataGridView2_ColumnHeaderMouseClick;

            // tabChat_dataGrid
            tabChat_dataGrid.AutoGenerateColumns = false;
            tabChat_dataGrid.Columns.Clear();
            tabChat_dataGrid.Columns.AddRange(new DataGridViewColumn[]
            {
                new DataGridViewTextBoxColumn { Name = "maDon", HeaderText = "Mã vận đơn", DataPropertyName = "WaybillNo", SortMode = DataGridViewColumnSortMode.Programmatic },
                new DataGridViewTextBoxColumn { Name = "nhanVien", HeaderText = "Tên nhân viên", DataPropertyName = "NguoiThaoTac", SortMode = DataGridViewColumnSortMode.Programmatic },
                new DataGridViewTextBoxColumn { Name = "trangThai", HeaderText = "Trạng thái hiện tại", DataPropertyName = "ThaoTacCuoi", SortMode = DataGridViewColumnSortMode.Programmatic },
                new DataGridViewTextBoxColumn { Name = "soLanNhac", HeaderText = "Số lần nhắc", DataPropertyName = "PrintCount", SortMode = DataGridViewColumnSortMode.Programmatic }
            });
            tabChat_dataGrid.ColumnHeaderMouseClick += tabChat_dataGrid_ColumnHeaderMouseClick;
        }

        private void ApplyStandardGridSettings(DataGridView grid)
        {
            if (grid == null) return;
            grid.ReadOnly = true;
            grid.ClipboardCopyMode = DataGridViewClipboardCopyMode.EnableWithoutHeaderText;
            grid.SelectionMode = DataGridViewSelectionMode.CellSelect;
            grid.AllowUserToResizeColumns = true;
            grid.AllowUserToResizeRows = false;
            grid.AllowUserToAddRows = false;
            grid.AllowUserToDeleteRows = false;
            grid.MultiSelect = true;
            grid.RowHeadersVisible = false;
            grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.DisplayedCells;
            grid.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.None;
            grid.RowTemplate.Height = 27;
            grid.ColumnHeadersHeight = 34;
            grid.ColumnHeadersDefaultCellStyle.BackColor = HeaderDark;
            grid.ColumnHeadersDefaultCellStyle.ForeColor = Color.White;
            grid.ColumnHeadersDefaultCellStyle.Font = new Font("Segoe UI Semibold", 7.5F, FontStyle.Bold);
            grid.ColumnHeadersDefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleLeft;
            grid.DefaultCellStyle.Font = new Font("Segoe UI", 7.5F);
            grid.DefaultCellStyle.ForeColor = TextPrimary;
            grid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(37, 99, 235);
            grid.DefaultCellStyle.SelectionForeColor = Color.White;
            grid.EnableHeadersVisualStyles = false;
            grid.DataError -= FullStackGrid_DataError;
            grid.DataError += FullStackGrid_DataError;
            if (grid is Sunny.UI.UIDataGridView uiGrid)
            {
                uiGrid.StripeOddColor = Color.White;
                uiGrid.StripeEvenColor = Color.FromArgb(249, 250, 251);
                uiGrid.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(249, 250, 251);
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
            if (tabDash_lblLastUpdate == null) return;

            void Apply()
            {
                if (tabDash_lblLastUpdate == null || tabDash_lblLastUpdate.IsDisposed) return;
                tabDash_lblLastUpdate.Text = text;
            }

            if (tabDash_lblLastUpdate.InvokeRequired)
                tabDash_lblLastUpdate.BeginInvoke(new Action(Apply));
            else
                Apply();
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
                await RefreshChatViewAsync(_cts.Token);
                UpdateLocalSnapshotStatus(snapshot);
            }
            catch (Exception ex)
            {
                AppLogger.Error("LoadDataAndRefreshViewsAsync failed", ex);
                SetFullStackStatus("Lỗi tải SQLite local");
            }
        }

        private async void tabDash_updateData_Click(object sender, EventArgs e)
        {
            if (_isSyncRunning) return;
            tabDash_updateData.Enabled = false;
            tabDash_updateData.Text = "Đang đồng bộ...";
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
                tabDash_updateData.Enabled = true;
                tabDash_updateData.Text = "Đồng bộ";
                _isSyncRunning = false;
            }
        }

        private async void tabDash_dataSource_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (_isRefreshingStatusCombos) return;
            await RefreshDashViewAsync(_cts.Token);
        }

        private void tabDash_statusSelect_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (_isRefreshingStatusCombos) return;
            _dashQuickFilter = GetSelectedDashStatus();
            UpdateDashQuickFilterButtons();
            if (_lastDashSourceData.Count == 0) return;
            RefreshFilteredGrid();
        }

        private void tabDash_timeUpdateData_SelectedIndexChanged(object sender, EventArgs e)
        {
            string text = tabDash_timeUpdateData.Text?.Replace(" PHÚT", "")?.Replace(" GIỜ", "")?.Trim() ?? "2";
            int minutes = 2;
            if (tabDash_timeUpdateData.Text.Contains("GIỜ"))
            {
                if (int.TryParse(text, out int h)) minutes = h * 60;
            }
            else
            {
                int.TryParse(text, out minutes);
            }
            if (_autoRefreshTimer != null)
                _autoRefreshTimer.Interval = Math.Max(1, minutes) * 60 * 1000;
        }

        private async Task RefreshDashViewAsync(CancellationToken ct)
        {
            string dataSourceOption = GetControlTextSafe(tabDash_dataSource);
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
            
            RefreshDashStatusCache(dashSourceData);
            PopulateDashStatusSelects();
            EnsureDashStatusSelectionValid();

            // Render Grids
            RefreshFilteredGrid();
        }

        private void RefreshFilteredGrid()
        {
            var filtered = ApplyDashFilter(_lastDashSourceData);
            UpdateDashSummaryLabels(_lastDashSourceData.Count, filtered.Count);

            // Load Thoi Hieu delivery tracking data
            RefreshThoiHieuKpiSheet();
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
            string search = _dashSearchBox?.Text?.Trim()?.ToLower() ?? "";
            if (!string.IsNullOrEmpty(search))
            {
                filtered = filtered.Where(x =>
                    (x.WaybillNo?.ToLower().Contains(search) == true) ||
                    (x.NguoiThaoTac?.ToLower().Contains(search) == true) ||
                    (x.NhanVienNhanHang?.ToLower().Contains(search) == true) ||
                    (x.ThaoTacCuoi?.ToLower().Contains(search) == true)
                ).ToList();
            }

            // Apply date range filter
            try
            {
                if (_dashDateFrom != null && _dashDateFrom.Checked)
                {
                    DateTime from = _dashDateFrom.Value.Date;
                    filtered = filtered.Where(x => DateTime.TryParse(x.ThoiGianThaoTac, out DateTime t) && t >= from).ToList();
                }
                if (_dashDateTo != null && _dashDateTo.Checked)
                {
                    DateTime to = _dashDateTo.Value.Date.AddDays(1);
                    filtered = filtered.Where(x => DateTime.TryParse(x.ThoiGianThaoTac, out DateTime t) && t <= to).ToList();
                }
            }
            catch { }

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
            return tabDash_statusSelect?.SelectedItem?.ToString()?.Trim()
                ?? tabDash_statusSelect?.Text?.Trim()
                ?? string.Empty;
        }

        private void EnsureDashStatusSelectionValid()
        {
            if (tabDash_statusSelect == null) return;
            var current = GetSelectedDashStatus();
            if (string.IsNullOrWhiteSpace(current) || current == "Tất cả") return;
            
            // Check custom filters too
            if (IsCustomFilter(current)) return;

            if (_dashStatusCache == null || !_dashStatusCache.Any(s => string.Equals(s, current, StringComparison.OrdinalIgnoreCase)))
            {
                tabDash_statusSelect.SelectedIndex = 0;
            }
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

        private void UpdateDashSummaryLabels(int sourceCount, int filteredCount)
        {
            if (this.InvokeRequired)
            {
                this.Invoke(new Action(() => UpdateDashSummaryLabels(sourceCount, filteredCount)));
                return;
            }
            
            if (tabDash_lblLastUpdate != null)
                tabDash_lblLastUpdate.Text = $"Local refresh: {DateTime.Now:HH:mm:ss}";

            if (_dashFilterInfo != null)
                _dashFilterInfo.Text = $"Inventory grid | Hiển thị {filteredCount:N0} / {sourceCount:N0} đơn";

            UpdatePriorityFocusCards(sourceCount, filteredCount);
            UpdateDashQueueInsight();
            UpdateOperationCenterChrome(sourceCount, filteredCount);
            UpdateOperationFilterToolbar();
        }

        private void UpdateDashGridDataSource(List<WaybillDbModel> data)
        {
            _lastFilteredDashRows = data?.ToList() ?? new List<WaybillDbModel>();

            // Compute hash for partial refresh
            string newHash = string.Join("|", _lastFilteredDashRows.Select(x => $"{x.WaybillNo}:{x.ThaoTacCuoi}:{x.ThoiGianThaoTac}"));
            if (newHash == _lastDataHash && tabDash_dataGridView.RowCount > 1)
            {
                UpdateSelectedOperationDetailFromGrid();
                return; // No changes, skip refresh
            }

            _lastDataHash = newHash;

            void Apply()
            {
                tabDash_dataGridView.DataSource = null;
                tabDash_dataGridView.DataSource = _lastFilteredDashRows;
                tabDash_dataGridView.Refresh();
                tabDash_dataGridView.ClearSelection();
                if (tabDash_dataGridView.Rows.Count > 0 && tabDash_dataGridView.Columns.Count > 0)
                {
                    tabDash_dataGridView.Rows[0].Selected = true;
                    tabDash_dataGridView.CurrentCell = tabDash_dataGridView.Rows[0].Cells[Math.Min(1, tabDash_dataGridView.Columns.Count - 1)];
                }
                UpdateSelectedOperationDetailFromGrid();
                UpdateOperationFilterToolbar();
            }

            if (tabDash_dataGridView.InvokeRequired)
                tabDash_dataGridView.Invoke(new Action(Apply));
            else
                Apply();
        }

        private void UpdateSlaGridDataSource(List<SlaAgingRow> data)
        {
            void Apply()
            {
                uiDataGridView2.DataSource = null;
                uiDataGridView2.DataSource = data;
                uiDataGridView2.Refresh();
            }

            if (uiDataGridView2.InvokeRequired)
                uiDataGridView2.Invoke(new Action(Apply));
            else
                Apply();
        }

        private void PopulateDashStatusSelects()
        {
            if (tabDash_statusSelect == null) return;
            _isRefreshingStatusCombos = true;
            try
            {
                string currentDash = GetSelectedDashStatus();
                tabDash_statusSelect.SuspendLayout();
                tabDash_statusSelect.Items.Clear();
                
                // Add default standard operations filters
                tabDash_statusSelect.Items.Add("Tất cả tồn kho");
                tabDash_statusSelect.Items.Add("Cần xử lý ngay");
                tabDash_statusSelect.Items.Add("Đơn mới hôm nay");
                tabDash_statusSelect.Items.Add("Chưa quét phát");
                tabDash_statusSelect.Items.Add("Giao thất bại");
                tabDash_statusSelect.Items.Add("Chờ hoàn");
                tabDash_statusSelect.Items.Add("SLA quá hạn");
                tabDash_statusSelect.Items.Add("Nguy cơ thất lạc");
                tabDash_statusSelect.Items.Add("Checked");
                tabDash_statusSelect.Items.Add("Has task");
                tabDash_statusSelect.Items.Add("Enriched");
                tabDash_statusSelect.Items.Add("Dừng 1 ngày");
                tabDash_statusSelect.Items.Add("Dừng 3 ngày");
                tabDash_statusSelect.Items.Add("Dừng 7 ngày");
                tabDash_statusSelect.Items.Add("Tồn quá hạn (>24h)");
                tabDash_statusSelect.Items.Add("Tồn quá hạn (>48h)");
                tabDash_statusSelect.Items.Add("SLA sắp trễ (<4 giờ)");
                tabDash_statusSelect.Items.Add("Hành trình đứng > 1 ngày");
                tabDash_statusSelect.Items.Add("Hành trình đứng > 3 ngày");
                tabDash_statusSelect.Items.Add("Hành trình đứng > 7 ngày");
                
                // Add physical scanner status values
                foreach (var s in _dashStatusCache)
                {
                    if (!IsCustomFilter(s))
                        tabDash_statusSelect.Items.Add(s);
                }

                if (tabDash_statusSelect.Items.Contains(currentDash))
                {
                    tabDash_statusSelect.SelectedItem = currentDash;
                }
                else
                {
                    tabDash_statusSelect.SelectedIndex = 0;
                }
            }
            finally
            {
                tabDash_statusSelect.ResumeLayout();
                _isRefreshingStatusCombos = false;
            }
        }

        private void RefreshDashStatusCache(List<WaybillDbModel> dashSourceData)
        {
            _dashStatusCache = dashSourceData
                .Select(x => x.ThaoTacCuoi)
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(s => s)
                .ToList();
        }

        private string GetControlTextSafe(Control ctrl)
        {
            if (ctrl.InvokeRequired)
            {
                return (string)ctrl.Invoke(new Func<string>(() => ctrl.Text?.Trim() ?? ""));
            }
            return ctrl.Text?.Trim() ?? "";
        }

        // ======================================================================================
        // TAB ZALO CHAT BOT (Reminder Panel)
        // ======================================================================================

        private async void uiTabControl1_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (uiTabControl1.SelectedTab == tabChat)
            {
                await InitZaloWebViewAsync();
            }
        }

        private async Task InitZaloWebViewAsync()
        {
            if (_isZaloLoaded || tabChat_webViewZalo.CoreWebView2 != null) return;
            try
            {
                string userDataFolder = AppPaths.ZaloProfileDir;
                var env = await CoreWebView2Environment.CreateAsync(null, userDataFolder);
                await tabChat_webViewZalo.EnsureCoreWebView2Async(env);
                tabChat_webViewZalo.CoreWebView2.Settings.UserAgent = CHROME_USER_AGENT;
                tabChat_webViewZalo.CoreWebView2.Navigate("https://chat.zalo.me/index.html");
                tabChat_webViewZalo.NavigationCompleted += (s, args) =>
                {
                    if (_zaloChatService == null)
                    {
                        _zaloChatService = new ZaloChatService(tabChat_webViewZalo, AppConfig.Current.AppsScriptUrl);
                        _zaloChatService.StartAutoReminder(5);
                    }
                };
                _isZaloLoaded = true;
            }
            catch (Exception ex)
            {
                MessageBox.Show("Lỗi khởi tạo Zalo Web: " + ex.Message, "Lỗi", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private async void tabChat_btnStart_Click(object sender, EventArgs e)
        {
            if (!_isZaloLoaded || _zaloChatService == null) { MessageBox.Show("Zalo chưa sẵn sàng!"); return; }
            tabChat_btnStart.Enabled = false;

            try
            {
                var reminders = new List<Reminder>();
                var modelsToUpdate = new List<WaybillDbModel>();

                foreach (var item in _cloudData)
                {
                    if (item.TrangThaiHienTai?.Trim().Equals("Quét phát hàng", StringComparison.OrdinalIgnoreCase) == true)
                    {
                        reminders.Add(new Reminder { maDon = item.WaybillNo ?? "", nhanVien = item.NguoiThaoTac ?? "", trangThai = item.TrangThaiHienTai });
                    }
                }

                if (reminders.Count == 0) { MessageBox.Show("Không có đơn nào ở trạng thái 'Quét phát hàng'."); return; }

                var groups = reminders.GroupBy(r => r.nhanVien.Trim(), StringComparer.OrdinalIgnoreCase).ToList();
                int totalGroups = groups.Count;
                int successCount = 0;

                for (int i = 0; i < totalGroups; i++)
                {
                    var group = groups[i];
                    string tenNV = group.Key;
                    var danhSachMa = group.Select(r => r.maDon).Distinct().ToList();
                    string danhSachMaDon = string.Join("\n", danhSachMa);
                    string noiDung = $"@{tenNV}\n{danhSachMaDon}";

                    bool result = await _zaloChatService.SendZaloMessage(noiDung);

                    if (result)
                    {
                        successCount++;
                        foreach (var wb in danhSachMa)
                        {
                            var target = _cloudData.FirstOrDefault(x => x.WaybillNo == wb);
                            if (target != null)
                            {
                                target.PrintCount++;
                                modelsToUpdate.Add(target);
                            }
                        }
                        await Task.Delay(2500);
                    }
                    else
                    {
                        if (MessageBox.Show($"Gửi thất bại cho nhân viên: {tenNV}\n\nTiếp tục gửi?", "Lỗi gửi tin", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.No) break;
                    }
                }

                if (modelsToUpdate.Count > 0)
                    await _fullStackDashboardService.SaveReminderCountsAsync(modelsToUpdate.Select(x => x.WaybillNo), _cts.Token);
                MessageBox.Show($"Hoàn tất! Đã gửi thành công {successCount}/{totalGroups} nhân viên.", "Kết quả", MessageBoxButtons.OK, MessageBoxIcon.Information);
                await LoadDataAndRefreshViewsAsync();
            }
            catch (Exception ex) { MessageBox.Show($"Lỗi: {ex.Message}", "Lỗi", MessageBoxButtons.OK, MessageBoxIcon.Error); }
            finally { tabChat_btnStart.Enabled = true; }
        }

        private async void tabChat_btnReload_Click(object sender, EventArgs e)
        {
            if (_zaloChatService == null) { MessageBox.Show("Vui lòng đợi Zalo khởi tạo xong!"); return; }
            tabChat_btnReload.Enabled = false; 
            tabChat_btnReload.Text = "Đang tải...";
            
            await LoadDataAndRefreshViewsAsync();
            
            tabChat_btnReload.Enabled = true; 
            tabChat_btnReload.Text = "-Làm mới-";
        }

        private void tabChat_statusSelect_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (_isRefreshingStatusCombos || _lastChatSourceData.Count == 0) return;
            ApplyChatFilter(_lastChatSourceData);
        }

        private async Task RefreshChatViewAsync(CancellationToken ct)
        {
            var phatLaiWaybills = await ZaloChatService.GetWaybillsFromPhatLaiAsync();
            var phatLaiSet = phatLaiWaybills
                .Where(wb => !string.IsNullOrWhiteSpace(wb))
                .Select(wb => wb.Trim())
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var chatSourceData = _cloudData
                .Where(x => !string.IsNullOrWhiteSpace(x.WaybillNo) && phatLaiSet.Contains(x.WaybillNo.Trim()))
                .ToList();

            _lastChatSourceData = chatSourceData;
            
            RefreshChatStatusCache(chatSourceData);
            PopulateChatStatusSelects();

            ApplyChatFilter(_lastChatSourceData);

            if (tabChat_sumFollow != null)
                tabChat_sumFollow.Text = $"Tổng đang theo dõi: {tabChat_dataGrid.RowCount}";

            int kienVanDeCount = chatSourceData.Count(r => r.TrangThaiHienTai?.Contains("vấn đề") == true);
            if (tabChat_hasKVD != null) tabChat_hasKVD.Text = $"Kiện vấn đề: {kienVanDeCount}";

            int xnchCount = chatSourceData.Count(r => r.TrangThaiHienTai?.Contains("Xác nhận chuyển hoàn") == true);
            if (tabChat_hasXNCH != null) tabChat_hasXNCH.Text = $"Xác nhận CH: {xnchCount}";
        }

        private void ApplyChatFilter(List<WaybillDbModel> baseData)
        {
            string selected = GetSelectedChatStatus();
            List<WaybillDbModel> filtered;

            if (selected == "Tất cả" || string.IsNullOrEmpty(selected))
            {
                filtered = baseData.OrderBy(x => x.NguoiThaoTac).ToList();
            }
            else
            {
                var multiStatuses = selected.Split(',').Select(s => s.Trim()).Where(s => s.Length > 0).ToHashSet(StringComparer.OrdinalIgnoreCase);
                filtered = baseData.Where(x => multiStatuses.Contains(x.ThaoTacCuoi ?? "")).OrderBy(x => x.NguoiThaoTac).ToList();
            }

            UpdateChatGridDataSource(filtered);
        }

        private string GetSelectedChatStatus()
        {
            return tabChat_statusSelect?.SelectedItem?.ToString()?.Trim()
                ?? tabChat_statusSelect?.Text?.Trim()
                ?? string.Empty;
        }

        private void PopulateChatStatusSelects()
        {
            if (tabChat_statusSelect == null) return;
            _isRefreshingStatusCombos = true;
            try
            {
                string currentChat = GetSelectedChatStatus();
                tabChat_statusSelect.SuspendLayout();
                tabChat_statusSelect.Items.Clear();
                tabChat_statusSelect.Items.Add("Tất cả");
                foreach (var s in _chatStatusCache) tabChat_statusSelect.Items.Add(s);

                if (tabChat_statusSelect.Items.Contains(currentChat))
                    tabChat_statusSelect.SelectedItem = currentChat;
                else
                    tabChat_statusSelect.SelectedIndex = 0;
            }
            finally
            {
                tabChat_statusSelect.ResumeLayout();
                _isRefreshingStatusCombos = false;
            }
        }

        private void RefreshChatStatusCache(List<WaybillDbModel> chatSourceData)
        {
            _chatStatusCache = chatSourceData
                .Select(x => x.ThaoTacCuoi)
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(s => s)
                .ToList();
        }

        private void UpdateChatGridDataSource(List<WaybillDbModel> data)
        {
            if (tabChat_dataGrid.InvokeRequired)
                tabChat_dataGrid.Invoke(new Action(() => tabChat_dataGrid.DataSource = data));
            else
                tabChat_dataGrid.DataSource = data;
        }

        // ======================================================================================
        // GRID COLUMN SORTING HANDLERS
        // ======================================================================================

        private void tabDash_dataGrid_ColumnHeaderMouseClick(object sender, DataGridViewCellMouseEventArgs e)
        {
            var dgv = tabDash_dataGridView;
            if (dgv == null || e.RowIndex != -1 || e.ColumnIndex < 0) return;
            var clicked = dgv.Columns[e.ColumnIndex];
            if (clicked == null || string.IsNullOrEmpty(clicked.DataPropertyName)) return;

            var direction = clicked.HeaderCell.SortGlyphDirection == SortOrder.Ascending ? SortOrder.Descending : SortOrder.Ascending;
            var dataSource = dgv.DataSource as List<WaybillDbModel>;
            if (dataSource != null)
            {
                var propInfo = typeof(WaybillDbModel).GetProperty(clicked.DataPropertyName);
                if (propInfo != null)
                {
                    if (direction == SortOrder.Ascending) dgv.DataSource = dataSource.OrderBy(x => propInfo.GetValue(x, null)).ToList();
                    else dgv.DataSource = dataSource.OrderByDescending(x => propInfo.GetValue(x, null)).ToList();
                }
            }

            clicked.HeaderCell.SortGlyphDirection = direction;
            foreach (DataGridViewColumn col in dgv.Columns) if (col != clicked) col.HeaderCell.SortGlyphDirection = SortOrder.None;
        }

        private void uiDataGridView2_ColumnHeaderMouseClick(object sender, DataGridViewCellMouseEventArgs e)
        {
            var dgv = uiDataGridView2;
            if (dgv == null || e.RowIndex != -1 || e.ColumnIndex < 0) return;
            var clicked = dgv.Columns[e.ColumnIndex];
            if (clicked == null || string.IsNullOrEmpty(clicked.DataPropertyName)) return;

            var direction = clicked.HeaderCell.SortGlyphDirection == SortOrder.Ascending ? SortOrder.Descending : SortOrder.Ascending;
            var dataSource = dgv.DataSource;

            if (dataSource is List<SlaAgingRow> slaList)
            {
                var propInfo = typeof(SlaAgingRow).GetProperty(clicked.DataPropertyName);
                if (propInfo != null)
                {
                    if (direction == SortOrder.Ascending) dgv.DataSource = slaList.OrderBy(x => propInfo.GetValue(x, null)).ToList();
                    else dgv.DataSource = slaList.OrderByDescending(x => propInfo.GetValue(x, null)).ToList();
                }
            }
            else if (dataSource is List<ThoiHieuRow> thList)
            {
                var propInfo = typeof(ThoiHieuRow).GetProperty(clicked.DataPropertyName);
                if (propInfo != null)
                {
                    if (direction == SortOrder.Ascending) dgv.DataSource = thList.OrderBy(x => propInfo.GetValue(x, null)).ToList();
                    else dgv.DataSource = thList.OrderByDescending(x => propInfo.GetValue(x, null)).ToList();
                    // Re-freeze columns after sorting
                    dgv.Columns["STT"].Frozen = true;
                }
            }

            clicked.HeaderCell.SortGlyphDirection = direction;
            foreach (DataGridViewColumn col in dgv.Columns) if (col != clicked) col.HeaderCell.SortGlyphDirection = SortOrder.None;
        }

        private void tabChat_dataGrid_ColumnHeaderMouseClick(object sender, DataGridViewCellMouseEventArgs e)
        {
            if (e.RowIndex != -1 || e.ColumnIndex < 0) return;
            var column = tabChat_dataGrid.Columns[e.ColumnIndex];
            if (column == null || string.IsNullOrEmpty(column.DataPropertyName)) return;

            var direction = column.HeaderCell.SortGlyphDirection == SortOrder.Ascending ? SortOrder.Descending : SortOrder.Ascending;
            var dataSource = tabChat_dataGrid.DataSource as List<WaybillDbModel>;
            if (dataSource != null)
            {
                var propInfo = typeof(WaybillDbModel).GetProperty(column.DataPropertyName);
                if (propInfo != null)
                {
                    if (direction == SortOrder.Ascending) tabChat_dataGrid.DataSource = dataSource.OrderBy(x => propInfo.GetValue(x, null)).ToList();
                    else tabChat_dataGrid.DataSource = dataSource.OrderByDescending(x => propInfo.GetValue(x, null)).ToList();
                }
            }

            column.HeaderCell.SortGlyphDirection = direction;
            foreach (DataGridViewColumn col in tabChat_dataGrid.Columns) if (col != column) col.HeaderCell.SortGlyphDirection = SortOrder.None;
        }

        // ======================================================================================
        // GRID COLOR STYLING HANDLERS
        // ======================================================================================

        private void tabDash_dataGridView_CellFormatting(object sender, DataGridViewCellFormattingEventArgs e)
        {
            if (e.RowIndex < 0 || e.RowIndex >= tabDash_dataGridView.Rows.Count) return;
            var row = tabDash_dataGridView.Rows[e.RowIndex];
            var model = row.DataBoundItem as WaybillDbModel;
            if (model == null) return;

            string colName = tabDash_dataGridView.Columns[e.ColumnIndex]?.Name ?? "";

            // STT column
            if (colName == "STT")
            {
                e.Value = (e.RowIndex + 1).ToString();
                e.CellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
                e.FormattingApplied = true;
                return;
            }

            NormalizeGridEmptyCell(e);

            // Set row background based on state (priority order)
            if (IsSlaBreached(model.ThoiGianNhanHang))
            {
                row.DefaultCellStyle.BackColor = Color.FromArgb(254, 242, 242);
                row.DefaultCellStyle.SelectionBackColor = Color.FromArgb(185, 28, 28);
            }
            else if (IsSlaUrgent(model.ThoiGianNhanHang))
            {
                row.DefaultCellStyle.BackColor = Color.FromArgb(255, 251, 235);
                row.DefaultCellStyle.SelectionBackColor = Color.FromArgb(180, 83, 9);
            }
            else if (IsFailedDelivery(model))
            {
                row.DefaultCellStyle.BackColor = Color.FromArgb(255, 247, 237);
                row.DefaultCellStyle.SelectionBackColor = Color.FromArgb(194, 65, 12);
            }
            else if (IsPendingReturn(model))
            {
                row.DefaultCellStyle.BackColor = Color.FromArgb(245, 243, 255);
                row.DefaultCellStyle.SelectionBackColor = Color.FromArgb(109, 40, 217);
            }
            else if (IsNotDispatched(model))
            {
                row.DefaultCellStyle.BackColor = Color.FromArgb(239, 246, 255);
                row.DefaultCellStyle.SelectionBackColor = Color.FromArgb(29, 78, 216);
            }
            else if (model.ThaoTacCuoi != null && model.ThaoTacCuoi.Contains("Ký nhận"))
            {
                row.DefaultCellStyle.BackColor = Color.FromArgb(240, 253, 244);
                row.DefaultCellStyle.SelectionBackColor = Color.FromArgb(21, 128, 61);
            }
            else if (GetWarehouseAgeDays(model.ThoiGianThaoTac) >= 2)
            {
                row.DefaultCellStyle.BackColor = Color.FromArgb(250, 245, 255);
                row.DefaultCellStyle.SelectionBackColor = Color.FromArgb(126, 34, 206);
            }
            else
            {
                row.DefaultCellStyle.BackColor = Color.White;
                row.DefaultCellStyle.SelectionBackColor = AccentBlue;
            }
            row.DefaultCellStyle.SelectionForeColor = Color.White;
        }

        private void uiDataGridView2_CellFormatting(object sender, DataGridViewCellFormattingEventArgs e)
        {
            if (e.RowIndex < 0 || e.RowIndex >= uiDataGridView2.Rows.Count) return;
            var row = uiDataGridView2.Rows[e.RowIndex];
            if (row.DataBoundItem == null) return;

            // Handle ThoiHieuRow (new delivery tracking)
            if (row.DataBoundItem is ThoiHieuRow th)
            {
                string colName = uiDataGridView2.Columns[e.ColumnIndex]?.Name ?? "";
                if (colName == "TiLeKyNhan")
                {
                    e.CellStyle.Format = "P1";
                    if (th.TiLeKyNhan >= 0.8)
                        e.CellStyle.BackColor = Color.FromArgb(198, 239, 206); // green
                    else if (th.TiLeKyNhan >= 0.6)
                        e.CellStyle.BackColor = Color.FromArgb(255, 235, 156); // orange
                    else
                        e.CellStyle.BackColor = Color.FromArgb(255, 199, 206); // red
                    e.CellStyle.SelectionBackColor = e.CellStyle.BackColor;
                }
                else if (colName.StartsWith("h_"))
                {
                    int hourIdx = int.Parse(colName.Substring(2));
                    if (hourIdx >= 8 && hourIdx <= 24)
                    {
                        bool completed = false;
                        // Map to individual property
                        string propName = $"H{hourIdx}";
                        var prop = typeof(ThoiHieuRow).GetProperty(propName);
                        if (prop != null)
                            completed = (bool)(prop.GetValue(th) ?? false);

                        e.CellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
                        if (completed)
                        {
                            e.CellStyle.BackColor = Color.FromArgb(198, 239, 206);
                            e.CellStyle.ForeColor = Color.FromArgb(0, 100, 0);
                            e.Value = "✓";
                        }
                        else
                        {
                            e.CellStyle.BackColor = Color.FromArgb(255, 199, 206);
                            e.CellStyle.ForeColor = Color.FromArgb(180, 0, 0);
                            e.Value = "✗";
                        }
                        e.CellStyle.SelectionBackColor = e.CellStyle.BackColor;
                        e.FormattingApplied = true;
                    }
                }
                else if (colName == "DonCanKy91" || colName == "KPI90")
                {
                    e.CellStyle.BackColor = Color.FromArgb(255, 255, 204); // light yellow highlight
                }
                NormalizeGridEmptyCell(e);
                return;
            }

            // Handle SlaAgingRow (legacy)
            if (row.DataBoundItem is SlaAgingRow model)
            {
                if (model.LevelCanhBao == "Nghiêm trọng")
                {
                    row.DefaultCellStyle.BackColor = Color.FromArgb(254, 219, 219);
                    row.DefaultCellStyle.SelectionBackColor = Color.FromArgb(253, 172, 172);
                }
                else if (model.LevelCanhBao == "Cảnh báo")
                {
                    row.DefaultCellStyle.BackColor = Color.FromArgb(255, 247, 212);
                    row.DefaultCellStyle.SelectionBackColor = Color.FromArgb(255, 236, 153);
                }
                else
                {
                    row.DefaultCellStyle.BackColor = Color.White;
                    row.DefaultCellStyle.SelectionBackColor = Color.FromArgb(80, 160, 255);
                }
            }
            NormalizeGridEmptyCell(e);
        }

        private void FullStackGrid_CellFormatting(object sender, DataGridViewCellFormattingEventArgs e)
        {
            if (e.RowIndex < 0 || e.ColumnIndex < 0)
                return;
            NormalizeGridEmptyCell(e);
        }

        private static void NormalizeGridEmptyCell(DataGridViewCellFormattingEventArgs e)
        {
            if (e == null)
                return;

            if (e.DesiredType != null && e.DesiredType != typeof(string) && e.DesiredType != typeof(object))
                return;

            if (TryDisplayDash(e.Value, out var display))
            {
                e.Value = display;
                e.FormattingApplied = true;
            }
        }

        private static bool TryDisplayDash(object value, out string display)
        {
            display = null;
            if (value == null || value == DBNull.Value)
            {
                display = "-";
                return true;
            }

            if (value is string text)
            {
                text = text.Trim();
                if (string.IsNullOrWhiteSpace(text) ||
                    string.Equals(text, "empty", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(text, "--", StringComparison.OrdinalIgnoreCase))
                {
                    display = "-";
                    return true;
                }
            }

            return false;
        }

        private void FullStackGrid_DataError(object sender, DataGridViewDataErrorEventArgs e)
        {
            e.ThrowException = false;
            e.Cancel = true;
            AppLogger.Warning(
                "[FullStackOperation] DataGridView data error suppressed " +
                $"grid={(sender as DataGridView)?.Name ?? "-"}; row={e.RowIndex}; col={e.ColumnIndex}; " +
                $"context={e.Context}; message={e.Exception?.Message}");
        }

        // ======================================================================================
        // ENHANCED DASHBOARD - REALTIME OPERATIONS COORDINATOR
        // ======================================================================================

        private void InitializeEnhancedUI()
        {
            SetupDetailTab();
            SetupThoiHieuTab();
            WireEnhancedEvents();
            SetupAlertCheck();
        }

        private void SetupDashToolbar()
        {
            try
            {
                if (_operationQueueSidebar != null && _operationDetailPanel != null)
                    return;

                var tl = uiTableLayoutPanel18;
                if (tl == null) return;

                var flow = new FlowLayoutPanel();
                flow.Dock = DockStyle.Fill;
                flow.FlowDirection = FlowDirection.LeftToRight;
                flow.WrapContents = false;
                flow.Margin = new Padding(2);
                flow.Padding = new Padding(3);
                flow.BackColor = Color.Transparent;

                _dashSearchBox = new TextBox();
                _dashSearchBox.Font = new Font("Segoe UI", 11F);
                _dashSearchBox.Margin = new Padding(2, 6, 4, 2);
                _dashSearchBox.Size = new Size(160, 26);
                _dashSearchBox.Text = "";
                _dashSearchBox.ForeColor = Color.Gray;
                _dashSearchBox.TextChanged += (s, e) =>
                {
                    if (_cloudData.Count == 0) return;
                    ApplyDashFilter(_cloudData);
                    UpdateFilterInfo();
                };
                flow.Controls.Add(_dashSearchBox);

                _dashDateFrom = new DateTimePicker();
                _dashDateFrom.Font = new Font("Segoe UI", 9F);
                _dashDateFrom.Format = DateTimePickerFormat.Short;
                _dashDateFrom.Margin = new Padding(4, 6, 2, 2);
                _dashDateFrom.Size = new Size(110, 24);
                _dashDateFrom.Checked = false;
                _dashDateFrom.ShowCheckBox = true;
                _dashDateFrom.ValueChanged += (s, e) =>
                {
                    if (_cloudData.Count == 0) return;
                    ApplyDashFilter(_cloudData);
                    UpdateFilterInfo();
                };
                flow.Controls.Add(_dashDateFrom);

                _dashDateTo = new DateTimePicker();
                _dashDateTo.Font = new Font("Segoe UI", 9F);
                _dashDateTo.Format = DateTimePickerFormat.Short;
                _dashDateTo.Margin = new Padding(2, 6, 4, 2);
                _dashDateTo.Size = new Size(110, 24);
                _dashDateTo.Checked = false;
                _dashDateTo.ShowCheckBox = true;
                _dashDateTo.ValueChanged += (s, e) =>
                {
                    if (_cloudData.Count == 0) return;
                    ApplyDashFilter(_cloudData);
                    UpdateFilterInfo();
                };
                flow.Controls.Add(_dashDateTo);

                _dashExportBtn = new UISymbolButton();
                _dashExportBtn.Font = new Font("Segoe UI Semibold", 10F, FontStyle.Bold);
                _dashExportBtn.Margin = new Padding(4, 4, 2, 2);
                _dashExportBtn.Size = new Size(40, 30);
                _dashExportBtn.Symbol = 61714;
                _dashExportBtn.SymbolSize = 16;
                _dashExportBtn.Radius = 8;
                _dashExportBtn.FillColor = Color.FromArgb(0, 150, 0);
                _dashExportBtn.FillHoverColor = Color.FromArgb(0, 180, 0);
                _tooltip.SetToolTip(_dashExportBtn, "Xuất Excel");
                _dashExportBtn.Click += ExportDashToExcel;
                flow.Controls.Add(_dashExportBtn);

                _dashFilterInfo = new Label();
                _dashFilterInfo.Font = new Font("Segoe UI", 9F, FontStyle.Italic);
                _dashFilterInfo.ForeColor = Color.DimGray;
                _dashFilterInfo.Margin = new Padding(6, 6, 2, 2);
                _dashFilterInfo.AutoSize = true;
                _dashFilterInfo.Text = "";
                flow.Controls.Add(_dashFilterInfo);

                var commandLayout = new TableLayoutPanel();
                commandLayout.Dock = DockStyle.Fill;
                commandLayout.ColumnCount = 1;
                commandLayout.RowCount = 2;
                commandLayout.Margin = Padding.Empty;
                commandLayout.Padding = Padding.Empty;
                commandLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38F));
                commandLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
                commandLayout.Controls.Add(flow, 0, 0);
                commandLayout.Controls.Add(CreateDashQuickFilterPanel(), 0, 1);

                tl.Controls.Add(commandLayout, 2, 0);
            }
            catch { }
        }

        private Control CreateDashQuickFilterPanel()
        {
            var host = new TableLayoutPanel();
            host.Dock = DockStyle.Fill;
            host.ColumnCount = 2;
            host.RowCount = 1;
            host.Margin = Padding.Empty;
            host.Padding = Padding.Empty;
            host.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            host.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 245F));
            host.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

            _dashQuickFilterPanel = new FlowLayoutPanel();
            _dashQuickFilterPanel.Dock = DockStyle.Fill;
            _dashQuickFilterPanel.FlowDirection = FlowDirection.LeftToRight;
            _dashQuickFilterPanel.WrapContents = false;
            _dashQuickFilterPanel.AutoScroll = true;
            _dashQuickFilterPanel.Margin = new Padding(2, 0, 2, 0);
            _dashQuickFilterPanel.Padding = new Padding(3, 2, 3, 2);
            _dashQuickFilterPanel.BackColor = Color.Transparent;

            AddDashQuickFilterButton("Cần xử lý", "Cần xử lý ngay", AccentRed);
            AddDashQuickFilterButton("Chưa phát", "Chưa quét phát", Color.FromArgb(220, 125, 40));
            AddDashQuickFilterButton("SLA trễ", "SLA quá hạn", Color.FromArgb(190, 40, 40));
            AddDashQuickFilterButton("Sắp trễ", "SLA sắp trễ (<4 giờ)", Color.FromArgb(190, 145, 25));
            AddDashQuickFilterButton(">48h", "Tồn quá hạn (>48h)", Color.FromArgb(120, 80, 180));
            AddDashQuickFilterButton("KVD", "Giao thất bại", AccentBlue);
            AddDashQuickFilterButton("Tất cả", "Tất cả tồn kho", Color.FromArgb(90, 105, 115));

            _dashQueueInsightLabel = new Label();
            _dashQueueInsightLabel.Dock = DockStyle.Fill;
            _dashQueueInsightLabel.TextAlign = ContentAlignment.MiddleRight;
            _dashQueueInsightLabel.Font = new Font("Segoe UI", 8.5F, FontStyle.Bold);
            _dashQueueInsightLabel.ForeColor = Color.FromArgb(75, 75, 75);
            _dashQueueInsightLabel.AutoEllipsis = true;
            _dashQueueInsightLabel.Margin = new Padding(0, 2, 4, 2);
            _dashQueueInsightLabel.Text = "Cần xử lý: 0";

            host.Controls.Add(_dashQuickFilterPanel, 0, 0);
            host.Controls.Add(_dashQueueInsightLabel, 1, 0);
            return host;
        }

        private void AddDashQuickFilterButton(string text, string status, Color color)
        {
            if (_dashQuickFilterPanel == null) return;

            var btn = new Button();
            btn.Text = text;
            btn.Tag = status;
            btn.AutoSize = false;
            btn.Size = new Size(76, 28);
            btn.Margin = new Padding(2, 1, 2, 1);
            btn.FlatStyle = FlatStyle.Flat;
            btn.FlatAppearance.BorderSize = 1;
            btn.FlatAppearance.BorderColor = Color.FromArgb(210, 215, 225);
            btn.BackColor = Color.White;
            btn.ForeColor = color;
            btn.Font = new Font("Segoe UI Semibold", 8.5F, FontStyle.Bold);
            btn.TextAlign = ContentAlignment.MiddleCenter;
            _tooltip?.SetToolTip(btn, status);
            btn.Click += DashQuickFilter_Click;
            _dashQuickFilterPanel.Controls.Add(btn);
        }

        private void DashQuickFilter_Click(object sender, EventArgs e)
        {
            if (sender is not Button btn) return;
            var status = btn.Tag?.ToString() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(status)) return;

            _dashQuickFilter = status;
            SelectDashStatus(status);
            UpdateDashQuickFilterButtons();
            RefreshFilteredGrid();
        }

        private void SelectDashStatus(string status)
        {
            if (tabDash_statusSelect == null) return;

            for (int i = 0; i < tabDash_statusSelect.Items.Count; i++)
            {
                if (string.Equals(tabDash_statusSelect.Items[i]?.ToString(), status, StringComparison.OrdinalIgnoreCase))
                {
                    tabDash_statusSelect.SelectedIndex = i;
                    return;
                }
            }

            tabDash_statusSelect.Items.Insert(0, status);
            tabDash_statusSelect.SelectedIndex = 0;
        }

        private void UpdateDashQuickFilterButtons()
        {
            if (_dashQuickFilterPanel == null) return;

            var selected = GetSelectedDashStatus();
            foreach (Control control in _dashQuickFilterPanel.Controls)
            {
                if (control is not Button btn) continue;
                var status = btn.Tag?.ToString() ?? string.Empty;
                bool active = string.Equals(status, selected, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(status, _dashQuickFilter, StringComparison.OrdinalIgnoreCase);
                btn.BackColor = active ? Color.FromArgb(35, 45, 55) : Color.White;
                btn.ForeColor = active ? Color.White : GetQuickFilterColor(status);
                btn.FlatAppearance.BorderColor = active ? Color.FromArgb(35, 45, 55) : Color.FromArgb(210, 215, 225);
            }
        }

        private static Color GetQuickFilterColor(string status)
        {
            return status switch
            {
                "Cần xử lý ngay" => AccentRed,
                "Chưa quét phát" => Color.FromArgb(220, 125, 40),
                "SLA quá hạn" => Color.FromArgb(190, 40, 40),
                "SLA sắp trễ (<4 giờ)" => Color.FromArgb(190, 145, 25),
                "Tồn quá hạn (>48h)" => Color.FromArgb(120, 80, 180),
                "Giao thất bại" => AccentBlue,
                _ => Color.FromArgb(90, 105, 115)
            };
        }

        private void UpdateDashQueueInsight()
        {
            if (_dashQueueInsightLabel == null) return;

            int needsAction = _cloudData.Count(IsNeedsAction);
            int slaLate = _cloudData.Count(x => IsSlaBreached(x.ThoiGianNhanHang));
            int over48 = _cloudData.Count(x => GetWarehouseAgeDays(x.ThoiGianThaoTac) >= 2.0);
            _dashQueueInsightLabel.Text = $"Cần xử lý {needsAction:N0} | SLA {slaLate:N0} | >48h {over48:N0}";
            _dashQueueInsightLabel.ForeColor = needsAction > 0 ? AccentRed : AccentGreen;
            UpdateDashQuickFilterButtons();
        }

        private void UpdateOperationCenterChrome(int sourceCount, int filteredCount)
        {
            if (_operationHeaderStatus != null)
                _operationHeaderStatus.Text = $"SQLite local | {filteredCount:N0}/{sourceCount:N0} đơn trong queue | {DateTime.Now:HH:mm:ss}";

            string selected = GetSelectedDashStatus();
            if (_operationStatusFooter != null)
            {
                int needsAction = _cloudData.Count(IsNeedsAction);
                int slaLate = _cloudData.Count(x => IsSlaBreached(x.ThoiGianNhanHang));
                int over48 = _cloudData.Count(x => GetWarehouseAgeDays(x.ThoiGianThaoTac) >= 2.0);
                _operationStatusFooter.SetStatus(
                    $"Queue: {(string.IsNullOrWhiteSpace(selected) ? "Tất cả tồn kho" : selected)}",
                    $"Cần xử lý {needsAction:N0} | SLA trễ {slaLate:N0} | Tồn >48h {over48:N0}");
            }

            UpdateOperationQueues();
            UpdateOperationMiniMetrics();
        }

        private void UpdatePriorityFocusCards(int sourceCount, int filteredCount)
        {
            int needsAction = _cloudData.Count(IsNeedsAction);
            int slaLate = _cloudData.Count(x => IsSlaBreached(x.ThoiGianNhanHang));
            int over48 = _cloudData.Count(x => GetWarehouseAgeDays(x.ThoiGianThaoTac) >= 2.0);
            int lostRisk = _cloudData.Count(IsLostRisk);
            int critical = _cloudData.Count(x => GetRiskScore(x) >= 80);

            _kpiCriticalFocus?.SetMetric(
                "Critical focus",
                needsAction.ToString("N0"),
                $"{critical:N0} critical risk | cần xử lý trước",
                "CRIT",
                needsAction > 0 ? AccentRed : AccentGreen,
                "Cần xử lý ngay");

            _kpiSlaBreach?.SetMetric(
                "SLA breach",
                slaLate.ToString("N0"),
                "Quá 24h từ thời gian nhận hàng",
                "SLA",
                slaLate > 0 ? AccentRed : AccentGreen,
                "SLA quá hạn");

            _kpiAgingRisk?.SetMetric(
                "Aging risk",
                over48.ToString("N0"),
                $"{lostRisk:N0} lost-risk | tồn trên 48h",
                "RISK",
                over48 > 0 ? AccentPurple : AccentGreen,
                "Tồn quá hạn (>48h)");

            _kpiDataHealth?.SetMetric(
                "Data health",
                $"{filteredCount:N0}/{sourceCount:N0}",
                $"Local rows {_cloudData.Count:N0} | {DateTime.Now:HH:mm:ss}",
                "LOCAL",
                AccentBlue,
                "Tất cả tồn kho");
        }

        private void UpdateOperationQueues()
        {
            if (_operationQueueSidebar == null) return;
            string selected = GetSelectedDashStatus();
            int Count(Func<WaybillDbModel, bool> predicate) => _lastDashSourceData.Count(predicate);

            var queues = new List<OperationQueueItem>
            {
                CreateQueue("Tất cả tồn kho", "Tất cả", "Toàn bộ dữ liệu local", _lastDashSourceData.Count, Color.FromArgb(90, 105, 115), selected),
                CreateQueue("Cần xử lý ngay", "Cần xử lý", "Gộp SLA/KVD/tồn lâu", Count(IsNeedsAction), AccentRed, selected),
                CreateQueue("Chưa quét phát", "Chưa phát", "Đã xuống kiện, chưa dispatch", Count(IsNotDispatched), Color.FromArgb(220, 125, 40), selected),
                CreateQueue("SLA quá hạn", "SLA trễ", "Quá 24h từ lúc nhận hàng", Count(x => IsSlaBreached(x.ThoiGianNhanHang)), Color.FromArgb(190, 40, 40), selected),
                CreateQueue("SLA sắp trễ (<4 giờ)", "Sắp trễ", "Còn dưới 4 giờ", Count(x => IsSlaUrgent(x.ThoiGianNhanHang)), Color.FromArgb(190, 145, 25), selected),
                CreateQueue("Giao thất bại", "KVD", "Có kiện vấn đề/giao thất bại", Count(IsFailedDelivery), AccentBlue, selected),
                CreateQueue("Chờ hoàn", "Chờ hoàn", "Có dấu chuyển hoàn chưa xong", Count(IsPendingReturn), AccentPurple, selected),
                CreateQueue("Tồn quá hạn (>48h)", ">48h", "Nằm kho từ 2 ngày trở lên", Count(x => GetWarehouseAgeDays(x.ThoiGianThaoTac) >= 2.0), Color.FromArgb(120, 80, 180), selected),
                CreateQueue("Dừng 3 ngày", "Dừng 3d", "Hành trình đứng từ 3 ngày", Count(x => GetWarehouseAgeDays(x.ThoiGianThaoTac) >= 3.0), Color.FromArgb(95, 80, 160), selected),
                CreateQueue("Nguy cơ thất lạc", "Lost risk", "Tồn lâu/chưa có kết thúc", Count(IsLostRisk), Color.FromArgb(70, 55, 140), selected),
                CreateQueue("Đơn mới hôm nay", "Đơn mới", "Vừa xuống kiện/đến kho", Count(IsNewArrival), AccentGreen, selected)
            };

            _operationQueueSidebar.SetQueues(queues);
        }

        private static OperationQueueItem CreateQueue(string key, string title, string description, int count, Color color, string selected)
        {
            return new OperationQueueItem
            {
                Key = key,
                Title = title,
                Description = description,
                Count = count,
                AccentColor = color,
                Active = string.Equals(key, selected, StringComparison.OrdinalIgnoreCase)
            };
        }

        private void UpdateOperationMiniMetrics()
        {
            if (_operationMiniMetricStrip == null) return;
            _operationMiniMetricStrip.SuspendLayout();
            try
            {
                _operationMiniMetricStrip.Controls.Clear();
                AddMiniMetric("Local rows", _cloudData.Count.ToString("N0"), AccentSlate);
                AddMiniMetric("Filtered", _lastFilteredDashRows.Count.ToString("N0"), AccentBlue);
                AddMiniMetric("Critical", _cloudData.Count(x => GetRiskScore(x) >= 80).ToString("N0"), AccentRed);
                AddMiniMetric("High", _cloudData.Count(x => GetRiskScore(x) >= 60 && GetRiskScore(x) < 80).ToString("N0"), Color.FromArgb(220, 125, 40));
                AddMiniMetric("Workflow", "SQLite", AccentGreen);
                AddMiniMetric("Lost risk", _cloudData.Count(IsLostRisk).ToString("N0"), AccentPurple);
                AddMiniMetric("Saved view", string.IsNullOrWhiteSpace(_savedOperationFilter) && string.IsNullOrWhiteSpace(_savedOperationSearch) ? "No" : "Yes", AccentBlue);
            }
            finally
            {
                _operationMiniMetricStrip.ResumeLayout(true);
            }
        }

        private void AddMiniMetric(string label, string value, Color color)
        {
            var item = new Label
            {
                AutoSize = false,
                Width = 118,
                Height = 24,
                Margin = new Padding(0, 0, 8, 0),
                TextAlign = ContentAlignment.MiddleCenter,
                Font = new Font("Segoe UI Semibold", 8.5F, FontStyle.Bold),
                BackColor = Color.FromArgb(245, 248, 251),
                ForeColor = color,
                Text = $"{label}: {value}"
            };
            _operationMiniMetricStrip.Controls.Add(item);
        }

        private void OperationQueueSidebar_QueueSelected(object sender, OperationQueueSelectedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(e?.Key)) return;
            _dashQuickFilter = e.Key;
            SelectDashStatus(e.Key);
            UpdateDashQuickFilterButtons();
            RefreshFilteredGrid();
            SetFullStackStatus($"Đang lọc grid: {e.Key}");
        }

        private void OperationKpiCard_Clicked(object sender, OperationQueueSelectedEventArgs e)
        {
            OperationQueueSidebar_QueueSelected(sender, e);
        }

        private void OperationGridFilterToolbar_FilterRequested(object sender, OperationGridFilterEventArgs e)
        {
            if (e == null || string.IsNullOrWhiteSpace(e.Key)) return;
            _dashQuickFilter = e.Key;
            SelectDashStatus(e.Key);
            UpdateDashQuickFilterButtons();
            RefreshFilteredGrid();
        }

        private void OperationGridFilterToolbar_PresetRequested(object sender, OperationGridFilterEventArgs e)
        {
            if (e == null) return;
            if (e.Key == "SAVE_PRESET")
                SaveCurrentOperationPreset();
            else if (e.Key == "APPLY_PRESET")
                ApplySavedOperationPreset();
        }

        private async void OperationDetailPanel_ActionRequested(object sender, OperationDetailActionEventArgs e)
        {
            if (e == null || string.IsNullOrWhiteSpace(e.WaybillNo))
            {
                MessageBox.Show("Chưa chọn vận đơn.", "Operation Center", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            try
            {
                switch (e.Action)
                {
                    case "COPY":
                        Clipboard.SetText(e.WaybillNo);
                        SetFullStackStatus($"Đã copy {e.WaybillNo}");
                        break;
                    case "ADD_NOTE":
                        if (string.IsNullOrWhiteSpace(e.Value))
                        {
                            MessageBox.Show("Nhập nội dung note trước khi lưu.", "Operation Center", MessageBoxButtons.OK, MessageBoxIcon.Information);
                            return;
                        }
                        await _fullStackWorkflowService.AddNoteAsync(e.WaybillNo, e.Value, Environment.UserName, _cts.Token);
                        SetFullStackStatus($"Đã thêm note cho {e.WaybillNo}");
                        break;
                    case "MARK_CHECKED":
                        await _fullStackWorkflowService.MarkCheckedAsync(e.WaybillNo, Environment.UserName, "Đã kiểm tra tồn thực tế", _cts.Token);
                        SetFullStackStatus($"Đã đánh dấu kiểm tra {e.WaybillNo}");
                        break;
                    case "CREATE_TASK":
                        await _fullStackWorkflowService.CreateTaskAsync(
                            e.WaybillNo,
                            "CHECK_PHYSICAL_STOCK",
                            Math.Max(50, GetRiskScore(GetWaybillByNo(e.WaybillNo))),
                            Environment.UserName,
                            "Tạo task kiểm tra tồn từ Operation Center",
                            _cts.Token);
                        SetFullStackStatus($"Đã tạo task kiểm tồn {e.WaybillNo}");
                        break;
                    case "EXPORT_SELECTED":
                        await ExportSelectedWaybillAsync(e.WaybillNo);
                        break;
                }

                var model = GetWaybillByNo(e.WaybillNo);
                await RefreshOperationMetadataAsync();
                RefreshFilteredGrid();
                model = GetWaybillByNo(e.WaybillNo) ?? model;
                if (model != null)
                    await UpdateOperationDetailPanelAsync(model);
            }
            catch (Exception ex)
            {
                AppLogger.Warning($"Operation Center action failed: {ex.Message}");
                MessageBox.Show("Lỗi thao tác Operation Center: " + ex.Message, "Operation Center", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private WaybillDbModel GetWaybillByNo(string waybillNo)
        {
            if (string.IsNullOrWhiteSpace(waybillNo)) return null;
            return _lastFilteredDashRows.FirstOrDefault(x => string.Equals(x.WaybillNo, waybillNo, StringComparison.OrdinalIgnoreCase))
                ?? _lastDashSourceData.FirstOrDefault(x => string.Equals(x.WaybillNo, waybillNo, StringComparison.OrdinalIgnoreCase))
                ?? _cloudData.FirstOrDefault(x => string.Equals(x.WaybillNo, waybillNo, StringComparison.OrdinalIgnoreCase));
        }

        private void UpdateSelectedOperationDetailFromGrid()
        {
            if (tabDash_dataGridView?.CurrentRow?.DataBoundItem is WaybillDbModel model)
                _ = UpdateOperationDetailPanelAsync(model);
            else if (_lastFilteredDashRows.Count > 0)
                _ = UpdateOperationDetailPanelAsync(_lastFilteredDashRows[0]);
            else
                _operationDetailPanel?.SetDetail(new OperationWaybillDetail());
        }

        private async Task UpdateOperationDetailPanelAsync(WaybillDbModel model)
        {
            if (model == null || _operationDetailPanel == null) return;

            FullStackWorkflowSnapshot workflow = new FullStackWorkflowSnapshot();
            IReadOnlyList<TrackingEvent> trackingEvents = Array.Empty<TrackingEvent>();
            try
            {
                workflow = await _fullStackWorkflowService.LoadWorkflowAsync(model.WaybillNo, _cts.Token);
                trackingEvents = await _fullStackWorkflowService.LoadTrackingEventsAsync(model.WaybillNo, _cts.Token);
            }
            catch (Exception ex)
            {
                AppLogger.Warning($"Load workflow failed for {model.WaybillNo}: {ex.Message}");
            }

            var notes = workflow.Notes
                .Select(x => $"{x.CreatedAt.ToLocalTime():HH:mm dd/MM} [{CleanDisplay(x.CreatedBy)}] {x.Note}")
                .ToList();
            var tasks = workflow.Tasks
                .Select(x => $"{x.Status} P{x.Priority} {x.TaskType} due {FormatLocalDate(x.DueAt)}")
                .ToList();

            if (!string.IsNullOrWhiteSpace(model.WaybillNo) && _operationMetadata.TryGetValue(model.WaybillNo, out var metadata))
            {
                if (metadata.IsChecked)
                    notes.Insert(0, $"Checked: {FormatLocalDate(metadata.CheckedAt)} bởi {CleanDisplay(metadata.CheckedBy)}");
                if (metadata.HasTask)
                    tasks.Insert(0, metadata.HasOpenTask ? "Task: còn task đang mở" : "Task: đã từng tạo task");
                if (metadata.IsEnriched)
                    tasks.Insert(0, $"Enriched: {FormatLocalDate(metadata.EnrichedAt)} | {metadata.TrackingEventCount:N0} event");
            }

            var detail = new OperationWaybillDetail
            {
                WaybillNo = CleanDisplay(model.WaybillNo),
                State = GetOperationState(model),
                RiskLevel = GetRiskLevel(model),
                RiskScore = GetRiskScore(model),
                SlaStatus = GetSlaStatusText(model),
                AgeText = CalculateWarehouseAge(model.ThoiGianThaoTac, model.ThaoTacCuoi),
                LastAction = CleanDisplay(model.ThaoTacCuoi),
                LastActionTime = CleanDisplay(model.ThoiGianThaoTac),
                Employee = CleanDisplay(model.NguoiThaoTac),
                Site = CleanDisplay(model.BuuCucThaoTac),
                KvdReason = CleanDisplay(model.NguyenNhanKienVanDe),
                RiskReasons = GetRiskReasons(model),
                RecommendedActions = GetRecommendedActions(model),
                Notes = notes,
                Tasks = tasks,
                Timeline = BuildDetailTimeline(model, trackingEvents)
            };

            if (_operationDetailPanel.InvokeRequired)
                _operationDetailPanel.BeginInvoke(new Action(() => _operationDetailPanel.SetDetail(detail)));
            else
                _operationDetailPanel.SetDetail(detail);
        }

        private string GetOperationState(WaybillDbModel row)
        {
            if (row == null) return "-";
            if (IsPendingReturn(row)) return "RETURN_PENDING";
            if (IsFailedDelivery(row)) return "FAILED_DELIVERY";
            if (IsNotDispatched(row)) return "WAITING_DISPATCH";
            if (IsSlaBreached(row.ThoiGianNhanHang)) return "SLA_BREACHED";
            if (IsSlaUrgent(row.ThoiGianNhanHang)) return "SLA_URGENT";
            if (IsLostRisk(row)) return "LOST_RISK";
            if (row.ThaoTacCuoi?.Contains("Ký nhận") == true) return "DELIVERED";
            return "IN_STOCK";
        }

        private string GetRiskLevel(WaybillDbModel row)
        {
            int score = GetRiskScore(row);
            if (score >= 80) return "CRITICAL";
            if (score >= 60) return "HIGH";
            if (score >= 35) return "MEDIUM";
            return "LOW";
        }

        private int GetRiskScore(WaybillDbModel row)
        {
            if (row == null) return 0;
            int score = 0;
            if (IsSlaBreached(row.ThoiGianNhanHang)) score += 35;
            if (IsSlaUrgent(row.ThoiGianNhanHang)) score += 20;
            if (IsFailedDelivery(row)) score += 30;
            if (IsPendingReturn(row)) score += 25;
            if (IsNotDispatched(row)) score += 20;
            double age = GetWarehouseAgeDays(row.ThoiGianThaoTac);
            if (age >= 7) score += 40;
            else if (age >= 3) score += 30;
            else if (age >= 2) score += 20;
            else if (age >= 1) score += 10;
            if (IsLostRisk(row)) score += 25;
            return Math.Min(100, score);
        }

        private string GetSlaStatusText(WaybillDbModel row)
        {
            if (row == null) return "-";
            string level;
            string remaining = CalculateSlaRemaining(row.ThoiGianNhanHang, out level);
            return $"{level} - {remaining}";
        }

        private IReadOnlyList<string> GetRiskReasons(WaybillDbModel row)
        {
            var reasons = new List<string>();
            if (row == null) return reasons;
            if (IsSlaBreached(row.ThoiGianNhanHang)) reasons.Add("SLA đã quá hạn.");
            if (IsSlaUrgent(row.ThoiGianNhanHang)) reasons.Add("SLA còn dưới 4 giờ.");
            if (IsNotDispatched(row)) reasons.Add("Đơn đã đến kho nhưng chưa quét phát.");
            if (IsFailedDelivery(row)) reasons.Add($"Kiện vấn đề/giao thất bại: {CleanDisplay(row.NguyenNhanKienVanDe)}.");
            if (IsPendingReturn(row)) reasons.Add("Đơn đang ở luồng chuyển hoàn.");
            if (GetWarehouseAgeDays(row.ThoiGianThaoTac) >= 2.0) reasons.Add("Tồn kho trên 48 giờ.");
            if (IsLostRisk(row)) reasons.Add("Hành trình đứng lâu, có nguy cơ thất lạc.");
            return reasons;
        }

        private IReadOnlyList<string> GetRecommendedActions(WaybillDbModel row)
        {
            var actions = new List<string>();
            if (row == null) return actions;
            if (IsNotDispatched(row)) actions.Add("Phân công quét phát hoặc kiểm tồn thực tế ngay.");
            if (IsSlaBreached(row.ThoiGianNhanHang)) actions.Add("Ưu tiên xử lý SLA trễ trước các queue thường.");
            if (IsSlaUrgent(row.ThoiGianNhanHang)) actions.Add("Đẩy vào tuyến phát gần nhất, tránh vượt SLA.");
            if (IsFailedDelivery(row)) actions.Add("Xác minh lý do KVD và chốt hướng giao lại/chuyển hoàn.");
            if (IsPendingReturn(row)) actions.Add("Kiểm tra trạng thái in/chốt chuyển hoàn.");
            if (IsLostRisk(row)) actions.Add("Đối soát scan thực tế với bưu cục/nhân viên cuối.");
            if (actions.Count == 0) actions.Add("Theo dõi tiếp, chưa cần can thiệp mạnh.");
            return actions;
        }

        private static IReadOnlyList<string> BuildDetailTimeline(WaybillDbModel model, IReadOnlyList<TrackingEvent> events)
        {
            var lines = new List<string>();
            if (events != null && events.Count > 0)
            {
                lines.AddRange(events.Select(e =>
                {
                    var time = e.EventTime.HasValue ? e.EventTime.Value.ToLocalTime().ToString("HH:mm dd/MM") : "-";
                    var action = CleanDisplay(e.Action);
                    var site = CleanDisplay(e.SiteName);
                    var status = CleanDisplay(e.Status);
                    return $"{time} | {action} | {site} | {status}";
                }));
                return lines;
            }

            lines.Add($"Nhận hàng: {CleanDisplay(model?.ThoiGianNhanHang)}");
            lines.Add($"Cập nhật cuối: {FormatLocalDate(model?.LastTrackedAt)}");
            return lines;
        }

        private async Task ExportOperationCurrentViewAsync(bool selectedOnly)
        {
            var rows = selectedOnly
                ? GetSelectedGridWaybills().ToList()
                : _lastFilteredDashRows.ToList();

            if (rows.Count == 0)
            {
                MessageBox.Show("Không có dữ liệu để xuất.", "Operation Center", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            string path = await _fullStackExportService.ExportWaybillsCsvAsync(rows, "operation-center", _cts.Token);
            SetFullStackStatus($"Đã xuất CSV: {Path.GetFileName(path)}");
            TryOpenPath(path);
        }

        private async Task ExportSelectedWaybillAsync(string waybillNo)
        {
            var model = GetWaybillByNo(waybillNo);
            if (model == null) return;
            string path = await _fullStackExportService.ExportWaybillsCsvAsync(new[] { model }, $"operation-{waybillNo}", _cts.Token);
            SetFullStackStatus($"Đã xuất {waybillNo}");
            TryOpenPath(path);
        }

        private IEnumerable<WaybillDbModel> GetSelectedGridWaybills()
        {
            if (tabDash_dataGridView == null) yield break;
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (DataGridViewCell cell in tabDash_dataGridView.SelectedCells)
            {
                if (cell?.OwningRow?.DataBoundItem is WaybillDbModel model && seen.Add(model.WaybillNo ?? string.Empty))
                    yield return model;
            }

            foreach (DataGridViewRow row in tabDash_dataGridView.SelectedRows)
            {
                if (row.DataBoundItem is WaybillDbModel model && seen.Add(model.WaybillNo ?? string.Empty))
                    yield return model;
            }

            if (seen.Count == 0 && tabDash_dataGridView.CurrentRow?.DataBoundItem is WaybillDbModel current)
                yield return current;
        }

        private async Task RefreshSelectedWorkflowDetailsAsync(IReadOnlyList<WaybillDbModel> rows)
        {
            var currentNo = GetCurrentSelectedWaybillNo();
            var current = rows?.FirstOrDefault(x => string.Equals(x.WaybillNo, currentNo, StringComparison.OrdinalIgnoreCase))
                ?? rows?.FirstOrDefault();
            if (current != null)
                await UpdateOperationDetailPanelAsync(current);
            await RefreshOperationMetadataAsync();
            UpdateOperationFilterToolbar();
        }

        private string GetCurrentSelectedWaybillNo()
        {
            return (tabDash_dataGridView?.CurrentRow?.DataBoundItem as WaybillDbModel)?.WaybillNo ?? string.Empty;
        }

        private void SaveCurrentOperationPreset()
        {
            _savedOperationFilter = GetSelectedDashStatus();
            _savedOperationSearch = _dashSearchBox?.Text?.Trim() ?? string.Empty;
            SetFullStackStatus($"Đã lưu view: {(string.IsNullOrWhiteSpace(_savedOperationFilter) ? "Tất cả tồn kho" : _savedOperationFilter)}");
            UpdateOperationFilterToolbar();
        }

        private void ApplySavedOperationPreset()
        {
            if (string.IsNullOrWhiteSpace(_savedOperationFilter) && string.IsNullOrWhiteSpace(_savedOperationSearch))
            {
                MessageBox.Show("Chưa có saved view trong phiên làm việc này.", "Operation Center", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            if (!string.IsNullOrWhiteSpace(_savedOperationFilter))
            {
                _dashQuickFilter = _savedOperationFilter;
                SelectDashStatus(_savedOperationFilter);
            }

            if (_dashSearchBox != null)
                _dashSearchBox.Text = _savedOperationSearch ?? string.Empty;

            UpdateDashQuickFilterButtons();
            RefreshFilteredGrid();
            SetFullStackStatus($"Đã áp dụng saved view: {_savedOperationFilter}");
        }

        private void UpdateOperationFilterToolbar()
        {
            if (_operationGridFilterToolbar == null) return;
            string filter = GetSelectedDashStatus();
            if (string.IsNullOrWhiteSpace(filter))
                filter = "Tất cả tồn kho";
            bool hasPreset = !string.IsNullOrWhiteSpace(_savedOperationFilter) || !string.IsNullOrWhiteSpace(_savedOperationSearch);
            _operationGridFilterToolbar.SetActiveFilter(filter, hasPreset);
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

        private static string FormatLocalDate(DateTime? value)
        {
            return value.HasValue ? value.Value.ToLocalTime().ToString("HH:mm dd/MM") : "-";
        }

        private static string FormatLocalDate(DateTime value)
        {
            return value == default ? "-" : value.ToLocalTime().ToString("HH:mm dd/MM");
        }

        private static string CleanDisplay(string value)
        {
            return string.IsNullOrWhiteSpace(value) || string.Equals(value, "empty", StringComparison.OrdinalIgnoreCase)
                ? "-"
                : value.Trim();
        }

        private void UpdateFilterInfo()
        {
            try
            {
                int total = tabDash_dataGridView.Rows.Count;
                string search = _dashSearchBox?.Text?.Trim() ?? "";
                bool hasDate = (_dashDateFrom?.Checked == true || _dashDateTo?.Checked == true);
                string status = GetSelectedDashStatus();

                var parts = new List<string>();
                parts.Add($"{total:N0} đơn");
                if (!string.IsNullOrWhiteSpace(status) && status != "Tất cả")
                    parts.Add($"● {status}");
                if (!string.IsNullOrWhiteSpace(search))
                    parts.Add($"🔍 \"{search}\"");
                if (hasDate)
                {
                    string from = _dashDateFrom.Checked ? _dashDateFrom.Value.ToString("dd/MM") : "...";
                    string to = _dashDateTo.Checked ? _dashDateTo.Value.ToString("dd/MM") : "...";
                    parts.Add($"📅 {from} - {to}");
                }
                _dashFilterInfo.Text = string.Join(" | ", parts);
            }
            catch { }
        }

        private void ExportDashToExcel(object sender, EventArgs e)
        {
            try
            {
                if (tabDash_dataGridView.Rows.Count == 0)
                {
                    MessageBox.Show("Không có dữ liệu để xuất.", "Xuất Excel",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                SaveFileDialog sfd = new SaveFileDialog();
                sfd.Filter = "Excel Files|*.xlsx";
                sfd.FileName = $"Dashboard_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx";
                if (sfd.ShowDialog() == DialogResult.OK)
                {
                    using (var sw = new StreamWriter(sfd.FileName, false, Encoding.UTF8))
                    {
                        var headers = new List<string>();
                        foreach (DataGridViewColumn col in tabDash_dataGridView.Columns)
                        {
                            if (col.Visible && col.Name != "STT")
                                headers.Add(col.HeaderText);
                        }
                        sw.WriteLine(string.Join("\t", headers));

                        foreach (DataGridViewRow row in tabDash_dataGridView.Rows)
                        {
                            if (row.IsNewRow) continue;
                            var vals = new List<string>();
                            foreach (DataGridViewColumn col in tabDash_dataGridView.Columns)
                            {
                                if (col.Visible && col.Name != "STT")
                                {
                                    var v = row.Cells[col.Name]?.Value;
                                    vals.Add(v?.ToString() ?? "");
                                }
                            }
                            sw.WriteLine(string.Join("\t", vals));
                        }
                    }
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(sfd.FileName) { UseShellExecute = true });
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Lỗi xuất Excel: {ex.Message}", "Lỗi",
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void SetupDetailTab()
        {
            _tabDetail = new TabPage("Chi tiết");
            _tabDetail.UseVisualStyleBackColor = true;
            uiTabControl2.TabPages.Add(_tabDetail);

            var outerLayout = new Sunny.UI.UITableLayoutPanel();
            outerLayout.Dock = DockStyle.Fill;
            outerLayout.ColumnCount = 2;
            outerLayout.RowCount = 1;
            outerLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            outerLayout.Margin = new Padding(0);
            outerLayout.TagString = null;

            // Lef t: Info grid
            var infoPanel = new Sunny.UI.UIPanel();
            infoPanel.Dock = DockStyle.Fill;
            infoPanel.Margin = new Padding(5);
            infoPanel.Padding = new Padding(10);
            infoPanel.Font = new Font("Microsoft Sans Serif", 12F);
            infoPanel.Text = null;
            infoPanel.TextAlignment = ContentAlignment.MiddleCenter;

            var infoLayout = new Sunny.UI.UITableLayoutPanel();
            infoLayout.Dock = DockStyle.Fill;
            infoLayout.ColumnCount = 2;
            infoLayout.RowCount = 14;
            infoLayout.Margin = new Padding(0);
            infoLayout.Padding = new Padding(5);
            infoLayout.TagString = null;
            for (int r = 0; r < 14; r++)
                infoLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30F));

            string[] fieldLabels = {
                "Mã vận đơn:", "Trạng thái hiện tại:", "Thao tác cuối:",
                "Người thao tac:", "Bưu cục:", "Người gửi:",
                "Địa chỉ lấy:", "Người nhận:", "Địa chỉ nhận:",
                "COD:", "PTTT:", "Trọng lượng:",
                "Đã chuyển hoàn:", "Số lần nhắc:"
            };
            string[] fieldProps = {
                "WaybillNo", "TrangThaiHienTai", "ThaoTacCuoi",
                "NguoiThaoTac", "BuuCucThaoTac", "TenNguoiGui",
                "DiaChiLayHang", "NhanVienNhanHang", "DiaChiNhanHang",
                "CODThucTe", "PTTT", "TrongLuong",
                "DauChuyenHoan", "PrintCount"
            };

            _detailLabels = new UILabel[fieldLabels.Length];
            for (int i = 0; i < fieldLabels.Length; i++)
            {
                var lbl = new UILabel();
                lbl.Text = fieldLabels[i];
                lbl.Font = new Font("Segoe UI Semibold", 11F, FontStyle.Bold);
                lbl.ForeColor = Color.FromArgb(80, 80, 80);
                lbl.Dock = DockStyle.Fill;
                lbl.TextAlign = ContentAlignment.MiddleLeft;
                lbl.Margin = new Padding(3);
                lbl.AutoSize = false;
                lbl.Height = 28;

                var val = new UILabel();
                val.Text = "-";
                val.Font = new Font("Segoe UI", 11F);
                val.ForeColor = Color.FromArgb(30, 30, 30);
                val.Dock = DockStyle.Fill;
                val.TextAlign = ContentAlignment.MiddleLeft;
                val.Margin = new Padding(3);
                val.AutoSize = false;
                val.Height = 28;

                _detailLabels[i] = val;
                infoLayout.Controls.Add(lbl, 0, i);
                infoLayout.Controls.Add(val, 1, i);
            }

            infoPanel.Controls.Add(infoLayout);
            outerLayout.Controls.Add(infoPanel, 0, 0);

            // Right SLA, Age, Actions
            var rightPanel = new Sunny.UI.UIPanel();
            rightPanel.Dock = DockStyle.Fill;
            rightPanel.Margin = new Padding(5);
            rightPanel.Padding = new Padding(10);
            rightPanel.Font = new Font("Microsoft Sans Serif", 12F);
            rightPanel.Text = null;
            rightPanel.TextAlignment = ContentAlignment.MiddleCenter;

            var rightLayout = new Sunny.UI.UITableLayoutPanel();
            rightLayout.Dock = DockStyle.Fill;
            rightLayout.ColumnCount = 1;
            rightLayout.RowCount = 3;
            rightLayout.Margin = new Padding(0);
            rightLayout.TagString = null;
            rightLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 120F));
            rightLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 120F));
            rightLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

            _detailSlaCard = new Sunny.UI.UIPanel();
            _detailSlaCard.Dock = DockStyle.Fill;
            _detailSlaCard.Margin = new Padding(3);
            _detailSlaCard.MinimumSize = new Size(1, 1);
            _detailSlaCard.FillColor = Color.FromArgb(240, 255, 240);
            _detailSlaCard.Text = null;
            _detailSlaCard.TextAlignment = ContentAlignment.MiddleCenter;

            var slaTitle = new UILabel();
            slaTitle.Text = "THỜI HIỆU SLA";
            slaTitle.Font = new Font("Segoe UI Semibold", 13F, FontStyle.Bold);
            slaTitle.ForeColor = Color.FromArgb(48, 48, 48);
            slaTitle.TextAlign = ContentAlignment.MiddleLeft;
            slaTitle.Dock = DockStyle.Top;
            slaTitle.Height = 35;
            slaTitle.Margin = new Padding(5);

            _detailSlaValue = new UILabel();
            _detailSlaValue.Text = "Đang tính...";
            _detailSlaValue.Font = new Font("Segoe UI", 14F, FontStyle.Bold);
            _detailSlaValue.TextAlign = ContentAlignment.MiddleCenter;
            _detailSlaValue.Dock = DockStyle.Fill;

            _detailSlaCard.Controls.Add(_detailSlaValue);
            _detailSlaCard.Controls.Add(slaTitle);
            rightLayout.Controls.Add(_detailSlaCard, 0, 0);

            var ageCard = new Sunny.UI.UIPanel();
            ageCard.Dock = DockStyle.Fill;
            ageCard.Margin = new Padding(3);
            ageCard.MinimumSize = new Size(1, 1);
            ageCard.FillColor = Color.FromArgb(240, 248, 255);
            ageCard.Text = null;
            ageCard.TextAlignment = ContentAlignment.MiddleCenter;

            var ageTitle = new UILabel();
            ageTitle.Text = "THỜI GIAN TỒN KHO";
            ageTitle.Font = new Font("Segoe UI Semibold", 13F, FontStyle.Bold);
            ageTitle.ForeColor = Color.FromArgb(48, 48, 48);
            ageTitle.TextAlign = ContentAlignment.MiddleLeft;
            ageTitle.Dock = DockStyle.Top;
            ageTitle.Height = 35;
            ageTitle.Margin = new Padding(5);

            _detailAgeValue = new UILabel();
            _detailAgeValue.Text = "Đang tính...";
            _detailAgeValue.Font = new Font("Segoe UI", 14F, FontStyle.Bold);
            _detailAgeValue.TextAlign = ContentAlignment.MiddleCenter;
            _detailAgeValue.Dock = DockStyle.Fill;

            ageCard.Controls.Add(_detailAgeValue);
            ageCard.Controls.Add(ageTitle);
            rightLayout.Controls.Add(ageCard, 0, 1);

            var actionPanel = new Sunny.UI.UIPanel();
            actionPanel.Dock = DockStyle.Fill;
            actionPanel.Margin = new Padding(3);
            actionPanel.MinimumSize = new Size(1, 1);
            actionPanel.Text = null;
            actionPanel.TextAlignment = ContentAlignment.MiddleCenter;

            var actionFlow = new FlowLayoutPanel();
            actionFlow.Dock = DockStyle.Fill;
            actionFlow.FlowDirection = FlowDirection.TopDown;
            actionFlow.Padding = new Padding(10);
            actionFlow.AutoScroll = true;

            var actions = new (string Text, int Symbol)[]
            {
                ("Gửi Zalo reminder", 61973),
                ("In chuyển hoàn", 61665),
                ("In lại đơn", 61641),
                ("Xem trên JMS", 61702)
            };

            foreach (var act in actions)
            {
                var btn = new Sunny.UI.UISymbolButton();
                btn.Text = act.Text;
                btn.Font = new Font("Segoe UI Semibold", 11F, FontStyle.Bold);
                btn.Size = new Size(200, 42);
                btn.Margin = new Padding(5, 4, 5, 4);
                btn.Radius = 8;
                btn.Symbol = act.Symbol;
                btn.SymbolSize = 18;
                btn.Tag = act.Text;
                btn.Click += DetailActionButton_Click;
                actionFlow.Controls.Add(btn);
            }

            actionPanel.Controls.Add(actionFlow);
            rightLayout.Controls.Add(actionPanel, 0, 2);

            rightPanel.Controls.Add(rightLayout);
            outerLayout.Controls.Add(rightPanel, 1, 0);

            _tabDetail.Controls.Add(outerLayout);
        }

        private void DetailActionButton_Click(object sender, EventArgs e)
        {
            var btn = sender as Sunny.UI.UISymbolButton;
            if (btn == null) return;
            string action = btn.Tag?.ToString() ?? "";
            if (tabDash_dataGridView.CurrentRow == null) { MessageBox.Show("Không có đơn nào được chọn.", "Thông báo"); return; }
            var waybill = tabDash_dataGridView.CurrentRow.DataBoundItem as WaybillDbModel;
            if (waybill == null) return;

            switch (action)
            {
                case "Gửi Zalo reminder":
                    MessageBox.Show($"Đã gửi reminder cho đơn {waybill.WaybillNo} (giả lập)", "Zalo");
                    break;
                case "In chuyển hoàn":
                    MessageBox.Show($"Đã gửi lệnh in CH cho đơn {waybill.WaybillNo} (giả lập)", "In ấn");
                    break;
                case "In lại đơn":
                    MessageBox.Show($"Đã gửi lệnh in lại đơn {waybill.WaybillNo} (giả lập)", "In ấn");
                    break;
                case "Xem trên JMS":
                    MessageBox.Show($"Mở JMS cho đơn {waybill.WaybillNo} (giả lập)", "JMS");
                    break;
            }
        }

        private void WireEnhancedEvents()
        {
            tabDash_dataGridView.CellDoubleClick -= TabDash_CellDoubleClick;
            tabDash_dataGridView.SelectionChanged -= TabDash_SelectionChanged;
            tabDash_dataGridView.CellDoubleClick += TabDash_CellDoubleClick;
            tabDash_dataGridView.SelectionChanged += TabDash_SelectionChanged;
            AppLogger.Info("[FullStackJourney] inventory grid double-click handler wired");
        }

        private void TabDash_CellDoubleClick(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0) return;
            var row = tabDash_dataGridView.Rows[e.RowIndex];
            var waybillNo = ResolveWaybillNoFromDashRow(row);
            if (string.IsNullOrWhiteSpace(waybillNo))
            {
                AppLogger.Warning($"[FullStackJourney] double-click ignored; row={e.RowIndex}; waybill empty");
                SetFullStackStatus("Không xác định được mã vận đơn từ dòng đang chọn.");
                return;
            }

            AppLogger.Info($"[FullStackJourney] double-click waybill={waybillNo}; row={e.RowIndex}; source=Fresh Tracking API");
            ShowWaybillJourneyWorkspace(waybillNo);
        }

        private static string ResolveWaybillNoFromDashRow(DataGridViewRow row)
        {
            if (row == null)
                return string.Empty;

            if (row.DataBoundItem is WaybillDbModel model && !string.IsNullOrWhiteSpace(model.WaybillNo))
                return model.WaybillNo.Trim();

            foreach (DataGridViewCell cell in row.Cells)
            {
                var column = cell.OwningColumn;
                if (column == null)
                    continue;

                var isWaybillColumn =
                    string.Equals(column.DataPropertyName, nameof(WaybillDbModel.WaybillNo), StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(column.Name, "Mã vận đơn", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(column.HeaderText, "Mã vận đơn", StringComparison.OrdinalIgnoreCase);

                if (!isWaybillColumn)
                    continue;

                var value = Convert.ToString(cell.Value)?.Trim();
                return string.IsNullOrWhiteSpace(value) ? string.Empty : value;
            }

            return string.Empty;
        }

        private void TabDash_SelectionChanged(object sender, EventArgs e)
        {
            UpdateSelectedOperationDetailFromGrid();
            UpdateOperationFilterToolbar();
        }

        private void ShowDetailForWaybill(WaybillDbModel model)
        {
            if (model == null) return;
            _ = UpdateOperationDetailPanelAsync(model);
            UpdateLegacyDetailLabels(model);
        }

        private void UpdateLegacyDetailLabels(WaybillDbModel model)
        {
            if (model == null) return;
            if (_detailLabels == null) return;

            var propNames = new[]
            {
                "WaybillNo", "TrangThaiHienTai", "ThaoTacCuoi",
                "NguoiThaoTac", "BuuCucThaoTac", "TenNguoiGui",
                "DiaChiLayHang", "NhanVienNhanHang", "DiaChiNhanHang",
                "CODThucTe", "PTTT", "TrongLuong",
                "DauChuyenHoan", "PrintCount"
            };

            for (int i = 0; i < propNames.Length && i < _detailLabels.Length; i++)
            {
                var prop = typeof(WaybillDbModel).GetProperty(propNames[i]);
                if (prop != null)
                {
                    var val = prop.GetValue(model)?.ToString() ?? "-";
                    _detailLabels[i].Text = string.IsNullOrWhiteSpace(val) || val == "empty" ? "-" : val;
                }
            }

            string warnLvl;
            string slaText = CalculateSlaRemaining(model.ThoiGianNhanHang, out warnLvl);
            _detailSlaValue.Text = slaText;

            if (warnLvl == "Nghiêm trọng")
            {
                _detailSlaCard.FillColor = Color.FromArgb(255, 220, 220);
                _detailSlaValue.ForeColor = Color.DarkRed;
            }
            else if (warnLvl == "Cảnh báo")
            {
                _detailSlaCard.FillColor = Color.FromArgb(255, 250, 210);
                _detailSlaValue.ForeColor = Color.FromArgb(180, 140, 0);
            }
            else
            {
                _detailSlaCard.FillColor = Color.FromArgb(220, 255, 220);
                _detailSlaValue.ForeColor = Color.DarkGreen;
            }

            string ageText = CalculateWarehouseAge(model.ThoiGianThaoTac, model.ThaoTacCuoi);
            _detailAgeValue.Text = ageText;
        }

        private void SetupAlertCheck()
        {
            _alertCheckTimer.Interval = 30000;
            _alertCheckTimer.Tick += (s, e) => CheckForAlerts();
            _alertCheckTimer.Start();
        }

        private void CheckForAlerts()
        {
            int slaLate = _cloudData.Count(x => IsSlaBreached(x.ThoiGianNhanHang));
            int lostRisk = _cloudData.Count(IsLostRisk);
            int storageLong = _cloudData.Count(x => GetWarehouseAgeDays(x.ThoiGianThaoTac) >= 3);

            int criticalTotal = slaLate + lostRisk + storageLong;

            if (criticalTotal > _lastCriticalAlertCount)
            {
                _lastCriticalAlertCount = criticalTotal;
                FlashTitleBar($"⚠ {criticalTotal} đơn cần xử lý gấp!");
            }
        }

        private void FlashTitleBar(string message)
        {
            if (this.InvokeRequired)
            {
                this.Invoke(new Action(() => FlashTitleBar(message)));
                return;
            }
            this.Text = $"⚠ {message}";

            Task.Delay(5000).ContinueWith(_ =>
            {
                if (!this.IsDisposed)
                    try { this.Invoke(new Action(() => this.Text = "Điều phối Vận hành Bưu cục Realtime")); } catch { }
});
        }

// ======================================================================================
        // THỜI HIỆU TAB - TOP-LEVEL TAB
        // ======================================================================================

        private void SetupThoiHieuTab()
        {
            // Create new top-level tab page
            _tabThoiHieu = new TabPage("Thời hiệu");
            _tabThoiHieu.UseVisualStyleBackColor = false;
            _tabThoiHieu.BackColor = Color.White;
            _tabThoiHieu.Font = new Font("Segoe UI Semibold", 12F, FontStyle.Bold);
            _tabThoiHieu.Margin = new Padding(0);

            // Insert into uiTabControl1 after tabDash (index 1)
            if (!uiTabControl1.TabPages.Contains(_tabThoiHieu))
                uiTabControl1.TabPages.Insert(1, _tabThoiHieu);

            _tabThoiHieu.Controls.Clear();
            _thoiHieuLayout = new Sunny.UI.UITableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 2,
                Margin = Padding.Empty,
                Padding = Padding.Empty
            };
            _thoiHieuLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            _thoiHieuLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38F));
            _thoiHieuLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            _tabThoiHieu.Controls.Add(_thoiHieuLayout);

            var toolbar = CreateThoiHieuKpiToolbar();
            _thoiHieuLayout.Controls.Add(toolbar, 0, 0);

            _thoiHieuKpiSheet = new ThoiHieuKpiSheetControl
            {
                Dock = DockStyle.Fill,
                Margin = Padding.Empty
            };
            _thoiHieuKpiSheet.ViewWarningChanged += (s, warning) => UpdateThoiHieuStatus(string.IsNullOrWhiteSpace(warning) ? BuildThoiHieuViewStatus() : warning);
            _thoiHieuLayout.Controls.Add(_thoiHieuKpiSheet, 0, 1);
            RefreshThoiHieuKpiSheet();

            // Remove old tabPage4 from sub-tab control
            if (uiTabControl2.TabPages.Contains(tabPage4))
                uiTabControl2.TabPages.Remove(tabPage4);
        }

        private Control CreateThoiHieuKpiToolbar()
        {
            var panel = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(245, 245, 245),
                Padding = new Padding(6, 5, 6, 4)
            };

            var flow = new FlowLayoutPanel
            {
                Dock = DockStyle.Left,
                AutoSize = true,
                WrapContents = false,
                FlowDirection = FlowDirection.LeftToRight,
                BackColor = Color.Transparent,
                Margin = Padding.Empty
            };

            _thoiHieuNormalViewButton = CreateThoiHieuToolbarButton("Cuộn xem", (s, e) => SetThoiHieuViewMode(ThoiHieuViewMode.NormalScroll));
            _thoiHieuFitWidthButton = CreateThoiHieuToolbarButton("Vừa ngang", (s, e) => SetThoiHieuViewMode(ThoiHieuViewMode.FitWidth));
            _thoiHieuFitOnePageButton = CreateThoiHieuToolbarButton("Vừa 1 trang", (s, e) => SetThoiHieuViewMode(ThoiHieuViewMode.FitOnePage));
            _thoiHieuExportImageButton = CreateThoiHieuToolbarButton("Xuất ảnh đầy đủ", async (s, e) => await ExportThoiHieuFullImageAsync());
            _thoiHieuOpenExportFolderButton = CreateThoiHieuToolbarButton("Mở thư mục", (s, e) => OpenThoiHieuExportFolder());
            _thoiHieuOpenExportFolderButton.Enabled = false;

            flow.Controls.Add(_thoiHieuNormalViewButton);
            flow.Controls.Add(_thoiHieuFitWidthButton);
            flow.Controls.Add(_thoiHieuFitOnePageButton);
            flow.Controls.Add(_thoiHieuExportImageButton);
            flow.Controls.Add(_thoiHieuOpenExportFolderButton);

            _thoiHieuStatusLabel = new Label
            {
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleRight,
                Font = new Font("Segoe UI", 9F, FontStyle.Regular),
                ForeColor = Color.FromArgb(80, 80, 80),
                AutoEllipsis = true,
                Text = "Cuộn xem - giữ kích thước chuẩn"
            };

            panel.Controls.Add(_thoiHieuStatusLabel);
            panel.Controls.Add(flow);
            return panel;
        }

        private Button CreateThoiHieuToolbarButton(string text, EventHandler onClick)
        {
            var button = new Button
            {
                Text = text,
                AutoSize = true,
                Height = 28,
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.White,
                ForeColor = Color.FromArgb(45, 45, 45),
                Font = new Font("Segoe UI", 8.5F, FontStyle.Regular),
                Margin = new Padding(0, 0, 6, 0),
                Padding = new Padding(8, 1, 8, 1),
                UseVisualStyleBackColor = false
            };
            button.FlatAppearance.BorderColor = Color.FromArgb(190, 190, 190);
            button.Click += onClick;
            return button;
        }

        private void SetThoiHieuViewMode(ThoiHieuViewMode mode)
        {
            if (_thoiHieuKpiSheet == null || _thoiHieuKpiSheet.IsDisposed) return;

            _thoiHieuKpiSheet.SetViewMode(mode);
            UpdateThoiHieuModeButtons(mode);
            UpdateThoiHieuStatus(string.IsNullOrWhiteSpace(_thoiHieuKpiSheet.FitWarning)
                ? BuildThoiHieuViewStatus()
                : _thoiHieuKpiSheet.FitWarning);
        }

        private void UpdateThoiHieuModeButtons(ThoiHieuViewMode mode)
        {
            StyleThoiHieuModeButton(_thoiHieuNormalViewButton, mode == ThoiHieuViewMode.NormalScroll);
            StyleThoiHieuModeButton(_thoiHieuFitWidthButton, mode == ThoiHieuViewMode.FitWidth);
            StyleThoiHieuModeButton(_thoiHieuFitOnePageButton, mode == ThoiHieuViewMode.FitOnePage);
        }

        private static void StyleThoiHieuModeButton(Button button, bool selected)
        {
            if (button == null) return;
            button.BackColor = selected ? Color.FromArgb(198, 224, 180) : Color.White;
            button.Font = new Font(button.Font, selected ? FontStyle.Bold : FontStyle.Regular);
        }

        private string BuildThoiHieuViewStatus()
        {
            if (_thoiHieuKpiSheet == null) return "";

            string mode = _thoiHieuKpiSheet.ViewMode switch
            {
                ThoiHieuViewMode.FitWidth => "Vừa ngang",
                ThoiHieuViewMode.FitOnePage => "Vừa 1 trang",
                _ => "Cuộn xem"
            };

            int rows = _thoiHieuKpiSheetData?.Rows?.Count ?? 0;
            return $"{mode} | {rows:N0} nhân viên | Zoom {_thoiHieuKpiSheet.ViewScale:P0}";
        }

        private void UpdateThoiHieuStatus(string text)
        {
            if (_thoiHieuStatusLabel == null || _thoiHieuStatusLabel.IsDisposed) return;
            _thoiHieuStatusLabel.Text = text ?? "";
            _thoiHieuStatusLabel.ForeColor = (text ?? "").Contains("quá lớn", StringComparison.OrdinalIgnoreCase)
                ? Color.FromArgb(192, 0, 0)
                : Color.FromArgb(80, 80, 80);
        }

        private void RefreshThoiHieuKpiSheet()
        {
            if (_isClosing) return;

            _thoiHieuKpiSheetData = ThoiHieuKpiSampleData.Create();
            if (_thoiHieuKpiSheet == null || _thoiHieuKpiSheet.IsDisposed) return;

            if (_thoiHieuKpiSheet.InvokeRequired)
            {
                try { _thoiHieuKpiSheet.BeginInvoke(new Action(RefreshThoiHieuKpiSheet)); }
                catch (ObjectDisposedException) { }
                catch (InvalidOperationException) { }
                return;
            }

            _thoiHieuKpiSheet.SetData(_thoiHieuKpiSheetData);
            UpdateThoiHieuModeButtons(_thoiHieuKpiSheet.ViewMode);
            UpdateThoiHieuStatus(string.IsNullOrWhiteSpace(_thoiHieuKpiSheet.FitWarning)
                ? BuildThoiHieuViewStatus()
                : _thoiHieuKpiSheet.FitWarning);
        }

        private async Task ExportThoiHieuFullImageAsync()
        {
            if (_isClosing) return;
            if (_thoiHieuKpiSheetData == null || _thoiHieuKpiSheetData.Rows == null || _thoiHieuKpiSheetData.Rows.Count == 0)
            {
                MessageBox.Show("Chưa có dữ liệu thời hiệu để xuất ảnh.", "Xuất ảnh thời hiệu", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            string previousStatus = _thoiHieuStatusLabel?.Text ?? "";
            SetThoiHieuExportButtonsEnabled(false);
            UpdateThoiHieuStatus("Đang xuất ảnh thời hiệu...");

            try
            {
                string outputDir = GetThoiHieuExportDirectory();
                string path = await ThoiHieuKpiImageExporter.ExportFullImageAsync(
                    _thoiHieuKpiSheetData,
                    outputDir,
                    _cts.Token,
                    scale: 1.0f);

                _lastThoiHieuExportPath = path;
                if (_thoiHieuOpenExportFolderButton != null)
                    _thoiHieuOpenExportFolderButton.Enabled = true;

                UpdateThoiHieuStatus($"Đã xuất ảnh: {path}");
                AppLogger.Info($"ThoiHieu full image exported: {path}");
                try { UIMessageTip.Show("Đã xuất ảnh thời hiệu."); } catch { }
            }
            catch (OperationCanceledException)
            {
                UpdateThoiHieuStatus("Đã hủy xuất ảnh thời hiệu.");
            }
            catch (Exception ex)
            {
                AppLogger.Error("ExportThoiHieuFullImageAsync failed", ex);
                UpdateThoiHieuStatus("Lỗi xuất ảnh thời hiệu.");
                MessageBox.Show($"Lỗi xuất ảnh thời hiệu: {ex.Message}", "Lỗi", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                SetThoiHieuExportButtonsEnabled(true);
                if (string.IsNullOrWhiteSpace(_lastThoiHieuExportPath) && _thoiHieuStatusLabel != null)
                    UpdateThoiHieuStatus(previousStatus);
            }
        }

        private static string GetThoiHieuExportDirectory()
        {
            return Path.Combine(AppPaths.UserDataDir, "FullStack", "Exports", "ThoiHieu");
        }

        private void SetThoiHieuExportButtonsEnabled(bool enabled)
        {
            if (_thoiHieuExportImageButton != null)
                _thoiHieuExportImageButton.Enabled = enabled;
            if (_thoiHieuNormalViewButton != null)
                _thoiHieuNormalViewButton.Enabled = enabled;
            if (_thoiHieuFitWidthButton != null)
                _thoiHieuFitWidthButton.Enabled = enabled;
            if (_thoiHieuFitOnePageButton != null)
                _thoiHieuFitOnePageButton.Enabled = enabled;
            if (_thoiHieuOpenExportFolderButton != null)
                _thoiHieuOpenExportFolderButton.Enabled = enabled && !string.IsNullOrWhiteSpace(_lastThoiHieuExportPath);
        }

        private void OpenThoiHieuExportFolder()
        {
            try
            {
                string folder = !string.IsNullOrWhiteSpace(_lastThoiHieuExportPath)
                    ? Path.GetDirectoryName(_lastThoiHieuExportPath)
                    : GetThoiHieuExportDirectory();

                if (string.IsNullOrWhiteSpace(folder))
                    return;

                Directory.CreateDirectory(folder);
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = folder,
                    UseShellExecute = true,
                    Verb = "open"
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show("Không thể mở thư mục xuất ảnh: " + ex.Message, "Lỗi", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void CreateThoiHieuBanner()
        {
            var banner = new UIPanel();
            banner.Dock = DockStyle.Fill;
            banner.Margin = new Padding(0, 0, 0, 2);
            banner.FillColor = Color.FromArgb(169, 223, 191); // #A9DFBF
            banner.Text = "BẢNG KÝ NHẬN THỜI HIỆU THEO MỐC THỜI GIAN 214A02";
            banner.Font = new Font("Segoe UI", 13F, FontStyle.Bold);
            banner.ForeColor = Color.White;
            banner.TextAlignment = ContentAlignment.MiddleCenter;
            banner.MinimumSize = new Size(1, 1);
            _thoiHieuLayout.Controls.Add(banner, 0, 0);
        }

        private void CreateThoiHieuKpiZone()
        {
            var statusBar = new FlowLayoutPanel();
            statusBar.Dock = DockStyle.Fill;
            statusBar.Margin = new Padding(0, 0, 0, 2);
            statusBar.FlowDirection = FlowDirection.LeftToRight;
            statusBar.WrapContents = false;
            statusBar.BackColor = Color.Transparent;

            // 5 status cards
            string[] titles = { "Tỉ lệ phát hàng", "Tỉ lệ giao thành\ncông", "Tổng đơn đến", "Kiện vấn đề", "Cargo Information" };
            string[] values = { "35.2%", "48.2%", "1254", "05", "" };
            string[] changes = { "33%", "14.2%", "10.2%", "44.2%", "" };
            bool[] upArrows = { true, false, false, true, false };
            Color[] bgColors = { Color.FromArgb(142, 68, 173), Color.FromArgb(39, 174, 96), Color.FromArgb(41, 128, 185), Color.FromArgb(22, 160, 133), Color.FromArgb(245, 245, 245) };
            Color[] textColors = { Color.White, Color.White, Color.White, Color.White, Color.FromArgb(50, 50, 50) };

            for (int i = 0; i < titles.Length; i++)
            {
                var card = new UIPanel();
                card.Size = new Size(260, 89);
                card.Margin = new Padding(3, 3, 3, 3);
                card.FillColor = bgColors[i];
                card.FillColor2 = ControlPaint.Dark(bgColors[i]);
                card.FillColorGradient = (i < 4);
                card.FillColorGradientDirection = FlowDirection.LeftToRight;
                card.Text = string.Empty;
                card.Padding = new Padding(10, 5, 10, 5);

                var titleLbl = new Label();
                titleLbl.Text = titles[i];
                titleLbl.Font = new Font("Segoe UI", 8F, FontStyle.Bold);
                titleLbl.ForeColor = Color.FromArgb(255, 255, 255, 200);
                titleLbl.AutoSize = true;
                titleLbl.Location = new Point(10, 5);

                if (i < 4)
                {
                    var valLbl = new Label();
                    valLbl.Text = values[i];
                    valLbl.Font = new Font("Segoe UI", 22F, FontStyle.Bold);
                    valLbl.ForeColor = textColors[i];
                    valLbl.AutoSize = true;
                    valLbl.Location = new Point(10, 28);

                    var changeLbl = new Label();
                    changeLbl.Text = (upArrows[i] ? "▲ " : "▼ ") + changes[i];
                    changeLbl.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
                    changeLbl.ForeColor = upArrows[i] ? Color.FromArgb(46, 204, 113) : Color.FromArgb(231, 76, 60);
                    changeLbl.AutoSize = true;
                    changeLbl.Location = new Point(10, 60);

                    card.Controls.Add(titleLbl);
                    card.Controls.Add(valLbl);
                    card.Controls.Add(changeLbl);
                }
                else
                {
                    // Cargo card: special layout
                    titleLbl.ForeColor = Color.FromArgb(80, 80, 80);
                    titleLbl.Font = new Font("Segoe UI", 9F, FontStyle.Bold);

                    var cargoText = new Label();
                    cargoText.Text = "160 Cargo Trucks with 2500 Shipping Active";
                    cargoText.Font = new Font("Segoe UI", 7F, FontStyle.Regular);
                    cargoText.ForeColor = Color.FromArgb(120, 120, 120);
                    cargoText.AutoSize = true;
                    cargoText.Location = new Point(10, 25);

                    var viewAll = new Label();
                    viewAll.Text = "View all";
                    viewAll.Font = new Font("Segoe UI", 7F, FontStyle.Underline);
                    viewAll.ForeColor = Color.FromArgb(41, 128, 185);
                    viewAll.AutoSize = true;
                    viewAll.Location = new Point(10, 42);
                    viewAll.Cursor = Cursors.Hand;

                    var leftChart = CreateMiniDonut(160, Color.FromArgb(142, 68, 173), new Point(110, 5), "Cargo");
                    var rightChart = CreateMiniDonut(2500, Color.FromArgb(80, 80, 80), new Point(155, 5), "Shipping");

                    card.Controls.Add(titleLbl);
                    card.Controls.Add(cargoText);
                    card.Controls.Add(viewAll);
                    card.Controls.Add(leftChart);
                    card.Controls.Add(rightChart);
                }

                statusBar.Controls.Add(card);
            }

            _thoiHieuLayout.Controls.Add(statusBar, 0, 1);
        }

        private Panel CreateMiniDonut(int value, Color color, Point location, string label)
        {
            var p = new Panel();
            p.Size = new Size(42, 52);
            p.Location = location;
            p.BackColor = Color.Transparent;

            var valLbl = new Label();
            valLbl.Text = value.ToString("N0");
            valLbl.Font = new Font("Segoe UI", 10F, FontStyle.Bold);
            valLbl.ForeColor = color;
            valLbl.AutoSize = true;
            valLbl.Location = new Point(0, 8);
            valLbl.TextAlign = ContentAlignment.MiddleCenter;
            valLbl.Width = 42;

            var subLbl = new Label();
            subLbl.Text = label;
            subLbl.Font = new Font("Segoe UI", 7F, FontStyle.Regular);
            subLbl.ForeColor = Color.FromArgb(120, 120, 120);
            subLbl.AutoSize = true;
            subLbl.Location = new Point(0, 30);
            subLbl.TextAlign = ContentAlignment.MiddleCenter;
            subLbl.Width = 42;

            // Draw donut border (simulated circle)
            var donut = new Panel();
            donut.Size = new Size(38, 38);
            donut.Location = new Point(2, 0);
            donut.BackColor = Color.Transparent;
            donut.Paint += (s, pe) =>
            {
                using (var pen = new Pen(color, 4))
                {
                    pe.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                    pe.Graphics.DrawEllipse(pen, new Rectangle(3, 3, 30, 30));
                }
            };

            p.Controls.Add(donut); // behind
            p.Controls.Add(valLbl); // on top
            p.Controls.Add(subLbl);
            return p;
        }

        private void CreateThoiHieuFilterRow()
        {
            var filterPanel = new UIPanel();
            filterPanel.Dock = DockStyle.Fill;
            filterPanel.Margin = new Padding(0, 0, 0, 2);
            filterPanel.FillColor = Color.FromArgb(245, 245, 245);
            filterPanel.Text = string.Empty;

            var flow = new FlowLayoutPanel();
            flow.Dock = DockStyle.Left;
            flow.FlowDirection = FlowDirection.LeftToRight;
            flow.Padding = new Padding(5, 3, 5, 3);
            flow.AutoSize = true;

            var filterLabel = new Label();
            filterLabel.Text = "Tìm:";
            filterLabel.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
            filterLabel.ForeColor = Color.FromArgb(80, 80, 80);
            filterLabel.TextAlign = ContentAlignment.MiddleLeft;
            filterLabel.AutoSize = true;

            _thoiHieuFilterText = new TextBox();
            _thoiHieuFilterText.Width = 150;
            _thoiHieuFilterText.Font = new Font("Segoe UI", 9F);
            _thoiHieuFilterText.TextChanged += (s, e) => FilterThoiHieuGrid();

            var spacer = new Label();
            spacer.Text = "    Shipper:";
            spacer.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
            spacer.ForeColor = Color.FromArgb(80, 80, 80);
            spacer.TextAlign = ContentAlignment.MiddleLeft;
            spacer.AutoSize = true;

            _thoiHieuShipperFilter = new UIComboBox();
            _thoiHieuShipperFilter.Width = 200;
            _thoiHieuShipperFilter.Font = new Font("Segoe UI", 9F);
            _thoiHieuShipperFilter.Margin = new Padding(0, 0, 5, 0);
            _thoiHieuShipperFilter.SelectedIndexChanged += (s, e) => FilterThoiHieuGrid();

            flow.Controls.Add(filterLabel);
            flow.Controls.Add(_thoiHieuFilterText);
            flow.Controls.Add(spacer);
            flow.Controls.Add(_thoiHieuShipperFilter);
            filterPanel.Controls.Add(flow);
            _thoiHieuLayout.Controls.Add(filterPanel, 0, 2);
        }

        private void CreateThoiHieuGrid()
        {
            _thoiHieuGrid = new Sunny.UI.UIDataGridView();
            _thoiHieuGrid.Dock = DockStyle.Fill;
            _thoiHieuGrid.Margin = new Padding(0, 0, 0, 2);
            _thoiHieuGrid.AutoGenerateColumns = false;
            _thoiHieuGrid.ReadOnly = true;
            _thoiHieuGrid.AllowUserToResizeColumns = true;
            _thoiHieuGrid.RowHeadersVisible = false;
            _thoiHieuGrid.SelectionMode = DataGridViewSelectionMode.CellSelect;
            _thoiHieuGrid.MultiSelect = false;
            _thoiHieuGrid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.DisplayedCells;
            _thoiHieuGrid.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.None;
            _thoiHieuGrid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
            _thoiHieuGrid.ColumnHeadersHeight = 50;
            _thoiHieuGrid.ColumnHeadersDefaultCellStyle.WrapMode = DataGridViewTriState.True;
            _thoiHieuGrid.ColumnHeadersDefaultCellStyle.Font = new Font("Segoe UI", 7.5F, FontStyle.Bold);
            _thoiHieuGrid.DefaultCellStyle.Font = new Font("Segoe UI", 7.5F, FontStyle.Regular);
            _thoiHieuGrid.BackgroundColor = Color.White;
            _thoiHieuGrid.StripeOddColor = Color.White;
            _thoiHieuGrid.StripeEvenColor = Color.White;
            _thoiHieuGrid.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(245, 245, 245);
            _thoiHieuGrid.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.Single;
            _thoiHieuGrid.EnableHeadersVisualStyles = false;
            _thoiHieuGrid.DataError += FullStackGrid_DataError;

            var cols = new List<DataGridViewColumn>();

            cols.Add(new DataGridViewTextBoxColumn { Name = "STT", HeaderText = "STT", DataPropertyName = "STT", Width = 38, Frozen = true, SortMode = DataGridViewColumnSortMode.Programmatic });
            cols.Add(new DataGridViewTextBoxColumn { Name = "SPV", HeaderText = "Người phụ trách\n( SPV )", DataPropertyName = "SPV", Width = 110, Frozen = true, SortMode = DataGridViewColumnSortMode.Programmatic });
            cols.Add(new DataGridViewTextBoxColumn { Name = "QuetMa", HeaderText = "Quét mã\nở Bưu cục", DataPropertyName = "QuetMa", Width = 80, Frozen = true, SortMode = DataGridViewColumnSortMode.Programmatic });
            cols.Add(new DataGridViewTextBoxColumn { Name = "MaNVPhat", HeaderText = "Mã nhân viên\nphát", DataPropertyName = "MaNVPhat", Width = 90, Frozen = true, SortMode = DataGridViewColumnSortMode.Programmatic });
            cols.Add(new DataGridViewTextBoxColumn { Name = "TenNVPhat", HeaderText = "Nhân viên phát", DataPropertyName = "TenNVPhat", Width = 130, Frozen = true, SortMode = DataGridViewColumnSortMode.Programmatic });
            cols.Add(new DataGridViewTextBoxColumn { Name = "DonPhat", HeaderText = "Số đơn phát", DataPropertyName = "DonPhat", Width = 80, SortMode = DataGridViewColumnSortMode.Programmatic });
            cols.Add(new DataGridViewTextBoxColumn { Name = "DonCanKy91", HeaderText = "Số đơn cần kí\nnhận thời hiệu 91%", DataPropertyName = "DonCanKy91", Width = 110, SortMode = DataGridViewColumnSortMode.Programmatic });
            cols.Add(new DataGridViewTextBoxColumn { Name = "KPI90", HeaderText = "Số đơn cần kí\nBaseline 90%", DataPropertyName = "KPI90", Width = 110, SortMode = DataGridViewColumnSortMode.Programmatic });
            cols.Add(new DataGridViewTextBoxColumn { Name = "SoVDKyNhan", HeaderText = "Số vận đơn\nký nhận", DataPropertyName = "SoVDKyNhan", Width = 80, SortMode = DataGridViewColumnSortMode.Programmatic });
            cols.Add(new DataGridViewTextBoxColumn { Name = "TiLeKyNhan", HeaderText = "Tỉ lệ ký nhận", DataPropertyName = "TiLeKyNhan", Width = 90, SortMode = DataGridViewColumnSortMode.Programmatic });

            for (int h = 8; h <= 24; h++)
                cols.Add(new DataGridViewTextBoxColumn { Name = $"h_{h}", HeaderText = $"{h}h", DataPropertyName = $"H{h}", Width = 32, SortMode = DataGridViewColumnSortMode.NotSortable, Resizable = DataGridViewTriState.False });

            cols.Add(new DataGridViewTextBoxColumn { Name = "ConThieu91", HeaderText = "Còn thiếu\nmốc 91%", DataPropertyName = "ConThieu91", Width = 80, SortMode = DataGridViewColumnSortMode.Programmatic });
            cols.Add(new DataGridViewTextBoxColumn { Name = "ConThieu90", HeaderText = "Còn thiếu\nmốc Baseline 90%", DataPropertyName = "ConThieu90", Width = 80, SortMode = DataGridViewColumnSortMode.Programmatic });

            _thoiHieuGrid.Columns.AddRange(cols.ToArray());

            // Header styling
            foreach (DataGridViewColumn c in _thoiHieuGrid.Columns)
            {
                c.HeaderCell.Style.Alignment = DataGridViewContentAlignment.MiddleCenter;
                c.HeaderCell.Style.Font = new Font("Segoe UI", 8F, FontStyle.Bold);
                c.HeaderCell.Style.ForeColor = Color.FromArgb(48, 48, 48);

                if (c.Name == "DonCanKy91" || c.Name == "KPI90" || c.Name == "ConThieu91" || c.Name == "ConThieu90")
                {
                    c.HeaderCell.Style.BackColor = Color.FromArgb(255, 255, 180);
                }
                else if (c.Name == "DonPhat")
                {
                    c.HeaderCell.Style.BackColor = Color.FromArgb(169, 223, 191); // green
                }
                else if (c.Name == "TiLeKyNhan")
                {
                    c.HeaderCell.Style.BackColor = Color.FromArgb(39, 174, 96); // #27AE60 dark green
                    c.HeaderCell.Style.ForeColor = Color.White;
                }
                else if (c.Name.StartsWith("h_"))
                {
                    c.HeaderCell.Style.BackColor = Color.FromArgb(231, 76, 60); // #E74C3C red
                    c.HeaderCell.Style.ForeColor = Color.White;
                }
                else
                {
                    c.HeaderCell.Style.BackColor = Color.FromArgb(240, 240, 240);
                }
            }

            // Events
            _thoiHieuGrid.CellFormatting += thoiHieuGrid_CellFormatting;
            _thoiHieuGrid.CellPainting += thoiHieuGrid_CellPainting; // Add CellPainting for DataBar custom drawing
            _thoiHieuGrid.ColumnHeaderMouseClick += thoiHieuGrid_ColumnHeaderMouseClick;

            _thoiHieuLayout.Controls.Add(_thoiHieuGrid, 0, 3);
            AppLogger.Info("BuildThoiHieuPanel created grid");
        }

        private void thoiHieuGrid_CellPainting(object sender, DataGridViewCellPaintingEventArgs e)
        {
            if (e.RowIndex < 0 || e.ColumnIndex < 0) return;
            var dgv = sender as DataGridView;
            if (dgv == null) return;

            string colName = dgv.Columns[e.ColumnIndex]?.Name ?? "";
            var th = dgv.Rows[e.RowIndex].DataBoundItem as ThoiHieuRow;
            if (th == null) return;
            bool isTotalRow = (th.TenNVPhat == "Tổng");

            // Merge SPV cells: only paint text on first row of group
            if (colName == "SPV" && !isTotalRow)
            {
                string currentVal = e.Value?.ToString() ?? "";
                string prevVal = (e.RowIndex > 0) ? (dgv.Rows[e.RowIndex - 1].Cells[colName].Value?.ToString() ?? "") : "";

                e.PaintBackground(e.CellBounds, true);
                e.Paint(e.CellBounds, DataGridViewPaintParts.All & ~DataGridViewPaintParts.ContentForeground & ~DataGridViewPaintParts.ContentBackground);

                bool sameAsPrev = (currentVal == prevVal && !string.IsNullOrEmpty(currentVal));
                if (!sameAsPrev)
                {
                    using (var brush = new SolidBrush(e.CellStyle.ForeColor))
                    {
                        e.Graphics.DrawString(currentVal, e.CellStyle.Font ?? dgv.Font, brush,
                            new RectangleF(e.CellBounds.X + 4, e.CellBounds.Y, e.CellBounds.Width - 8, e.CellBounds.Height),
                            new StringFormat { Alignment = StringAlignment.Near, LineAlignment = StringAlignment.Center });
                    }
                }

                // Draw bottom border only on last row of group
                string nextVal = (e.RowIndex < dgv.RowCount - 1) ? (dgv.Rows[e.RowIndex + 1].Cells[colName].Value?.ToString() ?? "") : "";
                bool isLastOfGroup = (e.RowIndex == dgv.RowCount - 1) || (currentVal != nextVal);
                using (var p = new Pen(dgv.GridColor))
                {
                    e.Graphics.DrawLine(p, e.CellBounds.Left, e.CellBounds.Bottom - 1, e.CellBounds.Right - 1, e.CellBounds.Bottom - 1);
                    e.Graphics.DrawLine(p, e.CellBounds.Right - 1, e.CellBounds.Top, e.CellBounds.Right - 1, e.CellBounds.Bottom - 1);
                }

                e.Handled = true;
                return;
            }

            // DataBar for TiLeKyNhan
            if (colName == "TiLeKyNhan")
            {
                double pct = th.TiLeKyNhan;
                double target = GetThoiHieuTargetPct();
                Color barColor = (pct >= target) ? Color.FromArgb(198, 239, 206) : Color.FromArgb(255, 199, 206);
                Color textColor = (pct >= target) ? Color.FromArgb(0, 97, 0) : Color.FromArgb(156, 0, 6);

                if (isTotalRow)
                    barColor = (pct >= target) ? Color.FromArgb(160, 220, 170) : Color.FromArgb(245, 160, 170);

                e.Paint(e.CellBounds, DataGridViewPaintParts.All & ~DataGridViewPaintParts.ContentBackground & ~DataGridViewPaintParts.ContentForeground);

                int barWidth = (int)Math.Round((e.CellBounds.Width - 6) * Math.Clamp(pct, 0.0, 1.0));
                if (barWidth > 0)
                {
                    using (var brush = new SolidBrush(barColor))
                    {
                        var barRect = new Rectangle(e.CellBounds.X + 3, e.CellBounds.Y + 3, barWidth, e.CellBounds.Height - 7);
                        e.Graphics.FillRectangle(brush, barRect);
                    }
                }

                string displayTxt = pct.ToString("P1");
                TextRenderer.DrawText(e.Graphics, displayTxt, e.CellStyle.Font ?? dgv.Font, e.CellBounds, textColor,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
                e.Handled = true;
                return;
            }

            // Also handle the QuetMa/MaNVPhat columns for total row styling (red background)
            if (isTotalRow && (colName == "QuetMa" || colName == "MaNVPhat"))
            {
                e.PaintBackground(e.CellBounds, true);
                e.Paint(e.CellBounds, DataGridViewPaintParts.Border);
                e.CellStyle.BackColor = Color.FromArgb(235, 22, 22);
                e.CellStyle.ForeColor = Color.White;
                e.CellStyle.SelectionBackColor = Color.FromArgb(235, 22, 22);
                e.CellStyle.SelectionForeColor = Color.White;
                return;
            }
        }

        private double GetThoiHieuTargetPct()
        {
            if (_thoiHieuGridData == null || _thoiHieuGridData.Count == 0) return 0.9;
            
            int maxHour = 8;
            foreach (var r in _thoiHieuGridData)
            {
                if (r.H24 > 0) maxHour = Math.Max(maxHour, 24);
                else if (r.H23 > 0) maxHour = Math.Max(maxHour, 23);
                else if (r.H22 > 0) maxHour = Math.Max(maxHour, 22);
                else if (r.H21 > 0) maxHour = Math.Max(maxHour, 21);
                else if (r.H20 > 0) maxHour = Math.Max(maxHour, 20);
                else if (r.H19 > 0) maxHour = Math.Max(maxHour, 19);
                else if (r.H18 > 0) maxHour = Math.Max(maxHour, 18);
                else if (r.H17 > 0) maxHour = Math.Max(maxHour, 17);
                else if (r.H16 > 0) maxHour = Math.Max(maxHour, 16);
                else if (r.H15 > 0) maxHour = Math.Max(maxHour, 15);
                else if (r.H14 > 0) maxHour = Math.Max(maxHour, 14);
                else if (r.H13 > 0) maxHour = Math.Max(maxHour, 13);
                else if (r.H12 > 0) maxHour = Math.Max(maxHour, 12);
                else if (r.H11 > 0) maxHour = Math.Max(maxHour, 11);
                else if (r.H10 > 0) maxHour = Math.Max(maxHour, 10);
                else if (r.H9 > 0) maxHour = Math.Max(maxHour, 9);
                else if (r.H8 > 0) maxHour = Math.Max(maxHour, 8);
            }

            if (maxHour <= 8) return 0.02;
            if (maxHour == 9) return 0.05;
            if (maxHour == 10) return 0.15;
            if (maxHour == 11) return 0.30;
            if (maxHour == 12) return 0.45;
            if (maxHour == 13) return 0.55;
            if (maxHour == 14) return 0.65;
            if (maxHour == 15) return 0.75;
            if (maxHour == 16) return 0.80;
            if (maxHour == 17) return 0.85;
            if (maxHour == 18) return 0.90;
            return 0.91;
        }

        private void thoiHieuGrid_CellFormatting(object sender, DataGridViewCellFormattingEventArgs e)
        {
            if (e.RowIndex < 0 || e.RowIndex >= _thoiHieuGrid.Rows.Count) return;
            var row = _thoiHieuGrid.Rows[e.RowIndex];
            var th = row.DataBoundItem as ThoiHieuRow;
            if (th == null) return;

            string colName = _thoiHieuGrid.Columns[e.ColumnIndex]?.Name ?? "";
            bool isTotalRow = (th.TenNVPhat == "Tổng");

            // STT
            if (colName == "STT")
            {
                e.Value = isTotalRow ? "" : (e.RowIndex + 1).ToString();
                e.CellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
                e.FormattingApplied = true;
                return;
            }

            // Numeric formatting with null check
            if (colName == "DonPhat" || colName == "DonCanKy91" || colName == "KPI90" || colName == "SoVDKyNhan" || colName == "ConThieu91" || colName == "ConThieu90")
            {
                if (e.Value != null && int.TryParse(e.Value.ToString(), out int val))
                {
                    e.Value = val.ToString("N0");
                    e.CellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
                    e.FormattingApplied = true;
                }
                // Continue to next style blocks for color
            }

            // Yellow columns
            if (colName == "DonCanKy91" || colName == "KPI90" || colName == "ConThieu91" || colName == "ConThieu90")
            {
                e.CellStyle.BackColor = Color.FromArgb(255, 255, 180);
                e.CellStyle.SelectionBackColor = Color.FromArgb(255, 255, 140);
                if (isTotalRow)
                {
                    e.CellStyle.Font = new Font(_thoiHieuGrid.Font, FontStyle.Bold);
                    e.CellStyle.ForeColor = Color.Black;
                }
            }

            // Hourly columns
            if (colName.StartsWith("h_"))
            {
                int hourIdx = int.Parse(colName.Substring(2));
                string propName = $"H{hourIdx}";
                var prop = typeof(ThoiHieuRow).GetProperty(propName);
                if (prop != null)
                {
                    int val = (int)(prop.GetValue(th) ?? 0);
                    e.CellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
                    e.CellStyle.Font = new Font("Segoe UI", 8F, isTotalRow ? FontStyle.Bold : FontStyle.Regular);
                    if (val > 0)
                    {
                        e.Value = val.ToString("N0");
                        e.CellStyle.BackColor = Color.FromArgb(180, 210, 255);
                        e.CellStyle.ForeColor = Color.FromArgb(0, 50, 150);
                    }
                    else
                    {
                        e.Value = "-";
                        e.CellStyle.BackColor = Color.FromArgb(231, 76, 60); // #E74C3C
                        e.CellStyle.ForeColor = Color.White;
                    }
                    e.CellStyle.SelectionBackColor = e.CellStyle.BackColor;
                    e.CellStyle.SelectionForeColor = e.CellStyle.ForeColor;
                    e.FormattingApplied = true;
                }
                return;
            }

            // General styling for the Total row
            if (isTotalRow)
            {
                if (colName == "SPV" || colName == "TenNVPhat" || colName == "QuetMa" || colName == "MaNVPhat")
                {
                    if (colName != "SPV" && colName != "TenNVPhat") e.Value = "";
                    e.CellStyle.Font = new Font(_thoiHieuGrid.Font, FontStyle.Bold);
                    e.CellStyle.BackColor = Color.FromArgb(192, 57, 43); // #C0392B
                    e.CellStyle.ForeColor = Color.White;
                    e.CellStyle.SelectionBackColor = Color.FromArgb(160, 40, 30);
                    e.CellStyle.SelectionForeColor = Color.White;
                    e.FormattingApplied = true;
                }
            }

            if (!isTotalRow)
                NormalizeGridEmptyCell(e);
        }

        private void thoiHieuGrid_ColumnHeaderMouseClick(object sender, DataGridViewCellMouseEventArgs e)
        {
            var dgv = _thoiHieuGrid;
            if (dgv == null || e.RowIndex != -1 || e.ColumnIndex < 0) return;
            var clicked = dgv.Columns[e.ColumnIndex];
            if (clicked == null || string.IsNullOrEmpty(clicked.DataPropertyName)) return;
            if (clicked.DataPropertyName == "STT") return;

            var direction = clicked.HeaderCell.SortGlyphDirection == SortOrder.Ascending ? SortOrder.Descending : SortOrder.Ascending;
            var propInfo = typeof(ThoiHieuRow).GetProperty(clicked.DataPropertyName);
            if (propInfo != null)
            {
                // Sort master data (excluding "Tổng" row which is not in _thoiHieuGridData)
                if (direction == SortOrder.Ascending)
                    _thoiHieuGridData = _thoiHieuGridData.OrderBy(x => propInfo.GetValue(x, null) ?? "").ToList();
                else
                    _thoiHieuGridData = _thoiHieuGridData.OrderByDescending(x => propInfo.GetValue(x, null) ?? "").ToList();

                int seq = 1;
                foreach (var r in _thoiHieuGridData) r.STT = seq++;

UpdateThoiHieuGridData();
            }

            clicked.HeaderCell.SortGlyphDirection = direction;
            foreach (DataGridViewColumn col in dgv.Columns)
                if (col != clicked) col.HeaderCell.SortGlyphDirection = SortOrder.None;
        }

        private void CreateThoiHieuLegend()
        {
            var legend = new FlowLayoutPanel();
            legend.Dock = DockStyle.Fill;
            legend.Margin = new Padding(0, 2, 0, 2);
            legend.FlowDirection = FlowDirection.LeftToRight;
            legend.WrapContents = false;
            legend.BackColor = Color.FromArgb(240, 240, 240);
            legend.Padding = new Padding(5, 2, 5, 2);

            var labelTitle = new Label { Text = "Quy ước màu tỷ lệ:", Font = new Font("Segoe UI", 8F, FontStyle.Bold), ForeColor = Color.FromArgb(60, 60, 60), AutoSize = true, Margin = new Padding(1, 3, 6, 3) };
            legend.Controls.Add(labelTitle);

            AddLegendItem(legend, Color.FromArgb(39, 174, 96), ">= 90% trở lên");
            AddLegendItem(legend, Color.FromArgb(255, 255, 255), "80% - <90%", Color.FromArgb(180, 180, 180));
            AddLegendItem(legend, Color.FromArgb(231, 76, 60), "< 80%");
        }

        private void AddLegendItem(FlowLayoutPanel parent, Color color, string text)
        {
            AddLegendItem(parent, color, text, Color.FromArgb(200, 200, 200));
        }

        private void AddLegendItem(FlowLayoutPanel parent, Color color, string text, Color borderColor)
        {
            var box = new Panel { Size = new Size(16, 16), BackColor = color, Margin = new Padding(2, 3, 1, 3) };
            if (color == Color.White || color.A < 255)
            {
                box.BorderStyle = BorderStyle.FixedSingle;
            }
            var lbl = new Label { Text = text, AutoSize = true, Font = new Font("Segoe UI", 8F), ForeColor = Color.FromArgb(80, 80, 80), Margin = new Padding(1, 3, 10, 3) };
            parent.Controls.Add(box);
            parent.Controls.Add(lbl);
        }

        private void CreateThoiHieuFooter()
        {
            var footer = new UIPanel();
            footer.Dock = DockStyle.Fill;
            footer.Margin = new Padding(0, 0, 0, 0);
            footer.FillColor = Color.Red;
            footer.Text = string.Empty;

            _thoiHieuFooterLabel = new Label();
            _thoiHieuFooterLabel.Dock = DockStyle.Fill;
            _thoiHieuFooterLabel.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
            _thoiHieuFooterLabel.ForeColor = Color.White;
            _thoiHieuFooterLabel.TextAlign = ContentAlignment.MiddleCenter;
            _thoiHieuFooterLabel.Text = "Đang tải...";

            footer.Controls.Add(_thoiHieuFooterLabel);
            _thoiHieuLayout.Controls.Add(footer, 0, 5);
        }

        private void SetupThoiHieuTimer()
        {
            _thoiHieuTimer = new System.Windows.Forms.Timer();
            _thoiHieuTimer.Interval = 60000;
            _thoiHieuTimer.Tick += (s, e) => LoadThoiHieuData();
            _thoiHieuTimer.Start();
        }

        private void LoadThoiHieuData()
        {
            try
            {
                var rawData = _cloudData;
                string selectedShipper = _thoiHieuShipperFilter?.SelectedItem?.ToString()?.Trim() ?? "Tất cả";
                if (selectedShipper != "Tất cả" && !string.IsNullOrEmpty(selectedShipper))
                    rawData = rawData.Where(w => string.Equals(w.NguoiThaoTac?.Trim(), selectedShipper, StringComparison.OrdinalIgnoreCase)).ToList();

                var staffGroups = rawData
                    .Where(w => !string.IsNullOrWhiteSpace(w.NguoiThaoTac))
                    .GroupBy(w => w.NguoiThaoTac.Trim(), StringComparer.OrdinalIgnoreCase)
                    .ToList();

                var rows = new List<ThoiHieuRow>();
                int seq = 1;

                foreach (var group in staffGroups)
                {
                    var list = group.ToList();
                    string quetMa = list.FirstOrDefault(w => !string.IsNullOrEmpty(w.BuuCucThaoTac))?.BuuCucThaoTac ?? "214A02";
                    int donPhat = list.Count;
                    int signed = list.Count(w => w.ThaoTacCuoi != null && w.ThaoTacCuoi.Contains("Ký nhận"));
                    int donCanKy91 = (int)Math.Ceiling(donPhat * 0.91);
                    int kpi90target = (int)Math.Ceiling(donPhat * 0.9);
                    double tiLe = donPhat > 0 ? (double)signed / donPhat : 0;
                    int conThieu91 = Math.Max(0, donCanKy91 - signed);
                    int conThieu90 = Math.Max(0, kpi90target - signed);

                    var row = new ThoiHieuRow
                    {
                        STT = seq++,
                        SPV = "Vũ Đức Toàn", // mapped based on J&T 214A02 standard
                        QuetMa = quetMa,
                        MaNVPhat = "", // empty as shown in images
                        TenNVPhat = group.Key,
                        DonPhat = donPhat,
                        DonCanKy91 = donCanKy91,
                        KPI90 = kpi90target,
                        SoVDKyNhan = signed,
                        TiLeKyNhan = tiLe,
                        ConThieu91 = conThieu91,
                        ConThieu90 = conThieu90
                    };

                    // Count signed per hour (int)
                    foreach (var w in list)
                    {
                        if (string.IsNullOrWhiteSpace(w.ThoiGianThaoTac) || w.ThoiGianThaoTac == "empty") continue;
                        if (!DateTime.TryParse(w.ThoiGianThaoTac, out DateTime t)) continue;
                        bool isSigned = w.ThaoTacCuoi != null && w.ThaoTacCuoi.Contains("Ký nhận");
                        if (!isSigned) continue;

                        int h = t.Hour;
                        if (h == 8) row.H8++;
                        else if (h == 9) row.H9++;
                        else if (h == 10) row.H10++;
                        else if (h == 11) row.H11++;
                        else if (h == 12) row.H12++;
                        else if (h == 13) row.H13++;
                        else if (h == 14) row.H14++;
                        else if (h == 15) row.H15++;
                        else if (h == 16) row.H16++;
                        else if (h == 17) row.H17++;
                        else if (h == 18) row.H18++;
                        else if (h == 19) row.H19++;
                        else if (h == 20) row.H20++;
                        else if (h == 21) row.H21++;
                        else if (h == 22) row.H22++;
                        else if (h == 23) row.H23++;
                        else if (h == 24) row.H24++;
                    }

                    rows.Add(row);
                }

                var computed = rows.OrderBy(r => r.SPV).ThenByDescending(r => r.DonPhat).ToList();
                SetThoiHieuData(computed);
            }
            catch (Exception ex)
            {
                AppLogger.Error("LoadThoiHieuData failed", ex);
            }
        }

        /// <summary>
        /// Apply freshly-computed ThoiHieu rows to the grid. If the UI isn't
        /// ready yet (data arrived before the grid was built), cache the rows
        /// and replay them once <see cref="FullStackOperation_Load"/> finishes.
        /// </summary>
        private void SetThoiHieuData(List<ThoiHieuRow> rows)
        {
            if (_isClosing) return;

            if (!_uiReady || _thoiHieuGrid == null || _thoiHieuGrid.IsDisposed)
            {
                _pendingThoiHieuRows = rows ?? new List<ThoiHieuRow>();
                AppLogger.Info($"ThoiHieu data cached before UI ready (count={_pendingThoiHieuRows.Count}).");
                return;
            }

            _thoiHieuGridData = rows ?? new List<ThoiHieuRow>();
            PopulateThoiHieuShipperFilter();
            UpdateThoiHieuGridData();
            UpdateThoiHieuFooter();
        }

        private void PopulateThoiHieuShipperFilter()
        {
            if (_thoiHieuShipperFilter == null) return;
            string current = _thoiHieuShipperFilter.SelectedItem?.ToString();
            _thoiHieuShipperFilter.Items.Clear();
            _thoiHieuShipperFilter.Items.Add("Tất cả");

            var shippers = _thoiHieuGridData
                .Select(r => r.TenNVPhat)
                .Where(s => !string.IsNullOrEmpty(s))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(s => s)
                .ToList();

            foreach (var s in shippers)
                _thoiHieuShipperFilter.Items.Add(s);

            if (!string.IsNullOrEmpty(current) && _thoiHieuShipperFilter.Items.Contains(current))
                _thoiHieuShipperFilter.SelectedItem = current;
            else
                _thoiHieuShipperFilter.SelectedIndex = 0;
        }

        private void FilterThoiHieuGrid()
        {
            UpdateThoiHieuGridData();
        }

        private void UpdateThoiHieuGridData()
        {
            // Lifecycle guards — never touch the grid when it isn't ready.
            if (_isClosing) return;

            if (!_uiReady)
            {
                AppLogger.Info("UpdateThoiHieuGridData skipped because UI not ready");
                return;
            }

            if (_thoiHieuGrid == null || _thoiHieuGrid.IsDisposed)
            {
                AppLogger.Warning("UpdateThoiHieuGridData skipped because grid null/disposed");
                return;
            }

            // Marshal to the UI thread (background ticks / realtime can call this).
            if (_thoiHieuGrid.InvokeRequired)
            {
                try { _thoiHieuGrid.BeginInvoke(new Action(UpdateThoiHieuGridData)); }
                catch (ObjectDisposedException) { /* grid went away mid-call */ }
                catch (InvalidOperationException) { /* handle not created/destroyed */ }
                return;
            }

            var filtered = _thoiHieuGridData;
            string search = _thoiHieuFilterText?.Text?.Trim()?.ToLower() ?? "";
            if (!string.IsNullOrEmpty(search))
            {
                filtered = filtered.Where(r =>
                    (r.TenNVPhat?.ToLower().Contains(search) == true) ||
                    (r.SPV?.ToLower().Contains(search) == true)
                ).ToList();
            }

            // Re-apply shipper filter on already shipper-filtered data
            string selectedShipper = _thoiHieuShipperFilter?.SelectedItem?.ToString()?.Trim() ?? "Tất cả";
            if (selectedShipper != "Tất cả" && !string.IsNullOrEmpty(selectedShipper))
                filtered = filtered.Where(r => string.Equals(r.TenNVPhat, selectedShipper, StringComparison.OrdinalIgnoreCase)).ToList();

            // Calculatedynamic total row
            if (filtered.Count > 0)
            {
                var totalRow = new ThoiHieuRow
                {
                    STT = 0,
                    SPV = "",
                    QuetMa = "",
                    MaNVPhat = "",
                    TenNVPhat = "Tổng", // triggers special styling in CellFormatting
                    DonPhat = filtered.Sum(r => r.DonPhat),
                    DonCanKy91 = filtered.Sum(r => r.DonCanKy91),
                    KPI90 = filtered.Sum(r => r.KPI90),
                    SoVDKyNhan = filtered.Sum(r => r.SoVDKyNhan),
                    ConThieu91 = filtered.Sum(r => r.ConThieu91),
                    ConThieu90 = filtered.Sum(r => r.ConThieu90),
                    H8 = filtered.Sum(r => r.H8),
                    H9 = filtered.Sum(r => r.H9),
                    H10 = filtered.Sum(r => r.H10),
                    H11 = filtered.Sum(r => r.H11),
                    H12 = filtered.Sum(r => r.H12),
                    H13 = filtered.Sum(r => r.H13),
                    H14 = filtered.Sum(r => r.H14),
                    H15 = filtered.Sum(r => r.H15),
                    H16 = filtered.Sum(r => r.H16),
                    H17 = filtered.Sum(r => r.H17),
                    H18 = filtered.Sum(r => r.H18),
                    H19 = filtered.Sum(r => r.H19),
                    H20 = filtered.Sum(r => r.H20),
                    H21 = filtered.Sum(r => r.H21),
                    H22 = filtered.Sum(r => r.H22),
                    H23 = filtered.Sum(r => r.H23),
                    H24 = filtered.Sum(r => r.H24)
                };
                totalRow.TiLeKyNhan = totalRow.DonPhat > 0 ? (double)totalRow.SoVDKyNhan / totalRow.DonPhat : 0;

                var displayList = new List<ThoiHieuRow>(filtered);
                displayList.Add(totalRow);

                _thoiHieuGrid.DataSource = null;
                _thoiHieuGrid.DataSource = displayList;
            }
            else
            {
                _thoiHieuGrid.DataSource = null;
                _thoiHieuGrid.DataSource = filtered;
            }

            _thoiHieuGrid.Refresh();
        }

        private void UpdateThoiHieuFooter()
        {
            if (_thoiHieuFooterLabel == null) return;
            int totalDon = _thoiHieuGridData.Sum(r => r.DonPhat);
            int totalKy = _thoiHieuGridData.Sum(r => r.SoVDKyNhan);
            int total91 = _thoiHieuGridData.Sum(r => r.DonCanKy91);
            int totalKpi90 = _thoiHieuGridData.Sum(r => r.KPI90);
            double overall = totalDon > 0 ? totalKy * 100.0 / totalDon : 0;

            _thoiHieuFooterLabel.Text = $"Tổng đơn: {totalDon:N0} | Cần ký 91%: {total91:N0} | KPI 90%: {totalKpi90:N0} | Đã ký: {totalKy:N0} | Tỉ lệ: {overall:F1}%";
        }
    }

    public class ThoiHieuRow
    {
        public int STT { get; set; }
        public string SPV { get; set; }
        public string QuetMa { get; set; }
        public string MaNVPhat { get; set; }
        public string TenNVPhat { get; set; }
        public int DonPhat { get; set; }
        public int DonCanKy91 { get; set; }
        public int KPI90 { get; set; }
        public int SoVDKyNhan { get; set; }
        public double TiLeKyNhan { get; set; }
        public int H8 { get; set; }
        public int H9 { get; set; }
        public int H10 { get; set; }
        public int H11 { get; set; }
        public int H12 { get; set; }
        public int H13 { get; set; }
        public int H14 { get; set; }
        public int H15 { get; set; }
        public int H16 { get; set; }
        public int H17 { get; set; }
        public int H18 { get; set; }
        public int H19 { get; set; }
        public int H20 { get; set; }
        public int H21 { get; set; }
        public int H22 { get; set; }
        public int H23 { get; set; }
        public int H24 { get; set; }
        public int ConThieu91 { get; set; }
        public int ConThieu90 { get; set; }
    }

    public class SlaAgingRow
    {
        public string WaybillNo { get; set; }
        public string TrangThaiHienTai { get; set; }
        public string ThaoTacCuoi { get; set; }
        public string NguoiThaoTac { get; set; }
        public string ThoiGianThaoTac { get; set; }
        public string TonKhoDuration { get; set; }
        public string SlaRemaining { get; set; }
        public string LevelCanhBao { get; set; }
        public string ThoiGianNhanHang { get; set; }
        public string TenNguoiGui { get; set; }
    }
}



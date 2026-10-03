using AutoJMS.UI.DesignSystem;
using System;
using System.Collections.Concurrent;
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
    public partial class FullStackOperation : Form
    {
        private const string CHROME_USER_AGENT = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36";

        private List<WaybillDbModel> _cloudData = new();
        // Timestamp the dashboard data was last refreshed from the local snapshot (for "Last Update").
        private DateTime? _lastDataUpdate;
        private List<WaybillDbModel> _lastDashSourceData = new();
        private List<WaybillDbModel> _lastChatSourceData = new();
        private List<string> _dashStatusCache = new();
        private List<string> _chatStatusCache = new();

        private ZaloChatService _zaloChatService;
        private bool _isZaloLoaded = false;
        private bool _isRefreshingStatusCombos = false;
        private System.Windows.Forms.Timer _autoRefreshTimer;
        // Phase 2 leader tiered sync: T0 head probe (this timer) + T1 hot-set. T2 = the auto full sync.
        private System.Windows.Forms.Timer _leaderTierTimer;
        private string _lastHeadHash = string.Empty;
        private readonly HashSet<string> _lastHeadBillcodes = new(StringComparer.OrdinalIgnoreCase);
        private DateTime _lastHotSetAtUtc = DateTime.MinValue;
        private volatile bool _leaderTierBusy;
        private int _headProbeFailures;
        private DateTime _headBackoffUntilUtc = DateTime.MinValue;
        private const int LeaderHeadProbeMs = 90 * 1000;                 // T0 heartbeat
        private static readonly TimeSpan LeaderHotSetInterval = TimeSpan.FromMinutes(3); // T1 cadence
        private readonly CancellationTokenSource _cts = new();
        private TabPage _tabDetail;
        private readonly System.Windows.Forms.Timer _alertCheckTimer = new();
        private int _lastCriticalAlertCount = 0;
        private Label[] _detailLabels;
        private Panel _detailSlaCard;
        private Label _detailSlaValue;
        private Label _detailAgeValue;

        // Thoi Hieu tab
        private TabPage _tabThoiHieu;

        // Set true on FormClosing so background ticks stop touching UI.
        private volatile bool _isClosing;

        // Dash enhancements
        private TextBox _dashSearchBox;
        private DateTimePicker _dashDateFrom;
        private DateTimePicker _dashDateTo;
        private AButton _dashExportBtn;
        private List<int> _kpiHistory = new();
        private string _lastDataHash = string.Empty;
        private ToolTip _tooltip;
        private bool _isRealtimeStarted = false;

        /// <summary>
        /// Latch for the read half of the runtime — loading local SQLite into the views and
        /// starting DataHub sync. Separate from <see cref="_isRealtimeStarted"/> because the
        /// two halves are gated on different credentials: reading needs only the ULTRA
        /// entitlement, while fetching from JMS needs the JMS auth token. They used to share
        /// one latch, which meant a licensed machine with a valid device token but no JMS
        /// login opened this window on an empty grid, with local rows sitting unread on disk.
        /// </summary>
        private bool _isLocalRuntimeStarted = false;
        private readonly FullStackDashboardService _fullStackDashboardService = new();
        private readonly FullStackWorkflowService _fullStackWorkflowService = new();
        private readonly FullStackExportService _fullStackExportService = new();
        private readonly IFullStackJourneyService _fullStackJourneyService = new FullStackJourneyService();
        private readonly JourneyHistoryService _journeyHistoryService = new();
        private List<WaybillDbModel> _lastFilteredDashRows = new();
        private bool _isSyncRunning = false;
        // 0 = idle, 1 = a stream render is in flight. Interlocked because the sync consumer thread
        // and the UI thread (re-arm at the end of a render) both try to claim it.
        private int _streamRefreshInFlight = 0;
        // Pages of enriched rows waiting to be spliced into _cloudData. Written from the sync's
        // background consumer, drained only on the UI thread.
        private readonly ConcurrentQueue<IReadOnlyList<WaybillDbModel>> _pendingStreamRows = new();
        // Set when rows changed in SQLite outside this form's knowledge (DataHub merge, hot-set
        // enrich): RAM cannot be patched from a batch, so the next render re-reads the snapshot.
        private volatile bool _streamReloadRequested = false;
        private IReadOnlyDictionary<string, FullStackOperationMetadata> _operationMetadata = new Dictionary<string, FullStackOperationMetadata>(StringComparer.OrdinalIgnoreCase);
        private Action<string> _authTokenHandler;

        protected override bool ShowWithoutActivation => true;

        public FullStackOperation()
        {
            // Gate an ninh, không phải gate UX. Main đã kiểm tra trước khi tạo form,
            // nhưng gate ở UI có thể bị đi vòng; cửa sổ ULTRA không được phép TỒN TẠI
            // khi entitlement không cho. PreCreateFullStackForm và ShowFullStackForm đều
            // bắt exception nên ném ở đây là fail-closed an toàn.
            if (!TierRuntimePolicy.Current.EnableFullStackOperation)
            {
                AppLogger.Warning(
                    $"[FullStack] Chặn khởi tạo: tier={TierRuntimePolicy.Current.Tier} " +
                    "không có entitlement FullStackOperation.");
                throw new UnauthorizedAccessException(
                    "FullStackOperation yêu cầu entitlement ULTRA.");
            }

            ConfigureFormShell();
            BuildUiInCode();
            WireCodeFirstEvents();

            _tooltip = new ToolTip();
            _tooltip.InitialDelay = 500;
            _tooltip.ReshowDelay = 200;
            _tooltip.AutoPopDelay = 10000;

            // After _tooltip so the hint can be attached — see FullStackOperation.ExcelSeed.cs.
            AttachExcelSeedMenu();

            // Subscribe to auth — will activate runtime when token arrives
            _authTokenHandler = async token => await StartRealtimeRuntimeAsync();
            AuthStateService.Instance.TokenAcquired += _authTokenHandler;
        }

        public void ClearDashGridSelection()
        {
            if (tabDash_dataGridView == null) return;
            if (tabDash_dataGridView.InvokeRequired)
            {
                tabDash_dataGridView.Invoke(new Action(() => 
                {
                    tabDash_dataGridView.ClearSelection();
                    tabDash_dataGridView.CurrentCell = null;
                }));
            }
            else
            {
                tabDash_dataGridView.ClearSelection();
                tabDash_dataGridView.CurrentCell = null;
            }
        }

        private async void FullStackOperation_Load(object sender, EventArgs e)
        {
            if (_webView != null)
            {
                _ = _webView.Handle;
                _ = InitializeWebView2Async();
            }

            // STATE 1 — IDLE: UI only, no API calls, no realtime
            SetupGrids();
            InitializeEnhancedUI();
            ApplyFullStackContentTheme();
            _contentThemeHook = new ThemeHook(this, ApplyFullStackContentTheme);
            tabDash_dataSource.SelectedIndex = 1;
            tabDash_timeUpdateData.Text = "30 PHÚT";

            AppLogger.Info("FullStack UI initialized");
            await InitializeLocalFullStackAsync();
            _ = CleanupJourneyDetailsCacheAsync();

            // Reading does not wait for the JMS token. The licence already said this machine
            // may operate FullStack, and everything below reads local SQLite plus DataHub —
            // neither of which JMS issues credentials for.
            await StartLocalRuntimeAsync();

            AppLogger.Info("FullStackOperation loaded (READ-ONLY) — waiting for auth token to enable JMS fetch");

            // If token was already set before form created, start ACTIVE now
            if (AuthStateService.Instance.IsAuthenticated)
                _ = StartRealtimeRuntimeAsync();
        }

        /// <summary>
        /// The read half: local rows into the views, plus the DataHub sync loop that keeps
        /// them current from other machines at the site. Gated on the ULTRA entitlement only,
        /// because nothing here talks to JMS — the projection comes from SQLite and the
        /// device token, and blocking it on a JMS login was showing operators an empty grid
        /// while the data they wanted sat in the local database.
        ///
        /// Idempotent: <see cref="StartRealtimeRuntimeAsync"/> calls it too, for the path
        /// where the token arrives before this form finishes loading.
        /// </summary>
        private async Task StartLocalRuntimeAsync()
        {
            if (!TierRuntimePolicy.Current.EnableFullStackOperation)
            {
                AppLogger.Warning(
                    "[FullStack] Chặn StartLocalRuntimeAsync: tier=" +
                    $"{TierRuntimePolicy.Current.Tier} không có entitlement FullStackOperation.");
                return;
            }

            if (_isLocalRuntimeStarted) return;
            _isLocalRuntimeStarted = true;

            await LoadDataAndRefreshViewsAsync();

            // Hybrid local-first + DataHub sync (docs/hybrid-datahub-sync-plan.md):
            // background outbox flush + delta-pull + realtime doorbell. No-op when disabled.
            StartCloudSync();
        }

        private async Task StartRealtimeRuntimeAsync()
        {
            // Gate thứ hai: đây là nơi phát sinh side effect thật (realtime, sync,
            // lease, timer). Nếu entitlement không cho thì dừng ngay tại đây.
            if (!TierRuntimePolicy.Current.EnableFullStackOperation)
            {
                AppLogger.Warning(
                    "[FullStack] Chặn StartRealtimeRuntimeAsync: tier=" +
                    $"{TierRuntimePolicy.Current.Tier} không có entitlement FullStackOperation.");
                return;
            }

            if (_isRealtimeStarted) return;
            _isRealtimeStarted = true;

            // STATE 2 — ACTIVE: auth token available, start realtime runtime
            AppLogger.Info("FullStackOperation activating — token acquired");
            SetFullStackStatus("AuthToken sẵn sàng - local-first SQLite");

            // Covers the token-before-Load ordering; a no-op on the usual path where Load
            // already ran it. What follows is only the JMS-fetching half.
            await StartLocalRuntimeAsync();

            _autoRefreshTimer = new System.Windows.Forms.Timer();
            // Auto-sync cadence follows the "Cập nhật sau" dropdown (default 30 phút) and runs the
            // same sync as the manual button (silent — no modal popups).
            _autoRefreshTimer.Tick += async (s, ev) => await RunSyncAsync(silent: true);
            tabDash_timeUpdateData_SelectedIndexChanged(null, null); // set interval from the combo
            _autoRefreshTimer.Start();

            // Leader tiered sync (only fires work when THIS machine holds the lease).
            _leaderTierTimer = new System.Windows.Forms.Timer { Interval = LeaderHeadProbeMs };
            _leaderTierTimer.Tick += async (s, ev) => await LeaderTierTickAsync();
            _leaderTierTimer.Start();
        }

        private void StartCloudSync()
        {
            try
            {
                if (!DataHubSyncService.IsEnabled)
                {
                    AppLogger.Info("[HybridSync] cloud sync disabled (flag/site/credentials) — local-only mode");
                    return;
                }

                DataHubSyncService.Instance.DataMerged += OnCloudDataMerged;
                DataHubSyncService.Instance.StatusChanged += OnCloudSyncStatus;
                _ = DataHubSyncService.Instance.StartAsync(_cts.Token);
            }
            catch (Exception ex)
            {
                AppLogger.Warning("[HybridSync] StartCloudSync failed: " + ex.Message);
            }
        }

        // Remote rows were merged into SQLite — reuse the coalesced stream refresh.
        private void OnCloudDataMerged()
        {
            if (_isClosing) return;
            _ = OnSyncBatchPersistedAsync();
        }

        private void OnCloudSyncStatus(string message)
        {
            if (_isClosing || string.IsNullOrWhiteSpace(message)) return;
            try
            {
                if (InvokeRequired) BeginInvoke(new Action(() => SetFullStackStatus(message)));
                else SetFullStackStatus(message);
            }
            catch (Exception ex)
            {
                AppLogger.Warning("[HybridSync] status update error: " + ex.Message);
            }
        }

        private void FullStackOperation_FormClosing(object sender, FormClosingEventArgs e)
        {
            // Stop background work first so no tick touches a disposing grid.
            _isClosing = true;
            _autoRefreshTimer?.Stop();
            _autoRefreshTimer?.Dispose();
            _leaderTierTimer?.Stop();
            _leaderTierTimer?.Dispose();
            _alertCheckTimer?.Stop();
            _alertCheckTimer?.Dispose();
            CancelCurrentJourneyLoad();
            _cts.Cancel();
            try
            {
                DataHubSyncService.Instance.DataMerged -= OnCloudDataMerged;
                DataHubSyncService.Instance.StatusChanged -= OnCloudSyncStatus;
                // Not awaited on purpose: StopAsync releases the lease over the network and
                // FormClosing must not freeze the UI for that. The continuation is only here so a
                // fault is logged instead of silently discarded as an unobserved task exception.
                _ = DataHubSyncService.Instance.StopAsync().ContinueWith(
                    t => AppLogger.Warning("[HybridSync] stop on close failed: " + t.Exception?.GetBaseException().Message),
                    TaskContinuationOptions.OnlyOnFaulted);
            }
            catch (Exception ex)
            {
                AppLogger.Warning("[HybridSync] stop on close failed: " + ex.Message);
            }
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
            // Dark theme: render EVERY cell like the selected one — dark background, white text.
            grid.BackgroundColor = GridDarkBg;
            grid.GridColor = GridDarkLine;
            grid.DefaultCellStyle.Font = new Font("Segoe UI", 7.5F);
            grid.DefaultCellStyle.BackColor = GridDarkBg;
            grid.DefaultCellStyle.ForeColor = Color.White;
            grid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(37, 99, 235);
            grid.DefaultCellStyle.SelectionForeColor = Color.White;
            grid.RowsDefaultCellStyle.BackColor = GridDarkBg;
            grid.RowsDefaultCellStyle.ForeColor = Color.White;
            grid.AlternatingRowsDefaultCellStyle.BackColor = GridDarkAltBg;
            grid.AlternatingRowsDefaultCellStyle.ForeColor = Color.White;
            grid.EnableHeadersVisualStyles = false;
            grid.DataError -= FullStackGrid_DataError;
            grid.DataError += FullStackGrid_DataError;
            // DataGridView không tự tô lại ô bao giờ, nên bảng màu tối đặt ở trên là
            // trạng thái cuối cùng — không cần chốt lại theme sau khi dựng.
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
                _lastDataUpdate = snapshot.LastSyncAt?.ToLocalTime() ?? DateTime.Now;
                await RefreshArrivalMonitorAsync(_cts.Token);
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
            await RunSyncAsync(silent: false);
        }

        // Shared sync for the manual "Đồng bộ" button and the auto-refresh timer. The button is
        // disabled and shows "Đang đồng bộ" for the whole run (manual OR auto) to prevent spam.
        // Auto (silent) runs skip modal popups and just update the status line.
        private async Task RunSyncAsync(bool silent)
        {
            if (_isSyncRunning) return;
            tabDash_updateData.Enabled = false;
            tabDash_updateData.Text = "Đang đồng bộ";
            _isSyncRunning = true;
            _leaderTierTimer?.Stop(); // pause T0/T1 while the full sync (T2) runs — no overlap
            try
            {
                var ct = _cts.Token;
                if (!JmsAuthStateService.HasToken && !AuthStateService.Instance.IsAuthenticated)
                {
                    // No JMS token means this machine cannot fetch from JMS, so it must not
                    // take the lease either — a leader that cannot pull starves the whole
                    // site. The follower path is still open to it: PullAllAsync reads what
                    // another machine already published, using the DataHub device token.
                    if (!DataHubSyncService.IsEnabled)
                    {
                        SetFullStackStatus("Đang chờ đăng nhập / authToken");
                        if (!silent)
                            MessageBox.Show("Đang chờ đăng nhập JMS / authToken. Vui lòng đăng nhập JMS trước khi đồng bộ tồn kho.", "FullStack local-first", MessageBoxButtons.OK, MessageBoxIcon.Information);
                        return;
                    }

                    SetFullStackStatus("Chưa đăng nhập JMS — lấy dữ liệu chia sẻ từ cloud...");
                    int cloudMerged = await DataHubSyncService.Instance.PullAllAsync(ct);
                    await LoadDataAndRefreshViewsAsync();
                    SetFullStackStatus($"Đồng bộ từ cloud xong ({cloudMerged:N0} thay đổi) — đăng nhập JMS để kéo dữ liệu mới");
                    return;
                }

                // Hybrid sync: only the lease-holding machine pulls JMS; others delta-pull the
                // shared canonical data from DataHub (falls back to JMS when cloud is off/down).
                bool isLeader = await DataHubSyncService.Instance.TryBecomeLeaderAsync(ct);
                if (!isLeader)
                {
                    SetFullStackStatus("Máy khác đang giữ lease — lấy dữ liệu chia sẻ từ cloud...");
                    int merged = await DataHubSyncService.Instance.PullAllAsync(ct);
                    await LoadDataAndRefreshViewsAsync();
                    SetFullStackStatus($"Đồng bộ từ cloud xong ({merged:N0} thay đổi) — máy giữ lease đang kéo JMS");
                    return;
                }

                SetFullStackStatus("Đang đồng bộ tồn kho 30 ngày...");
                // Stream results into the grid as each page is fetched + tracked (realtime).
                var result = await _fullStackDashboardService.SyncInventoryAndRefreshTrackingAsync(
                    from: null, to: null, onBatchPersisted: OnSyncBatchPersistedAsync, ct: ct);
                AppLogger.Info($"[FullStackOperation] manual sync finished runId={result.RunId}, fetched={result.TotalFetched}, new={result.NewWaybills}, left={result.LeftInventory}");
                SetFullStackStatus($"Sync xong: {result.TotalFetched:N0} đơn, mới {result.NewWaybills:N0}, rời tồn {result.LeftInventory:N0}");
                await LoadDataAndRefreshViewsAsync();

                // Leader shares the freshly enriched rows with the rest of the site.
                if (DataHubSyncService.IsEnabled && DataHubSyncService.Instance.HasLease)
                {
                    try
                    {
                        await DataHubSyncService.Instance.PushDashboardRowsAsync(ct);
                        SetFullStackStatus("Đã chia sẻ dữ liệu lên cloud cho các máy khác");
                    }
                    catch (Exception pushEx)
                    {
                        AppLogger.Warning("[HybridSync] post-sync push failed: " + pushEx.Message);
                    }
                }
            }
            catch (Exception ex)
            {
                if (!silent)
                    MessageBox.Show("Lỗi làm mới dữ liệu: " + ex.Message, "Lỗi", MessageBoxButtons.OK, MessageBoxIcon.Error);
                SetFullStackStatus("Sync failed");
            }
            finally
            {
                _isSyncRunning = false;
                // T2 just refreshed everything → reset hot-set cadence and resume the tiered timer.
                _lastHotSetAtUtc = DateTime.UtcNow;
                // Touching the button after the window closed throws ObjectDisposedException from a
                // finally block, which escapes the async void timer handler and kills the process.
                if (!_isClosing && !IsDisposed)
                {
                    tabDash_updateData.Enabled = true;
                    tabDash_updateData.Text = "Đồng bộ";
                    _leaderTierTimer?.Start();
                }
            }
        }

        // Phase 2 leader tiered sync tick (T0 head probe + T1 hot-set). Only the lease-holding machine
        // does work; followers rely on opportunistic refresh + cloud delta pull. T2 full sync is the
        // dropdown-driven auto sync (RunSyncAsync), left untouched.
        private async Task LeaderTierTickAsync()
        {
            if (_leaderTierBusy || _isSyncRunning) return;
            if (!DataHubSyncService.Instance.HasLease) return;
            if (!JmsAuthStateService.HasToken && !AuthStateService.Instance.IsAuthenticated) return;
            if (DateTime.UtcNow < _headBackoffUntilUtc) return; // JMS backoff after repeated head failures

            _leaderTierBusy = true;
            try
            {
                var ct = _cts.Token;

                // T0 — cheap page-1 head probe (1 request) to detect working-set changes.
                var head = await _fullStackDashboardService.FetchInventoryHeadAsync(ct).ConfigureAwait(false);
                if (!head.Success)
                {
                    _headProbeFailures++;
                    // exponential-ish backoff, capped ~9 min, so a slow/down JMS isn't hammered every 90s.
                    int backoffTicks = Math.Min(_headProbeFailures, 6);
                    _headBackoffUntilUtc = DateTime.UtcNow.AddMilliseconds((double)LeaderHeadProbeMs * backoffTicks);
                    AppLogger.Warning($"[LeaderTier] head probe failed ({head.ErrorCode}); backoff x{backoffTicks}");
                    return;
                }
                _headProbeFailures = 0;
                _headBackoffUntilUtc = DateTime.MinValue;

                bool headChanged = !string.Equals(head.Hash, _lastHeadHash, StringComparison.Ordinal);
                var newCodes = new List<string>();
                if (headChanged)
                {
                    foreach (var c in head.Billcodes)
                        if (!_lastHeadBillcodes.Contains(c)) newCodes.Add(c);
                    _lastHeadHash = head.Hash;
                    _lastHeadBillcodes.Clear();
                    foreach (var c in head.Billcodes) _lastHeadBillcodes.Add(c);
                }

                // T1 — hot-set enrich when the head changed OR the hot-set cadence elapsed.
                bool dueHotSet = (DateTime.UtcNow - _lastHotSetAtUtc) >= LeaderHotSetInterval;
                if (headChanged || dueHotSet)
                {
                    _lastHotSetAtUtc = DateTime.UtcNow;
                    int n = await _fullStackDashboardService.SyncHotSetAsync(newCodes, cap: 300, ct).ConfigureAwait(false);
                    if (n > 0)
                    {
                        if (DataHubSyncService.IsEnabled && DataHubSyncService.Instance.HasLease)
                        {
                            try { await DataHubSyncService.Instance.PushDashboardRowsAsync(ct).ConfigureAwait(false); }
                            catch (Exception pushEx) { AppLogger.Warning("[LeaderTier] push failed: " + pushEx.Message); }
                        }
                        await OnSyncBatchPersistedAsync().ConfigureAwait(false);
                        AppLogger.Info($"[LeaderTier] hot-set enriched={n} headChanged={headChanged} newCodes={newCodes.Count}");
                    }
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                AppLogger.Warning("[LeaderTier] tick error: " + ex.Message);
            }
            finally
            {
                _leaderTierBusy = false;
            }
        }

        // Realtime streaming refresh: invoked from the sync consumer after each page of codes is
        // enriched, carrying THAT page's merged rows. The batch is queued BEFORE the coalescing
        // check on purpose — the old code returned early while a render was in flight, which was
        // harmless when every refresh re-read the whole DB, but silently drops the page now that
        // the page itself is the only copy of that data in this direction.
        private Task OnSyncBatchPersistedAsync(IReadOnlyList<WaybillDbModel> pageRows)
        {
            if (pageRows != null && pageRows.Count > 0) _pendingStreamRows.Enqueue(pageRows);
            ScheduleStreamRefresh();
            return Task.CompletedTask;
        }

        // Rows landed in SQLite without passing through this form (DataHub merge, leader hot-set),
        // so there is no batch to splice — the render has to re-read the snapshot from disk.
        private Task OnSyncBatchPersistedAsync()
        {
            _streamReloadRequested = true;
            ScheduleStreamRefresh();
            return Task.CompletedTask;
        }

        // Coalesces renders, not data: a caller that loses the race leaves its work in the queue
        // (or its reload flag set) and the running pass picks it up when it finishes.
        private void ScheduleStreamRefresh()
        {
            try
            {
                if (_isClosing || IsDisposed) return;
                if (Interlocked.CompareExchange(ref _streamRefreshInFlight, 1, 0) != 0) return;
                if (InvokeRequired) BeginInvoke(new Action(() => _ = RunStreamRefreshAsync()));
                else _ = RunStreamRefreshAsync();
            }
            catch (Exception ex)
            {
                Interlocked.Exchange(ref _streamRefreshInFlight, 0);
                AppLogger.Warning("[FullStackOperation] stream refresh dispatch error: " + ex.Message);
            }
        }

        // Splices the queued pages into _cloudData IN PLACE and returns how many rows were applied.
        // MUST run on the UI thread with no await inside: the dashboard render walks _cloudData with
        // dozens of LINQ passes, so a mutation from the sync's background thread would throw
        // InvalidOperationException mid-enumeration.
        private int DrainPendingStreamRows()
        {
            if (_pendingStreamRows.IsEmpty) return 0;

            var index = new Dictionary<string, int>(_cloudData.Count, StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < _cloudData.Count; i++)
            {
                var key = _cloudData[i]?.WaybillNo?.Trim();
                if (!string.IsNullOrEmpty(key)) index[key] = i;
            }

            int applied = 0;
            while (_pendingStreamRows.TryDequeue(out var batch))
            {
                if (batch == null) continue;
                foreach (var row in batch)
                {
                    var key = row?.WaybillNo?.Trim();
                    if (string.IsNullOrEmpty(key)) continue;
                    if (index.TryGetValue(key, out int at)) _cloudData[at] = row;
                    else { index[key] = _cloudData.Count; _cloudData.Add(row); }
                    applied++;
                }
            }
            return applied;
        }

        private async Task RunStreamRefreshAsync()
        {
            try
            {
                // Before the first await, on the UI thread — see DrainPendingStreamRows.
                int spliced = DrainPendingStreamRows();
                bool reload = _streamReloadRequested;
                _streamReloadRequested = false;

                if (reload)
                {
                    var snapshot = await _fullStackDashboardService.LoadSnapshotAsync(_cts.Token);
                    _cloudData = snapshot.Rows ?? new List<WaybillDbModel>();
                    _lastDataUpdate = snapshot.LastSyncAt?.ToLocalTime() ?? _lastDataUpdate;
                    // A page may have been queued while the disk read was running.
                    spliced += DrainPendingStreamRows();
                }
                else if (spliced == 0)
                {
                    return; // nothing new — don't burn a full grid render
                }
                else
                {
                    _lastDataUpdate = DateTime.Now;
                }

                await RefreshDashViewAsync(_cts.Token);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                AppLogger.Warning("[FullStackOperation] stream refresh error: " + ex.Message);
            }
            finally
            {
                Interlocked.Exchange(ref _streamRefreshInFlight, 0);
                // Work that arrived while this render was running was queued, not dropped — but
                // nothing will pick it up unless the pass that blocked it re-arms here. Posted
                // rather than called inline so the message pump gets a turn between batches: the
                // grid repaints and the operator can still click while the sync keeps streaming.
                try
                {
                    if (!_isClosing && !IsDisposed && IsHandleCreated
                        && (!_pendingStreamRows.IsEmpty || _streamReloadRequested))
                        BeginInvoke(new Action(ScheduleStreamRefresh));
                }
                catch (Exception rearmEx)
                {
                    AppLogger.Warning("[FullStackOperation] stream refresh re-arm skipped: " + rearmEx.Message);
                }
            }
        }

        private async void tabDash_dataSource_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (_isRefreshingStatusCombos) return;
            // RefreshDashViewAsync hits the network for the PHATLAI source and has no guard of its
            // own; in an async void handler an escaping exception crashes the process.
            try
            {
                await RefreshDashViewAsync(_cts.Token);
            }
            catch (OperationCanceledException)
            {
                // Form closing — nothing to report.
            }
            catch (Exception ex)
            {
                AppLogger.Warning("[FullStackOperation] refresh dash view failed: " + ex.Message);
                SetFullStackStatus("Không tải được nguồn dữ liệu đã chọn");
            }
        }

        private void tabDash_statusSelect_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (_isRefreshingStatusCombos) return;
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
                    DateTime from = _dashDateFrom.Value;
                    filtered = filtered.Where(x => DateTime.TryParse(x.ThoiGianThaoTac, out DateTime t) && t >= from).ToList();
                }
                if (_dashDateTo != null && _dashDateTo.Checked)
                {
                    DateTime to = _dashDateTo.Value;
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

            UpdatePriorityFocusCards(sourceCount, filteredCount);
            UpdateOperationCenterChrome(sourceCount, filteredCount);
        }

        private void UpdateDashGridDataSource(List<WaybillDbModel> data)
        {
            _lastFilteredDashRows = data?.ToList() ?? new List<WaybillDbModel>();
            PostStateToWebView2();

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

            try
            {
                await LoadDataAndRefreshViewsAsync();
            }
            finally
            {
                // Without the finally the button stays disabled on "Đang tải..." for the life
                // of the process: the throw reaches Application.ThreadException, which logs it
                // and carries on, so nothing restores the button and chat can't be reloaded.
                tabChat_btnReload.Enabled = true;
                tabChat_btnReload.Text = "-Làm mới-";
            }
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
        }

        private void UpdatePriorityFocusCards(int sourceCount, int filteredCount)
        {
            int total = _cloudData.Count;
            int inbound = _cloudData.Count(x => x.TrangThaiHienTai == "Hàng đến");
            int delivery = _cloudData.Count(x => x.TrangThaiHienTai == "Phát hàng");
            int returnQ = _cloudData.Count(x => x.TrangThaiHienTai == "Chuyển hoàn");
            int needAction = _cloudData.Count(IsNeedsAction);
            int lateSla = _cloudData.Count(x => IsSlaBreached(x.ThoiGianNhanHang));

            _kpiTotalInventory?.SetMetric("TỔNG TỒN", total.ToString("N0"), "Tổng số đơn", "ALL", AccentBlue, "Tất cả tồn kho");
            _kpiInbound?.SetMetric("HÀNG ĐẾN", inbound.ToString("N0"), "Đang trung chuyển", "IN", AccentBlue, "Hàng đến");
            _kpiDelivery?.SetMetric("PHÁT HÀNG", delivery.ToString("N0"), "Đang đi giao", "DEL", AccentGreen, "Phát hàng");
            _kpiBacklog?.SetMetric("BACKLOG", needAction.ToString("N0"), "Tồn đọng", "BL", AccentRed, "Cần xử lý ngay");
            _kpiReturn?.SetMetric("CHUYỂN HOÀN", returnQ.ToString("N0"), "Đang hoàn", "RET", Color.Orange, "Chuyển hoàn");
            _kpiInventoryCheck?.SetMetric("KIỂM KHO", lateSla.ToString("N0"), "Quá SLA", "SLA", AccentRed, "SLA quá hạn");
            _kpiCustomerService?.SetMetric("CSKH", "0", "Khiếu nại", "CS", AccentPurple, "CSKH");
            _kpiStationHalt?.SetMetric("DỪNG TRẠM", "0", "Hold", "HLT", Color.Gray, "Dừng trạm");
            _kpiStarred?.SetMetric("STAR", "0", "Đánh dấu", "FAV", Color.Gold, "Đánh dấu");
            PostStateToWebView2();
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

        private void OperationQueueSidebar_QueueSelected(object sender, OperationQueueSelectedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(e?.Key)) return;
            SelectDashStatus(e.Key);
            RefreshFilteredGrid();
            SetFullStackStatus($"Đang lọc grid: {e.Key}");
        }

        private void OperationKpiCard_Clicked(object sender, OperationQueueSelectedEventArgs e)
        {
            OperationQueueSidebar_QueueSelected(sender, e);
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
        }

        private string GetCurrentSelectedWaybillNo()
        {
            return (tabDash_dataGridView?.CurrentRow?.DataBoundItem as WaybillDbModel)?.WaybillNo ?? string.Empty;
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

            var outerLayout = new TableLayoutPanel();
            outerLayout.Dock = DockStyle.Fill;
            outerLayout.ColumnCount = 2;
            outerLayout.RowCount = 1;
            outerLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            outerLayout.Margin = new Padding(0);

            // Lef t: Info grid
            var infoPanel = new APanel();
            infoPanel.Dock = DockStyle.Fill;
            infoPanel.Margin = new Padding(5);
            infoPanel.Padding = new Padding(10);
            infoPanel.Font = new Font("Microsoft Sans Serif", 12F);

            var infoLayout = new TableLayoutPanel();
            infoLayout.Dock = DockStyle.Fill;
            infoLayout.ColumnCount = 2;
            infoLayout.RowCount = 14;
            infoLayout.Margin = new Padding(0);
            infoLayout.Padding = new Padding(5);
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

            _detailLabels = new Label[fieldLabels.Length];
            for (int i = 0; i < fieldLabels.Length; i++)
            {
                var lbl = new Label();
                lbl.Text = fieldLabels[i];
                lbl.Font = new Font("Segoe UI Semibold", 11F, FontStyle.Bold);
                lbl.ForeColor = Color.FromArgb(80, 80, 80);
                lbl.Dock = DockStyle.Fill;
                lbl.TextAlign = ContentAlignment.MiddleLeft;
                lbl.Margin = new Padding(3);
                lbl.AutoSize = false;
                lbl.Height = 28;

                var val = new Label();
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
            var rightPanel = new APanel();
            rightPanel.Dock = DockStyle.Fill;
            rightPanel.Margin = new Padding(5);
            rightPanel.Padding = new Padding(10);
            rightPanel.Font = new Font("Microsoft Sans Serif", 12F);

            var rightLayout = new TableLayoutPanel();
            rightLayout.Dock = DockStyle.Fill;
            rightLayout.ColumnCount = 1;
            rightLayout.RowCount = 3;
            rightLayout.Margin = new Padding(0);
            rightLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 120F));
            rightLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 120F));
            rightLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

            // Panel thường chứ không phải APanel: nền thẻ này mang nghĩa mức SLA
            // (lục / vàng / đỏ, đặt lại ở UpdateDetailPanel), APanel thì luôn tô surface.
            _detailSlaCard = new Panel();
            _detailSlaCard.Dock = DockStyle.Fill;
            _detailSlaCard.Margin = new Padding(3);
            _detailSlaCard.MinimumSize = new Size(1, 1);
            _detailSlaCard.BackColor = Color.FromArgb(240, 255, 240);

            var slaTitle = new Label();
            slaTitle.Text = "THỜI HIỆU SLA";
            slaTitle.Font = new Font("Segoe UI Semibold", 13F, FontStyle.Bold);
            slaTitle.ForeColor = Color.FromArgb(48, 48, 48);
            slaTitle.TextAlign = ContentAlignment.MiddleLeft;
            slaTitle.Dock = DockStyle.Top;
            slaTitle.Height = 35;
            slaTitle.Margin = new Padding(5);

            _detailSlaValue = new Label();
            _detailSlaValue.Text = "Đang tính...";
            _detailSlaValue.Font = new Font("Segoe UI", 14F, FontStyle.Bold);
            _detailSlaValue.TextAlign = ContentAlignment.MiddleCenter;
            _detailSlaValue.Dock = DockStyle.Fill;

            _detailSlaCard.Controls.Add(_detailSlaValue);
            _detailSlaCard.Controls.Add(slaTitle);
            rightLayout.Controls.Add(_detailSlaCard, 0, 0);

            var ageCard = new Panel();
            ageCard.Dock = DockStyle.Fill;
            ageCard.Margin = new Padding(3);
            ageCard.MinimumSize = new Size(1, 1);
            ageCard.BackColor = Color.FromArgb(240, 248, 255);

            var ageTitle = new Label();
            ageTitle.Text = "THỜI GIAN TỒN KHO";
            ageTitle.Font = new Font("Segoe UI Semibold", 13F, FontStyle.Bold);
            ageTitle.ForeColor = Color.FromArgb(48, 48, 48);
            ageTitle.TextAlign = ContentAlignment.MiddleLeft;
            ageTitle.Dock = DockStyle.Top;
            ageTitle.Height = 35;
            ageTitle.Margin = new Padding(5);

            _detailAgeValue = new Label();
            _detailAgeValue.Text = "Đang tính...";
            _detailAgeValue.Font = new Font("Segoe UI", 14F, FontStyle.Bold);
            _detailAgeValue.TextAlign = ContentAlignment.MiddleCenter;
            _detailAgeValue.Dock = DockStyle.Fill;

            ageCard.Controls.Add(_detailAgeValue);
            ageCard.Controls.Add(ageTitle);
            rightLayout.Controls.Add(ageCard, 0, 1);

            var actionPanel = new APanel();
            actionPanel.Dock = DockStyle.Fill;
            actionPanel.Margin = new Padding(3);
            actionPanel.MinimumSize = new Size(1, 1);

            var actionFlow = new FlowLayoutPanel();
            actionFlow.Dock = DockStyle.Fill;
            actionFlow.FlowDirection = FlowDirection.TopDown;
            actionFlow.Padding = new Padding(10);
            actionFlow.AutoScroll = true;

            // Mã biểu tượng là codepoint MDL2 — xem ASymbols.
            var actions = new (string Text, int Symbol)[]
            {
                ("Gửi Zalo reminder", ASymbols.Send),
                ("In chuyển hoàn", ASymbols.Back),
                ("In lại đơn", ASymbols.Print),
                ("Xem trên JMS", ASymbols.View)
            };

            foreach (var act in actions)
            {
                var btn = new AButton();
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
            var btn = sender as AButton;
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

            // Opportunistic near-realtime refresh: fetch the latest tracking for THIS waybill in the
            // background, merge local, and scoped-push to DataHub so other machines see it in seconds.
            // UI already shows above; this never blocks the detail view. Applies to leader + follower.
            FireOpportunisticWaybillRefresh(waybillNo);
        }

        private readonly Dictionary<string, DateTime> _opportunisticLastRun = new(StringComparer.OrdinalIgnoreCase);
        private readonly object _opportunisticLock = new();
        private static readonly TimeSpan OpportunisticDebounce = TimeSpan.FromSeconds(45);

        private void FireOpportunisticWaybillRefresh(string waybillNo)
        {
            if (string.IsNullOrWhiteSpace(waybillNo)) return;
            string code = waybillNo.Trim().ToUpperInvariant();

            lock (_opportunisticLock)
            {
                if (_opportunisticLastRun.TryGetValue(code, out var last) &&
                    (DateTime.UtcNow - last) < OpportunisticDebounce)
                {
                    AppLogger.Info($"[Opportunistic] skipped debounce waybill={code}");
                    return;
                }
                _opportunisticLastRun[code] = DateTime.UtcNow;
            }

            _ = OpportunisticWaybillRefreshAsync(code);
        }

        private async Task OpportunisticWaybillRefreshAsync(string code)
        {
            try
            {
                var ct = _cts.Token;
                if (!JmsAuthStateService.HasToken && !AuthStateService.Instance.IsAuthenticated)
                    return;

                // before snapshot (local last_action_time)
                string before = await _fullStackDashboardService.GetWaybillLastActionTimeAsync(code, ct).ConfigureAwait(false);

                // fetch fresh route history for this single waybill + merge into local DB
                var results = await _fullStackDashboardService.EnrichTrackingWithResultAsync(new[] { code }, ct).ConfigureAwait(false);
                int events = 0;
                foreach (var r in results)
                    if (string.Equals(r.WaybillNo, code, StringComparison.OrdinalIgnoreCase)) events = r.TrackingEventCount;

                // after snapshot
                string after = await _fullStackDashboardService.GetWaybillLastActionTimeAsync(code, ct).ConfigureAwait(false);

                bool changed = !string.IsNullOrEmpty(after) &&
                               !string.Equals(before ?? string.Empty, after, StringComparison.Ordinal);
                if (!changed)
                {
                    AppLogger.Info($"[Opportunistic] enriched no change waybill={code} events={events}");
                    return;
                }

                // scoped push (no-op if cloud disabled; no lease required)
                await DataHubSyncService.Instance.PushWaybillRowsAsync(new[] { code }, ct).ConfigureAwait(false);
                AppLogger.Info($"[Opportunistic] enriched pushed waybill={code} events={events}");

                // refresh grid / WebView from the freshly merged local row
                await OnSyncBatchPersistedAsync().ConfigureAwait(false);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                AppLogger.Warning($"[Opportunistic] refresh error waybill={code}: {ex.Message}");
            }
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
                _detailSlaCard.BackColor = Color.FromArgb(255, 220, 220);
                _detailSlaValue.ForeColor = Color.DarkRed;
            }
            else if (warnLvl == "Cảnh báo")
            {
                _detailSlaCard.BackColor = Color.FromArgb(255, 250, 210);
                _detailSlaValue.ForeColor = Color.FromArgb(180, 140, 0);
            }
            else
            {
                _detailSlaCard.BackColor = Color.FromArgb(220, 255, 220);
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
            _tabThoiHieu.Controls.Add(new ThoiHieuKpiView { Dock = DockStyle.Fill });

            // Remove old tabPage4 from sub-tab control
            if (uiTabControl2.TabPages.Contains(tabPage4))
                uiTabControl2.TabPages.Remove(tabPage4);
        }
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









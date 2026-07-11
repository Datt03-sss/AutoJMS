using AutoJMS.Data;
using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace AutoJMS
{
    public enum PrintMode
    {
        InHoan,
        InChuyenTiep,
        InLaiDon,
        InReverse
    }

    public class PrintService : IPrintService
    {
        private readonly DataGridView _grid;
        private readonly ITrackingService _trackingService;
        private readonly IPrintSafetyGuard _printSafetyGuard;
        private readonly ISiteContextProvider _siteContextProvider;
        private readonly Func<string> _authTokenProvider;
        private readonly SemaphoreSlim _printGate = new(1, 1);
        private readonly DataTable _displayTable = new DataTable();
        private readonly BindingSource _bindingSource = new BindingSource();
        private readonly List<TrackingRow> _printRows = new();
        private readonly Dictionary<string, PrintSafetyResult> _lastAllowedSafetyByWaybill = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, PrintReadinessContext> _readinessByWaybill = new(StringComparer.OrdinalIgnoreCase);
        private readonly List<PrintStatusSnapshot> _lastPrintStatusSnapshots = new();
        private static readonly TimeSpan PrintReadinessTtl = TimeSpan.FromSeconds(60);
        private CancellationTokenSource _printCts;
        private string _currentPrintWaybill = "";
        private PrintMode _currentMode = PrintMode.InHoan;

        public PrintService(DataGridView grid, ITrackingService trackingService)
            : this(
                grid,
                trackingService,
                new PrintSafetyGuard(),
                () => JmsAuthStateService.CurrentToken,
                new SiteContextProvider())
        {
        }

        public PrintService(
            DataGridView grid,
            ITrackingService trackingService,
            IPrintSafetyGuard printSafetyGuard,
            Func<string> authTokenProvider,
            ISiteContextProvider siteContextProvider)
        {
            _grid = grid ?? throw new ArgumentNullException(nameof(grid));
            _trackingService = trackingService ?? throw new ArgumentNullException(nameof(trackingService));
            _printSafetyGuard = printSafetyGuard ?? throw new ArgumentNullException(nameof(printSafetyGuard));
            _siteContextProvider = siteContextProvider ?? throw new ArgumentNullException(nameof(siteContextProvider));
            _authTokenProvider = authTokenProvider ?? (() => string.Empty);

            // THỦ THUẬT 1 CLICK: Click vào bất kỳ điểm nào trong Cell cũng sẽ tự động đổi trạng thái
            _grid.CellClick += (s, e) =>
            {
                if (e.RowIndex >= 0 && _grid.Columns[e.ColumnIndex].Name == "Select")
                {
                    // Lấy ô hiện tại
                    var cell = _grid.Rows[e.RowIndex].Cells[e.ColumnIndex];

                    // Lấy trạng thái hiện tại (nếu null thì coi như false)
                    bool isChecked = cell.Value != DBNull.Value && (bool)cell.Value;

                    // Đảo ngược trạng thái và gán lại
                    cell.Value = !isChecked;

                    // Ép Grid chốt dữ liệu xuống DataTable ngay lập tức
                    _grid.EndEdit();
                    UpdateStatsAndVisibility();
                }
            };

            WaybillTrackingService.EnableDoubleBuffering(_grid);

            SetupGrid();
            _bindingSource.DataSource = _displayTable;
            _grid.DataSource = _bindingSource;
            RebuildTable();
        }

        public void Reset()
        {
            _printCts?.Cancel();
            _currentPrintWaybill = string.Empty;
            _currentMode = PrintMode.InHoan;
            _displayTable.Rows.Clear();
            _displayTable.Columns.Clear();
            _printRows.Clear();
            _lastAllowedSafetyByWaybill.Clear();
            _readinessByWaybill.Clear();
            _lastPrintStatusSnapshots.Clear();
            _bindingSource.ResetBindings(false);
            RebuildTable();
            UpdateStatsAndVisibility();
        }
        // CÁI LOA PHÁT THANH: Bắn tín hiệu (Số đã chọn, Tổng số) ra ngoài Form chính
        public event Action<int, int> OnPrintStatsChanged;
        public event Action<PrintSafetyResult> OnPrintSafetyBlocked;
        public event Action OnPrintSelectionCleared;

        // HÀM TỔNG QUẢN: Đếm số lượng, Ẩn/Hiện GridView và phát loa thông báo
        private void UpdateStatsAndVisibility()
        {
            int total = _displayTable.Rows.Count;
            int selected = 0;

            // Đếm số lượng dòng đang được đánh dấu tick
            foreach (DataRow row in _displayTable.Rows)
            {
                if (row["Select"] != DBNull.Value && (bool)row["Select"])
                {
                    selected++;
                }
            }

            // Giữ grid hiển thị cả khi chưa có dữ liệu để header vẫn autosize đúng.
            if (_grid.InvokeRequired)
                _grid.Invoke(new Action(() => _grid.Visible = true));
            else
                _grid.Visible = true;

            // Bắn tín hiệu ra ngoài để Form chính cập nhật 2 cái Label
            OnPrintStatsChanged?.Invoke(selected, total);
        }
        private void SetupGrid()
        {
            _grid.AllowUserToAddRows = false;
            _grid.AllowUserToDeleteRows = false;
            _grid.SelectionMode = DataGridViewSelectionMode.CellSelect;
            _grid.EditMode = DataGridViewEditMode.EditOnEnter; // Quan trọng để click phát ăn ngay
            _grid.RowHeadersVisible = false;
            _grid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize;
            _grid.AutoGenerateColumns = true; // Để DataTable tự sinh cột

            DisableSorting();
        }

        private void AutoSizePrintGridColumns()
        {
            if (_grid.Columns.Count == 0) return;

            try
            {
                _grid.AutoResizeColumns(DataGridViewAutoSizeColumnsMode.AllCells);
                foreach (DataGridViewColumn col in _grid.Columns)
                {
                    string headerText = string.IsNullOrWhiteSpace(col.HeaderText) ? col.Name : col.HeaderText;
                    int headerWidth = TextRenderer.MeasureText(
                        headerText,
                        _grid.ColumnHeadersDefaultCellStyle.Font ?? _grid.Font).Width + 24;
                    int currentWidth = Math.Max(col.Width, headerWidth);

                    foreach (DataGridViewRow row in _grid.Rows)
                    {
                        if (row.IsNewRow) continue;
                        string cellText = Convert.ToString(row.Cells[col.Index].FormattedValue) ?? "";
                        int cellWidth = TextRenderer.MeasureText(
                            cellText,
                            row.Cells[col.Index].InheritedStyle.Font ?? _grid.Font).Width + 18;
                        currentWidth = Math.Max(currentWidth, cellWidth);
                    }

                    col.AutoSizeMode = DataGridViewAutoSizeColumnMode.None;
                    col.Width = Math.Max(currentWidth, col.MinimumWidth);
                }
            }
            catch (InvalidOperationException ex)
            {
                AppLogger.Warning($"Print grid autosize failed: {ex.Message}");
            }
        }

        private void SetColumnAlignments()
        {
            if (_grid.Columns.Count == 0) return;

            foreach (DataGridViewColumn col in _grid.Columns)
            {
                col.ReadOnly = true;

                if (col.Name == "Select")
                {
                    col.HeaderText = "Chọn";
                    col.Width = 50;
                }

                // Căn giữa các cột số lượng và mã
                switch (col.Name)
                {
                    case "Select":
                    case "Mã vận đơn":
                    case "Mã đoạn":
                    case "Mã đoạn 2 sau khi in":
                    case "Số lượng bản in":
                    case "Số bản in":
                        col.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
                        break;
                    default:
                        col.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleLeft;
                        break;
                }
            }
            _grid.ColumnHeadersDefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
        }

        public void SetMode(PrintMode mode)
        {
            if (_currentMode == mode) return;
            _currentMode = mode;
            ClearPrintableState(resetCurrent: true);
            RebuildTable();
        }

        private void RebuildTable()
        {
            _grid.SuspendLayout();
            try
            {
                _displayTable.Columns.Clear();
                _displayTable.Rows.Clear();

                // ĐỊNH NGHĨA CỘT SELECT TRONG DATATABLE (ĐÂY LÀ NƠI DUY NHẤT TẠO CHECKBOX)
                _displayTable.Columns.Add("Select", typeof(bool));

                switch (_currentMode)
                {
                    case PrintMode.InHoan:
                        _displayTable.Columns.Add("Mã vận đơn", typeof(string));
                        _displayTable.Columns.Add("Trạng thái", typeof(string));
                        _displayTable.Columns.Add("Trạng thái phê duyệt", typeof(string));
                        _displayTable.Columns.Add("Số bản in", typeof(int));
                        _displayTable.Columns.Add("Mã đoạn 2 sau khi in", typeof(string));
                        _displayTable.Columns.Add("Thời gian in", typeof(string));
                        _displayTable.Columns.Add("Người in", typeof(string));
                        break;
                    case PrintMode.InChuyenTiep:
                        _displayTable.Columns.Add("Mã vận đơn", typeof(string));
                        _displayTable.Columns.Add("Trạng thái", typeof(string));
                        _displayTable.Columns.Add("Số bản in", typeof(int));
                        _displayTable.Columns.Add("SĐT người nhận", typeof(string));
                        _displayTable.Columns.Add("Địa chỉ người nhận", typeof(string));
                        _displayTable.Columns.Add("Mã đoạn 2 sau khi in", typeof(string));
                        _displayTable.Columns.Add("Thời gian in", typeof(string));
                        _displayTable.Columns.Add("Người in", typeof(string));
                        break;
                    case PrintMode.InLaiDon:
                        _displayTable.Columns.Add("Mã vận đơn", typeof(string));
                        _displayTable.Columns.Add("Số bản in", typeof(int));
                        _displayTable.Columns.Add("Nội dung hàng hóa", typeof(string));
                        _displayTable.Columns.Add("Mã đoạn 2 sau khi in", typeof(string));
                        break;
                    case PrintMode.InReverse:
                        _displayTable.Columns.Add("Mã vận đơn", typeof(string));
                        _displayTable.Columns.Add("Nhân viên lấy hàng", typeof(string));
                        _displayTable.Columns.Add("Địa chỉ lấy hàng", typeof(string));
                        _displayTable.Columns.Add("Tên người gửi", typeof(string));
                        _displayTable.Columns.Add("Thời gian gửi", typeof(string));
                        _displayTable.Columns.Add("Số bản in", typeof(int));
                        _displayTable.Columns.Add("Nội dung hàng hóa", typeof(string));
                        _displayTable.Columns.Add("Mã đoạn 2 sau khi in", typeof(string));
                        break;
                }
            }
            finally
            {
                _grid.ResumeLayout();
            }

            LoadDataToGrid();
            SetColumnAlignments();
            AutoSizePrintGridColumns();
        }

        private void LoadDataToGrid()
        {
            var rows = _printRows.ToList();
            if (rows.Count == 0)
            {
                _displayTable.Rows.Clear();
                _bindingSource.ResetBindings(false);
                AutoSizePrintGridColumns();
                UpdateStatsAndVisibility();
                return;
            }

            _grid.SuspendLayout();
            _displayTable.BeginLoadData();
            try
            {
                _displayTable.Rows.Clear();
                string now = DateTime.Now.ToString("dd/MM/yyyy HH:mm");
                string printerName = Environment.UserName;

                foreach (var r in rows)
                {
                    string finalWaybill = r.WaybillNo ?? string.Empty;
                    bool isKyNhan = (r.TrangThaiHienTai?.Contains("Ký nhận", StringComparison.OrdinalIgnoreCase) == true) ||
                                       (r.ThaoTacCuoi?.Contains("Ký nhận", StringComparison.OrdinalIgnoreCase) == true);

                    if (string.IsNullOrWhiteSpace(finalWaybill))
                        continue;

                    if (isKyNhan && !finalWaybill.EndsWith("-001", StringComparison.Ordinal))
                    {
                        finalWaybill += "-001";
                    }

                    object[] rowData = _currentMode switch
                    {
                        PrintMode.InHoan => new object[] { true, finalWaybill, DisplayValue(r.ThaoTacCuoi), ResolveApprovalStatus(r), ResolvePrintCount(r), ResolveSenderNetworkCode(r), string.IsNullOrEmpty(r.InHoanScanTime) ? "-" : r.InHoanScanTime, ResolveApplyStaffName(r, printerName) },
                        PrintMode.InChuyenTiep => new object[] { true, finalWaybill, DisplayValue(r.ThaoTacCuoi), ResolvePrintCount(r, 1), "", DisplayValue(r.DiaChiNhanHang), ResolveSenderNetworkCode(r), now, ResolveApplyStaffName(r, printerName) },
                        PrintMode.InLaiDon => new object[] { true, finalWaybill, ResolvePrintCount(r, 1), DisplayValue(r.NoiDungHangHoa), ResolveSenderNetworkCode(r) },
                        PrintMode.InReverse => new object[] { true, finalWaybill, DisplayValue(r.NhanVienNhanHang), DisplayValue(r.DiaChiLayHang), DisplayValue(r.TenNguoiGui), DisplayValue(r.ThoiGianNhanHang), ResolvePrintCount(r, 1), DisplayValue(r.NoiDungHangHoa), ResolveSenderNetworkCode(r) },
                        _ => null
                    };

                    if (rowData != null) _displayTable.Rows.Add(rowData);
                }
            }
            finally
            {
                _displayTable.EndLoadData();
                _grid.ResumeLayout();
                AutoSizePrintGridColumns();
                UpdateStatsAndVisibility();
            }
        }

        private void DisableSorting()
        {
            foreach (DataGridViewColumn col in _grid.Columns)
                col.SortMode = DataGridViewColumnSortMode.NotSortable;
        }

        public async System.Threading.Tasks.Task SearchAndLoadAsync(string waybillsText, PrintMode mode)
        {
            var totalWatch = Stopwatch.StartNew();
            _currentMode = mode;
            _printCts?.Cancel();
            _printCts?.Dispose();
            _printCts = new CancellationTokenSource();
            var token = _printCts.Token;

            var requestedWaybills = ExtractWaybills(waybillsText);
            _currentPrintWaybill = requestedWaybills.FirstOrDefault() ?? string.Empty;
            ClearPrintableState();

            if (requestedWaybills.Count == 0)
            {
                NotifyBlocked(new PrintSafetyResult
                {
                    CanPrint = false,
                    ReasonCode = "EMPTY_WAYBILL",
                    UserMessage = "Mã vận đơn rỗng. Không in."
                });
                return;
            }

            await _printGate.WaitAsync(token);
            try
            {
                var guardWatch = Stopwatch.StartNew();
                await _siteContextProvider.RefreshAsync(token);
                SiteContext siteContext = _siteContextProvider.Current;
                string authToken = _authTokenProvider()?.Trim() ?? string.Empty;
                if (!JmsAuthTokenService.IsValidJmsToken(authToken))
                {
                    var guard = _printSafetyGuard.ValidateBeforePrint(_currentPrintWaybill, siteContext, null);
                    ClearPrintableState(resetCurrent: true);
                    NotifyBlocked(guard);
                    return;
                }
                guardWatch.Stop();

                int printType = mode switch
                {
                    PrintMode.InChuyenTiep => 2,
                    PrintMode.InLaiDon => 1,
                    PrintMode.InReverse => 1,
                    _ => 1
                };
                var trackingWaybills = requestedWaybills
                    .Select(NormalizeTrackingWaybillNo)
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                var trackingWatch = Stopwatch.StartNew();
                var printInfoWatch = Stopwatch.StartNew();
                var trackingTask = FetchTrackingRowsDirectAsync(requestedWaybills, token)
                    .ContinueWith(t =>
                    {
                        trackingWatch.Stop();
                        return t.GetAwaiter().GetResult();
                    }, token, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
                var printInfoTask = FetchPrintApprovalInfoSafeAsync(trackingWaybills, printType, PrintStatusRefreshReason.Search.ToString())
                    .ContinueWith(t =>
                    {
                        printInfoWatch.Stop();
                        return t.GetAwaiter().GetResult();
                    }, token, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);

                await Task.WhenAll(trackingTask, printInfoTask).ConfigureAwait(true);

                IReadOnlyList<TrackingRow> directRows = trackingTask.Result;
                IReadOnlyList<PrintApprovalInfo> approvals = printInfoTask.Result;

                if (_currentPrintWaybill != requestedWaybills[0] || token.IsCancellationRequested)
                    return;

                var rowsByBase = directRows
                    .Where(r => !string.IsNullOrWhiteSpace(r.WaybillNo))
                    .GroupBy(r => NormalizeBaseWaybill(r.WaybillNo), StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(g => g.Key, g => CloneTrackingRow(g.First()), StringComparer.OrdinalIgnoreCase);

                var allowedRows = new List<TrackingRow>();
                var snapshots = new List<PrintStatusSnapshot>();
                var approvalByBase = approvals
                    .GroupBy(x => NormalizeBaseWaybill(x.WaybillNo), StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
                bool blocked = false;

                foreach (string waybill in requestedWaybills)
                {
                    token.ThrowIfCancellationRequested();
                    rowsByBase.TryGetValue(NormalizeBaseWaybill(waybill), out var trackingRow);
                    var guard = _printSafetyGuard.ValidateBeforePrint(waybill, siteContext, trackingRow);

                    if (_currentPrintWaybill != requestedWaybills[0] || token.IsCancellationRequested)
                        return;

                    if (guard.CanPrint)
                    {
                        _lastAllowedSafetyByWaybill[NormalizeBaseWaybill(waybill)] = guard;
                        var printableRow = CloneTrackingRow(trackingRow);
                        printableRow.WaybillNo = waybill;
                        allowedRows.Add(printableRow);

                        string trackingWaybill = NormalizeTrackingWaybillNo(waybill);
                        approvalByBase.TryGetValue(trackingWaybill, out var approval);
                        var snapshot = BuildPrintStatusSnapshot(waybill, trackingWaybill, printableRow, approval, PrintStatusRefreshReason.Search);
                        snapshots.Add(snapshot);
                        _readinessByWaybill[NormalizeBaseWaybill(waybill)] = new PrintReadinessContext
                        {
                            InputWaybillNo = NormalizeWaybill(waybill),
                            TrackingWaybillNo = trackingWaybill,
                            VerifiedAt = DateTime.Now,
                            SafetyResult = guard,
                            StatusSnapshot = snapshot
                        };
                    }
                    else
                    {
                        NotifyBlocked(guard);
                        blocked = true;
                        break;
                    }
                }

                if (blocked || allowedRows.Count == 0)
                {
                    ClearPrintableState(resetCurrent: true);
                    return;
                }

                _printRows.Clear();
                _printRows.AddRange(allowedRows);
                MergePrintApprovalInfo(approvals);
                _lastPrintStatusSnapshots.Clear();
                _lastPrintStatusSnapshots.AddRange(snapshots);
                foreach (var snapshot in snapshots)
                    ApplySnapshotToPrintableRow(snapshot);
                RebuildTable();
                _bindingSource.ResetBindings(false);
                AppLogger.Info($"[PrintPerf] phase=Search waybill={requestedWaybills.FirstOrDefault()} trackingMs={trackingWatch.ElapsedMilliseconds} printInfoMs={printInfoWatch.ElapsedMilliseconds} guardMs={guardWatch.ElapsedMilliseconds} totalMs={totalWatch.ElapsedMilliseconds}");
            }
            catch (OperationCanceledException)
            {
                ClearPrintableState(resetCurrent: true);
            }
            catch (Exception ex)
            {
                ClearPrintableState(resetCurrent: true);
                AppLogger.Error("PrintService.SearchAndLoadAsync failed", ex);
                throw;
            }
            finally
            {
                _printGate.Release();
            }
        }

        public async Task<bool> ValidateSelectedBeforePrintAsync(IEnumerable<string> waybills, string currentInputText)
        {
            var totalWatch = Stopwatch.StartNew();
            var selected = ExtractWaybills(string.Join(Environment.NewLine, waybills ?? Enumerable.Empty<string>()));
            var currentInput = ExtractWaybills(currentInputText);

            if (selected.Count == 0)
                return false;

            var inputBases = new HashSet<string>(currentInput.Select(NormalizeBaseWaybill), StringComparer.OrdinalIgnoreCase);
            var selectedBases = new HashSet<string>(selected.Select(NormalizeBaseWaybill), StringComparer.OrdinalIgnoreCase);
            if (inputBases.Count == 0 || !selectedBases.SetEquals(inputBases))
            {
                ClearPrintableState(resetCurrent: true);
                NotifyBlocked(new PrintSafetyResult
                {
                    CanPrint = false,
                    ReasonCode = "WAYBILL_MISMATCH",
                    UserMessage = "Dữ liệu in không khớp mã vận đơn hiện tại."
                });
                return false;
            }

            if (TryUseFreshReadiness(selected, out long readinessAgeMs))
            {
                AppLogger.Info($"[PrintPerf] phase=Print usingFreshReadiness=true waybill={selected.FirstOrDefault()} readinessAgeMs={readinessAgeMs} totalMs={totalWatch.ElapsedMilliseconds}");
                return true;
            }

            _printCts?.Cancel();
            _printCts?.Dispose();
            _printCts = new CancellationTokenSource();
            var token = _printCts.Token;
            _currentPrintWaybill = selected.FirstOrDefault() ?? string.Empty;

            await _printGate.WaitAsync(token);
            try
            {
                var guardWatch = Stopwatch.StartNew();
                await _siteContextProvider.RefreshAsync(token);
                SiteContext siteContext = _siteContextProvider.Current;
                string authToken = _authTokenProvider()?.Trim() ?? string.Empty;
                if (!JmsAuthTokenService.IsValidJmsToken(authToken))
                {
                    var guard = _printSafetyGuard.ValidateBeforePrint(_currentPrintWaybill, siteContext, null);
                    ClearPrintableState(resetCurrent: true);
                    NotifyBlocked(guard);
                    return false;
                }

                var trackingWatch = Stopwatch.StartNew();
                var directRows = await FetchTrackingRowsDirectAsync(selected, token).ConfigureAwait(true);
                trackingWatch.Stop();

                if (_currentPrintWaybill != selected[0] || token.IsCancellationRequested)
                    return false;

                var rowsByBase = directRows
                    .Where(r => !string.IsNullOrWhiteSpace(r.WaybillNo))
                    .GroupBy(r => NormalizeBaseWaybill(r.WaybillNo), StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(g => g.Key, g => CloneTrackingRow(g.First()), StringComparer.OrdinalIgnoreCase);

                foreach (string waybill in selected)
                {
                    token.ThrowIfCancellationRequested();
                    rowsByBase.TryGetValue(NormalizeBaseWaybill(waybill), out var trackingRow);
                    var guard = _printSafetyGuard.ValidateBeforePrint(waybill, siteContext, trackingRow);

                    if (_currentPrintWaybill != selected[0] || token.IsCancellationRequested)
                        return false;

                    if (!guard.CanPrint)
                    {
                        ClearPrintableState(resetCurrent: true);
                        NotifyBlocked(guard);
                        return false;
                    }

                    _lastAllowedSafetyByWaybill[NormalizeBaseWaybill(waybill)] = guard;
                    _readinessByWaybill[NormalizeBaseWaybill(waybill)] = new PrintReadinessContext
                    {
                        InputWaybillNo = NormalizeWaybill(waybill),
                        TrackingWaybillNo = NormalizeTrackingWaybillNo(waybill),
                        VerifiedAt = DateTime.Now,
                        SafetyResult = guard,
                        StatusSnapshot = FindLastSnapshot(waybill) ?? BuildPrintStatusSnapshot(waybill, NormalizeTrackingWaybillNo(waybill), trackingRow, null, PrintStatusRefreshReason.BeforePrint)
                    };
                }

                guardWatch.Stop();
                AppLogger.Info($"[PrintPerf] phase=Print usingFreshReadiness=false waybill={selected.FirstOrDefault()} preflightTrackingMs={trackingWatch.ElapsedMilliseconds} guardMs={guardWatch.ElapsedMilliseconds} totalMs={totalWatch.ElapsedMilliseconds}");
                return true;
            }
            catch (OperationCanceledException)
            {
                ClearPrintableState(resetCurrent: true);
                return false;
            }
            finally
            {
                _printGate.Release();
            }
        }

        public PrintSafetyResult GetLastAllowedPrintSafetyResult(string waybillNo)
        {
            if (string.IsNullOrWhiteSpace(waybillNo))
                return null;

            return _lastAllowedSafetyByWaybill.TryGetValue(NormalizeBaseWaybill(waybillNo), out var result)
                ? result
                : null;
        }

        public IReadOnlyList<PrintStatusSnapshot> GetLastPrintStatusSnapshots()
            => _lastPrintStatusSnapshots.ToList();

        public void QueuePostPrintRefresh(IEnumerable<string> waybills, int printType)
        {
            var requested = ExtractWaybills(string.Join(Environment.NewLine, waybills ?? Enumerable.Empty<string>()));
            if (requested.Count == 0)
                return;
            string expectedCurrent = _currentPrintWaybill;

            _ = Task.Run(async () =>
            {
                var totalWatch = Stopwatch.StartNew();
                try
                {
                    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                    var printWaybills = requested.Select(NormalizeTrackingWaybillNo)
                        .Where(x => !string.IsNullOrWhiteSpace(x))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList();

                    var trackingWatch = Stopwatch.StartNew();
                    var printInfoWatch = Stopwatch.StartNew();
                    var trackingTask = FetchTrackingRowsDirectAsync(requested, cts.Token)
                        .ContinueWith(t =>
                        {
                            trackingWatch.Stop();
                            return t.GetAwaiter().GetResult();
                        }, cts.Token, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
                    var printInfoTask = FetchPrintApprovalInfoSafeAsync(
                        printWaybills,
                        printType,
                        PrintStatusRefreshReason.AfterPrint.ToString())
                        .ContinueWith(t =>
                        {
                            printInfoWatch.Stop();
                            return t.GetAwaiter().GetResult();
                        }, cts.Token, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);

                    await Task.WhenAll(trackingTask, printInfoTask).ConfigureAwait(false);

                    if (cts.IsCancellationRequested)
                        return;

                    IReadOnlyList<TrackingRow> trackingRows = trackingTask.Result;
                    IReadOnlyList<PrintApprovalInfo> approvals = printInfoTask.Result;

                    if (!string.Equals(expectedCurrent, _currentPrintWaybill, StringComparison.OrdinalIgnoreCase))
                    {
                        AppLogger.Info($"[PrintPostRefresh] ignored stale refresh expected={expectedCurrent} current={_currentPrintWaybill}");
                        return;
                    }

                    var snapshots = BuildSnapshotsFromRows(requested, trackingRows, approvals, PrintStatusRefreshReason.AfterPrint, cts.Token);

                    void Apply()
                    {
                        if (!string.Equals(expectedCurrent, _currentPrintWaybill, StringComparison.OrdinalIgnoreCase))
                            return;

                        MergePrintApprovalInfo(approvals);
                        _lastPrintStatusSnapshots.Clear();
                        _lastPrintStatusSnapshots.AddRange(snapshots);
                        foreach (var snapshot in snapshots)
                            ApplySnapshotToPrintableRow(snapshot);
                        RebuildTable();
                        _bindingSource.ResetBindings(false);
                    }

                    if (_grid.IsHandleCreated && !_grid.IsDisposed)
                    {
                        if (_grid.InvokeRequired)
                            _grid.BeginInvoke((MethodInvoker)Apply);
                        else
                            Apply();
                    }

                    AppLogger.Info($"[PrintPerf] phase=PostRefresh waybill={requested.FirstOrDefault()} trackingMs={trackingWatch.ElapsedMilliseconds} printInfoMs={printInfoWatch.ElapsedMilliseconds} totalMs={totalWatch.ElapsedMilliseconds}");
                }
                catch (Exception ex)
                {
                    AppLogger.Warning($"[PrintPostRefresh] failed: {ex.Message}");
                }
            });
        }

        public async Task<IReadOnlyList<PrintStatusSnapshot>> RefreshPrintStatusAsync(
            IEnumerable<string> waybills,
            int printType,
            PrintStatusRefreshReason reason,
            CancellationToken cancellationToken = default)
        {
            var requested = ExtractWaybills(string.Join(Environment.NewLine, waybills ?? Enumerable.Empty<string>()));
            if (requested.Count == 0)
                return Array.Empty<PrintStatusSnapshot>();

            _printCts?.Cancel();
            _printCts?.Dispose();
            _printCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var token = _printCts.Token;
            _currentPrintWaybill = requested.FirstOrDefault() ?? string.Empty;

            await _printGate.WaitAsync(token);
            try
            {
                string safeInput = string.Join(Environment.NewLine, requested.Select(NormalizeTrackingWaybillNo));
                await _trackingService.SearchTrackingAsync(safeInput, updateMainGrid: false);
                if (token.IsCancellationRequested)
                    return Array.Empty<PrintStatusSnapshot>();

                var snapshots = await RefreshPrintStatusCoreAsync(requested, printType, reason, token);
                RebuildTable();
                _bindingSource.ResetBindings(false);
                return snapshots;
            }
            catch (OperationCanceledException)
            {
                return Array.Empty<PrintStatusSnapshot>();
            }
            finally
            {
                _printGate.Release();
            }
        }

        private async Task<IReadOnlyList<PrintStatusSnapshot>> RefreshPrintStatusCoreAsync(
            IReadOnlyList<string> inputWaybills,
            int printType,
            PrintStatusRefreshReason reason,
            CancellationToken cancellationToken)
        {
            var trackingRowsByBase = _trackingService.GetAllRows()
                .Where(r => !string.IsNullOrWhiteSpace(r.WaybillNo))
                .GroupBy(r => NormalizeBaseWaybill(r.WaybillNo), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => CloneTrackingRow(g.First()), StringComparer.OrdinalIgnoreCase);

            var trackingWaybills = inputWaybills
                .Select(NormalizeTrackingWaybillNo)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            var approvals = await FetchPrintApprovalInfoAsync(trackingWaybills, printType, reason.ToString()).ConfigureAwait(true);
            MergePrintApprovalInfo(approvals);

            var approvalByBase = approvals
                .GroupBy(x => NormalizeBaseWaybill(x.WaybillNo), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

            var snapshots = new List<PrintStatusSnapshot>();
            foreach (var input in inputWaybills)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string trackingWaybill = NormalizeTrackingWaybillNo(input);
                trackingRowsByBase.TryGetValue(trackingWaybill, out var trackingRow);
                approvalByBase.TryGetValue(trackingWaybill, out var approval);

                var snapshot = BuildPrintStatusSnapshot(input, trackingWaybill, trackingRow, approval, reason);
                snapshots.Add(snapshot);
                ApplySnapshotToPrintableRow(snapshot);
            }

            _lastPrintStatusSnapshots.Clear();
            _lastPrintStatusSnapshots.AddRange(snapshots);
            return snapshots;
        }

        private static PrintStatusSnapshot BuildPrintStatusSnapshot(
            string inputWaybillNo,
            string trackingWaybillNo,
            TrackingRow trackingRow,
            PrintApprovalInfo approval,
            PrintStatusRefreshReason reason)
        {
            var latestSafetyEvent = (trackingRow?.SafetyEvents ?? new List<TrackingSafetyEvent>())
                .Select(e => new { Event = e, Time = ParseDateTime(e.ScanTime) })
                .OrderByDescending(x => x.Time ?? DateTime.MinValue)
                .FirstOrDefault();

            return new PrintStatusSnapshot
            {
                InputWaybillNo = inputWaybillNo ?? "",
                TrackingWaybillNo = trackingWaybillNo ?? "",
                CurrentStatusName = DisplayValue(trackingRow?.ThaoTacCuoi),
                LastScanTime = ParseDateTime(trackingRow?.ThoiGianThaoTac) ?? latestSafetyEvent?.Time,
                LastScanNetworkCode = DisplayValue(latestSafetyEvent?.Event.ScanNetworkCode),
                LastTrackingContent = DisplayValue(trackingRow?.TrangThaiHienTai),
                MaDoan2 = trackingRow?.MaDoan2 ?? "",
                ApprovalStatusName = DisplayValue(approval?.StatusName),
                SenderNetworkCodeAfterPrint = DisplayValue(approval?.SenderNetworkCode),
                PrintCount = approval?.PrintCount ?? 0,
                ApplyStaffName = DisplayValue(approval?.ApplyStaffName),
                RefreshedAt = DateTime.Now,
                Source = $"FreshApi:{reason}"
            };
        }

        private void ApplySnapshotToPrintableRow(PrintStatusSnapshot snapshot)
        {
            if (snapshot == null)
                return;

            var row = _printRows.FirstOrDefault(r => MatchesBaseWaybill(r.WaybillNo, snapshot.InputWaybillNo));
            if (row == null)
                return;

            row.ThaoTacCuoi = snapshot.CurrentStatusName == "--" ? row.ThaoTacCuoi : snapshot.CurrentStatusName;
            row.ThoiGianThaoTac = snapshot.LastScanTime?.ToString("yyyy-MM-dd HH:mm:ss") ?? row.ThoiGianThaoTac;
            row.MaDoan2 = string.IsNullOrWhiteSpace(snapshot.MaDoan2) ? row.MaDoan2 : snapshot.MaDoan2;
            row.PrintApprovalStatusName = snapshot.ApprovalStatusName == "--" ? "" : snapshot.ApprovalStatusName;
            row.PrintSenderNetworkCode = snapshot.SenderNetworkCodeAfterPrint == "--" ? "" : snapshot.SenderNetworkCodeAfterPrint;
            row.PrintApprovalPrintCount = snapshot.PrintCount;
            row.PrintApplyStaffName = snapshot.ApplyStaffName == "--" ? "" : snapshot.ApplyStaffName;
        }

        public async Task<IReadOnlyList<PrintApprovalInfo>> RefreshPrintApprovalInfoAsync(IEnumerable<string> waybills, int printType, string phase)
        {
            var requested = ExtractWaybills(string.Join(Environment.NewLine, waybills ?? Enumerable.Empty<string>()))
                .Select(NormalizeTrackingWaybillNo)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (requested.Count == 0)
                return Array.Empty<PrintApprovalInfo>();

            try
            {
                var records = await FetchPrintApprovalInfoAsync(requested, printType, phase).ConfigureAwait(true);
                if (records.Count > 0)
                {
                    MergePrintApprovalInfo(records);
                    RebuildTable();
                    _bindingSource.ResetBindings(false);
                }

                return records;
            }
            catch (Exception ex)
            {
                AppLogger.Warning($"[PrintApproval] phase={phase} failed: {ex.Message}");
                return Array.Empty<PrintApprovalInfo>();
            }
        }

        private async Task<IReadOnlyList<PrintApprovalInfo>> FetchPrintApprovalInfoAsync(IReadOnlyList<string> waybills, int printType, string phase)
        {
            var payload = new Dictionary<string, object>
            {
                ["current"] = 1,
                ["size"] = Math.Max(20, waybills.Count),
                ["pringFlag"] = 0,
                ["applyNetworkId"] = 5165,
                ["waybillIds"] = waybills,
                ["applyTimeFrom"] = DateTime.Now.Date.ToString("yyyy-MM-dd 00:00:00"),
                ["applyTimeTo"] = DateTime.Now.Date.ToString("yyyy-MM-dd 23:59:59"),
                ["cancelOrderType"] = 3,
                ["pringType"] = printType,
                ["countryId"] = "1"
            };

            string jsonPayload = JsonSerializer.Serialize(payload);
            string[] endpoints =
            {
                "operatingplatform/rebackTransferExpress/pringListPage",
                "operatingplatform/rebackTransferExpress/listPage"
            };

            foreach (string relativeEndpoint in endpoints)
            {
                string apiUrl = AppConfig.Current.BuildJmsApiUrl(relativeEndpoint);

                using var response = await JmsApiClient.PostJsonAsync(apiUrl, jsonPayload, routeName: "trackingExpress").ConfigureAwait(false);
                if (response == null)
                {
                    AppLogger.Warning($"[PrintApproval] phase={phase} endpoint={relativeEndpoint} failed: empty response");
                    continue;
                }

                string rawJson = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

                if (!response.IsSuccessStatusCode || string.IsNullOrWhiteSpace(rawJson))
                {
                    AppLogger.Warning($"[PrintApproval] phase={phase} endpoint={relativeEndpoint} http={(int)response.StatusCode} bodyLength={rawJson?.Length ?? 0} preview={Preview(rawJson, 260)}");
                    continue;
                }

                var records = ParsePrintApprovalInfo(rawJson, waybills);
                if (records.Count > 0)
                    return records;
            }

            return Array.Empty<PrintApprovalInfo>();
        }

        private static IReadOnlyList<PrintApprovalInfo> ParsePrintApprovalInfo(string rawJson, IReadOnlyList<string> requestedWaybills)
        {
            var requested = requestedWaybills
                .Select(NormalizeBaseWaybill)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var records = new List<PrintApprovalInfo>();

            using var doc = JsonDocument.Parse(rawJson);
            JsonElement root = doc.RootElement;
            string code = GetRawText(root, "code");
            if (!string.IsNullOrWhiteSpace(code) && code != "1" && code != "0" && code != "200")
                return records;

            if (!root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object)
                return records;

            JsonElement list = default;
            bool hasList = data.TryGetProperty("records", out list) && list.ValueKind == JsonValueKind.Array;
            if (!hasList)
                hasList = data.TryGetProperty("list", out list) && list.ValueKind == JsonValueKind.Array;
            if (!hasList)
                return records;

            foreach (var item in list.EnumerateArray())
            {
                string waybillNo = GetRawText(item, "waybillNo");
                if (string.IsNullOrWhiteSpace(waybillNo))
                    waybillNo = GetRawText(item, "billCode");

                string normalized = NormalizeBaseWaybill(waybillNo);
                if (requested.Count > 0 && !requested.Contains(normalized))
                    continue;

                records.Add(new PrintApprovalInfo
                {
                    WaybillNo = normalized,
                    StatusName = DisplayValue(GetRawText(item, "statusName")),
                    SenderNetworkCode = DisplayValue(FirstText(item, "senderNetworkCode", "newSenderNetworkCode")),
                    PrintCount = GetNullableInt(item, "printCount"),
                    ApplyStaffName = DisplayValue(GetRawText(item, "applyStaffName")),
                    FetchedAt = DateTime.Now
                });
            }

            return records;
        }

        private void MergePrintApprovalInfo(IReadOnlyList<PrintApprovalInfo> records)
        {
            if (records == null || records.Count == 0)
                return;

            var byWaybill = records
                .GroupBy(r => NormalizeBaseWaybill(r.WaybillNo), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

            foreach (var row in _printRows)
            {
                if (row == null) continue;
                if (!byWaybill.TryGetValue(NormalizeBaseWaybill(row.WaybillNo), out var info))
                    continue;

                row.PrintApprovalStatusName = info.StatusName == "-" ? "" : info.StatusName;
                row.PrintSenderNetworkCode = info.SenderNetworkCode == "-" ? "" : info.SenderNetworkCode;
                row.PrintApprovalPrintCount = info.PrintCount;
                row.PrintApplyStaffName = info.ApplyStaffName == "-" ? "" : info.ApplyStaffName;
            }
        }

        private static string ResolveApprovalStatus(TrackingRow row)
            => DisplayValue(FirstNonEmpty(row?.PrintApprovalStatusName, row?.RebackStatus));

        private static int ResolvePrintCount(TrackingRow row, int defaultValue = 0)
        {
            if (row?.PrintApprovalPrintCount.HasValue == true)
                return row.PrintApprovalPrintCount.Value;

            if (row != null && row.PrintCount > 0)
                return row.PrintCount;

            return defaultValue;
        }

        private static string ResolveSenderNetworkCode(TrackingRow row)
            => DisplayValue(FirstNonEmpty(row?.PrintSenderNetworkCode, row?.NewTerminalDispatchCode, row?.MaDoanFull, row?.MaDoan2));

        private static string ResolveApplyStaffName(TrackingRow row, string fallback)
            => DisplayValue(FirstNonEmpty(row?.PrintApplyStaffName, fallback));

        private static string FirstNonEmpty(params string[] values)
        {
            foreach (var value in values)
            {
                if (!string.IsNullOrWhiteSpace(value))
                    return value.Trim();
            }

            return "";
        }

        private static string DisplayValue(string value)
            => string.IsNullOrWhiteSpace(value) ? "-" : value.Trim();

        private static string GetRawText(JsonElement item, string name)
        {
            if (!item.TryGetProperty(name, out var prop))
                return "";

            return prop.ValueKind switch
            {
                JsonValueKind.String => prop.GetString()?.Trim() ?? "",
                JsonValueKind.Number => prop.ToString(),
                JsonValueKind.True => "true",
                JsonValueKind.False => "false",
                _ => ""
            };
        }

        private static string FirstText(JsonElement item, params string[] names)
        {
            foreach (var name in names)
            {
                string value = GetRawText(item, name);
                if (!string.IsNullOrWhiteSpace(value))
                    return value;
            }

            return "";
        }

        private static int? GetNullableInt(JsonElement item, string name)
        {
            if (!item.TryGetProperty(name, out var prop))
                return null;

            if (prop.ValueKind == JsonValueKind.Number && prop.TryGetInt32(out int number))
                return number;

            if (prop.ValueKind == JsonValueKind.String && int.TryParse(prop.GetString(), out int parsed))
                return parsed;

            return null;
        }

        private static string Preview(string text, int max)
        {
            if (string.IsNullOrEmpty(text)) return "";
            return text.Length <= max ? text : text.Substring(0, max) + "...";
        }

        private static DateTime? ParseDateTime(string value)
        {
            if (DateTime.TryParse(value, out var parsed))
                return parsed;

            return null;
        }

        public void SelectAll(bool isChecked)
        {
            _grid.EndEdit();
            foreach (DataRow row in _displayTable.Rows)
            {
                row["Select"] = isChecked;
            }
            _displayTable.AcceptChanges();
            UpdateStatsAndVisibility();
        }

        public void ClearSelection()
        {
            if (_grid.InvokeRequired)
            {
                _grid.Invoke(new Action(() => 
                {
                    _grid.ClearSelection();
                    _grid.CurrentCell = null;
                }));
            }
            else
            {
                _grid.ClearSelection();
                _grid.CurrentCell = null;
            }
            AppLogger.Info("PRINT_SELECTION_CLEAR_DONE");
            try { OnPrintSelectionCleared?.Invoke(); } catch { }
        }

        public List<string> GetSelectedWaybills()
        {
            _grid.EndEdit();
            var list = new List<string>();
            foreach (DataRow row in _displayTable.Rows)
            {
                if (row["Select"] != DBNull.Value && (bool)row["Select"])
                {
                    var wb = row["Mã vận đơn"]?.ToString();
                    if (!string.IsNullOrEmpty(wb)) list.Add(wb);
                }
            }
            return list;
        }

        public PrintMode CurrentMode => _currentMode;

        private void ClearPrintableState(bool resetCurrent = false)
        {
            if (resetCurrent)
                _currentPrintWaybill = string.Empty;

            _printRows.Clear();
            _displayTable.Rows.Clear();
            _lastAllowedSafetyByWaybill.Clear();
            _lastPrintStatusSnapshots.Clear();
            _bindingSource.ResetBindings(false);
            if (_grid.InvokeRequired)
            {
                _grid.Invoke(new Action(() =>
                {
                    _grid.ClearSelection();
                    _grid.CurrentCell = null;
                }));
            }
            else
            {
                _grid.ClearSelection();
                _grid.CurrentCell = null;
            }
            UpdateStatsAndVisibility();
        }

        private void NotifyBlocked(PrintSafetyResult result)
        {
            try { OnPrintSafetyBlocked?.Invoke(result); }
            catch { }
        }

        private bool TryUseFreshReadiness(IReadOnlyList<string> selected, out long readinessAgeMs)
        {
            readinessAgeMs = 0;
            if (selected == null || selected.Count == 0)
                return false;

            long maxAge = 0;
            foreach (var waybill in selected)
            {
                string inputWaybill = NormalizeWaybill(waybill);
                string trackingWaybill = NormalizeTrackingWaybillNo(waybill);
                if (!_readinessByWaybill.TryGetValue(NormalizeBaseWaybill(waybill), out var context))
                    return false;

                if (!context.CanPrintFast(inputWaybill, trackingWaybill, PrintReadinessTtl))
                    return false;

                if (!_printRows.Any(r => MatchesBaseWaybill(r.WaybillNo, waybill)))
                    return false;

                maxAge = Math.Max(maxAge, (long)(DateTime.Now - context.VerifiedAt).TotalMilliseconds);
                if (context.SafetyResult != null)
                    _lastAllowedSafetyByWaybill[NormalizeBaseWaybill(waybill)] = context.SafetyResult;
            }

            readinessAgeMs = maxAge;
            return true;
        }

        private PrintStatusSnapshot FindLastSnapshot(string waybillNo)
        {
            string normalized = NormalizeBaseWaybill(waybillNo);
            return _lastPrintStatusSnapshots.FirstOrDefault(x =>
                string.Equals(NormalizeBaseWaybill(x.InputWaybillNo), normalized, StringComparison.OrdinalIgnoreCase)
                || string.Equals(NormalizeBaseWaybill(x.TrackingWaybillNo), normalized, StringComparison.OrdinalIgnoreCase));
        }

        private async Task<IReadOnlyList<PrintApprovalInfo>> FetchPrintApprovalInfoSafeAsync(
            IReadOnlyList<string> waybills,
            int printType,
            string phase)
        {
            try
            {
                return await FetchPrintApprovalInfoAsync(waybills, printType, phase).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                AppLogger.Warning($"[PrintApproval] phase={phase} failed: {ex.Message}");
                return Array.Empty<PrintApprovalInfo>();
            }
        }

        private async Task<IReadOnlyList<TrackingRow>> FetchTrackingRowsDirectAsync(
            IReadOnlyList<string> inputWaybills,
            CancellationToken cancellationToken)
        {
            var trackingWaybills = inputWaybills
                .Select(NormalizeTrackingWaybillNo)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (trackingWaybills.Count == 0)
                return Array.Empty<TrackingRow>();

            string apiUrl = AppConfig.Current.BuildJmsApiUrl("operatingplatform/podTracking/inner/query/keywordList");
            string payload = JsonSerializer.Serialize(new
            {
                keywordList = trackingWaybills,
                trackingTypeEnum = "WAYBILL",
                countryId = "1"
            });

            using var response = await JmsApiClient.PostJsonAsync(apiUrl, payload, routeName: "trackingExpress", ct: cancellationToken).ConfigureAwait(false);
            if (response == null || !response.IsSuccessStatusCode)
                return Array.Empty<TrackingRow>();

            string rawJson = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(rawJson))
                return Array.Empty<TrackingRow>();

            return ParseTrackingRowsDirect(rawJson, trackingWaybills);
        }

        private static bool IsNoiseScan(string scanTypeName)
        {
            string type = scanTypeName ?? string.Empty;
            return type.Contains("Kiểm tra hàng tồn kho", StringComparison.OrdinalIgnoreCase)
                || type.Contains("Lịch sử cuộc gọi", StringComparison.OrdinalIgnoreCase)
                || type.Contains("cuộc gọi-phát", StringComparison.OrdinalIgnoreCase);
        }

        private static IReadOnlyList<TrackingRow> ParseTrackingRowsDirect(string rawJson, IReadOnlyList<string> trackingWaybills)
        {
            var requested = trackingWaybills
                .Select(NormalizeBaseWaybill)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var rows = new List<TrackingRow>();

            using var doc = JsonDocument.Parse(rawJson);
            if (!doc.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
                return rows;

            foreach (var item in data.EnumerateArray())
            {
                string waybill = FirstText(item, "keyword", "billCode", "waybillNo");
                string normalizedWaybill = NormalizeBaseWaybill(waybill);
                if (requested.Count > 0 && !requested.Contains(normalizedWaybill))
                    continue;

                if (!item.TryGetProperty("details", out var details) || details.ValueKind != JsonValueKind.Array)
                    continue;

                var detailRows = details.EnumerateArray().ToList();
                var orderedByTime = detailRows
                    .Select(d => new { Detail = d, Time = ParseDateTime(FirstText(d, "uploadTime", "scanTime")) })
                    .OrderByDescending(x => x.Time ?? DateTime.MinValue)
                    .ToList();
                // Bỏ qua thao tác nhiễu (kiểm kho, lịch sử cuộc gọi); nếu toàn nhiễu thì dùng bản mới nhất làm dự phòng.
                var latest = (orderedByTime.FirstOrDefault(x => !IsNoiseScan(GetRawText(x.Detail, "scanTypeName")))
                              ?? orderedByTime.FirstOrDefault())?.Detail;

                var row = new TrackingRow
                {
                    WaybillNo = normalizedWaybill,
                    ThaoTacCuoi = latest.HasValue ? GetRawText(latest.Value, "scanTypeName") : "",
                    TrangThaiHienTai = latest.HasValue ? GetRawText(latest.Value, "waybillTrackingContent") : "",
                    ThoiGianThaoTac = latest.HasValue ? FirstText(latest.Value, "uploadTime", "scanTime") : "",
                    BuuCucThaoTac = latest.HasValue ? GetRawText(latest.Value, "scanNetworkName") : "",
                    NguoiThaoTac = latest.HasValue ? GetRawText(latest.Value, "scanByName") : "",
                    SafetyEvents = detailRows.Select(d => new TrackingSafetyEvent
                    {
                        WaybillNo = FirstText(d, "waybillNo", "billCode"),
                        BillCode = FirstText(d, "billCode", "waybillNo"),
                        ScanTime = GetRawText(d, "scanTime"),
                        ScanNetworkCode = GetRawText(d, "scanNetworkCode")
                    }).ToList()
                };

                rows.Add(row);
            }

            return rows;
        }

        private IReadOnlyList<PrintStatusSnapshot> BuildSnapshotsFromCurrentTracking(
            IReadOnlyList<string> inputWaybills,
            IReadOnlyList<PrintApprovalInfo> approvals,
            PrintStatusRefreshReason reason,
            CancellationToken cancellationToken)
        {
            var trackingRowsByBase = _trackingService.GetAllRows()
                .Where(r => !string.IsNullOrWhiteSpace(r.WaybillNo))
                .GroupBy(r => NormalizeBaseWaybill(r.WaybillNo), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => CloneTrackingRow(g.First()), StringComparer.OrdinalIgnoreCase);

            return BuildSnapshotsFromRows(inputWaybills, trackingRowsByBase.Values.ToList(), approvals, reason, cancellationToken);
        }

        private static IReadOnlyList<PrintStatusSnapshot> BuildSnapshotsFromRows(
            IReadOnlyList<string> inputWaybills,
            IReadOnlyList<TrackingRow> trackingRows,
            IReadOnlyList<PrintApprovalInfo> approvals,
            PrintStatusRefreshReason reason,
            CancellationToken cancellationToken)
        {
            var trackingRowsByBase = (trackingRows ?? Array.Empty<TrackingRow>())
                .Where(r => !string.IsNullOrWhiteSpace(r.WaybillNo))
                .GroupBy(r => NormalizeBaseWaybill(r.WaybillNo), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => CloneTrackingRow(g.First()), StringComparer.OrdinalIgnoreCase);

            var approvalByBase = (approvals ?? Array.Empty<PrintApprovalInfo>())
                .GroupBy(x => NormalizeBaseWaybill(x.WaybillNo), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

            var snapshots = new List<PrintStatusSnapshot>();
            foreach (var input in inputWaybills)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string trackingWaybill = NormalizeTrackingWaybillNo(input);
                trackingRowsByBase.TryGetValue(trackingWaybill, out var trackingRow);
                approvalByBase.TryGetValue(trackingWaybill, out var approval);
                snapshots.Add(BuildPrintStatusSnapshot(input, trackingWaybill, trackingRow, approval, reason));
            }

            return snapshots;
        }

        private static List<string> ExtractWaybills(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return new List<string>();
            return text.Split(new[] { '\r', '\n', ',', ';', '|', ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(x => x.Trim().ToUpperInvariant())
                .Where(x => x.Length > 5)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static string NormalizeWaybill(string waybill)
            => (waybill ?? string.Empty).Trim().ToUpperInvariant();

        private static string NormalizeBaseWaybill(string waybill)
        {
            string normalized = NormalizeWaybill(waybill);
            int hyphenIndex = normalized.IndexOf('-');
            return hyphenIndex > 0 ? normalized.Substring(0, hyphenIndex) : normalized;
        }

        private static string NormalizeTrackingWaybillNo(string input)
            => NormalizeBaseWaybill(input);

        private static bool MatchesBaseWaybill(string left, string right)
            => string.Equals(NormalizeBaseWaybill(left), NormalizeBaseWaybill(right), StringComparison.OrdinalIgnoreCase);

        private static TrackingRow CloneTrackingRow(TrackingRow r)
        {
            if (r == null) return null;
            return new TrackingRow
            {
                WaybillNo = r.WaybillNo,
                TrangThaiHienTai = r.TrangThaiHienTai,
                ThaoTacCuoi = r.ThaoTacCuoi,
                ThoiGianThaoTac = r.ThoiGianThaoTac,
                ThoiGianYeuCauPhatLai = r.ThoiGianYeuCauPhatLai,
                NhanVienKienVanDe = r.NhanVienKienVanDe,
                NguyenNhanKienVanDe = r.NguyenNhanKienVanDe,
                BuuCucThaoTac = r.BuuCucThaoTac,
                NguoiThaoTac = r.NguoiThaoTac,
                DauChuyenHoan = r.DauChuyenHoan,
                DiaChiNhanHang = r.DiaChiNhanHang,
                Phuong = r.Phuong,
                NoiDungHangHoa = r.NoiDungHangHoa,
                CODThucTe = r.CODThucTe,
                PTTT = r.PTTT,
                NhanVienNhanHang = r.NhanVienNhanHang,
                DiaChiLayHang = r.DiaChiLayHang,
                ThoiGianNhanHang = r.ThoiGianNhanHang,
                TenNguoiGui = r.TenNguoiGui,
                TrongLuong = r.TrongLuong,
                MaDoanFull = r.MaDoanFull,
                MaDoan1 = r.MaDoan1,
                MaDoan2 = r.MaDoan2,
                MaDoan3 = r.MaDoan3,
                RebackStatus = r.RebackStatus,
                PrintCount = r.PrintCount,
                NewTerminalDispatchCode = r.NewTerminalDispatchCode,
                InHoanScanTime = r.InHoanScanTime,
                PrintApprovalStatusName = r.PrintApprovalStatusName,
                PrintSenderNetworkCode = r.PrintSenderNetworkCode,
                PrintApprovalPrintCount = r.PrintApprovalPrintCount,
                PrintApplyStaffName = r.PrintApplyStaffName,
                SafetyEvents = r.SafetyEvents?
                    .Select(e => new TrackingSafetyEvent
                    {
                        WaybillNo = e.WaybillNo,
                        BillCode = e.BillCode,
                        ScanTime = e.ScanTime,
                        ScanNetworkCode = e.ScanNetworkCode
                    })
                    .ToList() ?? new List<TrackingSafetyEvent>()
            };
        }
    }
}

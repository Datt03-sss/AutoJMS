using AutoJMS.Data;
using AutoUpdaterDotNET;
using DocumentFormat.OpenXml.Drawing.Charts;
using DocumentFormat.OpenXml.Spreadsheet;
using Microsoft.Web.WebView2.Core;
using Sunny.UI;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Controls;
using System.Windows.Forms;
using static System.Windows.Forms.VisualStyles.VisualStyleElement;
using DataTable = System.Data.DataTable;
using Size = System.Drawing.Size;

namespace AutoJMS
{
    public partial class Main : UIForm
    {
        private static string JmsHomeUrl => RuntimeConfigManager.Current.JmsBaseUrl.TrimEnd('/');
        private static string AppsScriptUrl => RuntimeConfigManager.Current.AppsScriptUrl;

        //private const string FIREBASE_URL = "https://keyauthjms-default-rtdb.asia-southeast1.firebasedatabase.app/";
        //private const string DATABASE_SECRET = "29m37qye6O3YvtBeWuwDf26cUINV6Zyk7EZVdALm";

        private AppSettings _settings;
        public static string CapturedAuthToken = "";
        public static WaybillTrackingService _trackingService;
        private DkchManager _dkchManager;
        private PrintService _printService;
        public ZaloChatService _zaloChatService;
        private System.Windows.Forms.Timer _queueTimer;
        private System.Windows.Forms.Timer _dataReloadTimer;   // Timer reload dữ liệu tabChat + tabDash
        private List<Reminder> _trackedReminders = new List<Reminder>();   // Nguồn dữ liệu chính cho tabChat
        private bool _isUpdatingUI = false;
        private BindingSource _chatBindingSource;
        private BindingSource _dashBindingSource = new BindingSource();

        public bool _isZaloLoaded = false;
        private bool _isDkchNeedReload = false;
        private bool _isHomeNeedReload = false;
        private readonly object _authTokenLock = new object();
        private CancellationTokenSource _authTokenSaveCts;
        private System.Windows.Forms.Timer _dkchUiStateTimer;
        private bool _isDkchStarting = false;
        private static readonly Regex DkchWaybillRegex = new Regex("^[A-Za-z0-9]{1,20}$", RegexOptions.Compiled);
        private const string CHROME_USER_AGENT = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36";
        public Main()
        {
            InitializeComponent();
            this.AutoScaleMode = AutoScaleMode.Dpi;
            this.AutoScaleDimensions = new SizeF(96F, 96F);
            this.MinimumSize = new Size(1366, 768);
            _settings = SettingsManager.Load();

            // UI styling
            tabDKCH_inputNewBill.Font = new System.Drawing.Font("Segoe UI Semibold", 12f, System.Drawing.FontStyle.Bold);
            tabDKCH_inputNewBill.WordWrap = false;
            tabDKCH_newBillDone.WordWrap = false;

            string sharedFolder = Path.Combine(Application.StartupPath, "SharedBrowserData");
            var sharedProps = new Microsoft.Web.WebView2.WinForms.CoreWebView2CreationProperties()
            {
                UserDataFolder = sharedFolder
            };

            // TabChat: BindingSource + DataTable (chỉ 4 cột cố định)
            _chatBindingSource = LocalTrackingManager.Instance.ChatBindingSource;
            tabChat_dataGrid.DataSource = _chatBindingSource;
            tabChat_dataGrid.AutoGenerateColumns = false;
            tabChat_dataGrid.AllowUserToAddRows = false;
            tabChat_dataGrid.AllowUserToDeleteRows = false;
            tabChat_dataGrid.AllowUserToResizeRows = false;
            tabChat_dataGrid.ReadOnly = true;
            tabChat_dataGrid.EditMode = DataGridViewEditMode.EditProgrammatically;
            tabChat_dataGrid.RowHeadersVisible = false;
            tabChat_dataGrid.SelectionMode = DataGridViewSelectionMode.CellSelect;
            tabChat_dataGrid.MultiSelect = true;
            tabChat_dataGrid.ClipboardCopyMode = DataGridViewClipboardCopyMode.EnableWithoutHeaderText;
            tabChat_dataGrid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.DisplayedCells;
            tabChat_dataGrid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize;
            tabChat_dataGrid.Columns.Clear();
            tabChat_dataGrid.Columns.AddRange(new DataGridViewColumn[]
            {
                new DataGridViewTextBoxColumn { Name = "maDon", HeaderText = "Mã vận đơn", DataPropertyName = "Mã vận đơn",AutoSizeMode = DataGridViewAutoSizeColumnMode.AllCells, SortMode = DataGridViewColumnSortMode.Programmatic },
                new DataGridViewTextBoxColumn { Name = "nhanVien", HeaderText = "Tên nhân viên", DataPropertyName = "Tên nhân viên", SortMode = DataGridViewColumnSortMode.Programmatic },
                new DataGridViewTextBoxColumn { Name = "trangThai", HeaderText = "Trạng thái hiện tại", DataPropertyName = "Trạng thái hiện tại", SortMode = DataGridViewColumnSortMode.Programmatic },
                new DataGridViewTextBoxColumn { Name = "soLanNhac", HeaderText = "Số lần nhắc", DataPropertyName = "Số lần nhắc", SortMode = DataGridViewColumnSortMode.Programmatic }
            });
            tabChat_dataGrid.ColumnHeaderMouseClick += tabChat_dataGrid_ColumnHeaderMouseClick;
            tabChat_timeCheck.SelectedIndexChanged += (s, e) => StartPeriodicDataReload();
            // TabDash: BindingSource/DataTable
            SetupDashGridBinding();
            tabDash_dataGrid.AutoGenerateColumns = false;

            // Sự kiện cập nhật từ LocalTrackingManager (chỉ dành cho tabDash)
            LocalTrackingManager.Instance.OnDataUpdated += () =>
            {
                if (this.InvokeRequired)
                    this.Invoke((Action)(() =>
                    {
                        RefreshDashboardUI(); // cho tabDash
                        if (tabControl.SelectedTab == tabChat)
                        {
                            PopulateStatusSelect();
                            ApplyFilterAndSort();
                        }
                    }));
                else
                {
                    RefreshDashboardUI();
                    if (tabControl.SelectedTab == tabChat)
                    {
                        PopulateStatusSelect();
                        ApplyFilterAndSort();
                    }
                }
            };

            // Sự kiện lọc cho tabChat
            tabChat_statusSelect.SelectedIndexChanged += tabChat_statusSelect_SelectedIndexChanged;
            tabChat_timeSelect.SelectedIndexChanged += (s, e) => UpdateTrackingInterval();

            // Double buffering cho tabTracking
            var doubleBufferPropertyInfo = tabTracking_dataView.GetType().GetProperty("DoubleBuffered", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            doubleBufferPropertyInfo?.SetValue(tabTracking_dataView, true, null);

            // WebView
            tabHome_webView.CreationProperties = sharedProps;
            tabDKCH_webView.CreationProperties = sharedProps;
            tabDKCH_sheetName.SelectedItem = _settings.DefaultSheet;
            tabDKCH_useSheet.Active = _settings.UseSheetByDefault;
            tabDKCH_numRow.Value = _settings.DefaultRowCount;

            // Sự kiện bàn phím
            tabTracking_inputWaybill.KeyDown += tabTracking_inputWaybill_KeyDown;
            tabPrint_btnSelectAll.CheckedChanged += tabPrint_btnSelectAll_CheckedChanged;
            tabHome_webView.NavigationCompleted += tabHome_WebView_NavigationCompleted;

            // Timer xử lý lệnh RUN
            _queueTimer = new System.Windows.Forms.Timer();
            _queueTimer.Interval = 30000;
            _queueTimer.Tick += async (s, e) => await ProcessDirectTrackingAsync();
            _queueTimer.Start();

            tabChat_timeSelect.DropDownStyle = UIDropDownStyle.DropDown;

            // DKCH buttons
            tabDKCH_btnDKCH1.Visible = true;
            tabDKCH_btnDKCH2.Visible = true;
            tabDKCH_btnStop.Visible = false;
            tabDKCH_btnDKCH1.Enabled = false;
            tabDKCH_btnDKCH2.Enabled = false;
            UpdateDkchButtonsByState(false);

            _dkchUiStateTimer = new System.Windows.Forms.Timer();
            _dkchUiStateTimer.Interval = 300;
            _dkchUiStateTimer.Tick += (s, e) => UpdateDkchButtonsByState((_dkchManager?.IsRunning == true) || _isDkchStarting);
            _dkchUiStateTimer.Start();

            CheckForIllegalCrossThreadCalls = false;
            this.KeyPreview = true;

            _dkchManager = new DkchManager();
            _dkchManager.OnSaveCountChanged += (count) => tabDKCH_countSave.Text = count.ToString();
            _dkchManager.OnTrackingHistoryChanged += (history) =>
            {
                // Đảm bảo thao tác vẽ màu luôn được đẩy về luồng chính (Main Thread)
                if (this.InvokeRequired)
                {
                    this.Invoke(new Action(() => FormatNowTracking(history ?? "Không có dữ liệu")));
                }
                else
                {
                    FormatNowTracking(history ?? "Không có dữ liệu");
                }
            };
            _dkchManager.OnWaybillCompleted += AppendDoneWaybill;
        }


        private void UpdateDkchButtonsByState(bool isRunning)
        {
            if (tabDKCH_btnDKCH1 == null || tabDKCH_btnDKCH2 == null || tabDKCH_btnStop == null) return;

            tabDKCH_btnDKCH1.Visible = !isRunning;
            tabDKCH_btnDKCH2.Visible = !isRunning;
            tabDKCH_btnDKCH1.Enabled = !isRunning;
            tabDKCH_btnDKCH2.Enabled = !isRunning;
            tabDKCH_btnStop.Visible = isRunning;
            tabDKCH_btnStop.Enabled = isRunning;

            if (isRunning)
                tabDKCH_btnStop.BringToFront();
        }
        // ==================== TAB DKCH - XỬ LÝ NHẬP MÃ ====================

        private void tabDKCH_inputNewBill_KeyPress(object sender, KeyPressEventArgs e)
        {
            if (e.KeyChar == (char)Keys.Enter)
            {
                e.Handled = true;

                var allLines = tabDKCH_inputNewBill.Text
                    .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => x.Trim())
                    .Where(x => !string.IsNullOrWhiteSpace(x) && IsValidDkchWaybill(x))
                    .ToList();

                if (allLines.Count == 0) return;

                // 1. Xóa sạch inputNewBill
                tabDKCH_inputNewBill.Text = "";

                // 2. Hiển thị toàn bộ tại newBillDone + chạy auto
                foreach (var waybill in allLines)
                {
                    AppendToNewBillDone(waybill);
                    _dkchManager.AddPriorityWaybill(waybill);
                }
            }
        }

        // Thêm mã vào newBillDone (hiển thị danh sách đang chạy)
        private void AppendToNewBillDone(string waybill)
        {
            if (tabDKCH_newBillDone == null || tabDKCH_newBillDone.IsDisposed) return;

            if (tabDKCH_newBillDone.InvokeRequired)
            {
                tabDKCH_newBillDone.Invoke(new Action(() => AppendToNewBillDone(waybill)));
                return;
            }

            var currentLines = tabDKCH_newBillDone.Text
                .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(x => x.Trim())
                .ToList();

            if (!currentLines.Any(x => string.Equals(x, waybill, StringComparison.OrdinalIgnoreCase)))
            {
                currentLines.Add(waybill);
            }

            tabDKCH_newBillDone.Text = string.Join(Environment.NewLine, currentLines);
            tabDKCH_newBillDone.SelectionStart = tabDKCH_newBillDone.TextLength;
            tabDKCH_newBillDone.ScrollToCaret();
        }

        // Xóa mã đã chạy xong khỏi newBillDone (gọi từ OnWaybillCompleted)
        private void AppendDoneWaybill(string waybill)
        {
            if (tabDKCH_newBillDone == null || tabDKCH_newBillDone.IsDisposed) return;

            if (tabDKCH_newBillDone.InvokeRequired)
            {
                tabDKCH_newBillDone.Invoke(new Action(() => AppendDoneWaybill(waybill)));
                return;
            }

            var currentLines = tabDKCH_newBillDone.Text
                .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(x => x.Trim())
                .Where(x => !string.Equals(x, waybill, StringComparison.OrdinalIgnoreCase))
                .ToList();

            tabDKCH_newBillDone.Text = string.Join(Environment.NewLine, currentLines);
            tabDKCH_newBillDone.SelectionStart = tabDKCH_newBillDone.TextLength;
            tabDKCH_newBillDone.ScrollToCaret();
        }

        

        private static bool IsValidDkchWaybill(string waybill)
        {
            if (string.IsNullOrWhiteSpace(waybill)) return false;
            return DkchWaybillRegex.IsMatch(waybill.Trim());
        }
        

        // ========== CẤU HÌNH TAB DASH THEO BINDING ==========
        private void SetupDashGridBinding()
        {
            var dgv = tabDash_dataGrid;
            dgv.VirtualMode = false;
            dgv.AutoGenerateColumns = false;
            dgv.AllowUserToAddRows = false;
            dgv.AllowUserToDeleteRows = false;
            dgv.ReadOnly = true;
            dgv.EditMode = DataGridViewEditMode.EditProgrammatically;
            dgv.RowHeadersVisible = false;
            dgv.SelectionMode = DataGridViewSelectionMode.CellSelect;
            dgv.MultiSelect = true;
            dgv.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.DisplayedCells;
            dgv.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize;
            dgv.DataSource = _dashBindingSource;
            dgv.ColumnHeaderMouseClick += tabDash_dataGrid_ColumnHeaderMouseClick;

            dgv.Columns.Clear();
            dgv.Columns.AddRange(new DataGridViewColumn[]
            {
                new DataGridViewTextBoxColumn { Name = "Mã vận đơn", HeaderText = "Mã vận đơn", DataPropertyName = "WaybillNo", SortMode = DataGridViewColumnSortMode.Programmatic },
                new DataGridViewTextBoxColumn { Name = "Trạng thái hiện tại", HeaderText = "Trạng thái hiện tại", DataPropertyName = "TrangThaiHienTai", SortMode = DataGridViewColumnSortMode.Programmatic },
                new DataGridViewTextBoxColumn { Name = "Loại quét kiện cuối", HeaderText = "Loại quét kiện cuối", DataPropertyName = "ThaoTacCuoi", SortMode = DataGridViewColumnSortMode.Programmatic },
                new DataGridViewTextBoxColumn { Name = "Tên nhân viên", HeaderText = "Tên nhân viên", DataPropertyName = "NguoiThaoTac", SortMode = DataGridViewColumnSortMode.Programmatic },
                new DataGridViewTextBoxColumn { Name = "Thời gian thao tác", HeaderText = "Thời gian thao tác", DataPropertyName = "ThoiGianThaoTac", SortMode = DataGridViewColumnSortMode.Programmatic },
                new DataGridViewTextBoxColumn { Name = "Thời gian yêu cầu phát lại", HeaderText = "Thời gian yêu cầu phát lại", DataPropertyName = "ThoiGianYeuCauPhatLai", SortMode = DataGridViewColumnSortMode.Programmatic },
                new DataGridViewTextBoxColumn { Name = "Số lần nhắc", HeaderText = "Số lần nhắc", DataPropertyName = "SoLanNhac", SortMode = DataGridViewColumnSortMode.Programmatic },
                new DataGridViewTextBoxColumn { Name = "Thời gian nhắc gần nhất", HeaderText = "Thời gian nhắc gần nhất", DataPropertyName = "ThoiGianNhacGanNhat", SortMode = DataGridViewColumnSortMode.Programmatic },
                new DataGridViewTextBoxColumn { Name = "Số lần đăng ký chuyển hoàn", HeaderText = "Số lần đăng ký chuyển hoàn", DataPropertyName = "SoLanDangKyChuyenHoan", SortMode = DataGridViewColumnSortMode.Programmatic },
                new DataGridViewTextBoxColumn { Name = "Số lần giao lại hàng", HeaderText = "Số lần giao lại hàng", DataPropertyName = "SoLanGiaoLaiHang", SortMode = DataGridViewColumnSortMode.Programmatic },
                new DataGridViewTextBoxColumn { Name = "Nguồn đơn đặt", HeaderText = "Nguồn đơn đặt", DataPropertyName = "NguonDonDat", SortMode = DataGridViewColumnSortMode.Programmatic },
                new DataGridViewTextBoxColumn { Name = "Cập nhật lần cuối", HeaderText = "Cập nhật lần cuối", DataPropertyName = "LastUpdate", SortMode = DataGridViewColumnSortMode.Programmatic }
            });
        }

        private void RefreshDashboardUI()
        {
            if (tabDash_dataGrid == null) return;
            try
            {
                var virtualRows = LocalTrackingManager.Instance?.GetVirtualList();

                var dt = new DataTable();
                dt.Columns.Add("WaybillNo", typeof(string));
                dt.Columns.Add("TrangThaiHienTai", typeof(string));
                dt.Columns.Add("ThaoTacCuoi", typeof(string));
                dt.Columns.Add("NguoiThaoTac", typeof(string));
                dt.Columns.Add("ThoiGianThaoTac", typeof(string));        // Cột index 4
                dt.Columns.Add("ThoiGianYeuCauPhatLai", typeof(string));
                dt.Columns.Add("SoLanNhac", typeof(int));
                dt.Columns.Add("ThoiGianNhacGanNhat", typeof(string));
                dt.Columns.Add("SoLanDangKyChuyenHoan", typeof(int));
                dt.Columns.Add("SoLanGiaoLaiHang", typeof(int));
                dt.Columns.Add("NguonDonDat", typeof(string));
                dt.Columns.Add("LastUpdate", typeof(string));
                if (virtualRows != null)
                {
                    foreach (var r in virtualRows)
                    {
                        dt.Rows.Add(
                            r.WaybillNo,
                            r.TrangThaiHienTai,
                            r.ThaoTacCuoi,
                            r.NguoiThaoTac,
                            r.ThoiGianThaoTac,
                            r.ThoiGianYeuCauPhatLai,
                            r.SoLanNhac,
                            r.ThoiGianNhacGanNhat,
                            r.SoLanDangKyChuyenHoan,
                            r.SoLanGiaoLaiHang,
                            r.NguonDonDat,
                            r.LastUpdate.ToString("HH:mm:ss")
                        );
                    }
                }
                    _dashBindingSource.DataSource = dt;
                    tabDash_dataGrid.DataSource = _dashBindingSource;

                    if (virtualRows == null || virtualRows.Count == 0)
                    {
                        _dashBindingSource.DataSource = null;
                        tabDash_dataGrid.DataSource = null;
                        if (tabDash_lblDebug != null)
                            tabDash_lblDebug.Text = "Không có dữ liệu | Cập nhật: --:--:--";
                    }
                    else
                    {
                        tabDash_dataGrid.AutoResizeColumns(DataGridViewAutoSizeColumnsMode.DisplayedCells);
                        if (tabDash_lblDebug != null)
                            tabDash_lblDebug.Text = $"Đã load {virtualRows.Count} đơn | Cập nhật: {LocalTrackingManager.Instance.LastTrackedTime:HH:mm:ss}";
                    }
                }
            

            catch (Exception ex) 
            {
                System.Diagnostics.Debug.WriteLine($"[RefreshDashboardUI Error] {ex.Message}");
            }
        
        }
        private void tabchat_datagrid_DataError(object sender, DataGridViewDataErrorEventArgs e)
        {
            // Bỏ qua lỗi và không làm gì cả để tránh crash UI
            e.Cancel = true;
        }

        private void tabDash_dataGrid_ColumnHeaderMouseClick(object sender, DataGridViewCellMouseEventArgs e)
        {
            var dgv = tabDash_dataGrid;
            if (dgv == null || e.RowIndex != -1 || e.ColumnIndex < 0) return;
            var clicked = dgv.Columns[e.ColumnIndex];
            if (clicked == null || string.IsNullOrEmpty(clicked.DataPropertyName)) return;

            if (_dashBindingSource.DataSource is DataTable dt)
            {
                string columnName = clicked.DataPropertyName;
                if (!dt.Columns.Contains(columnName)) return;

                var direction = clicked.HeaderCell.SortGlyphDirection == SortOrder.Ascending
                    ? ListSortDirection.Descending
                    : ListSortDirection.Ascending;

                try
                {
                    dt.DefaultView.Sort = $"[{columnName}] {(direction == ListSortDirection.Ascending ? "ASC" : "DESC")}";
                    _dashBindingSource.DataSource = dt.DefaultView;
                    dgv.DataSource = _dashBindingSource;

                    clicked.HeaderCell.SortGlyphDirection = direction == ListSortDirection.Ascending ? SortOrder.Ascending : SortOrder.Descending;
                    foreach (DataGridViewColumn col in dgv.Columns)
                        if (col != clicked) col.HeaderCell.SortGlyphDirection = SortOrder.None;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[TabDash Sort Error] {ex.Message}");
                }
            }
        }

        // ========== TAB CHAT: LỌC VÀ HIỂN THỊ ==========
        private void tabChat_statusSelect_SelectedIndexChanged(object sender, EventArgs e)
        {
            ApplyFilterAndSort();
        }

        private async Task RefreshTrackingDataAsync()
        {
            try
            {
                var waybills = _zaloChatService?.GetWaybillsFromPhatLai();
                if (waybills == null || waybills.Count == 0) return;

                string waybillText = string.Join("\n", waybills);
                await Main._trackingService.SearchTrackingAsync(waybillText, updateMainGrid: false);

                var trackedRows = _trackingService.GetAllRows();
                _trackedReminders = trackedRows.Select(r => new Reminder
                {
                    maDon = r.WaybillNo,
                    nhanVien = r.NhanVienKienVanDe,
                    trangThai = r.ThaoTacCuoi ?? "",
                    soLanNhac = 0,
                    thoiGianNhac = "",
                    row = 0
                }).ToList();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[RefreshTrackingDataAsync] Lỗi: {ex.Message}");
            }
        }

        private async Task RefreshZaloDataAsync(bool forceReload = false)
        {
            // Nếu chưa có dữ liệu hoặc cần tải lại, gọi RefreshTrackingDataAsync
            if (_trackedReminders == null || _trackedReminders.Count == 0 || forceReload)
            {
                await RefreshTrackingDataAsync();
            }

            // Cập nhật ComboBox trạng thái dựa trên _trackedReminders
            PopulateStatusSelect();

            // Áp dụng lọc và hiển thị
            ApplyFilterAndSort();
        }

        private void ApplyFilterAndSort()
        {
            if (_isUpdatingUI) return;
            _isUpdatingUI = true;

            try
            {
                if (_chatBindingSource == null || _chatBindingSource.DataSource is not DataTable dt)
                    return;

                string selected = tabChat_statusSelect?.SelectedItem?.ToString() ?? "Tất cả";

                if (selected == "Tất cả")
                {
                    _chatBindingSource.RemoveFilter();
                }
                else if (dt.Columns.Contains("Trạng thái hiện tại")) // kiểm tra cột tồn tại
                {
                    _chatBindingSource.Filter = $"[Trạng thái hiện tại] = '{selected.Replace("'", "''")}'";
                }

                // Áp dụng sắp xếp an toàn
                if (dt.Columns.Contains("Tên nhân viên"))
                    _chatBindingSource.Sort = "[Tên nhân viên] ASC";

                RestoreChatGridHeaderSizing();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ApplyFilterAndSort Error] {ex.Message}");
            }
            finally
            {
                _isUpdatingUI = false;
            }
        }

        private void RestoreChatGridHeaderSizing()
        {
            if (tabChat_dataGrid == null) return;
            tabChat_dataGrid.AutoResizeColumnHeadersHeight();
            tabChat_dataGrid.AutoResizeColumns(DataGridViewAutoSizeColumnsMode.DisplayedCells);
        }

        private void tabChat_dataGrid_ColumnHeaderMouseClick(object sender, DataGridViewCellMouseEventArgs e)
        {
            // 1. Kiểm tra vị trí click xem có hợp lệ không (phải click vào tiêu đề)
            if (e.RowIndex != -1 || e.ColumnIndex < 0) return;
            if (tabChat_dataGrid?.Columns == null || tabChat_dataGrid.Columns.Count == 0) return;

            var column = tabChat_dataGrid.Columns[e.ColumnIndex];
            if (column == null) return;

            // 2. Xác định hướng sắp xếp (Tăng dần hay Giảm dần)
            var direction = ListSortDirection.Ascending;
            if (tabChat_dataGrid.SortedColumn == column && tabChat_dataGrid.SortOrder == SortOrder.Ascending)
            {
                direction = ListSortDirection.Descending;
            }

            // 3. Thực hiện sắp xếp dữ liệu
            tabChat_dataGrid.Sort(column, direction);

            // 4. Căn chỉnh lại độ rộng toàn bộ lưới (đã được sửa thành DisplayedCells ở hàm này)
            RestoreChatGridHeaderSizing();
        }

        private DataTable ConvertToDataTable<T>(IList<T> data)
        {
            PropertyDescriptorCollection properties = TypeDescriptor.GetProperties(typeof(T));
            DataTable table = new DataTable();

            foreach (PropertyDescriptor prop in properties)
            {
                Type type = Nullable.GetUnderlyingType(prop.PropertyType) ?? prop.PropertyType;
                table.Columns.Add(prop.Name, type);
            }

            foreach (T item in data)
            {
                DataRow row = table.NewRow();
                foreach (PropertyDescriptor prop in properties)
                {
                    row[prop.Name] = prop.GetValue(item) ?? DBNull.Value;
                }
                table.Rows.Add(row);
            }

            return table;
        }

        private void UpdateZaloGridUI(List<Reminder> data)
        {
            if (tabChat_dataGrid == null) return;

            // Đảm bảo chạy trên luồng UI
            if (tabChat_dataGrid.InvokeRequired)
            {
                tabChat_dataGrid.Invoke(new Action(() => UpdateZaloGridUI(data)));
                return;
            }

            // Tạm dừng vẽ giao diện lưới
            tabChat_dataGrid.SuspendLayout();

            // BƯỚC 2: Tạm dừng đồng bộ dữ liệu để tránh lỗi văng (crash) Index 0
            _chatBindingSource.SuspendBinding();

            try
            {
                tabChat_dataGrid.AutoGenerateColumns = false;
                tabChat_dataGrid.AllowUserToAddRows = false;
                tabChat_dataGrid.RowHeadersVisible = false;
                tabChat_dataGrid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.DisplayedCells;
                tabChat_dataGrid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize;

                var table = ConvertToDataTable(data ?? new List<Reminder>());

                // MẸO QUAN TRỌNG: Ngắt kết nối dữ liệu cũ trước khi gán dữ liệu mới
                // Việc này xóa sạch bộ nhớ đệm của CurrencyManager, ngăn chặn triệt để lỗi NullReference
                tabChat_dataGrid.DataSource = null;
                _chatBindingSource.DataSource = null;

                // Gán dữ liệu mới
                _chatBindingSource.DataSource = table;
                tabChat_dataGrid.DataSource = _chatBindingSource;

                tabChat_dataGrid.AutoResizeColumns(DataGridViewAutoSizeColumnsMode.DisplayedCells);
            }
            finally
            {
                // BƯỚC 2: Khôi phục lại đồng bộ dữ liệu
                _chatBindingSource.ResumeBinding();

                // Khôi phục vẽ giao diện
                tabChat_dataGrid.ResumeLayout();

                // Làm mới (vẽ lại) lưới sau khi đã khôi phục mọi thứ
                tabChat_dataGrid.Refresh();
            }
        
        }
        // ==================== RELOAD DỮ LIỆU TAB CHAT + TAB DASH THEO CHU KỲ ====================
        private void StartPeriodicDataReload()
        {
            _dataReloadTimer?.Stop();
            _dataReloadTimer?.Dispose();

            string text = tabChat_timeCheck.Text?.Replace(" phút", "").Trim() ?? "5";
            if (!int.TryParse(text, out int minutes) || minutes <= 0) minutes = 5;

            _dataReloadTimer = new System.Windows.Forms.Timer();
            _dataReloadTimer.Interval = minutes * 60 * 1000; // chuyển phút sang mili giây
            _dataReloadTimer.Tick += async (s, e) =>
            {
                try
                {
                    // Reload dữ liệu tracking
                    await LocalTrackingManager.Instance.PerformIncrementalTrackingAsync();

                    // Cập nhật cả 2 tab
                    if (tabControl.SelectedTab == tabChat)
                    {
                        PopulateStatusSelect();
                        ApplyFilterAndSort();
                    }

                    RefreshDashboardUI();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Periodic Reload Error] {ex.Message}");
                }
            };

            _dataReloadTimer.Start();
        }
        private void PopulateStatusSelect()
        {
            if (tabChat_statusSelect == null) return;
            tabChat_statusSelect.Items.Clear();
            tabChat_statusSelect.Items.Add("Tất cả");

            if (_trackedReminders == null || _trackedReminders.Count == 0) return;

            var uniqueStatuses = _trackedReminders
                .Select(r => r.trangThai?.Trim())
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(s => s)
                .ToList();

            foreach (var s in uniqueStatuses)
                tabChat_statusSelect.Items.Add(s);

            tabChat_statusSelect.SelectedIndex = 0;
        }

        private void UpdateTrackingInterval()
        {
            string text = tabChat_timeSelect.Text?.Replace(" phút", "").Trim() ?? "5";
            if (int.TryParse(text, out int minutes) && minutes > 0)
            {
                LocalTrackingManager.Instance.StopAutoTracking();
                LocalTrackingManager.Instance.StartAutoTracking(minutes);
            }
        }

        // ========== ONLOAD ==========
        protected override async void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            if (this.IsDisposed) return;

            _ = tabHome_webView.Handle;
            _ = tabDKCH_webView.Handle;

            try
            {
                await tabHome_webView.EnsureCoreWebView2Async(null);
                await tabDKCH_webView.EnsureCoreWebView2Async(null);
                tabHome_webView.CoreWebView2.Settings.UserAgent = CHROME_USER_AGENT;
                tabDKCH_webView.CoreWebView2.Settings.UserAgent = CHROME_USER_AGENT;
                GoogleSheetService.InitService();

                tabHome_webView.CoreWebView2.Settings.IsPasswordAutosaveEnabled = true;
                tabHome_webView.CoreWebView2.Settings.IsGeneralAutofillEnabled = true;
                tabDKCH_webView.CoreWebView2.Settings.IsPasswordAutosaveEnabled = true;
                tabDKCH_webView.CoreWebView2.Settings.IsGeneralAutofillEnabled = true;

                tabHome_webView.CoreWebView2.Navigate(JmsHomeUrl);
                tabDKCH_webView.CoreWebView2.Navigate(JmsHomeUrl);

                tabDKCH_webView.CoreWebView2.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.Fetch);
                tabDKCH_webView.CoreWebView2.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.XmlHttpRequest);
                tabDKCH_webView.CoreWebView2.WebResourceRequested += CoreWebView2_WebResourceRequested;
                tabDKCH_webView.CoreWebView2.NavigationCompleted += (s, args) => { if (args.IsSuccess) ApplyZoomFactor(); };
            }
            catch (Exception ex)
            {
                MessageBox.Show("Lỗi khởi tạo trình duyệt: " + ex.Message);
            }

            // Khởi tạo tracking service
            _trackingService = new WaybillTrackingService(tabTracking_dataView, tabTracking_process);
            LocalTrackingManager.Instance.TrackingService = _trackingService;
            _printService = new PrintService(tabPrint_dataView, _trackingService);

            // Chạy tracking lần đầu
            await LocalTrackingManager.Instance.PerformIncrementalTrackingAsync();
            LocalTrackingManager.Instance.StartAutoTracking(5);

            WaybillTrackingService.EnableDoubleBuffering(tabTracking_dataView);
            tabTracking_dataView.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.ColumnHeader;
            tabTracking_dataView.EditMode = DataGridViewEditMode.EditProgrammatically;
            tabTracking_dataView.AutoGenerateColumns = false;

            // DKCH
            _dkchManager.SetWebView(tabDKCH_webView);
            _dkchManager.SetTrackingService(_trackingService);
            _dkchManager.SetSettingsGetter(() => (
                useSheet: tabDKCH_useSheet.Active,
                sheetName: tabDKCH_sheetName.Text,
                rowCount: (int)tabDKCH_numRow.Value
            ));
            _dkchManager.StartDaemon();

            tabDKCH_btnDKCH1.Enabled = true;
            tabDKCH_btnDKCH2.Enabled = true;

            await WebViewHost.InitAsync(tabDKCH_webView);
            ApplyZoomFactor();

            tabDKCH_webView.ZoomFactorChanged += (s, args) =>
            {
                _settings.ZoomFactor = tabDKCH_webView.ZoomFactor;
                SettingsManager.Save(_settings);
            };

            await WebViewHost.NavigateAsync(_settings.DefaultUrl);
            await Task.Delay(2000);
            await RefreshAuthTokenAsync();
        }

        // ========== CÁC SỰ KIỆN KHÁC ==========
        private bool ShowCustomExitDialog()
        {
            // Khởi tạo một Form tùy chỉnh chuẩn SunnyUI
            using (UIForm form = new UIForm())
            {
                form.Text = "Đóng ứng dụng";

                // TÙY CHỈNH KÍCH THƯỚC HỘP THOẠI (Rộng x Cao)
                form.ClientSize = new System.Drawing.Size(450, 220);
                form.StartPosition = FormStartPosition.CenterScreen;
                form.MaximizeBox = false;
                form.MinimizeBox = false;

                // TÙY CHỈNH FONT TIÊU ĐỀ
                form.Font = new System.Drawing.Font("Tahoma", 12F, System.Drawing.FontStyle.Regular);

                // TÙY CHỈNH LABEL NỘI DUNG
                UILabel lblMsg = new UILabel();
                lblMsg.Text = "Cứ ngỡ cống hiến trăm năm...\nAi ngờ 5h00.pm";
                // Tùy chỉnh Font chữ nội dung
                lblMsg.Font = new System.Drawing.Font("Tahoma", 13F, System.Drawing.FontStyle.Regular);
                lblMsg.Location = new System.Drawing.Point(20, 60);
                lblMsg.Size = new System.Drawing.Size(410, 70);
                lblMsg.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
                form.Controls.Add(lblMsg);

                // TÙY CHỈNH NÚT "CÓ"
                UIButton btnYes = new UIButton();
                btnYes.Text = "Thoát ngay";
                btnYes.Font = new System.Drawing.Font("Tahoma", 12F, System.Drawing.FontStyle.Bold);
                btnYes.Size = new System.Drawing.Size(140, 40);
                btnYes.Location = new System.Drawing.Point(60, 150);
                btnYes.DialogResult = DialogResult.Yes;

                // 1. Màu bình thường (Đỏ nhạt)
                btnYes.FillColor = System.Drawing.Color.IndianRed;
                btnYes.RectColor = System.Drawing.Color.IndianRed; // Màu viền

                // 2. THÊM HIỆU ỨNG HOVER (Khi chuột đưa vào) -> Đỏ đậm (DarkRed)
                btnYes.FillHoverColor = System.Drawing.Color.Red;
                btnYes.RectHoverColor = System.Drawing.Color.DarkRed;

                // 3. THÊM HIỆU ỨNG PRESS (Khi bấm chuột xuống) -> Đỏ mận (Maroon)
                btnYes.FillPressColor = System.Drawing.Color.Maroon;
                btnYes.RectPressColor = System.Drawing.Color.Maroon;

                form.Controls.Add(btnYes);

                // TÙY CHỈNH NÚT "KHÔNG"
                UIButton btnNo = new UIButton();
                btnNo.Text = "Hủy bỏ";
                btnNo.Font = new System.Drawing.Font("Tahoma", 12F, System.Drawing.FontStyle.Bold);
                btnNo.Size = new System.Drawing.Size(140, 40);
                btnNo.Location = new System.Drawing.Point(250, 150);
                btnNo.DialogResult = DialogResult.No;
                form.Controls.Add(btnNo);

                // Hiển thị và trả kết quả
                return form.ShowDialog() == DialogResult.Yes;
            }
        }
        private void Main_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (e.CloseReason == CloseReason.UserClosing)
            {
                // GỌI HÀM TÙY CHỈNH VỪA TẠO
                bool isConfirmExit = ShowCustomExitDialog();

                if (!isConfirmExit)
                {
                    e.Cancel = true;
                }
                else
                {
                    if (_zaloChatService != null)
                    {
                        // Dọn dẹp WebView2 hoặc các luồng ngầm ở đây (nếu cần)
                    }
                }
            }
        }
        private void ApplyZoomFactor()
        {
            if (tabDKCH_webView?.CoreWebView2 != null)
                tabDKCH_webView.ZoomFactor = _settings.ZoomFactor;
        }

        private void tabTracking_inputWaybill_KeyDown(object sender, KeyEventArgs e)
        {
            // Bắt sự kiện nhấn Ctrl + V
            if (e.Control && e.KeyCode == Keys.V)
            {
                e.SuppressKeyPress = true; // Khóa mỏ trình dán mặc định của Windows (Chống x2)

                if (Clipboard.ContainsText())
                {
                    // Lấy text thô từ Clipboard
                    string plainText = Clipboard.GetText(TextDataFormat.UnicodeText);

                    // Đưa qua hàm xử lý Regex "đỉnh cao" mà ta vừa xây dựng
                    string cleaned = NormalizeWaybillInput(plainText);

                    // Dán nội dung đã lọc sạch sẽ, chẻ dòng sẵn vào vị trí con trỏ
                    if (!string.IsNullOrEmpty(cleaned))
                    {
                        tabTracking_inputWaybill.SelectedText = cleaned + Environment.NewLine;
                    }
                }
            }
        }
        private void FormatNowTracking(string historyText)
        {
            // 1. Đổ dữ liệu vào UIRichTextBox
            tabDKCH_nowTracking.Clear();
            tabDKCH_nowTracking.Text = historyText;

            // 2. Định nghĩa các từ khóa và màu sắc tương ứng
            var formatRules = new Dictionary<string, System.Drawing.Color>
    {
        { "Đăng ký chuyển hoàn", System.Drawing.Color.Red },
        { "Đăng ký chuyển hoàn lần 2", System.Drawing.Color.Red },
        { "Quét kiện vấn đề", System.Drawing.Color.Red },
        { "Giao lại hàng", System.Drawing.Color.DodgerBlue }, // Xanh dương
        { "Ký nhận CPN", System.Drawing.Color.ForestGreen },   // Xanh lá
        { "Đang chuyển hoàn", System.Drawing.Color.DarkOrange }, // Cam
        { "Xác nhận chuyển hoàn", System.Drawing.Color.DarkOrange } // Cam
    };

            // 3. Quét và bôi đen từng từ khóa để đổi màu
            foreach (var rule in formatRules)
            {
                int startIndex = 0;
                while (startIndex < tabDKCH_nowTracking.TextLength)
                {
                    // Tìm từ khóa trong RichTextBox
                    int wordStartIndex = tabDKCH_nowTracking.Find(rule.Key, startIndex, RichTextBoxFinds.None);
                    if (wordStartIndex != -1)
                    {
                        // Bôi đen và áp dụng định dạng
                        tabDKCH_nowTracking.SelectionStart = wordStartIndex;
                        tabDKCH_nowTracking.SelectionLength = rule.Key.Length;
                        tabDKCH_nowTracking.SelectionColor = rule.Value;
                        tabDKCH_nowTracking.SelectionFont = new System.Drawing.Font(tabDKCH_nowTracking.Font, System.Drawing.FontStyle.Bold);

                        // Tiếp tục tìm từ vị trí kế tiếp
                        startIndex = wordStartIndex + rule.Key.Length;
                    }
                    else
                    {
                        break;
                    }
                }
            }

            // 4. Trả con trỏ về cuối văn bản, reset định dạng mặc định
            tabDKCH_nowTracking.SelectionStart = tabDKCH_nowTracking.TextLength;
            tabDKCH_nowTracking.SelectionLength = 0;
            tabDKCH_nowTracking.SelectionColor = tabDKCH_nowTracking.ForeColor;
            tabDKCH_nowTracking.SelectionFont = tabDKCH_nowTracking.Font;
        }
        private string NormalizeWaybillInput(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return string.Empty;

            var parts = text.Split(new[] { '\r', '\n', ',', ';', '|', ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries)
                            .Select(t => t.Trim().ToUpper())
                            .Where(t => t.Length >= 6)
                            .ToList();

            var result = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // REGEX TỐI THƯỢNG: Đuôi bắt buộc là dấu trừ (-) và ĐÚNG 3 CHỮ SỐ (\d{3})
            // Nó sẽ tách hoàn hảo "842599186572-001802733204673" thành "842599186572-001" và "802733204673"
            var waybillRegex = new System.Text.RegularExpressions.Regex(
                @"((8\d{11}|[A-Za-z][A-Za-z0-9]{4,16})(-\d{3})?)",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);

            foreach (var part in parts)
            {
                var matches = waybillRegex.Matches(part);
                if (matches.Count > 0)
                {
                    foreach (System.Text.RegularExpressions.Match m in matches)
                    {
                        string code = m.Value.ToUpper();
                        if (seen.Add(code)) result.Add(code);
                    }
                }
                else if (part.Length <= 25)
                {
                    if (seen.Add(part)) result.Add(part);
                }
            }

            return string.Join(Environment.NewLine, result);
        }

        private void UpdateWaybillCount()
        {
            if (tabDKCH_countSum == null) return;
            var uniqueCodes = tabTracking_inputWaybill.Text
                .Split(new[] { '\r', '\n', ' ', ',', ';', '\t' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(x => x.Trim().ToUpper())
                .Where(x => x.Length > 5)
                .Distinct(StringComparer.OrdinalIgnoreCase);
            tabDKCH_countSum.Text = uniqueCodes.Count().ToString("N0");
        }


        //=================================================================================
        //========================TAB CONTROL CHANGE========================================
        private async void tabControl_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (this.IsDisposed) return;
            

            try
            {
                if (tabControl.SelectedTab == tabHome) 
                {
                    
                    if (_isHomeNeedReload && tabHome_webView != null && tabHome_webView.CoreWebView2 != null)
                    {
                        tabHome_webView.CoreWebView2.Reload();
                        _isHomeNeedReload = false;

                    }
                }
                else if (tabControl.SelectedTab == tabDKCH)
                {
                    if (_isDkchNeedReload && tabDKCH_webView != null && tabDKCH_webView.CoreWebView2 != null)
                    {
                        tabDKCH_webView.CoreWebView2.Reload();
                        _isDkchNeedReload = false;
                    }
                }
                else if (tabControl.SelectedTab == tabTracking)
                {
                    if (string.IsNullOrEmpty(Main.CapturedAuthToken))
                        await RefreshAuthTokenAsync();
                    if (tabTracking_btnSearch.Enabled)
                    {
                        if (tabTracking_btnSearch.Enabled)
                        {
                            if (tabTracking_process != null && !tabTracking_process.IsDisposed)
                            {
                                tabTracking_process.Value = 0;
                                tabTracking_process.Visible = false;
                            }
                        }
                    }
                }
                else if (tabControl.SelectedTab == tabChat)
                {
                    if (!_isZaloLoaded && tabChat_webViewZalo.CoreWebView2 == null)
                    {
                        try
                        {
                            string userDataFolder = Path.Combine(Application.StartupPath, "ZaloProfile");
                            var env = await CoreWebView2Environment.CreateAsync(null, userDataFolder);
                            await tabChat_webViewZalo.EnsureCoreWebView2Async(env);
                            tabChat_webViewZalo.CoreWebView2.Settings.UserAgent = CHROME_USER_AGENT;
                            tabChat_webViewZalo.CoreWebView2.Navigate("https://chat.zalo.me/index.html");
                            tabChat_webViewZalo.NavigationCompleted += (s, args) =>
                            {
                                if (_zaloChatService == null)
                                {
                                    _zaloChatService = new ZaloChatService(tabChat_webViewZalo, AppsScriptUrl);
                                    _zaloChatService.StartAutoReminder(5);
                                }
                            };
                            await LocalTrackingManager.Instance.PerformIncrementalTrackingAsync();
                            // Cập nhật ComboBox trạng thái
                            PopulateStatusSelect();
                            // Áp dụng lọc hiện tại
                            ApplyFilterAndSort();
                            _isZaloLoaded = true;
                        }
                        catch (Exception ex) { MessageBox.Show("Lỗi khởi tạo Zalo Web: " + ex.Message); }
                    }
                    await RefreshZaloDataAsync(forceReload: false);
                }
                else if (tabControl.SelectedTab == tabDash)
                {
                    RefreshDashboardUI();
                }
            }
            catch (Exception ex) { MessageBox.Show("Lỗi xử lý Tab: " + ex.Message); }
        }

        private void CoreWebView2_WebResourceRequested(object sender, CoreWebView2WebResourceRequestedEventArgs e)
        {
            try
            {
                if (e.Request.Headers.Contains("authToken"))
                {
                    string token = e.Request.Headers.GetHeader("authToken");
                    if (!string.IsNullOrEmpty(token) && token.Length > 20)
                    {
                        bool changed = false;
                        lock (_authTokenLock)
                        {
                            if (!string.Equals(_settings.LastAuthToken, token, StringComparison.Ordinal))
                            {
                                _settings.LastAuthToken = token;
                                changed = true;
                            }
                            CapturedAuthToken = token;
                        }

                        if (changed)
                            _ = SaveAuthTokenDebouncedAsync();
                    }
                }
            }
            catch { }
        }

        private async Task SaveAuthTokenDebouncedAsync()
        {
            CancellationTokenSource currentCts;
            lock (_authTokenLock)
            {
                _authTokenSaveCts?.Cancel();
                _authTokenSaveCts?.Dispose();
                _authTokenSaveCts = new CancellationTokenSource();
                currentCts = _authTokenSaveCts;
            }

            try
            {
                await Task.Delay(400, currentCts.Token);
                AppSettings snapshot;
                lock (_authTokenLock)
                {
                    snapshot = new AppSettings
                    {
                        ZoomFactor = _settings.ZoomFactor,
                        DefaultUrl = _settings.DefaultUrl,
                        LastAuthToken = _settings.LastAuthToken,
                        DownloadFolder = _settings.DownloadFolder,
                        DefaultSheet = _settings.DefaultSheet,
                        UseSheetByDefault = _settings.UseSheetByDefault,
                        AutoRefreshToken = _settings.AutoRefreshToken,
                        LastMode = _settings.LastMode,
                        DefaultRowCount = _settings.DefaultRowCount
                    };
                }

                await SettingsManager.SaveAsync(snapshot);
            }
            catch (OperationCanceledException)
            {
                // Ignore previous scheduled saves.
            }
            catch
            {
                // Ignore errors to avoid affecting WebView request pipeline.
            }
        }

        public async Task RefreshAuthTokenAsync()
        {
            if (tabDKCH_webView.CoreWebView2 == null) return;
            string js = @"(function() {
                let token = localStorage.getItem('authToken') || localStorage.getItem('token');
                if (!token || token.length < 30) {
                    const userData = localStorage.getItem('userData');
                    if (userData) {
                        try {
                            const obj = JSON.parse(userData);
                            if (obj.uuid && obj.uuid.length > 20) token = obj.uuid;
                        } catch(e){}
                    }
                }
                return { found: !!token && token.length > 20, value: token || '' };
            })();";
            try
            {
                string result = await WebViewHost.ExecJsAsync(js);
                var obj = JsonSerializer.Deserialize<JsonElement>(result);
                if (obj.GetProperty("found").GetBoolean())
                    CapturedAuthToken = obj.GetProperty("value").GetString() ?? "";
            }
            catch { }
        }

        private async Task ProcessDirectTrackingAsync()
        {
            _queueTimer.Stop();
            try
            {
                string spreadsheetId = GoogleSheetService.DATA_SPREADSHEET_ID;
                string c5Value = "";
                var c5Data = GoogleSheetService.ReadRange(spreadsheetId, "PHATLAI!C5");
                if (c5Data != null && c5Data.Count > 0 && c5Data[0].Count > 0)
                    c5Value = c5Data[0][0].ToString().Trim().ToUpper();

                if (c5Value == "RUN")
                {
                    GoogleSheetService.UpdateCell(spreadsheetId, "PHATLAI!C5", "PROCESSING");
                    var columnAData = GoogleSheetService.ReadRange(spreadsheetId, "PHATLAI!A2:A");
                    List<string> waybills = new List<string>();
                    if (columnAData != null)
                    {
                        foreach (var row in columnAData)
                            if (row.Count > 0 && !string.IsNullOrWhiteSpace(row[0].ToString()))
                                waybills.Add(row[0].ToString().Trim());
                    }
                    if (waybills.Count > 0)
                    {
                        string waybillsText = string.Join("\n", waybills);
                        await _trackingService.SearchTrackingAsync(waybillsText, false);
                        var results = _trackingService.GetAllRows();
                        var sheetData = new List<IList<object>>();
                        foreach (var item in results)
                        {
                            sheetData.Add(new List<object>()
                            {
                                item.WaybillNo, item.TrangThaiHienTai, item.ThaoTacCuoi,
                                item.ThoiGianThaoTac, item.ThoiGianYeuCauPhatLai, item.NhanVienKienVanDe
                            });
                        }
                        GoogleSheetService.UpdateBumpSheet(sheetData, spreadsheetId, "BUMP!A2");
                    }
                    GoogleSheetService.UpdateCell(spreadsheetId, "PHATLAI!C5", "DONE");
                }
            }
            catch (Exception ex) { Console.WriteLine($"[Lỗi Tracking Direct] {ex.Message}"); }
            finally { _queueTimer.Start(); }
        }

        private async void tabHome_WebView_NavigationCompleted(object sender, CoreWebView2NavigationCompletedEventArgs e)
        {
            if (tabHome_webView.CoreWebView2 != null)
            {
                tabHome_btnBack.Enabled = tabHome_webView.CoreWebView2.CanGoBack;
                tabHome_btnForward.Enabled = tabHome_webView.CoreWebView2.CanGoForward;
                _isDkchNeedReload = true;
            }
        }


        private void btnBack_Click(object sender, EventArgs e)
        {
            if (tabHome_webView?.CoreWebView2 != null && tabHome_webView.CoreWebView2.CanGoBack)
                tabHome_webView.CoreWebView2.GoBack();
        }

        private void btnForward_Click(object sender, EventArgs e)
        {
            if (tabHome_webView?.CoreWebView2 != null && tabHome_webView.CoreWebView2.CanGoForward)
                tabHome_webView.CoreWebView2.GoForward();
        }

        private void btnReload_Click(object sender, EventArgs e)
        {
            if (tabHome_webView?.CoreWebView2 != null)
            {
                tabHome_webView.CoreWebView2.Reload();
                tabHome_webView.CoreWebView2.Navigate(JmsHomeUrl);
            }
        }

        private void btnHome_Click(object sender, EventArgs e)
        {
            if (tabHome_webView?.CoreWebView2 != null)
                tabHome_webView.CoreWebView2.Navigate(JmsHomeUrl);
        }

        private async void tabDKCH_btnDKCH1_Click(object sender, EventArgs e)
        {
            if (_dkchManager.IsRunning || _isDkchStarting) return;
            _isDkchStarting = true;
            UpdateDkchButtonsByState(true);
            try
            {
                await _dkchManager.StartAsync("DKCH1");
                await RefreshAuthTokenAsync();
                UpdateDkchButtonsByState(_dkchManager.IsRunning);
            }
            catch (Exception ex)
            {
                _dkchManager.Stop();
                UpdateDkchButtonsByState(false);
                UIMessageTip.ShowError("Không thể khởi động DKCH1: " + ex.Message);
            }
            finally
            {
                _isDkchStarting = false;
                UpdateDkchButtonsByState(_dkchManager.IsRunning);
            }
        }

        private async void tabDKCH_btnDKCH2_Click(object sender, EventArgs e)
        {
            if (_dkchManager.IsRunning || _isDkchStarting) return;
            _isDkchStarting = true;
            UpdateDkchButtonsByState(true);
            try
            {
                await _dkchManager.StartAsync("DKCH2");
                UpdateDkchButtonsByState(_dkchManager.IsRunning);
            }
            catch (Exception ex)
            {
                _dkchManager.Stop();
                UpdateDkchButtonsByState(false);
                UIMessageTip.ShowError("Không thể khởi động DKCH2: " + ex.Message);
            }
            finally
            {
                _isDkchStarting = false;
                UpdateDkchButtonsByState(_dkchManager.IsRunning);
            }
        }

        private void tabDKCH_btnStop_Click(object sender, EventArgs e)
        {
            _isDkchStarting = false;
            _dkchManager.Stop();
            UpdateDkchButtonsByState(false);
        }

        private async void btn_Refresh_Click(object sender, EventArgs e)
        {
            _dkchManager.Stop();
            await WebViewHost.NavigateAsync(JmsHomeUrl);
        }

        private async void btnSearch_Click(object sender, EventArgs e)
        {
            // 1. Chuẩn hóa và hiển thị ngay lập tức
            string input = NormalizeWaybillInput(tabTracking_inputWaybill.Text);
            tabTracking_inputWaybill.Text = input;

            if (string.IsNullOrWhiteSpace(input))
            {
                UIMessageTip.ShowWarning("Chưa nhập mã vận đơn!");
                return;
            }

            try
            {
                tabTracking_btnSearch.Enabled = false;

                // 2. Kích hoạt Progress Bar trước khi chạy
                if (tabTracking_process != null)
                {
                    tabTracking_process.Value = 0;
                    tabTracking_process.Visible = true;
                }

                // 3. CORE: Trả lại nhiệm vụ tra cứu song song cho Service (Tốc độ cao nhất)
                // Bỏ qua mọi tính toán phần trăm ở đây để không làm nghẽn luồng UI
                await _trackingService.SearchTrackingAsync(input);

                // 4. Hiển thị kết quả sau khi tra cứu xong
                int tongSoDon = _trackingService.GetAllRows().Count;
                tabTracking_countSum.Text = tongSoDon.ToString("N0");

                if (tongSoDon == 0)
                {
                    UIMessageTip.ShowWarning("Không tìm thấy vận đơn nào!");
                }
            }
            catch (Exception ex)
            {
                UIMessageTip.ShowError("Lỗi khi tra cứu: " + ex.Message);
            }
            finally
            {
                tabTracking_btnSearch.Enabled = true;
                UpdateWaybillCount();

                // 5. CHỐT CHẶN TUYỆT ĐỐI: Dù thành công hay lỗi, khi kết thúc hàm sẽ ép 100% và ẩn ngay
                if (tabTracking_process != null)
                {
                    tabTracking_process.Value = tabTracking_process.Maximum; // Ép mượt lên 100
                    tabTracking_process.Visible = false;                     // Tàng hình lập tức
                }
            }
        }

        private void btn_Export_Click(object sender, EventArgs e) => _trackingService?.ExportToExcel();
        private void btn_Clear_Click(object sender, EventArgs e) { _trackingService?.ClearData(); tabTracking_inputWaybill.Clear(); }
        private void btn_Export_Spe_Click(object sender, EventArgs e) => _trackingService.ExportSpecial();

        private async void tabTracking_btnUpload_Click(object sender, EventArgs e)
        {
            tabTracking_btnUpload.Enabled = false;
            string oldText = tabTracking_btnUpload.Text;
            tabTracking_btnUpload.Text = "Đang đồng bộ...";
            try
            {
                DataGridView grid = tabTracking_dataView;
                if (grid.Rows.Count == 0 && grid.Columns.Count == 0)
                {
                    MessageBox.Show("Không có dữ liệu trên bảng để tải lên!", "Thông báo", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                var sheetData = new List<IList<object>>();
                var headerRow = new List<object>();
                foreach (DataGridViewColumn col in grid.Columns) headerRow.Add(col.HeaderText);
                sheetData.Add(headerRow);
                foreach (DataGridViewRow row in grid.Rows)
                {
                    if (row.IsNewRow) continue;
                    var rowData = new List<object>();
                    foreach (DataGridViewCell cell in row.Cells) rowData.Add(cell.Value?.ToString() ?? "");
                    sheetData.Add(rowData);
                }
                await Task.Run(() =>
                {
                    string spreadsheetId = GoogleSheetService.DATA_SPREADSHEET_ID;
                    string targetSheetName = "BUMP";
                    GoogleSheetService.ClearSheet(spreadsheetId, targetSheetName);
                    GoogleSheetService.UpdateBumpSheet(sheetData, spreadsheetId, $"{targetSheetName}!A1");
                });
                MessageBox.Show("Đã tải lên thành công!", "Hoàn tất", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex) { MessageBox.Show($"Lỗi: {ex.Message}", "Lỗi", MessageBoxButtons.OK, MessageBoxIcon.Error); }
            finally { tabTracking_btnUpload.Enabled = true; tabTracking_btnUpload.Text = oldText; }
        }

        private string _downloadFolderPath = Path.Combine(Application.StartupPath, "Downloads");
        private void btn_Download_Click(object sender, EventArgs e)
        {
            try
            {
                if (!Directory.Exists(_downloadFolderPath)) Directory.CreateDirectory(_downloadFolderPath);
                Process.Start(new ProcessStartInfo { FileName = _downloadFolderPath, UseShellExecute = true, Verb = "open" });
            }
            catch (Exception ex) { MessageBox.Show("Không thể mở thư mục: " + ex.Message); }
        }

        private async void print_TimKiem_Click(object sender, EventArgs e)
        {
            string input = tabPrint_inputWaybill.Text.Trim();
            await _printService.SearchAndLoadAsync(input, _printService.CurrentMode);
        }

        private void print_LamMoi_Click(object sender, EventArgs e)
        {
            _trackingService.ClearData();
            _printService.Reset();
            tabPrint_btnSelectAll.Checked = false;
            tabPrint_countSelect.Text = "000";
            tabPrint_countSum.Text = "000";
        }

        private void print_InChuyenHoan_Click(object sender, EventArgs e) => _printService.SetMode(PrintMode.InHoan);
        private void print_InChuyenTiep_Click(object sender, EventArgs e) => _printService.SetMode(PrintMode.InChuyenTiep);
        private void print_InLaiDon_Click(object sender, EventArgs e) => _printService.SetMode(PrintMode.InLaiDon);
        private void print_InReverse_Click(object sender, EventArgs e) => _printService.SetMode(PrintMode.InReverse);
        private void tabPrint_btnSelectAll_CheckedChanged(object sender, EventArgs e) => _printService.SelectAll(tabPrint_btnSelectAll.Checked);

        private async void tabPrint_btnPrint_Click(object sender, EventArgs e)
        {
            if (_printService == null)
            {
                MessageBox.Show("Chưa khởi tạo PrintService.", "Lỗi");
                return;
            }
            var selected = _printService.GetSelectedWaybills();
            if (selected == null || selected.Count == 0)
            {
                MessageBox.Show("Chưa chọn vận đơn nào!", "Thông báo");
                return;
            }
            var originalOrder = tabPrint_inputWaybill.Text
                .Split(new[] { '\r', '\n', ',', ';', ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(x => x.Trim().ToUpper())
                .Where(x => x.Length > 5)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            selected = selected.OrderBy(wb => originalOrder.IndexOf(wb)).ToList();

            tabPrint_btnPrint.Enabled = false;
            tabPrint_btnPrint.Text = "Đang in...";
            try
            {
                int printType = 1;
                int applyTypeCode = (_printService.CurrentMode == PrintMode.InChuyenTiep) ? 2 : 4;
                string pdfUrl = await GetPdfUrlViaCSharpAsync(selected, printType, applyTypeCode);
                if (!string.IsNullOrEmpty(pdfUrl))
                {
                    string localPath = await DownloadPdfToTempFileAsync(pdfUrl);
                    if (!string.IsNullOrEmpty(localPath))
                    {
                        for (int i = 0; i < selected.Count; i++)
                        {
                            var psi = new ProcessStartInfo(localPath) { Verb = "print", UseShellExecute = true };
                            Process.Start(psi);
                            await Task.Delay(800);
                        }
                    }
                }
                else MessageBox.Show("Không lấy được PDF.", "Lỗi");
            }
            catch { }
            finally { tabPrint_btnPrint.Enabled = true; tabPrint_btnPrint.Text = "IN"; }
        }

        private async Task<string> GetPdfUrlViaCSharpAsync(List<string> waybills, int printType, int applyTypeCode)
        {
            try
            {
                await RefreshAuthTokenAsync();
                string token = Main.CapturedAuthToken ?? "";
                using (HttpClient client = new HttpClient())
                {
                    client.DefaultRequestHeaders.Clear();
                    client.DefaultRequestHeaders.Add("authToken", token);
                    client.DefaultRequestHeaders.Add("lang", "VN");
                    client.DefaultRequestHeaders.Add("langType", "VN");
                    client.DefaultRequestHeaders.Add("routeName", "returnAndForwardMaintain");
                    client.DefaultRequestHeaders.Add("routerNameList", "%E6%93%8D%E4%BD%9C%E5%B9%B3%E5%8F%B0%3E%E6%8B%A6%E6%88%AA%E9%80%80%E8%BD%AC%3E%E9%80%80%E8%BD%AC%E4%BB%B6%E7%AE%A1%E7%90%86");
                    client.DefaultRequestHeaders.Add("sec-ch-ua", "\"Not-A.Brand\";v=\"24\", \"Microsoft Edge\";v=\"146\", \"Chromium\";v=\"146\", \"Microsoft Edge WebView2\";v=\"146\"");
                    client.DefaultRequestHeaders.Add("sec-ch-ua-mobile", "?0");
                    client.DefaultRequestHeaders.Add("sec-ch-ua-platform", "\"Windows\"");
                    client.DefaultRequestHeaders.Add("timezone", "GMT+0700");
                    client.DefaultRequestHeaders.Add("User-Agent", CHROME_USER_AGENT);
                    client.DefaultRequestHeaders.Add("Origin", JmsHomeUrl);
                    client.DefaultRequestHeaders.Add("Referer", JmsHomeUrl + "/");
                    client.DefaultRequestHeaders.Add("Accept", "application/json, text/plain, */*");
                    client.DefaultRequestHeaders.Add("Accept-Language", "en-US,en;q=0.9");

                    var payload1 = new { current = 1, size = 20, pringFlag = 0, applyNetworkId = 5165, waybillIds = waybills, applyTimeFrom = "2020-01-01 00:00:00", applyTimeTo = "2030-01-01 23:59:59", pringType = printType, countryId = "1" };
                    var content1 = new StringContent(JsonSerializer.Serialize(payload1), Encoding.UTF8, "application/json");
                    await client.PostAsync(RuntimeConfigManager.Current.BuildJmsApiUrl("operatingplatform/rebackTransferExpress/pringListPage"), content1);

                    var payload2 = new { waybillIds = waybills, applyTypeCode = applyTypeCode, countryId = "1" };
                    var content2 = new StringContent(JsonSerializer.Serialize(payload2), Encoding.UTF8, "application/json");
                    var response = await client.PostAsync(RuntimeConfigManager.Current.BuildJmsApiUrl("operatingplatform/rebackTransferExpress/printWaybill"), content2);
                    string rawJson = await response.Content.ReadAsStringAsync();
                    using (JsonDocument doc = JsonDocument.Parse(rawJson))
                    {
                        if (doc.RootElement.TryGetProperty("data", out JsonElement data) && data.ValueKind == JsonValueKind.String)
                        {
                            string url = data.GetString();
                            if (url.StartsWith("http")) return url;
                        }
                    }
                }
            }
            catch { }
            return null;
        }

        private async Task<string> DownloadPdfToTempFileAsync(string pdfUrl)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(pdfUrl) || !Uri.TryCreate(pdfUrl.Trim(), UriKind.Absolute, out _))
                    throw new Exception("Link PDF không hợp lệ.");
                using (HttpClient client = new HttpClient())
                {
                    client.DefaultRequestHeaders.Add("User-Agent", CHROME_USER_AGENT);
                    byte[] bytes = await client.GetByteArrayAsync(pdfUrl.Trim());
                    string path = Path.Combine(Path.GetTempPath(), $"AutoJMS_{DateTime.Now:yyyyMMdd_HHmmss}.pdf");
                    File.WriteAllBytes(path, bytes);
                    return path;
                }
            }
            catch { return null; }
        }

        private async void tabChat_btnReload_Click(object sender, EventArgs e)
        {
            if (_zaloChatService == null)
            {
                MessageBox.Show("Vui lòng đợi Zalo khởi tạo xong!");
                return;
            }
            tabChat_btnReload.Enabled = false;
            tabChat_btnReload.Text = "Đang tải...";

            await LocalTrackingManager.Instance.PerformIncrementalTrackingAsync();
            PopulateStatusSelect();
            ApplyFilterAndSort();

            tabChat_btnReload.Enabled = true;
            tabChat_btnReload.Text = "Làm mới Data";
        }


        private void tabAbout_btnCheckUpdate_Click(object sender, EventArgs e)
        {
            try
            {
                AutoUpdater.ShowSkipButton = false;
                AutoUpdater.ReportErrors = true;
                AutoUpdater.HttpUserAgent = $"AutoJMS/{Application.ProductVersion}";
                AutoUpdater.ExecutablePath = Path.Combine(Application.StartupPath, "AutoJMS.exe");

                string xmlUrl = RuntimeConfigManager.Current.UpdateXmlUrl;
                if (string.IsNullOrWhiteSpace(xmlUrl))
                {
                    MessageBox.Show("Chưa cấu hình link update trên Firebase hoặc AutoJMS.secure.", "Update Check Failed", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                AutoUpdater.Start(xmlUrl);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Không thể kiểm tra cập nhật.\n\nChi tiết: " + ex.Message,
                    "Update Check Failed",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }
        }
        //====================================================================================================
        // Trong Main.cs - Sự kiện Click của nút bắt đầu
        //====================================================================================================

        private async void tabChat_btnStart_Click(object sender, EventArgs e)
        {
            if (!_isZaloLoaded)
            {
                MessageBox.Show("Zalo chưa sẵn sàng!");
                return;
            }

            tabChat_btnStart.Enabled = false;
            try
            {
                // 1. Lấy tất cả các đơn "Quét phát hàng" từ DataGridView
                var reminders = new List<Reminder>();
                foreach (DataGridViewRow row in tabChat_dataGrid.Rows)
                {
                    if (row.IsNewRow) continue;

                    string trangThai = row.Cells["trangThai"].Value?.ToString() ?? "";
                    if (trangThai.Trim().Equals("Quét phát hàng", StringComparison.OrdinalIgnoreCase))
                    {
                        reminders.Add(new Reminder
                        {
                            maDon = row.Cells["maDon"].Value?.ToString() ?? "",
                            nhanVien = row.Cells["nhanVien"].Value?.ToString() ?? "",
                            trangThai = trangThai
                        });
                    }
                }

                if (reminders.Count == 0)
                {
                    MessageBox.Show("Không có đơn nào ở trạng thái 'Quét phát hàng'.");
                    return;
                }

                // 2. Gom nhóm theo tên nhân viên
                var groups = reminders
                    .GroupBy(r => r.nhanVien.Trim(), StringComparer.OrdinalIgnoreCase)
                    .ToList();

                int totalGroups = groups.Count;
                int successCount = 0;

                for (int i = 0; i < totalGroups; i++)
                {
                    var group = groups[i];
                    string tenNV = group.Key;

                    // Tạo chuỗi mã đơn, mỗi mã một dòng
                    var danhSachMa = group.Select(r => r.maDon).Distinct().ToList();
                    string danhSachMaDon = string.Join("\n", danhSachMa);

                    // Tạo nội dung tin nhắn: mã đơn (xuống dòng) + @TênNV
                    string noiDung = $"@{tenNV}\n{danhSachMaDon}";

                    // Hiển thị trạng thái (có thể dùng label hoặc debug)
                    System.Diagnostics.Debug.WriteLine($"[Gửi] Nhân viên {i + 1}/{totalGroups}: {tenNV} ({danhSachMa.Count} mã)");

                    // 3. Gửi tin nhắn qua Zalo
                    bool result = await _zaloChatService.SendZaloMessage(noiDung);

                    if (result)
                    {
                        successCount++;
                        // Nghỉ 2-3 giây giữa các lần gửi để tránh Zalo chặn spam
                        await Task.Delay(2500);
                    }
                    else
                    {
                        // Nếu gửi thất bại, hỏi người dùng có tiếp tục không
                        var dlgResult = MessageBox.Show(
                            $"Gửi thất bại cho nhân viên: {tenNV}\n\nBạn có muốn tiếp tục gửi những người còn lại không?",
                            "Lỗi gửi tin",
                            MessageBoxButtons.YesNo,
                            MessageBoxIcon.Warning);

                        if (dlgResult == DialogResult.No)
                            break;
                    }
                }

                MessageBox.Show(
                    $"Hoàn tất! Đã gửi thành công {successCount}/{totalGroups} nhân viên.",
                    "Kết quả",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Lỗi: {ex.Message}", "Lỗi", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                tabChat_btnStart.Enabled = true;
            }
        }


        //=========================================================================================
    }
}

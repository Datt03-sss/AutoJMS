using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Sunny.UI;

namespace AutoJMS
{
    /// <summary>
    /// "In Reverse" (tabPrint_inRV).
    ///
    /// Luồng Owner chốt — hai lối vào, chung một nút Tìm kiếm và chung một nút IN:
    ///   nhập tên nhân viên -> chọn tên trong danh sách JMS trả về -> chọn khoảng thời gian,
    ///   HOẶC gõ/quét thẳng mã vào ô "Mã vận đơn"
    ///   -> Tìm kiếm -> lưới hiện đơn kèm bản xem trước -> bỏ tick mã không in -> IN.
    ///
    /// Tab này không đi qua tracking + SafetyGuard. Dữ liệu lấy từ
    /// <see cref="JmsSendWaybillService"/> rồi nạp thẳng vào lưới qua
    /// <see cref="IPrintService.LoadRowsDirect"/>; lệnh in dùng endpoint riêng
    /// <see cref="JmsSendWaybillService.CenterPrintEndpoint"/>.
    ///
    /// Sáu ô nhập của tab đã có sẵn trong Main.Designer.cs — file này chỉ đấu dây, thêm
    /// dòng trạng thái và danh sách chọn nhân viên.
    /// </summary>
    public partial class Main
    {
        private const int ReverseStaffDebounceMs = 450;
        private const int ReverseStaffMinChars = 2;
        private const int ReverseStaffPopupRows = 6;
        private const int ReverseClearSymbol = 61453;   // FontAwesome v4 fa-times
        private const string ReverseHint =
            "Nhập mã vận đơn, hoặc chọn nhân viên + thời gian, rồi bấm Tìm kiếm.";

        // ── controls dựng trong BuildTabPrintInReverseSection ──
        private UILabel _reverseStatus;
        private ListBox _reverseStaffList;

        // ── state ──
        private System.Windows.Forms.Timer _reverseStaffDebounce;
        private CancellationTokenSource _reverseStaffCts;
        private CancellationTokenSource _reverseSearchCts;
        private JmsSendWaybillService.StaffInfo _reverseStaff;
        private bool _reverseSuppressLookup;

        private bool IsReverseModeActive =>
            _printService != null && _printService.CurrentMode == PrintMode.InReverse;

        // ==================================================================================
        // UI
        // ==================================================================================

        /// <summary>
        /// Đấu dây tab "In Reverse". Gọi từ constructor của Main, trước khi AppTheme áp lại,
        /// để dòng trạng thái ăn theme như mọi control dựng động khác.
        /// </summary>
        private void BuildTabPrintInReverseSection()
        {
            if (tabPrint_inRV == null || tabPrint_inRV.IsDisposed) return;
            if (uiTableLayoutPanel24 == null || uiTableLayoutPanel24.IsDisposed) return;
            if (uiTableLayoutPanel24.Controls.Find("tabPrint_reverseStatus", false).Length > 0) return;

            // Hàng thứ ba, cao cố định: hai hàng ô nhập vẫn chia đôi phần còn lại như designer.
            uiTableLayoutPanel24.RowCount = 3;
            uiTableLayoutPanel24.RowStyles.Add(new RowStyle(SizeType.Absolute, 20F));

            _reverseStatus = new UILabel
            {
                Name = "tabPrint_reverseStatus",
                Dock = DockStyle.Fill,
                Text = ReverseHint,
                TextAlign = ContentAlignment.MiddleLeft,
                Font = new Font("Segoe UI", 9F, FontStyle.Regular),
                Margin = new Padding(6, 0, 6, 0)
            };
            uiTableLayoutPanel24.Controls.Add(_reverseStatus, 0, 2);
            uiTableLayoutPanel24.SetColumnSpan(_reverseStatus, 3);

            // Danh sách gợi ý treo trên Form chứ không trong tab page: tab page chỉ cao
            // ~150px nên thả xuống trong đó là bị cắt cụt ngay.
            _reverseStaffList = new ListBox
            {
                Name = "tabPrint_reverseStaffList",
                Visible = false,
                IntegralHeight = false,
                DisplayMember = nameof(JmsSendWaybillService.StaffInfo.Display),
                Font = new Font("Segoe UI", 9F, FontStyle.Regular)
            };
            _reverseStaffList.Click += ReverseStaffList_Commit;
            _reverseStaffList.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Enter) { e.Handled = true; ReverseStaffList_Commit(s, e); }
                else if (e.KeyCode == Keys.Escape) { e.Handled = true; HideReverseStaffPopup(); tabPrint_tenNV?.Focus(); }
            };
            _reverseStaffList.Leave += (s, e) => HideReverseStaffPopup();
            Controls.Add(_reverseStaffList);

            if (tabPrint_tenNV != null && !tabPrint_tenNV.IsDisposed)
            {
                tabPrint_tenNV.Watermark = "Tên nhân viên lấy hàng";
                tabPrint_tenNV.TextChanged += TabPrint_tenNV_TextChanged;
                tabPrint_tenNV.KeyDown += TabPrint_tenNV_KeyDown;
                tabPrint_tenNV.Leave += (s, e) =>
                {
                    if (_reverseStaffList == null || !_reverseStaffList.Focused) HideReverseStaffPopup();
                };

                // Nút "X" trong lòng ô: đổi người tra không phải xoá tay từng ký tự nữa.
                // UITextBox có sẵn nút này (ShowButton + ButtonClick) nên khỏi chồng thêm control.
                tabPrint_tenNV.ShowButton = true;
                tabPrint_tenNV.ButtonSymbol = ReverseClearSymbol;
                tabPrint_tenNV.ButtonSymbolSize = 16;
                tabPrint_tenNV.ButtonWidth = 26;
                tabPrint_tenNV.ButtonClick += (s, e) =>
                {
                    ClearReverseStaffInput();
                    tabPrint_tenNV.Focus();
                    SetReverseStatus(ReverseHint);
                };
            }

            if (tabPrint_maCOD != null && !tabPrint_maCOD.IsDisposed)
                tabPrint_maCOD.Watermark = "Mã khách hàng (tuỳ chọn)";

            _reverseStaffDebounce = new System.Windows.Forms.Timer { Interval = ReverseStaffDebounceMs };
            _reverseStaffDebounce.Tick += ReverseStaffDebounce_Tick;

            // "Làm mới" dọn phần dùng chung của tab IN ĐƠN trong print_LamMoi_Click; sáu ô nhập
            // và nhân viên đã chọn của tab này thì chỉ file này biết. Đăng ký thêm ở đây chứ
            // không sửa handler kia: hàm này chạy sau InitializeComponent nên handler của
            // designer vẫn chạy trước, mình chỉ nối thêm phần của In Reverse.
            if (tabPrint_btnLamMoi != null && !tabPrint_btnLamMoi.IsDisposed)
                tabPrint_btnLamMoi.Click += (s, e) => ResetTabPrintInReverseState();

            // Ô "Mã vận đơn" dùng chung cho cả bốn tab con, nhưng handler Enter của designer
            // bỏ qua In Reverse (ExecuteTabPrintSearchAsync thoát sớm ở mode này). Nối thêm
            // một handler ở đây để Enter — và máy quét, vốn tự bắn Enter sau mỗi mã — chạy
            // đúng lượt tra của tab này, thay vì không làm gì cả như trước.
            if (tabPrint_inputWaybill != null && !tabPrint_inputWaybill.IsDisposed)
                tabPrint_inputWaybill.KeyDown += (s, e) =>
                {
                    if (e.KeyCode != Keys.Enter) return;
                    if (GetTabPrintModeFromSelectedTab() != PrintMode.InReverse) return;
                    e.SuppressKeyPress = true;
                    _ = ExecuteTabPrintReverseSearchAsync();
                };

            ResetReverseTimeRange();
        }

        /// <summary>
        /// Đưa tab về đúng trạng thái lúc vừa mở app: bỏ nhân viên đã chọn, xoá sáu ô nhập, trả
        /// khoảng thời gian về hôm nay. Huỷ luôn hai lượt gọi đang bay — một lượt tìm kiếm về
        /// muộn sẽ nạp lưới lại ngay sau khi người dùng vừa bấm Làm mới.
        /// </summary>
        private void ResetTabPrintInReverseState()
        {
            _reverseSearchCts?.Cancel();
            ClearReverseStaffInput();

            foreach (var box in new[] { tabPrint_maCOD, tabPrint_sdtNG, tabPrint_sdtNN })
                if (box != null && !box.IsDisposed) box.Text = "";

            ResetReverseTimeRange();
            SetReverseStatus(ReverseHint);
        }

        /// <summary>
        /// Quên nhân viên đang chọn và dọn ô tên — dùng cho cả nút "X" lẫn Làm mới. Không đụng
        /// thời gian hay ba ô còn lại: "X" chỉ để đổi người tra, dọn cả tab là việc của Làm mới.
        /// </summary>
        private void ClearReverseStaffInput()
        {
            _reverseStaffCts?.Cancel();
            _reverseStaffDebounce?.Stop();
            _reverseStaff = null;
            HideReverseStaffPopup();

            // Xoá ô tên sẽ bắn TextChanged; chặn lại kẻo tự mở một lượt tra nhân viên với ô rỗng.
            _reverseSuppressLookup = true;
            try
            {
                if (tabPrint_tenNV != null && !tabPrint_tenNV.IsDisposed) tabPrint_tenNV.Text = "";
            }
            finally
            {
                _reverseSuppressLookup = false;
            }
        }

        /// <summary>Mặc định: trọn ngày hôm nay — đúng ca làm việc người dùng hay tra nhất.</summary>
        private void ResetReverseTimeRange()
        {
            DateTime today = DateTime.Today;
            if (tabPrint_timeFrom != null && !tabPrint_timeFrom.IsDisposed)
                tabPrint_timeFrom.Value = today;
            if (tabPrint_timeTo != null && !tabPrint_timeTo.IsDisposed)
                tabPrint_timeTo.Value = today.AddDays(1).AddSeconds(-1);
        }

        private void SetReverseStatus(string message, bool isError = false)
        {
            if (_reverseStatus == null || _reverseStatus.IsDisposed) return;
            if (_reverseStatus.InvokeRequired)
            {
                _reverseStatus.BeginInvoke((MethodInvoker)(() => SetReverseStatus(message, isError)));
                return;
            }

            bool isDark = UI.AppTheme.CurrentTheme == UI.ThemeMode.Dark;
            _reverseStatus.Text = message ?? "";
            _reverseStatus.ForeColor = isError
                ? (isDark ? Color.FromArgb(252, 115, 115) : Color.Red)
                : (isDark ? Color.White : Color.FromArgb(48, 48, 48));
        }

        // ==================================================================================
        // Bước 1 — nhập tên, chọn nhân viên
        // ==================================================================================

        private void TabPrint_tenNV_TextChanged(object sender, EventArgs e)
        {
            if (_reverseSuppressLookup) return;

            // Gõ tiếp nghĩa là mã nhân viên đang giữ không còn khớp ô tên nữa.
            _reverseStaff = null;
            _reverseStaffDebounce?.Stop();
            _reverseStaffDebounce?.Start();
        }

        private void TabPrint_tenNV_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Down && _reverseStaffList != null && _reverseStaffList.Visible)
            {
                e.Handled = true;
                _reverseStaffList.Focus();
                if (_reverseStaffList.Items.Count > 0 && _reverseStaffList.SelectedIndex < 0)
                    _reverseStaffList.SelectedIndex = 0;
                return;
            }

            if (e.KeyCode == Keys.Escape)
            {
                e.Handled = true;
                HideReverseStaffPopup();
                return;
            }

            if (e.KeyCode != Keys.Enter) return;
            e.SuppressKeyPress = true;

            // Enter khi danh sách đang mở = chọn dòng đang sáng; ngược lại = tra ngay,
            // khỏi chờ hết debounce.
            if (_reverseStaffList != null && _reverseStaffList.Visible && _reverseStaffList.SelectedIndex >= 0)
                ReverseStaffList_Commit(sender, e);
            else
                _ = LookupReverseStaffAsync();
        }

        private void ReverseStaffDebounce_Tick(object sender, EventArgs e)
        {
            _reverseStaffDebounce?.Stop();
            _ = LookupReverseStaffAsync();
        }

        private async Task LookupReverseStaffAsync()
        {
            string name = tabPrint_tenNV?.Text?.Trim() ?? "";
            if (name.Length < ReverseStaffMinChars)
            {
                HideReverseStaffPopup();
                return;
            }

            _reverseStaffCts?.Cancel();
            _reverseStaffCts?.Dispose();
            _reverseStaffCts = new CancellationTokenSource();
            var ct = _reverseStaffCts.Token;

            try
            {
                SetReverseStatus($"Đang tra nhân viên \"{name}\"...");
                var staff = await JmsSendWaybillService
                    .SearchStaffAsync(name, SiteContextProvider.Get(), ct)
                    .ConfigureAwait(true);

                if (ct.IsCancellationRequested) return;

                if (staff.Count == 0)
                {
                    HideReverseStaffPopup();
                    SetReverseStatus($"Không tìm thấy nhân viên nào khớp \"{name}\".", true);
                    return;
                }

                ShowReverseStaffPopup(staff);
                SetReverseStatus($"Tìm thấy {staff.Count} nhân viên — chọn một người trong danh sách.");
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                AppLogger.Error("[TabPrint] Tra nhân viên thất bại", ex);
                SetReverseStatus($"Lỗi tra nhân viên: {ex.Message}", true);
            }
        }

        private void ShowReverseStaffPopup(IReadOnlyList<JmsSendWaybillService.StaffInfo> staff)
        {
            if (_reverseStaffList == null || _reverseStaffList.IsDisposed) return;
            if (tabPrint_tenNV == null || tabPrint_tenNV.IsDisposed || !tabPrint_tenNV.IsHandleCreated) return;

            _reverseStaffList.BeginUpdate();
            try
            {
                _reverseStaffList.Items.Clear();
                foreach (var s in staff) _reverseStaffList.Items.Add(s);
            }
            finally
            {
                _reverseStaffList.EndUpdate();
            }

            var anchor = PointToClient(tabPrint_tenNV.PointToScreen(new Point(0, tabPrint_tenNV.Height)));
            int rows = Math.Min(staff.Count, ReverseStaffPopupRows);
            _reverseStaffList.Bounds = new Rectangle(
                anchor.X,
                anchor.Y,
                Math.Max(tabPrint_tenNV.Width, 200),
                Math.Max(_reverseStaffList.ItemHeight * rows + 4, 24));
            _reverseStaffList.SelectedIndex = 0;
            _reverseStaffList.Visible = true;
            _reverseStaffList.BringToFront();
        }

        private void HideReverseStaffPopup()
        {
            if (_reverseStaffList == null || _reverseStaffList.IsDisposed) return;
            _reverseStaffList.Visible = false;
        }

        private void ReverseStaffList_Commit(object sender, EventArgs e)
        {
            if (_reverseStaffList?.SelectedItem is not JmsSendWaybillService.StaffInfo staff) return;

            _reverseStaff = staff;
            HideReverseStaffPopup();

            // Ghi tên đã chọn vào ô mà không kích hoạt lại vòng tra cứu.
            _reverseSuppressLookup = true;
            try
            {
                if (tabPrint_tenNV != null && !tabPrint_tenNV.IsDisposed)
                    tabPrint_tenNV.Text = staff.Name;
            }
            finally
            {
                _reverseSuppressLookup = false;
            }

            _reverseStaffDebounce?.Stop();
            tabPrint_tenNV?.Focus();
            SetReverseStatus($"Đã chọn {staff.Display}. Chọn khoảng thời gian rồi bấm Tìm kiếm.");
        }

        // ==================================================================================
        // Bước 2 — tìm kiếm đơn đã lấy
        // ==================================================================================

        /// <summary>
        /// Tìm kiếm của tab "In Reverse". Hai lối vào chung một nút Tìm kiếm:
        /// <list type="bullet">
        ///   <item>ô "Mã vận đơn" có chữ → tra thẳng theo mã rồi dựng bản xem trước;</item>
        ///   <item>ô đó rỗng → tra theo nhân viên đã chọn + khoảng thời gian.</item>
        /// </list>
        /// Ô mã được xét trước vì nó là thứ người dùng vừa gõ; nhân viên và thời gian có thể
        /// còn sót lại từ lượt tra trước.
        /// </summary>
        private async Task ExecuteTabPrintReverseSearchAsync()
        {
            if (_printService == null) return;
            _printService.SetMode(PrintMode.InReverse);
            HideReverseStaffPopup();

            string manualText = tabPrint_inputWaybill?.Text?.Trim() ?? "";
            if (manualText.Length > 0)
            {
                var codes = ParseWaybillOrder(manualText);
                if (codes.Count == 0)
                {
                    SetReverseStatus("Mã vận đơn không hợp lệ.", true);
                    return;
                }

                await SearchReverseByWaybillAsync(codes).ConfigureAwait(true);
                return;
            }

            if (_reverseStaff == null || string.IsNullOrWhiteSpace(_reverseStaff.Code))
            {
                SetReverseStatus(
                    "Chưa có gì để tra: nhập mã vận đơn, hoặc chọn một nhân viên trong danh sách.", true);
                return;
            }

            DateTime from = tabPrint_timeFrom?.Value ?? DateTime.Today;
            DateTime to = tabPrint_timeTo?.Value ?? DateTime.Today.AddDays(1).AddSeconds(-1);
            if (to < from)
            {
                SetReverseStatus("Thời gian đến phải sau thời gian từ.", true);
                return;
            }

            string siteCode = SiteContextProvider.Get();
            if (string.IsNullOrWhiteSpace(siteCode))
            {
                SetReverseStatus("Chưa xác định được mã bưu cục. Đăng nhập lại JMS rồi thử lại.", true);
                return;
            }

            _reverseSearchCts?.Cancel();
            _reverseSearchCts?.Dispose();
            _reverseSearchCts = new CancellationTokenSource();
            var ct = _reverseSearchCts.Token;

            if (tabPrint_btnTimKiem != null) tabPrint_btnTimKiem.Enabled = false;
            try
            {
                SetReverseStatus($"Đang lấy danh sách đơn của {_reverseStaff.Display}...");
                ClearPrintJobCaches();

                var rows = await JmsSendWaybillService.SearchShippingWaybillsAsync(
                    _reverseStaff.Code,
                    from,
                    to,
                    tabPrint_maCOD?.Text?.Trim() ?? "",
                    ct).ConfigureAwait(true);

                if (ct.IsCancellationRequested) return;

                // Người dùng có thể đã đổi sang tab con khác trong lúc chờ mạng.
                if (_printService.CurrentMode != PrintMode.InReverse) return;

                await LoadReverseRowsAndPreviewAsync(
                    rows, "Không có đơn nào trong khoảng thời gian này.", ct).ConfigureAwait(true);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                AppLogger.Error("[TabPrint] Tìm kiếm In Reverse thất bại", ex);
                SetReverseStatus($"Lỗi tìm kiếm: {ex.Message}", true);
            }
            finally
            {
                if (tabPrint_btnTimKiem != null) tabPrint_btnTimKiem.Enabled = true;
            }
        }

        /// <summary>
        /// Tra theo mã người dùng gõ/quét vào ô "Mã vận đơn". Lấy được dòng nào thì dựng luôn
        /// bản xem trước — luồng Owner chốt là nhập mã → tìm kiếm → xem preview → bấm IN.
        /// </summary>
        private async Task SearchReverseByWaybillAsync(List<string> waybills)
        {
            _reverseSearchCts?.Cancel();
            _reverseSearchCts?.Dispose();
            _reverseSearchCts = new CancellationTokenSource();
            var ct = _reverseSearchCts.Token;

            if (tabPrint_btnTimKiem != null) tabPrint_btnTimKiem.Enabled = false;
            try
            {
                SetReverseStatus($"Đang tra {waybills.Count} mã vận đơn...");
                ClearPrintJobCaches();

                var rows = await JmsSendWaybillService
                    .SearchShippingWaybillsByNoAsync(string.Join(",", waybills), ct)
                    .ConfigureAwait(true);

                if (ct.IsCancellationRequested) return;
                if (_printService.CurrentMode != PrintMode.InReverse) return;

                await LoadReverseRowsAndPreviewAsync(
                    rows, "Không tìm thấy đơn nào khớp mã đã nhập.", ct).ConfigureAwait(true);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                AppLogger.Error("[TabPrint] Tìm kiếm In Reverse theo mã thất bại", ex);
                SetReverseStatus($"Lỗi tìm kiếm: {ex.Message}", true);
            }
            finally
            {
                if (tabPrint_btnTimKiem != null) tabPrint_btnTimKiem.Enabled = true;
            }
        }

        /// <summary>
        /// Phần đuôi dùng chung của hai lối tra: nạp lưới, tick hết, rồi dựng bản xem trước.
        /// Cả hai lối đều xem trước — bản đang nhìn CHÍNH LÀ bản nút IN sẽ đẩy ra máy in.
        /// </summary>
        private async Task LoadReverseRowsAndPreviewAsync(
            IReadOnlyList<TrackingRow> rows, string emptyMessage, CancellationToken ct)
        {
            _printService.LoadRowsDirect(rows, PrintMode.InReverse);
            _printService.SelectAll(true);
            if (tabPrint_btnSelectAll != null) tabPrint_btnSelectAll.Checked = true;

            if (rows.Count == 0)
            {
                if (tabPrint_printPreview?.CoreWebView2 != null)
                    tabPrint_printPreview.CoreWebView2.Navigate("about:blank");
                SetReverseStatus(emptyMessage, true);
                return;
            }

            // Lấy danh sách từ chính lưới chứ không từ rows: đó là nguồn ExecutePrintAsync đọc
            // khi bấm IN, nên khoá cache hai bên mới khớp nhau từng ký tự.
            await ShowReversePreviewAsync(_printService.GetSelectedWaybills(), ct).ConfigureAwait(true);
        }

        /// <summary>
        /// Bản xem trước của JMS: vẫn endpoint in, nhưng <c>printMode=1</c> nên không tính vào
        /// ba lượt in của vận đơn. Phản hồi trả sẵn link PDF đã ký trong <c>data.pdfFullPath</c>;
        /// tải về một lần rồi vừa đem hiển thị vừa cất vào cache in, nên bấm IN là in đúng tờ
        /// đang nhìn mà không gọi lại JMS — xem <see cref="CacheReversePrintJob"/>.
        /// <para>Không bao giờ ném: lưới đã có dữ liệu, hỏng preview thì vẫn bấm IN được.</para>
        /// </summary>
        private async Task ShowReversePreviewAsync(List<string> waybills, CancellationToken ct)
        {
            SetReverseStatus($"{waybills.Count} đơn — đang lấy bản xem trước...");
            try
            {
                using var response = await JmsApiClient.PostJsonAsync(
                    AppConfig.Current.BuildJmsApiUrl(JmsSendWaybillService.CenterPrintEndpoint),
                    JmsSendWaybillService.BuildCenterPrintPayload(
                        waybills, JmsSendWaybillService.CenterPrintModePreview),
                    routeName: JmsSendWaybillService.CenterPrintRouteName,
                    routerNameList: JmsSendWaybillService.CenterPrintRouterNameList,
                    ct: ct).ConfigureAwait(true);

                string body = response == null
                    ? ""
                    : await response.Content.ReadAsStringAsync(ct).ConfigureAwait(true);
                if (ct.IsCancellationRequested) return;

                // JMS nhét lỗi nghiệp vụ vào thân HTTP 200 (vd "quá 3 lượt in"), đọc trước khi
                // bóc URL — nếu không thì mọi lỗi đều hiện thành "không có link PDF".
                string error = JmsSendWaybillService.ReadBusinessError(body);
                if (error != null)
                {
                    SetReverseStatus($"{waybills.Count} đơn — không xem trước được. {error}", true);
                    return;
                }

                string pdfUrl = ResolvePrintPdfUrl(ParsePrintWaybillResponse(body, waybills[0]), waybills[0]);
                if (tabPrint_printPreview == null || tabPrint_printPreview.IsDisposed) return;

                TryReadPrintConfig(out int keepPdfs, out _);
                string localPath = await DownloadPdfWithRetryAsync(pdfUrl, keepPdfs, waybills[0])
                    .ConfigureAwait(true);
                if (ct.IsCancellationRequested) return;

                if (CacheReversePrintJob(waybills, localPath))
                {
                    NavigatePreviewTo(localPath);
                    SetReverseStatus(
                        $"{waybills.Count} đơn — xem trước bên phải. Bỏ tick mã không in rồi bấm IN.");
                    return;
                }

                // Tải hụt: vẫn cho xem bằng link đã ký để không mất luôn bản xem trước, nhưng
                // cache in trống nên lượt IN tới sẽ phải hỏi JMS một lần nữa.
                if (tabPrint_printPreview.CoreWebView2 != null)
                    tabPrint_printPreview.CoreWebView2.Navigate(pdfUrl);
                else
                    tabPrint_printPreview.Source = new Uri(pdfUrl);

                SetReverseStatus(
                    $"{waybills.Count} đơn — xem trước bên phải (chưa giữ được bản in, bấm IN sẽ lấy lại).");
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                AppLogger.Error("[TabPrint] Lấy bản xem trước In Reverse thất bại", ex);
                SetReverseStatus($"{waybills.Count} đơn — không xem trước được: {ex.Message}", true);
            }
        }

        // ==================================================================================
        // Bước 3 — in
        // ==================================================================================

        /// <summary>
        /// Cất bản vừa xem trước vào cache in, dưới ĐÚNG khoá <c>ExecutePrintAsync</c> sẽ tra
        /// khi bấm IN — <c>BuildPrintPdfCacheKey(selected, printType: 1, applyTypeCode: 4)</c>,
        /// bộ số tab IN ĐƠN dùng cho mọi mode trừ "In chuyển tiếp". Khoá khớp thì lượt IN là
        /// một lần cache hit: đẩy thẳng bytes này ra máy in, không gọi lại JMS nên không ăn
        /// thêm lượt in nào trong ba lượt của vận đơn.
        /// <para>
        /// Đổi hai con số kia ở <c>ExecutePrintAsync</c> mà quên đổi ở đây thì không ai báo lỗi:
        /// cache chỉ lặng lẽ trượt và lượt IN quay lại hỏi JMS.
        /// </para>
        /// <para>
        /// TTL mượn của "In lại đơn" (30 phút) chứ không dùng 60 giây mặc định của cache in:
        /// người dùng còn soi bản in, còn bỏ tick từng mã, một phút là quá ngắn cho thao tác tay.
        /// </para>
        /// </summary>
        private bool CacheReversePrintJob(List<string> waybills, string localPath)
        {
            if (string.IsNullOrWhiteSpace(localPath) || !File.Exists(localPath)) return false;

            byte[] pdfBytes = File.ReadAllBytes(localPath);
            if (pdfBytes.Length == 0) return false;

            string cacheKey = BuildPrintPdfCacheKey(waybills, 1, 4);
            RememberPrintJob(cacheKey, new PrintJobCacheEntry
            {
                CacheKey = cacheKey,
                WaybillNo = waybills[0],
                PdfBytes = pdfBytes,
                LocalPdfPath = localPath,
                CreatedAt = DateTime.Now,
                ExpiresAt = DateTime.Now.Add(ReprintJobTtl),
                PdfHash = ComputeSha256(pdfBytes)
            });
            return true;
        }

        /// <summary>
        /// URL + payload cho lệnh in của màn "Quản lý vận đơn gửi". Endpoint và body khác hẳn
        /// luồng in mặc định (<c>rebackTransferExpress/printWaybill</c>), nhưng phần còn lại
        /// của pipeline — parse, lấy link PDF, tải về, đẩy spooler — dùng chung.
        /// <para>
        /// Đường dự phòng, không phải đường chính: bấm IN với đúng bộ mã đã xem trước là cache
        /// hit nên không ai gọi tới đây. Chỉ khi người dùng bỏ tick vài mã — bộ mã đổi thì bản
        /// PDF cũ không còn đúng nữa — mới cần dựng lại, và lúc đó vẫn xin <c>printMode=1</c>:
        /// tờ giấy do spooler in ra, JMS không cần đếm thêm một lượt để việc đó xảy ra.
        /// </para>
        /// </summary>
        private (string Url, string Payload, string RouteName, string RouterNameList) BuildReversePrintRequest(
            List<string> waybills)
        {
            return (
                AppConfig.Current.BuildJmsApiUrl(JmsSendWaybillService.CenterPrintEndpoint),
                JmsSendWaybillService.BuildCenterPrintPayload(
                    waybills, JmsSendWaybillService.CenterPrintModePreview),
                JmsSendWaybillService.CenterPrintRouteName,
                JmsSendWaybillService.CenterPrintRouterNameList);
        }
    }
}

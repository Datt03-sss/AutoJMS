using System;
using System.Collections.Generic;
using System.Drawing;
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
    /// Luồng Owner chốt:
    ///   nhập tên nhân viên -> chọn tên trong danh sách JMS trả về -> chọn khoảng thời gian
    ///   -> Tìm kiếm -> lưới hiện các đơn nhân viên đó đã lấy -> tick chọn -> IN.
    ///
    /// Đây là tab con duy nhất KHÔNG tra theo mã vận đơn, nên nó không dùng ô
    /// <c>tabPrint_inputWaybill</c> và không đi qua tracking + SafetyGuard. Dữ liệu lấy từ
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
                Text = "Nhập tên nhân viên, chọn trong danh sách rồi bấm Tìm kiếm.",
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
            }

            if (tabPrint_maCOD != null && !tabPrint_maCOD.IsDisposed)
                tabPrint_maCOD.Watermark = "Mã khách hàng (tuỳ chọn)";

            _reverseStaffDebounce = new System.Windows.Forms.Timer { Interval = ReverseStaffDebounceMs };
            _reverseStaffDebounce.Tick += ReverseStaffDebounce_Tick;

            ResetReverseTimeRange();
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
        /// Tìm kiếm của tab "In Reverse": nhân viên đã chọn + khoảng thời gian, không dùng ô
        /// mã vận đơn. Gọi từ nút Tìm kiếm khi tab con đang mở là In Reverse.
        /// </summary>
        private async Task ExecuteTabPrintReverseSearchAsync()
        {
            if (_printService == null) return;
            _printService.SetMode(PrintMode.InReverse);
            HideReverseStaffPopup();

            if (_reverseStaff == null || string.IsNullOrWhiteSpace(_reverseStaff.Code))
            {
                SetReverseStatus("Chưa chọn nhân viên. Nhập tên rồi chọn một người trong danh sách.", true);
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
                    siteCode,
                    _reverseStaff.Code,
                    from,
                    to,
                    tabPrint_maCOD?.Text?.Trim() ?? "",
                    ct).ConfigureAwait(true);

                if (ct.IsCancellationRequested) return;

                // Người dùng có thể đã đổi sang tab con khác trong lúc chờ mạng.
                if (_printService.CurrentMode != PrintMode.InReverse) return;

                _printService.LoadRowsDirect(rows, PrintMode.InReverse);
                _printService.SelectAll(true);
                if (tabPrint_btnSelectAll != null) tabPrint_btnSelectAll.Checked = true;

                if (tabPrint_printPreview?.CoreWebView2 != null)
                    tabPrint_printPreview.CoreWebView2.Navigate("about:blank");

                SetReverseStatus(rows.Count == 0
                    ? "Không có đơn nào trong khoảng thời gian này."
                    : $"{rows.Count} đơn. Bỏ tick những mã không in rồi bấm IN.",
                    rows.Count == 0);
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

        // ==================================================================================
        // Bước 3 — in
        // ==================================================================================

        /// <summary>
        /// URL + payload cho lệnh in của màn "Quản lý vận đơn gửi". Endpoint và body khác hẳn
        /// luồng in mặc định (<c>rebackTransferExpress/printWaybill</c>), nhưng phần còn lại
        /// của pipeline — parse, lấy link PDF, tải về, đẩy spooler — dùng chung.
        /// </summary>
        private (string Url, string Payload, string RouteName, string RouterNameList) BuildReversePrintRequest(
            List<string> waybills)
        {
            return (
                AppConfig.Current.BuildJmsApiUrl(JmsSendWaybillService.CenterPrintEndpoint),
                JmsSendWaybillService.BuildCenterPrintPayload(waybills),
                JmsSendWaybillService.CenterPrintRouteName,
                JmsSendWaybillService.CenterPrintRouterNameList);
        }
    }
}

using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

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
    /// Toàn bộ giao diện của tab dựng bằng code trong file này — sáu ô nhập, dòng trạng thái,
    /// danh sách chọn nhân viên và cụm nút dưới lưới. Không còn gì của tab này nằm trong
    /// Main.Designer.cs ngoài chính TabPage <c>tabPrint_inRV</c>, và không dùng control
    /// SunnyUI nào: bản dựng bằng designer lồng UIPanel trong UITableLayoutPanel nên mỗi lần
    /// đổi kích thước là một lượt vẽ lại thủ công của cả cụm.
    /// </summary>
    public partial class Main
    {
        private const int ReverseStaffDebounceMs = 450;
        private const int ReverseStaffMinChars = 2;
        private const int ReverseStaffPopupRows = 6;
        private const string ReversePrintFolderName = "Thu hồi đã in";

        // Chiều cao ba dải của một ô nhập — nhãn trên, ô nhập dưới — và của dòng trạng thái.
        private const int ReverseCaptionHeight = 20;
        private const int ReverseInputHeight = 30;
        private const int ReverseStatusHeight = 26;

        /// <summary>
        /// Số đơn hiện mỗi trang lưới. Bằng đúng <c>PageSize</c> của
        /// <see cref="JmsSendWaybillService"/> nên một trang API là một trang lưới.
        /// </summary>
        private const int ReversePageSize = 20;

        /// <summary>
        /// Owner chốt: bản in thu hồi cần tra lại theo NGÀY, nên thư mục riêng của tab này dọn
        /// theo tuổi file chứ không theo <c>KeepRecentPdfCount</c> như "Vận đơn đã in".
        /// </summary>
        private const int ReversePrintRetentionDays = 7;
        private const string ReverseHint =
            "Nhập mã vận đơn, hoặc chọn nhân viên + thời gian, rồi bấm Tìm kiếm.";

        private static readonly Font ReverseFieldFont = new("Segoe UI Semibold", 12F, FontStyle.Bold);
        private static readonly Font ReverseUiFont = new("Segoe UI", 9F, FontStyle.Regular);

        // ── sáu ô nhập, dựng trong BuildReverseInputPanel ──
        private DateTimePicker tabPrint_timeFrom;
        private DateTimePicker tabPrint_timeTo;
        private TextBox tabPrint_tenNV;
        private TextBox tabPrint_maCOD;
        private TextBox tabPrint_sdtNG;
        private TextBox tabPrint_sdtNN;

        // ── controls dựng trong BuildTabPrintInReverseSection ──
        private Label _reverseStatus;
        private ListBox _reverseStaffList;
        private FlowLayoutPanel _reverseGridToolbar;
        private Button _reversePrevPage;
        private Button _reverseNextPage;
        private Label _reversePageLabel;
        private Button _reverseClearStaff;
        private readonly List<Label> _reverseCaptions = new();
        private readonly List<Button> _reverseToolbarButtons = new();

        // ── state ──
        private System.Windows.Forms.Timer _reverseStaffDebounce;
        private CancellationTokenSource _reverseStaffCts;
        private CancellationTokenSource _reverseSearchCts;
        private JmsSendWaybillService.StaffInfo _reverseStaff;
        private bool _reverseSuppressLookup;
        private bool _reverseStatusIsError;

        /// <summary>
        /// Toàn bộ kết quả của lượt tra gần nhất, đã sắp xếp. Lưới chỉ giữ 20 dòng của trang
        /// đang xem nên danh sách đầy đủ phải nằm ở đây.
        /// </summary>
        private readonly List<TrackingRow> _reverseAllRows = new();
        private int _reversePageIndex;

        private int ReversePageCount =>
            Math.Max(1, (_reverseAllRows.Count + ReversePageSize - 1) / ReversePageSize);

        /// <summary>Phần đuôi "(trang x/y của N đơn)" — bỏ hẳn khi chỉ có một trang.</summary>
        private string ReversePageSuffix => ReversePageCount <= 1
            ? ""
            : $" (trang {_reversePageIndex + 1}/{ReversePageCount} của {_reverseAllRows.Count} đơn)";

        private bool IsReverseModeActive =>
            _printService != null && _printService.CurrentMode == PrintMode.InReverse;

        // ==================================================================================
        // UI
        // ==================================================================================

        /// <summary>
        /// Dựng và đấu dây tab "In Reverse". Gọi từ constructor của Main, trước khi AppTheme áp
        /// lại — AppTheme chỉ nhận ra control SunnyUI nên màu của cụm này do
        /// <see cref="ApplyReverseTheme"/> tự đặt.
        /// </summary>
        private void BuildTabPrintInReverseSection()
        {
            if (tabPrint_inRV == null || tabPrint_inRV.IsDisposed) return;
            if (tabPrint_inRV.Controls.Find("tabPrint_reverseLayout", false).Length > 0) return;

            BuildReverseInputPanel();

            // Danh sách gợi ý treo trên Form chứ không trong tab page: tab page chỉ cao
            // ~150px nên thả xuống trong đó là bị cắt cụt ngay.
            _reverseStaffList = new ListBox
            {
                Name = "tabPrint_reverseStaffList",
                Visible = false,
                IntegralHeight = false,
                DisplayMember = nameof(JmsSendWaybillService.StaffInfo.Display),
                Font = ReverseUiFont
            };
            _reverseStaffList.Click += ReverseStaffList_Commit;
            _reverseStaffList.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Enter) { e.Handled = true; ReverseStaffList_Commit(s, e); }
                else if (e.KeyCode == Keys.Escape) { e.Handled = true; HideReverseStaffPopup(); tabPrint_tenNV?.Focus(); }
            };
            _reverseStaffList.Leave += (s, e) => HideReverseStaffPopup();
            Controls.Add(_reverseStaffList);

            tabPrint_tenNV.TextChanged += TabPrint_tenNV_TextChanged;
            tabPrint_tenNV.KeyDown += TabPrint_tenNV_KeyDown;
            tabPrint_tenNV.Leave += (s, e) =>
            {
                if (_reverseStaffList == null || !_reverseStaffList.Focused) HideReverseStaffPopup();
            };

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

            // Nút IN cũng dùng chung cho bốn tab con. Ba tab kia đi pipeline mặc định trong
            // ExecutePrintAsync; In Reverse cần đường riêng — thư mục tải riêng, dọn theo ngày,
            // và giữ nguyên lưới sau khi in. Chen một bộ phân luồng vào TRƯỚC handler của
            // designer (chỉ cộng thêm handler là không đủ: handler kia vẫn chạy và vẫn in theo
            // đường cũ). Mode khác thì gọi lại đúng handler đó, nên ba tab kia không đổi một
            // dòng nào. Nếu dây này đứt, BuildReversePrintRequest là chốt chặn — xem hàm đó.
            if (tabPrint_btnPrint != null && !tabPrint_btnPrint.IsDisposed)
            {
                tabPrint_btnPrint.Click -= tabPrint_btnPrint_Click;
                tabPrint_btnPrint.Click += TabPrint_btnPrint_Dispatch;
            }

            BuildReverseGridToolbar();
            ResetReverseTimeRange();
            ApplyReverseTheme();
        }

        /// <summary>
        /// Sáu ô nhập của tab, dựng bằng code trên một TableLayoutPanel 3 cột — đúng bố cục cũ
        /// của designer: hàng 1 "Thời gian từ / Thời gian đến / SĐT người gửi", hàng 2
        /// "Tên nhân viên / Tên - Mã KH / SĐT người nhận", hàng 3 là dòng trạng thái trải hết
        /// ba cột.
        /// </summary>
        private void BuildReverseInputPanel()
        {
            tabPrint_timeFrom = NewReverseDatePicker("tabPrint_timeFrom");
            tabPrint_timeTo = NewReverseDatePicker("tabPrint_timeTo");
            tabPrint_tenNV = NewReverseTextBox("tabPrint_tenNV", "Tên nhân viên lấy hàng");
            tabPrint_maCOD = NewReverseTextBox("tabPrint_maCOD", "Mã khách hàng (tuỳ chọn)");
            tabPrint_sdtNG = NewReverseTextBox("tabPrint_sdtNG", "");
            tabPrint_sdtNN = NewReverseTextBox("tabPrint_sdtNN", "");

            // Nút "X" nằm sát mép phải ô tên: đổi người tra không phải xoá tay từng ký tự.
            // TabStop = false để Tab vẫn nhảy thẳng từ ô tên sang ô kế tiếp như trước.
            _reverseClearStaff = new Button
            {
                Name = "tabPrint_reverseClearStaff",
                Text = "✕",
                Dock = DockStyle.Right,
                Width = 28,
                FlatStyle = FlatStyle.Flat,
                TabStop = false,
                Cursor = Cursors.Hand,
                Font = ReverseUiFont,
                UseVisualStyleBackColor = false
            };
            _reverseClearStaff.FlatAppearance.BorderSize = 0;
            _reverseClearStaff.Click += (s, e) =>
            {
                ClearReverseStaffInput();
                tabPrint_tenNV.Focus();
                SetReverseStatus(ReverseHint);
            };

            _reverseStatus = new Label
            {
                Name = "tabPrint_reverseStatus",
                Dock = DockStyle.Fill,
                AutoSize = false,
                Text = ReverseHint,
                TextAlign = ContentAlignment.MiddleLeft,
                Font = ReverseUiFont,
                Margin = new Padding(6, 0, 6, 0)
            };

            var layout = new TableLayoutPanel
            {
                Name = "tabPrint_reverseLayout",
                Dock = DockStyle.Fill,
                ColumnCount = 3,
                RowCount = 3,
                Margin = new Padding(0),
                Padding = new Padding(0)
            };
            for (int i = 0; i < 3; i++)
                layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F / 3F));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 50F));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 50F));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, ReverseStatusHeight));

            layout.Controls.Add(NewReverseField("Thời gian từ:", tabPrint_timeFrom, null), 0, 0);
            layout.Controls.Add(NewReverseField("Thời gian đến:", tabPrint_timeTo, null), 1, 0);
            layout.Controls.Add(NewReverseField("SĐT người gửi:", tabPrint_sdtNG, null), 2, 0);
            layout.Controls.Add(NewReverseField("Tên nhân viên:", tabPrint_tenNV, _reverseClearStaff), 0, 1);
            layout.Controls.Add(NewReverseField("Tên - Mã KH", tabPrint_maCOD, null), 1, 1);
            layout.Controls.Add(NewReverseField("SĐT người nhận:", tabPrint_sdtNN, null), 2, 1);
            layout.Controls.Add(_reverseStatus, 0, 2);
            layout.SetColumnSpan(_reverseStatus, 3);

            tabPrint_inRV.Controls.Add(layout);
        }

        /// <summary>
        /// Một ô của lưới nhập: nhãn trên, ô nhập dưới, và tuỳ chọn một nút nhỏ sát mép phải ô.
        /// </summary>
        private Panel NewReverseField(string caption, Control input, Control trailing)
        {
            var label = new Label
            {
                Dock = DockStyle.Top,
                AutoSize = false,
                Height = ReverseCaptionHeight,
                Text = caption,
                Font = ReverseFieldFont,
                TextAlign = ContentAlignment.MiddleLeft
            };
            _reverseCaptions.Add(label);

            // Dải chứa ô nhập cao cố định: nút phụ Dock=Right luôn cao bằng đúng ô nhập.
            var inputRow = new Panel { Dock = DockStyle.Top, Height = ReverseInputHeight };
            inputRow.Controls.Add(input);
            if (trailing != null) inputRow.Controls.Add(trailing);

            // Dock xếp theo z-order ngược: chỉ số CAO dock trước và lấy mép ngoài. Thêm dải ô
            // nhập trước rồi mới tới nhãn, để nhãn chiếm mép trên và dải kia nằm ngay dưới.
            var cell = new Panel { Dock = DockStyle.Fill, Margin = new Padding(5, 2, 5, 2) };
            cell.Controls.Add(inputRow);
            cell.Controls.Add(label);
            return cell;
        }

        private static TextBox NewReverseTextBox(string name, string placeholder) => new()
        {
            Name = name,
            Dock = DockStyle.Fill,
            BorderStyle = BorderStyle.FixedSingle,
            Font = ReverseFieldFont,
            PlaceholderText = placeholder
        };

        private static DateTimePicker NewReverseDatePicker(string name) => new()
        {
            Name = name,
            Dock = DockStyle.Fill,
            Font = ReverseFieldFont,
            Format = DateTimePickerFormat.Custom,
            CustomFormat = "yyyy-MM-dd HH:mm:ss"
        };

        /// <summary>
        /// Nút "Chưa in", bộ chuyển trang và "Copy mã đã chọn", đặt cùng hàng với "Chọn tất cả"
        /// (uiPanel20). Hàng đó của chung bốn tab con nên cả cụm nằm trong một FlowLayoutPanel
        /// riêng và chỉ hiện khi In Reverse đang mở — ba tab kia nhìn y như trước.
        /// </summary>
        private void BuildReverseGridToolbar()
        {
            if (uiPanel20 == null || uiPanel20.IsDisposed) return;
            if (uiPanel20.Controls.Find("tabPrint_reverseGridToolbar", false).Length > 0) return;

            var unprinted = NewReverseToolbarButton("tabPrint_reverseUnprinted", "Chưa in", 84);
            unprinted.Click += (s, e) => SelectReverseUnprintedOnPage();

            _reversePrevPage = NewReverseToolbarButton("tabPrint_reversePrevPage", "‹", 34);
            _reversePrevPage.Click += (s, e) => _ = TurnReversePageAsync(-1);

            _reversePageLabel = new Label
            {
                Name = "tabPrint_reversePageLabel",
                AutoSize = false,
                Size = new Size(56, 29),
                Margin = new Padding(0, 3, 0, 3),
                Text = "0/0",
                TextAlign = ContentAlignment.MiddleCenter,
                Font = ReverseUiFont
            };

            _reverseNextPage = NewReverseToolbarButton("tabPrint_reverseNextPage", "›", 34);
            _reverseNextPage.Click += (s, e) => _ = TurnReversePageAsync(1);

            var copy = NewReverseToolbarButton("tabPrint_reverseCopy", "Copy mã đã chọn", 132);
            copy.Click += (s, e) => CopyReverseSelectionToClipboard();

            _reverseGridToolbar = new FlowLayoutPanel
            {
                Name = "tabPrint_reverseGridToolbar",
                Dock = DockStyle.Left,
                AutoSize = true,
                WrapContents = false,
                Margin = new Padding(0),
                Visible = false
            };
            _reverseGridToolbar.Controls.AddRange(new Control[]
                { unprinted, _reversePrevPage, _reversePageLabel, _reverseNextPage, copy });

            uiPanel20.Controls.Add(_reverseGridToolbar);

            // Dock=Left xếp theo z-order ngược — chỉ số CAO dock trước nên bám sát mép trái.
            // Đẩy cụm này về chỉ số 0 để "Chọn tất cả" giữ nguyên chỗ cũ, cụm mới nằm bên phải.
            _reverseGridToolbar.BringToFront();

            // Đổi tab con thì ẩn/hiện theo. Nối thêm handler thay vì sửa
            // TabPrint_printFunc_SelectedIndexChanged trong Main.cs (Protected File).
            if (tabPrint_printFunc != null && !tabPrint_printFunc.IsDisposed)
                tabPrint_printFunc.SelectedIndexChanged += (s, e) => SyncReverseGridToolbarVisibility();

            UpdateReversePagerUi();
            SyncReverseGridToolbarVisibility();
        }

        private Button NewReverseToolbarButton(string name, string text, int width)
        {
            var button = new Button
            {
                Name = name,
                Text = text,
                Size = new Size(width, 29),
                Margin = new Padding(6, 3, 0, 3),
                Font = ReverseUiFont,
                FlatStyle = FlatStyle.Flat,
                UseVisualStyleBackColor = false
            };
            _reverseToolbarButtons.Add(button);
            return button;
        }

        private void SyncReverseGridToolbarVisibility()
        {
            if (_reverseGridToolbar == null || _reverseGridToolbar.IsDisposed) return;
            _reverseGridToolbar.Visible = GetTabPrintModeFromSelectedTab() == PrintMode.InReverse;

            // AppTheme.Apply() chỉ nhận ra control SunnyUI nên cụm này không được đổi màu theo.
            // Bám vào lượt đổi tab con: đổi theme xong quay lại tab là màu đã đúng.
            ApplyReverseTheme();
        }

        /// <summary>
        /// Tô màu cụm control của tab theo bảng màu đang dùng. Gọi lúc dựng và mỗi lần đổi tab
        /// con. <see cref="DateTimePicker"/> là control của Windows, không nhận BackColor —
        /// hai ô thời gian luôn giữ nền sáng kể cả ở theme tối.
        /// </summary>
        private void ApplyReverseTheme()
        {
            if (_reverseStatus == null || _reverseStatus.IsDisposed) return;

            var colors = UI.AppTheme.Colors;
            foreach (var box in new Control[] { tabPrint_tenNV, tabPrint_maCOD, tabPrint_sdtNG, tabPrint_sdtNN })
            {
                if (box == null || box.IsDisposed) continue;
                box.BackColor = colors.InputBackground;
                box.ForeColor = colors.TextPrimary;
            }

            foreach (var caption in _reverseCaptions)
                caption.ForeColor = colors.TextPrimary;

            foreach (var button in _reverseToolbarButtons)
            {
                button.BackColor = colors.InputBackground;
                button.ForeColor = colors.TextPrimary;
                button.FlatAppearance.BorderColor = colors.InputBorder;
            }

            if (_reverseClearStaff != null && !_reverseClearStaff.IsDisposed)
            {
                _reverseClearStaff.BackColor = colors.InputBackground;
                _reverseClearStaff.ForeColor = colors.PrimaryAccent;
            }

            if (_reversePageLabel != null) _reversePageLabel.ForeColor = colors.TextPrimary;
            if (_reverseStaffList != null)
            {
                _reverseStaffList.BackColor = colors.InputBackground;
                _reverseStaffList.ForeColor = colors.TextPrimary;
            }

            // Dòng trạng thái tự chọn màu đỏ/thường trong SetReverseStatus, phát lại câu đang
            // hiện để nó tính lại theo theme mới thay vì ghi đè bằng TextPrimary.
            SetReverseStatus(_reverseStatus.Text, _reverseStatusIsError);
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

            _reverseAllRows.Clear();
            _reversePageIndex = 0;
            UpdateReversePagerUi();

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
            _reverseStatusIsError = isError;
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
        /// Phần đuôi dùng chung của hai lối tra: giữ cả kết quả lại, rồi mở trang đầu.
        /// </summary>
        private async Task LoadReverseRowsAndPreviewAsync(
            IReadOnlyList<TrackingRow> rows, string emptyMessage, CancellationToken ct)
        {
            // Mặc định xếp thời gian nhận hàng giảm dần — đơn mới nhất lên đầu, đúng thứ tự
            // người dùng cần in trong ca. collectTime của JMS luôn là "yyyy-MM-dd HH:mm:ss"
            // nên so chuỗi ordinal đã đúng thứ tự thời gian — khỏi DateTime.Parse rồi phải
            // đoán culture. Lưới tab IN ĐƠN khoá sort trên header (PrintService.DisableSorting)
            // nên đây là thứ tự duy nhất người dùng thấy, và cũng là thứ tự trang của bản in
            // vì GetSelectedWaybills đọc theo dòng lưới.
            _reverseAllRows.Clear();
            _reverseAllRows.AddRange(rows
                .Where(r => r != null)
                .OrderByDescending(r => r.ThoiGianNhanHang ?? "", StringComparer.Ordinal));

            if (_reverseAllRows.Count == 0)
            {
                _reversePageIndex = 0;
                _printService.LoadRowsDirect(_reverseAllRows, PrintMode.InReverse);
                UpdateReversePagerUi();
                if (tabPrint_printPreview?.CoreWebView2 != null)
                    tabPrint_printPreview.CoreWebView2.Navigate("about:blank");
                SetReverseStatus(emptyMessage, true);
                return;
            }

            await ShowReversePageAsync(0, ct).ConfigureAwait(true);
        }

        /// <summary>
        /// Nạp một trang <see cref="ReversePageSize"/> đơn vào lưới rồi dựng bản xem trước của
        /// đúng trang đó. Phân trang phía client: một lượt tra đã kéo đủ mọi trang của JMS về
        /// <see cref="_reverseAllRows"/> nên đổi trang KHÔNG gọi lại API tra — chỉ bản xem
        /// trước phải gọi, mà nó chạy <c>printMode=1</c> nên không tốn lượt in nào.
        /// </summary>
        private async Task ShowReversePageAsync(int pageIndex, CancellationToken ct)
        {
            _reversePageIndex = Math.Clamp(pageIndex, 0, ReversePageCount - 1);

            _printService.LoadRowsDirect(CurrentReversePageRows(), PrintMode.InReverse);
            _printService.SelectAll(true);
            if (tabPrint_btnSelectAll != null) tabPrint_btnSelectAll.Checked = true;
            UpdateReversePagerUi();

            if (_reverseAllRows.Count == 0) return;

            // Lấy danh sách từ chính lưới chứ không từ rows: đó là nguồn ExecutePrintAsync đọc
            // khi bấm IN, nên khoá cache hai bên mới khớp nhau từng ký tự.
            await ShowReversePreviewAsync(_printService.GetSelectedWaybills(), ct).ConfigureAwait(true);
        }

        private List<TrackingRow> CurrentReversePageRows() => _reverseAllRows
            .Skip(_reversePageIndex * ReversePageSize)
            .Take(ReversePageSize)
            .ToList();

        /// <summary>
        /// Lật trang. Dùng chung <see cref="_reverseSearchCts"/> với lượt tra: lật trang giữa
        /// chừng thì lượt tra đang bay bị huỷ, và ngược lại — hai bên không giành lưới nhau.
        /// </summary>
        private async Task TurnReversePageAsync(int delta)
        {
            int target = _reversePageIndex + delta;
            if (_reverseAllRows.Count == 0 || target < 0 || target >= ReversePageCount) return;

            _reverseSearchCts?.Cancel();
            _reverseSearchCts?.Dispose();
            _reverseSearchCts = new CancellationTokenSource();

            try
            {
                await ShowReversePageAsync(target, _reverseSearchCts.Token).ConfigureAwait(true);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                AppLogger.Error("[TabPrint] In Reverse: lật trang thất bại", ex);
                SetReverseStatus($"Lỗi đổi trang: {ex.Message}", true);
            }
        }

        private void UpdateReversePagerUi()
        {
            if (_reversePageLabel == null || _reversePageLabel.IsDisposed) return;

            _reversePageLabel.Text = _reverseAllRows.Count == 0
                ? "0/0"
                : $"{_reversePageIndex + 1}/{ReversePageCount}";
            _reversePrevPage.Enabled = _reverseAllRows.Count > 0 && _reversePageIndex > 0;
            _reverseNextPage.Enabled = _reversePageIndex + 1 < ReversePageCount;
        }

        /// <summary>
        /// Tick riêng những mã trang hiện tại chưa in lần nào (<c>printsNumber = 0</c>), phần
        /// còn lại bỏ tick. Chỉ xét trang đang xem vì lưới chỉ giữ đúng 20 dòng của trang đó.
        /// </summary>
        private void SelectReverseUnprintedOnPage()
        {
            var unprinted = CurrentReversePageRows()
                .Where(r => r.PrintCount <= 0)
                .Select(r => r.WaybillNo)
                .ToList();

            // Gạt ô "Chọn tất cả" TRƯỚC: nó bắn CheckedChanged -> SelectAll, làm sau là xoá
            // sạch phần vừa tick. SelectAll(false) gọi thêm để phủ cả trường hợp ô đã bỏ tick
            // sẵn (đặt Checked = false khi nó đang false thì không bắn sự kiện nào).
            if (tabPrint_btnSelectAll != null) tabPrint_btnSelectAll.Checked = false;
            _printService.SelectAll(false);

            if (unprinted.Count == 0)
            {
                SetReverseStatus("Trang này không còn mã nào chưa in." + ReversePageSuffix, true);
                return;
            }

            _printService.SetSelected(unprinted, true);
            SetReverseStatus($"Đã chọn {unprinted.Count} mã chưa in." + ReversePageSuffix);
        }

        /// <summary>
        /// Bản xem trước của JMS: vẫn endpoint in, nhưng <c>printMode=1</c> nên không tính vào
        /// ba lượt in của vận đơn. Phản hồi trả sẵn link PDF đã ký trong <c>data.pdfFullPath</c>,
        /// thả thẳng vào WebView2 — không tải về đĩa: thư mục "Vận đơn đã in" chỉ giữ bản đã in
        /// thật, và <c>DownloadPdfWithRetryAsync</c> dọn bớt theo <c>keepPdfs</c> nên bản xem
        /// trước lọt vào đó sẽ đẩy bản in thật ra ngoài.
        /// <para>Không bao giờ ném: lưới đã có dữ liệu, hỏng preview thì vẫn bấm IN được.</para>
        /// </summary>
        private async Task ShowReversePreviewAsync(List<string> waybills, CancellationToken ct)
        {
            SetReverseStatus($"{waybills.Count} đơn — đang lấy bản xem trước...");
            try
            {
                string body = await PostCenterPrintAsync(
                    waybills, JmsSendWaybillService.CenterPrintModePreview, ct).ConfigureAwait(true);
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

                if (tabPrint_printPreview.CoreWebView2 != null)
                    tabPrint_printPreview.CoreWebView2.Navigate(pdfUrl);
                else
                    tabPrint_printPreview.Source = new Uri(pdfUrl);

                SetReverseStatus(
                    $"{waybills.Count} đơn — xem trước bên phải. Bỏ tick mã không in rồi bấm IN."
                    + ReversePageSuffix);
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
        /// Nút IN: chỉ In Reverse đi lối này, ba tab con còn lại trả về handler gốc của designer.
        /// </summary>
        private async void TabPrint_btnPrint_Dispatch(object sender, EventArgs e)
        {
            if (GetTabPrintModeFromSelectedTab() != PrintMode.InReverse)
            {
                tabPrint_btnPrint_Click(sender, e);
                return;
            }

            await ExecuteReversePrintAsync().ConfigureAwait(true);
        }

        /// <summary>
        /// Lệnh in của In Reverse, cùng ba bước như "In chuyển hoàn" — xin link, tải PDF về,
        /// đẩy bytes vừa tải ra spooler — chỉ khác endpoint và hai điểm riêng của tab này:
        /// <list type="number">
        ///   <item>PDF về thư mục riêng <c>Downloads/Thu hồi đã in</c>, dọn theo ngày;</item>
        ///   <item>in xong giữ nguyên lưới, chỉ bỏ tick đúng những mã vừa in.</item>
        /// </list>
        /// Giới hạn ba lượt in của JMS giữ nguyên như mọi tab khác: hết lượt thì báo đúng câu
        /// JMS trả về rồi dừng — nút "Copy mã đã chọn" để người dùng mang danh sách đi chỗ khác.
        /// Không gọi <c>QueuePostPrintRefresh</c>: nguồn của lưới này là
        /// <see cref="JmsSendWaybillService"/> chứ không phải tracking, một lượt làm mới theo
        /// tracking chỉ ghi đè các cột bằng dữ liệu rỗng.
        /// </summary>
        private async Task ExecuteReversePrintAsync()
        {
            var selected = _printService?.GetSelectedWaybills();
            if (selected == null || selected.Count == 0)
            {
                SetReverseStatus("Chưa tick mã nào để in.", true);
                return;
            }

            // Mượn đúng khoá của pipeline in mặc định: hai tab không bao giờ in chồng lên nhau.
            if (!await _printLock.WaitAsync(0).ConfigureAwait(true))
            {
                SetReverseStatus("Đang xử lý lệnh in hiện tại...", true);
                return;
            }

            SetPrintButtonState(false);
            try
            {
                SetReverseStatus($"Đang lấy bản in cho {selected.Count} đơn...");
                string body = await PostCenterPrintAsync(
                    selected, JmsSendWaybillService.CenterPrintModePrint, CancellationToken.None)
                    .ConfigureAwait(true);

                // JMS nhét lỗi nghiệp vụ vào thân HTTP 200 — quá ba lượt in là code 121003005.
                // Dừng đúng ở đây và giữ nguyên tick: ba lượt là luật của JMS, tab này không
                // tìm đường vòng. Mã vẫn còn trên lưới để bấm "Copy mã đã chọn".
                string error = JmsSendWaybillService.ReadBusinessError(body);
                if (error != null)
                {
                    AppLogger.Warning($"[TabPrint] In Reverse: JMS từ chối lệnh in — {error}");
                    SetReverseStatus($"In thất bại: {error}", true);
                    return;
                }

                string pdfUrl = ResolvePrintPdfUrl(ParsePrintWaybillResponse(body, selected[0]), selected[0]);
                string localPath = await DownloadReversePdfAsync(pdfUrl, selected[0]).ConfigureAwait(true);

                byte[] pdfBytes = string.IsNullOrEmpty(localPath) ? null : File.ReadAllBytes(localPath);
                if (pdfBytes == null || pdfBytes.Length == 0)
                {
                    SetReverseStatus("In thất bại: không tải được PDF từ JMS.", true);
                    return;
                }

                var result = await SubmitPrintImmediatelyAsync(new PrintJobCacheEntry
                {
                    WaybillNo = selected[0],
                    PdfBytes = pdfBytes,
                    LocalPdfPath = localPath,
                    CreatedAt = DateTime.Now,
                    ExpiresAt = DateTime.Now.Add(ReprintJobTtl),
                    PdfHash = ComputeSha256(pdfBytes)
                }, selected[0]).ConfigureAwait(true);

                if (result == null || !result.CompletedBySpooler)
                {
                    SetReverseStatus($"In thất bại: {result?.Reason ?? "máy in không nhận lệnh"}", true);
                    return;
                }

                // Giữ nguyên danh sách đã tra, chỉ bỏ tick phần vừa in: in tiếp phần còn lại
                // không phải tra lại từ đầu.
                _printService.SetSelected(selected, false);
                if (tabPrint_btnSelectAll != null) tabPrint_btnSelectAll.Checked = false;

                // Cột "Số bản in" là ảnh chụp lúc tra, nhưng nút "Chưa in" đọc từ đây — không
                // cộng thì vừa in xong bấm "Chưa in" lại tick đúng những mã vừa in ra.
                var printed = new HashSet<string>(selected, StringComparer.OrdinalIgnoreCase);
                foreach (var row in _reverseAllRows.Where(r => printed.Contains(r.WaybillNo ?? "")))
                    row.PrintCount++;

                SetReverseStatus($"Đã in {selected.Count} đơn." + ReversePageSuffix);
            }
            catch (Exception ex)
            {
                AppLogger.Error("[TabPrint] In Reverse thất bại", ex);
                SetReverseStatus($"In thất bại: {ex.Message}", true);
            }
            finally
            {
                _printLock.Release();
                SetPrintButtonState(true);
            }
        }

        /// <summary>
        /// Tải PDF về thư mục riêng <c>Downloads/Thu hồi đã in</c>, rồi xoá file quá
        /// <see cref="ReversePrintRetentionDays"/> ngày. Khác "Vận đơn đã in" — thư mục kia giữ
        /// theo SỐ LƯỢNG (<c>KeepRecentPdfCount</c>) nên một ngày in nhiều là mất dấu ngày cũ;
        /// bản thu hồi cần tra lại theo ngày nên phải dọn theo tuổi file. Hai thư mục tách nhau
        /// để lượt dọn của tab này không đẩy bản in của tab kia ra ngoài.
        /// </summary>
        private static async Task<string> DownloadReversePdfAsync(string pdfUrl, string waybillTag)
        {
            if (string.IsNullOrWhiteSpace(pdfUrl)) return "";

            string folder = Path.Combine(AppPaths.DownloadsDir, ReversePrintFolderName);
            Directory.CreateDirectory(folder);
            string path = Path.Combine(
                folder, $"{waybillTag.Replace("/", "_")}-{DateTime.Now:yyyyMMdd_HHmmssfff}.pdf");

            using var timeoutCts = new CancellationTokenSource(PrintPdfDownloadTimeout);
            var result = await DownloadPdfFromUrlWithRetriesAsync(
                pdfUrl.Trim(), path, waybillTag, timeoutCts.Token).ConfigureAwait(true);
            if (!result.Success || !File.Exists(path)) return "";

            PruneReversePrintFolder(folder);
            return path;
        }

        /// <summary>
        /// Xoá mọi PDF trong thư mục quá <see cref="ReversePrintRetentionDays"/> ngày. Mốc là
        /// <c>CreationTime</c> — tên file có sẵn ngày giờ nhưng đọc từ tên thì một lần đổi định
        /// dạng tên là lượt dọn câm luôn mà không ai biết.
        /// <para>Không bao giờ ném: file đang mở trong trình xem PDF thì bỏ qua, lượt in sau dọn tiếp.</para>
        /// </summary>
        private static void PruneReversePrintFolder(string folder)
        {
            // Cùng cách tính mốc với lượt dọn log của tab IN ĐƠN: so theo NGÀY, không theo giờ.
            DateTime cutoff = DateTime.Now.Date.AddDays(-ReversePrintRetentionDays);
            foreach (var file in new DirectoryInfo(folder).GetFiles("*.pdf"))
            {
                if (file.CreationTime.Date >= cutoff) continue;
                try { file.Delete(); } catch { }
            }
        }

        /// <summary>
        /// Một lượt gọi endpoint in của màn "Quản lý vận đơn gửi", trả về thân phản hồi thô.
        /// <c>printMode=1</c> là bản xem trước (JMS không tính lượt), <c>printMode=2</c> là in
        /// thật (JMS đếm, quá ba lần trả code 121003005).
        /// </summary>
        private static async Task<string> PostCenterPrintAsync(
            List<string> waybills, int printMode, CancellationToken ct)
        {
            using var response = await JmsApiClient.PostJsonAsync(
                AppConfig.Current.BuildJmsApiUrl(JmsSendWaybillService.CenterPrintEndpoint),
                JmsSendWaybillService.BuildCenterPrintPayload(waybills, printMode),
                routeName: JmsSendWaybillService.CenterPrintRouteName,
                routerNameList: JmsSendWaybillService.CenterPrintRouterNameList,
                ct: ct).ConfigureAwait(true);

            return response == null
                ? ""
                : await response.Content.ReadAsStringAsync(ct).ConfigureAwait(true);
        }

        /// <summary>
        /// Chép các mã đang tick vào clipboard, mỗi mã một dòng — đúng định dạng
        /// <c>ParseWaybillOrder</c> đọc lại được, nên dán thẳng vào ô "Mã vận đơn" là chạy.
        /// Dùng nhiều nhất khi JMS báo hết ba lượt in: danh sách vẫn còn nguyên trên lưới.
        /// </summary>
        private void CopyReverseSelectionToClipboard()
        {
            var selected = _printService?.GetSelectedWaybills();
            if (selected == null || selected.Count == 0)
            {
                SetReverseStatus("Chưa tick mã nào để copy.", true);
                return;
            }

            try
            {
                Clipboard.SetText(string.Join(Environment.NewLine, selected));
                SetReverseStatus($"Đã copy {selected.Count} mã vào clipboard.");
            }
            catch (Exception ex)
            {
                AppLogger.Warning($"[TabPrint] In Reverse: không copy được mã — {ex.Message}");
                SetReverseStatus($"Không copy được: {ex.Message}", true);
            }
        }

        /// <summary>
        /// Chốt chặn, KHÔNG phải đường chạy. Nút IN của tab này đi
        /// <see cref="ExecuteReversePrintAsync"/>, nên pipeline in mặc định không bao giờ chạm
        /// tới mode InReverse — trừ khi dây của <see cref="TabPrint_btnPrint_Dispatch"/> đứt
        /// (designer đổi handler, hoặc <see cref="BuildTabPrintInReverseSection"/> thoát sớm).
        /// Ném ngay tại đây để hỏng dây thì hiện thành lỗi đỏ, thay vì im lặng in vào thư mục
        /// của tab khác rồi xoá trắng lưới. Chữ ký phải giữ nguyên cho
        /// <c>RequestPrintWaybillApiAsync</c> biên dịch được.
        /// </summary>
        private (string Url, string Payload, string RouteName, string RouterNameList) BuildReversePrintRequest(
            List<string> waybills)
        {
            AppLogger.Error(
                $"[TabPrint] In Reverse rơi vào pipeline in mặc định (count={waybills?.Count ?? 0}) — "
                + "nút IN không còn trỏ vào TabPrint_btnPrint_Dispatch.");
            throw new PrintPipelineException(
                "PrintWaybillApi", PrintFailureApi, "In Reverse phải in qua ExecuteReversePrintAsync.");
        }
    }
}

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using AutoJMS.UI.DesignSystem;

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
    /// Main.Designer.cs ngoài chính TabPage <c>tabPrint_inRV</c>.
    /// </summary>
    public partial class Main
    {
        private const int ReverseStaffDebounceMs = 450;
        private const int ReverseStaffMinChars = 2;
        private const int ReverseStaffPopupRows = 6;
        private const string ReversePrintFolderName = "Thu hồi đã in";

        // Chiều cao ba dải của một ô nhập — nhãn trên, ô nhập dưới — và của dòng trạng thái.
        // Hàng nhập để ĐÚNG bằng tổng hai dải cộng lề: TableLayoutPanel chia phần trăm thì chỗ
        // thừa rơi xuống đáy ô, đẩy nhãn và ô nhập xa nhau — đúng cái khoảng trống Owner báo.
        private const int ReverseCaptionHeight = 18;
        private const int ReverseInputHeight = ThemeMetrics.ControlHeight;
        private const int ReverseRowHeight = ReverseCaptionHeight + ReverseInputHeight + 2;
        private const int ReverseStatusHeight = ThemeMetrics.ControlHeight;

        /// <summary>Khe giữa khung chọn ngày và khung chọn giờ trong cùng một ô thời gian.</summary>
        private const int ReverseBoxGap = ThemeSpacing.Sm;

        /// <summary>
        /// Số đơn hiện mỗi trang lưới. Bằng đúng <c>PageSize</c> của
        /// <see cref="JmsSendWaybillService"/> nên một trang API là một trang lưới.
        /// </summary>
        private const int ReversePageSize = 20;

        /// <summary>
        /// Owner chốt: bản in thu hồi cần tra lại theo NGÀY, nên thư mục riêng của tab này dọn
        /// theo tuổi file chứ không theo <c>KeepRecentPdfCount</c> như "Vận đơn đã in".
        /// </summary>
        private const int ReversePrintRetentionDays = 10;
        private const string ReverseHint =
            "Nhập mã vận đơn, hoặc chọn nhân viên + thời gian, rồi bấm Tìm kiếm.";

        // Ba font này TRƯỚC ĐÂY là new Font(...) tự dựng: 10.5pt cho ô nhập, Semibold+Bold cho
        // nhãn, 9pt thường cho nút. Vì vậy tab "In chuyển hoàn" là chỗ DUY NHẤT trong app có
        // chữ nút nhỏ hơn và nhạt hơn mọi nút khác - đúng chỗ Owner báo là "định dạng khác biệt".
        // Nay trỏ vào token: cùng thang chữ với phần còn lại, và ThemeTypography.IsToken trả
        // true nên AppTheme không kéo chúng về font mặc định nữa.
        private static readonly Font ReverseFieldFont = ThemeTypography.Body;
        private static readonly Font ReverseCaptionFont = ThemeTypography.BodyStrong;
        private static readonly Font ReverseUiFont = ThemeTypography.Body;

        // ── các ô nhập, dựng trong BuildReverseInputPanel ──
        // Mỗi mốc thời gian là HAI picker: một chọn ngày, một chọn giờ. Cả hai vẫn giữ một
        // DateTime đầy đủ, nhưng mỗi cái chỉ hiện và chỉ cho sửa một nửa — ghép lại trong
        // ReverseRange.
        private ReverseDateTimePicker tabPrint_dateFrom;
        private ReverseDateTimePicker tabPrint_timeFrom;
        private ReverseDateTimePicker tabPrint_dateTo;
        private ReverseDateTimePicker tabPrint_timeTo;
        private TextBox tabPrint_tenNV;
        private TextBox tabPrint_maCOD;
        private TextBox tabPrint_sdtNG;
        private TextBox tabPrint_sdtNN;
        private DkchDropDown tabPrint_reverseFlag;

        /// <summary>
        /// Ba lựa chọn của "Dấu Reverse", xếp đúng thứ tự mà <see cref="ReverseFlagParam"/> dịch
        /// sang tham số <c>reverse</c>: bỏ hẳn trường / 1 / 0.
        /// </summary>
        private static readonly string[] ReverseFlagOptions = { "Tất cả", "Có", "Không" };

        /// <summary>
        /// Giá trị gửi lên cho ô "Dấu Reverse". Rỗng nghĩa là "Tất cả", và khi rỗng thì
        /// <c>BuildListForm</c> không gửi trường <c>reverse</c> — payload trở lại đúng bộ mười
        /// trường đã đối chiếu với cURL giao diện JMS.
        /// </summary>
        private string ReverseFlagParam() => tabPrint_reverseFlag?.SelectedIndex switch
        {
            1 => "1",
            2 => "0",
            _ => ""
        };

        // ── controls dựng trong BuildTabPrintInReverseSection ──
        private Label _reverseStatus;
        private ListBox _reverseStaffList;
        private FlowLayoutPanel _reverseGridToolbar;
        private ReverseRoundButton _reversePrevPage;
        private ReverseRoundButton _reverseNextPage;
        private Label _reversePageLabel;
        private ReverseRoundButton _reverseClearStaff;
        private readonly List<Label> _reverseCaptions = new();
        private readonly List<ReverseInputBox> _reverseFields = new();

        /// <summary>
        /// Nút dưới lưới kèm vai trò màu: "Chưa in" là bộ lọc nên lấy màu cảnh báo, phần còn lại
        /// lấy màu nhấn của theme. Giữ cặp này để đổi theme là tô lại được đúng vai trò.
        /// </summary>
        private readonly List<(ReverseRoundButton Button, bool Warning)> _reverseToolbarButtons = new();

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

        /// <summary>
        /// Bản in của lượt IN gần nhất và đúng bộ mã đã in ra nó. Bấm IN hai lần liền trên
        /// cùng một danh sách thì dùng lại bytes này, khỏi xin JMS một bản PDF y hệt — nhưng
        /// lượt in VẪN được ghi sổ, vì giấy vẫn ra khỏi máy in.
        /// <para><c>ExpiresAt</c> của chính job là hạn dùng lại: hết hạn thì tải bản mới.</para>
        /// </summary>
        private PrintJobCacheEntry _reverseLastPrint;
        private HashSet<string> _reverseLastPrintSet;

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
        /// lại — cụm này toàn control tự vẽ, không nằm trong nhánh nào của AppTheme, nên màu
        /// do <see cref="ApplyReverseTheme"/> tự đặt.
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
        /// "Tên nhân viên / Tên - Mã KH + Dấu Reverse / SĐT người nhận", hàng 3 là dòng trạng
        /// thái trải hết ba cột.
        /// </summary>
        private void BuildReverseInputPanel()
        {
            tabPrint_dateFrom = NewReverseDateOnlyPicker("tabPrint_dateFrom");
            tabPrint_timeFrom = NewReverseTimeOnlyPicker("tabPrint_timeFrom");
            tabPrint_dateTo = NewReverseDateOnlyPicker("tabPrint_dateTo");
            tabPrint_timeTo = NewReverseTimeOnlyPicker("tabPrint_timeTo");
            tabPrint_tenNV = NewReverseTextBox("tabPrint_tenNV", "Tên nhân viên lấy hàng");
            tabPrint_maCOD = NewReverseTextBox("tabPrint_maCOD", "Mã khách hàng");
            tabPrint_sdtNG = NewReverseTextBox("tabPrint_sdtNG", "");
            tabPrint_sdtNN = NewReverseTextBox("tabPrint_sdtNN", "");

            tabPrint_reverseFlag = new DkchDropDown
            {
                Name = "tabPrint_reverseFlag",
                Font = ReverseFieldFont,
                Margin = Padding.Empty
            };
            tabPrint_reverseFlag.Items.AddRange(ReverseFlagOptions);
            tabPrint_reverseFlag.SelectedIndex = 0;

            // Nút "X" nằm sát mép phải ô tên: đổi người tra không phải xoá tay từng ký tự.
            // TabStop = false để Tab vẫn nhảy thẳng từ ô tên sang ô kế tiếp như trước.
            _reverseClearStaff = new ReverseRoundButton
            {
                Name = "tabPrint_reverseClearStaff",
                Symbol = ASymbols.X,
                SymbolSize = 13,   // ô 22px nằm trong ô nhập, icon phải nhỏ hơn nút toolbar
                TabStop = false,
                Font = ThemeTypography.Button
            };
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
                BackColor = Color.Transparent,
                Text = ReverseHint,
                TextAlign = ContentAlignment.MiddleLeft,
                Font = ReverseUiFont,
                Margin = new Padding(S(ThemeSpacing.Sm), 0, S(ThemeSpacing.Sm), 0)
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
            // Cột co theo nội dung chứ không chia ba phần bằng nhau: chia đều thì ô thời gian
            // rộng gấp đôi giá trị nó chứa, còn lại là khoảng trống — đúng chỗ Owner khoanh.
            for (int i = 0; i < 3; i++)
                layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            // Hai hàng nhập cao CỐ ĐỊNH, chỗ thừa dồn hết xuống dòng trạng thái. Chia phần trăm
            // thì phần thừa rơi vào đáy từng ô, tách nhãn khỏi ô nhập — đúng khoảng trống Owner báo.
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, S(ReverseRowHeight)));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, S(ReverseRowHeight)));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

            // Chuỗi mẫu quyết định bề ngang từng ô — đo bằng font thật lúc dựng. Ô thời gian
            // lấy đúng chuỗi ngày giờ nó hiển thị, ba ô còn lại lấy chính chuỗi gợi ý của nó.
            layout.Controls.Add(NewReverseTimeField("Thời gian từ:", tabPrint_dateFrom, tabPrint_timeFrom), 0, 0);
            layout.Controls.Add(NewReverseTimeField("Thời gian đến:", tabPrint_dateTo, tabPrint_timeTo), 1, 0);
            layout.Controls.Add(NewReverseField("SĐT người gửi:", tabPrint_sdtNG, ReverseInputBox.Glyph.None, null, "0987 654 321 00"), 2, 0);
            layout.Controls.Add(NewReverseField("Tên nhân viên:", tabPrint_tenNV, ReverseInputBox.Glyph.Search, _reverseClearStaff, "Tên nhân viên lấy hàng"), 0, 1);
            layout.Controls.Add(NewReverseCustomerField(), 1, 1);
            layout.Controls.Add(NewReverseField("SĐT người nhận:", tabPrint_sdtNN, ReverseInputBox.Glyph.None, null, "0987 654 321 00"), 2, 1);
            layout.Controls.Add(_reverseStatus, 0, 2);
            layout.SetColumnSpan(_reverseStatus, 3);

            tabPrint_inRV.Controls.Add(layout);
        }

        /// <summary>
        /// Một ô của lưới nhập: nhãn trên, khung bo góc dưới. Khung tự vẽ nền, viền và biểu
        /// tượng trái; control nhập nằm lọt trong khung nên không còn viền vuông của WinForms.
        /// </summary>
        private Panel NewReverseField(
            string caption, Control input, ReverseInputBox.Glyph glyph, Control trailing, string widthSample)
        {
            var cell = NewReverseCell();
            cell.Controls.Add(NewReverseBox(input, glyph, trailing, widthSample, 0));
            cell.Controls.Add(NewReverseCaption(caption));
            return cell;
        }

        /// <summary>
        /// Ô thời gian: một nhãn, hai khung tách hẳn nhau — khung trái chọn NGÀY (xổ lịch),
        /// khung phải chọn GIỜ (nút tăng giảm). Gộp chung một ô thì sửa giờ phải rê qua cả
        /// phần ngày, mà xổ lịch ra để chỉnh giây thì càng vô nghĩa.
        /// </summary>
        private Panel NewReverseTimeField(string caption, DateTimePicker date, DateTimePicker time)
        {
            var dateBox = NewReverseBox(date, ReverseInputBox.Glyph.Calendar, null, "2026-09-20", 0);
            var timeBox = NewReverseBox(
                time, ReverseInputBox.Glyph.Clock, null, "00:00:00", dateBox.Right + S(ReverseBoxGap));

            var cell = NewReverseCell();
            cell.Controls.Add(timeBox);
            cell.Controls.Add(dateBox);
            cell.Controls.Add(NewReverseCaption(caption));
            return cell;
        }

        /// <summary>
        /// Ô "Tên - Mã KH" thu lại vừa đúng chuỗi gợi ý của nó, nhường nửa phải của cột cho
        /// dropdown "Dấu Reverse" — cột này vốn rộng theo ô thời gian ở hàng trên nên chỗ đó
        /// đang bỏ không. Dropdown tự vẽ khung bo góc của chính nó nên KHÔNG bọc trong
        /// <see cref="ReverseInputBox"/>: bọc vào là hai đường viền chồng lên nhau.
        /// </summary>
        private Panel NewReverseCustomerField()
        {
            var codeBox = NewReverseBox(
                tabPrint_maCOD, ReverseInputBox.Glyph.None, null, "Mã khách hàng", 0);

            // Thẳng cột với ô ngày / ô giờ của "Thời gian đến" ngay trên (Owner 2026-09-26): đo
            // cùng chuỗi mẫu như NewReverseTimeField nên mép trái "Dấu Reverse" trùng mép ô giờ.
            codeBox.Width = Math.Max(codeBox.Width, ReverseInputBox.MeasureWidth(
                this, "2026-09-20", ReverseFieldFont, ReverseInputBox.Glyph.Calendar, false, true));
            int timeWidth = ReverseInputBox.MeasureWidth(
                this, "00:00:00", ReverseFieldFont, ReverseInputBox.Glyph.Clock, false, true);

            tabPrint_reverseFlag.Location =
                new Point(codeBox.Right + S(ReverseBoxGap), S(ReverseCaptionHeight));
            tabPrint_reverseFlag.Size = new Size(
                Math.Max(timeWidth, DkchDropDown.WidthFor(tabPrint_reverseFlag, ReverseFieldFont)),
                S(ReverseInputHeight));
            // ItemHeight là pixel thật (dòng trong popup); constructor chỉ đặt số 96-DPI.
            tabPrint_reverseFlag.ItemHeight = S(26);

            var flagCaption = NewReverseCaption("Dấu Reverse:");
            flagCaption.Left = tabPrint_reverseFlag.Left;

            var cell = NewReverseCell();
            cell.Controls.Add(tabPrint_reverseFlag);
            cell.Controls.Add(flagCaption);
            cell.Controls.Add(codeBox);
            cell.Controls.Add(NewReverseCaption("Tên - Mã KH"));
            return cell;
        }

        private Label NewReverseCaption(string caption)
        {
            var label = new Label
            {
                AutoSize = true,
                BackColor = Color.Transparent,
                Location = Point.Empty,
                Margin = Padding.Empty,
                Text = caption,
                Font = ReverseCaptionFont,
                TextAlign = ContentAlignment.MiddleLeft
            };
            _reverseCaptions.Add(label);
            return label;
        }

        /// <summary>
        /// Margin phải là 0: panel AutoSize cộng cả margin con vào khung bao, để mặc định (3px
        /// mỗi phía) là cụm cao 52px trong khi hàng chỉ cao 48px và bị cắt mất đáy.
        /// </summary>
        private ReverseInputBox NewReverseBox(
            Control input, ReverseInputBox.Glyph glyph, Control trailing, string widthSample, int x)
        {
            var box = new ReverseInputBox(input, glyph, trailing)
            {
                Location = new Point(x, S(ReverseCaptionHeight)),
                Margin = Padding.Empty,
                Width = ReverseInputBox.MeasureWidth(
                    this, widthSample, ReverseFieldFont, glyph, trailing != null, input is DateTimePicker),
                Height = S(ReverseInputHeight)
            };
            _reverseFields.Add(box);
            return box;
        }

        /// <summary>
        /// Ô co theo nội dung nên không Dock được: panel AutoSize lấy đúng khung bao các control
        /// bên trong, rồi cột AutoSize của TableLayoutPanel lấy theo panel. Lề phải rộng hơn lề
        /// trái để hai cột cạnh nhau không dính vào nhau.
        /// </summary>
        private Panel NewReverseCell() => new()
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            BackColor = Color.Transparent,
            Margin = new Padding(S(ThemeSpacing.Xs), 0, S(ThemeSpacing.Md), 0)
        };

        // Không viền: viền duy nhất nhìn thấy là khung bo góc do ReverseInputBox vẽ.
        private static TextBox NewReverseTextBox(string name, string placeholder) => new()
        {
            Name = name,
            BorderStyle = BorderStyle.None,
            Font = ReverseFieldFont,
            PlaceholderText = placeholder
        };

        private static ReverseDateTimePicker NewReverseDateOnlyPicker(string name) => new()
        {
            Name = name,
            Font = ReverseFieldFont,
            Format = DateTimePickerFormat.Custom,
            CustomFormat = "yyyy-MM-dd"
        };

        // ShowUpDown bỏ hẳn nút xổ lịch, thay bằng nút tăng giảm — đúng thứ cần cho giờ/phút/giây.
        private static ReverseDateTimePicker NewReverseTimeOnlyPicker(string name) => new()
        {
            Name = name,
            Font = ReverseFieldFont,
            Format = DateTimePickerFormat.Custom,
            CustomFormat = "HH:mm:ss",
            ShowUpDown = true
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

            // "Chưa in" là bộ lọc, lấy tông cảnh báo; ba nút còn lại lấy tông nhấn của theme.
            var unprinted = NewReverseToolbarButton("tabPrint_reverseUnprinted", "Chưa in", 84, warning: true);
            unprinted.Click += (s, e) => SelectReverseUnprintedOnPage();

            _reversePrevPage = NewReverseToolbarButton("tabPrint_reversePrevPage", "", 34, warning: false);
            _reversePrevPage.Symbol = ASymbols.ChevronLeft;
            _reversePrevPage.Click += (s, e) => _ = TurnReversePageAsync(-1);

            _reversePageLabel = new Label
            {
                Name = "tabPrint_reversePageLabel",
                AutoSize = false,
                Size = new Size(S(56), S(ThemeMetrics.ControlHeight)),
                Margin = new Padding(0, S(ThemeSpacing.Xs), 0, S(ThemeSpacing.Xs)),
                Text = "0/0",
                TextAlign = ContentAlignment.MiddleCenter,
                Font = ReverseUiFont
            };

            _reverseNextPage = NewReverseToolbarButton("tabPrint_reverseNextPage", "", 34, warning: false);
            _reverseNextPage.Symbol = ASymbols.ChevronRight;
            _reverseNextPage.Click += (s, e) => _ = TurnReversePageAsync(1);

            var copy = NewReverseToolbarButton("tabPrint_reverseCopy", "Copy mã đã chọn", 132, warning: false);
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

        private ReverseRoundButton NewReverseToolbarButton(string name, string text, int width, bool warning)
        {
            var button = new ReverseRoundButton
            {
                Name = name,
                Text = text,
                Size = new Size(S(width), S(ThemeMetrics.ControlHeight)),
                Margin = new Padding(S(ThemeSpacing.Sm), S(ThemeSpacing.Xs), 0, S(ThemeSpacing.Xs)),
                Font = ThemeTypography.Button
            };
            _reverseToolbarButtons.Add((button, warning));
            return button;
        }

        private void SyncReverseGridToolbarVisibility()
        {
            if (_reverseGridToolbar == null || _reverseGridToolbar.IsDisposed) return;
            _reverseGridToolbar.Visible = GetTabPrintModeFromSelectedTab() == PrintMode.InReverse;
            // Không phát lại ApplyReverseTheme ở đây: đổi tab con không đổi theme, còn lượt
            // PerformLayout + Invalidate cả cụm mỗi lần bấm tab con là giật (Owner 2026-09-26).
        }

        /// <summary>
        /// Tô màu và trả lại font cho cụm control của tab. Gọi lúc dựng và ngay sau mỗi
        /// <c>AppTheme.Apply</c>. KHÔNG bỏ được: cụm này toàn
        /// control tự vẽ nên AppTheme không tô màu cho, nhưng vẫn gán đè Font = "Segoe UI" 10F
        /// lên MỌI control không mang font token — không phát lại là nhãn và ô nhập tụt cỡ chữ.
        /// <see cref="DateTimePicker"/> là control của Windows, không nhận BackColor — ở theme
        /// tối bốn ô ngày giờ được đổi tông khi vẽ, xem <see cref="ReverseDateTimePicker"/>.
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
                RestoreReverseFont(box, ReverseFieldFont);
            }

            foreach (var picker in new[] { tabPrint_dateFrom, tabPrint_timeFrom, tabPrint_dateTo, tabPrint_timeTo })
            {
                if (picker == null || picker.IsDisposed) continue;
                RestoreReverseFont(picker, ReverseFieldFont);
                picker.SetTone(colors.InputBackground, colors.TextPrimary);
            }

            // Khung bo góc tự vẽ nên phải tự nhận màu theme — nền, viền, viền lúc focus, và màu
            // biểu tượng trái. Font của control nhập vừa trả lại ở trên nên đo lại luôn chiều cao.
            foreach (var field in _reverseFields)
            {
                if (field.IsDisposed) continue;
                field.FieldBackColor = colors.InputBackground;
                field.BorderColor = colors.InputBorder;
                field.FocusBorderColor = colors.PrimaryAccent;
                field.GlyphColor = colors.TextSecondary;
                field.PerformLayout();
                field.Invalidate();
            }

            // Dropdown cũng tự vẽ khung, cũng phải tự nhận màu — thêm màu của dải xổ xuống.
            if (tabPrint_reverseFlag != null && !tabPrint_reverseFlag.IsDisposed)
            {
                tabPrint_reverseFlag.FieldBackColor = colors.InputBackground;
                tabPrint_reverseFlag.BorderColor = colors.InputBorder;
                tabPrint_reverseFlag.HoverBorderColor = colors.PrimaryAccent;
                tabPrint_reverseFlag.HighlightColor = colors.PrimaryAccent;
                tabPrint_reverseFlag.HighlightForeColor = colors.TextInverse;
                tabPrint_reverseFlag.HoverItemColor = colors.PrimaryHoverTint;
                tabPrint_reverseFlag.ForeColor = colors.TextPrimary;
                RestoreReverseFont(tabPrint_reverseFlag, ReverseFieldFont);
                tabPrint_reverseFlag.Invalidate();
            }

            foreach (var caption in _reverseCaptions)
            {
                caption.ForeColor = colors.TextPrimary;
                RestoreReverseFont(caption, ReverseCaptionFont);
            }

            foreach (var (button, warning) in _reverseToolbarButtons)
                StyleReverseToolbarButton(button, warning ? colors.Warning : colors.PrimaryAccent);

            if (_reverseClearStaff != null && !_reverseClearStaff.IsDisposed)
            {
                // Nút "X" nằm TRONG khung nhập nên phải chìm vào nền ô, chỉ dấu X mang màu nhấn.
                _reverseClearStaff.BackColor = colors.InputBackground;
                _reverseClearStaff.Fill = colors.InputBackground;
                _reverseClearStaff.HoverFill = colors.PrimaryHoverTint;
                _reverseClearStaff.DisabledFill = colors.InputBackground;
                _reverseClearStaff.ForeColor = colors.PrimaryAccent;
                _reverseClearStaff.DisabledForeColor = colors.TextSecondary;
                RestoreReverseFont(_reverseClearStaff, ReverseUiFont);
                _reverseClearStaff.Invalidate();
            }

            if (_reversePageLabel != null)
            {
                _reversePageLabel.ForeColor = colors.TextPrimary;
                RestoreReverseFont(_reversePageLabel, ReverseUiFont);
            }

            if (_reverseStaffList != null)
            {
                _reverseStaffList.BackColor = colors.InputBackground;
                _reverseStaffList.ForeColor = colors.TextPrimary;
                RestoreReverseFont(_reverseStaffList, ReverseUiFont);
            }

            RestoreReverseFont(_reverseStatus, ReverseUiFont);

            // Dòng trạng thái tự chọn màu đỏ/thường trong SetReverseStatus, phát lại câu đang
            // hiện để nó tính lại theo theme mới thay vì ghi đè bằng TextPrimary.
            SetReverseStatus(_reverseStatus.Text, _reverseStatusIsError);
        }

        /// <summary>
        /// Tô một nút dưới lưới theo tông màu vai trò của nó. <c>BackColor</c> là màu NGOÀI bốn
        /// góc bo — lấy màu bề mặt uiPanel20 thật sự vẽ ra (APanel tô theo Elevation), KHÔNG lấy
        /// uiPanel20.BackColor: thuộc tính đó là Transparent nên góc bo ra đen.
        /// </summary>
        private void StyleReverseToolbarButton(ReverseRoundButton button, Color tone)
        {
            if (button == null || button.IsDisposed) return;

            var colors = UI.AppTheme.Colors;
            button.BackColor = ControlStyler.SurfaceBehind(button.Parent ?? uiPanel20);
            // Như AButton viền: nghỉ thì nền panel + viền theo tông; hover thì tô đầy tông.
            button.Fill = button.BackColor;
            button.Border = tone;
            button.HoverFill = tone;
            button.HoverForeColor = colors.TextInverse;
            button.DisabledFill = colors.InputBorder;
            button.ForeColor = colors.TextPrimary;
            button.DisabledForeColor = colors.TextSecondary;
            RestoreReverseFont(button, ThemeTypography.Button);
            button.Invalidate();
        }

        /// <summary>
        /// Gán lại font khi theme đã ghi đè. So tham chiếu chứ không so nội dung: hai font này là
        /// static readonly nên control nào còn giữ đúng tham chiếu là chưa bị đụng, bỏ qua được
        /// một lượt layout thừa.
        /// </summary>
        private static void RestoreReverseFont(Control ctrl, Font font)
        {
            if (ctrl == null || ctrl.IsDisposed) return;
            if (!ReferenceEquals(ctrl.Font, font)) ctrl.Font = font;
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

            if (tabPrint_reverseFlag != null && !tabPrint_reverseFlag.IsDisposed)
                tabPrint_reverseFlag.SelectedIndex = 0;

            _reverseAllRows.Clear();
            _reversePageIndex = 0;
            _reverseLastPrint = null;
            _reverseLastPrintSet = null;
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
            SetReverseMoment(tabPrint_dateFrom, tabPrint_timeFrom, today);
            SetReverseMoment(tabPrint_dateTo, tabPrint_timeTo, today.AddDays(1).AddSeconds(-1));
        }

        /// <summary>Đặt cùng một mốc cho cả hai picker — mỗi cái chỉ hiện phần của mình.</summary>
        private static void SetReverseMoment(DateTimePicker date, DateTimePicker time, DateTime value)
        {
            if (date != null && !date.IsDisposed) date.Value = value;
            if (time != null && !time.IsDisposed) time.Value = value;
        }

        /// <summary>
        /// Ghép ngày của picker trái với giờ của picker phải. Hai picker giữ hai DateTime độc
        /// lập nên phải lấy đúng nửa của từng cái: nửa còn lại của mỗi cái là giá trị cũ, người
        /// dùng không nhìn thấy và không sửa được.
        /// </summary>
        private static DateTime ReverseMoment(DateTimePicker date, DateTimePicker time, DateTime fallback)
        {
            if (date == null || date.IsDisposed || time == null || time.IsDisposed) return fallback;
            return date.Value.Date + time.Value.TimeOfDay;
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

            // Bám vào KHUNG bo góc chứ không vào TextBox bên trong: TextBox đã thụt vào theo lề
            // của khung, neo vào nó thì danh sách lệch phải và hụt bề ngang.
            var host = (Control)tabPrint_tenNV.Parent ?? tabPrint_tenNV;
            var anchor = PointToClient(host.PointToScreen(new Point(0, host.Height)));
            int rows = Math.Min(staff.Count, ReverseStaffPopupRows);
            _reverseStaffList.Bounds = new Rectangle(
                anchor.X,
                anchor.Y,
                Math.Max(host.Width, S(200)),
                Math.Max(_reverseStaffList.ItemHeight * rows + S(4), S(24)));
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

            DateTime from = ReverseMoment(tabPrint_dateFrom, tabPrint_timeFrom, DateTime.Today);
            DateTime to = ReverseMoment(
                tabPrint_dateTo, tabPrint_timeTo, DateTime.Today.AddDays(1).AddSeconds(-1));
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
                    ReverseFlagParam(),
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

            // printsNumber của JMS chỉ đếm lượt printMode=2, nên mọi lượt đi đường xem trước
            // không có ở đó — chỉ lịch sử dưới đĩa biết. Kéo số của sổ lên ngay lúc nạp để cột
            // "Số bản in" hiện đúng tổng số bản đã in ra giấy, không phải mỗi phần JMS đếm.
            foreach (var row in _reverseAllRows)
                row.PrintCount = Math.Max(row.PrintCount, ReversePrintLedger.CountOf(row.WaybillNo));

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
        /// KHÔNG có trần in: lượt nào cũng thử <c>printMode=2</c> trước để JMS đếm như mọi tab
        /// khác, JMS từ chối vì quá ba lượt (code 121003005) thì lấy đúng bản xem trước
        /// <c>printMode=1</c> — cùng một PDF, nhưng JMS không tính lượt nên không bao giờ hết.
        /// Mỗi lượt in ra giấy ghi một dòng vào <see cref="ReversePrintLedger"/>.
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
                var job = ReuseReverseLastPrint(selected);
                if (job == null)
                {
                    SetReverseStatus($"Đang lấy bản in cho {selected.Count} đơn...");
                    string body = await PostCenterPrintAsync(
                        selected, JmsSendWaybillService.CenterPrintModePrint, CancellationToken.None)
                        .ConfigureAwait(true);

                    // JMS nhét lỗi nghiệp vụ vào thân HTTP 200 — quá ba lượt in là code
                    // 121003005. Đây là chỗ bypass: xin lại đúng bản xem trước, JMS trả cùng
                    // một PDF mà không tính lượt nên in được bao nhiêu lần cũng được. Thử
                    // printMode=2 trước chứ không đi thẳng đường này, để ba lượt đầu vẫn nằm
                    // trong printsNumber của JMS — đó là số duy nhất còn lại sau khi sổ bị dọn.
                    string error = JmsSendWaybillService.ReadBusinessError(body);
                    if (error != null)
                    {
                        AppLogger.Warning(
                            $"[TabPrint] In Reverse: JMS từ chối in thật ({error}) — chuyển sang bản xem trước.");
                        body = await PostCenterPrintAsync(
                            selected, JmsSendWaybillService.CenterPrintModePreview, CancellationToken.None)
                            .ConfigureAwait(true);
                        error = JmsSendWaybillService.ReadBusinessError(body);
                    }

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

                    job = new PrintJobCacheEntry
                    {
                        WaybillNo = selected[0],
                        PdfBytes = pdfBytes,
                        LocalPdfPath = localPath,
                        CreatedAt = DateTime.Now,
                        ExpiresAt = DateTime.Now.Add(ReprintJobTtl),
                        PdfHash = ComputeSha256(pdfBytes)
                    };
                }

                var result = await SubmitPrintImmediatelyAsync(job, selected[0]).ConfigureAwait(true);
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
                // cộng thì vừa in xong bấm "Chưa in" lại tick đúng những mã vừa in ra. Cộng
                // xong mới ghi sổ: dòng lịch sử chép lại đúng con số này.
                var printed = new HashSet<string>(selected, StringComparer.OrdinalIgnoreCase);
                var printedRows = _reverseAllRows
                    .Where(r => printed.Contains(r.WaybillNo ?? "")).ToList();
                foreach (var row in printedRows) row.PrintCount++;
                ReversePrintLedger.Record(printedRows.Select(r => (r.WaybillNo, r.PrintCount)));

                _reverseLastPrint = job;
                _reverseLastPrintSet = printed;

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
        /// Bản in của lượt trước, nếu lượt này tick ĐÚNG bộ mã đó và bản in chưa hết hạn. JMS
        /// dựng PDF theo danh sách mã, nên cùng bộ mã là cùng một file — xin lại chỉ tốn thêm
        /// một lượt gọi mạng và một lượt tải. Lệch dù chỉ một mã là phải tải bản mới: thứ tự
        /// trang trong PDF đi theo danh sách gửi lên.
        /// </summary>
        private PrintJobCacheEntry ReuseReverseLastPrint(List<string> selected)
        {
            if (_reverseLastPrint == null || _reverseLastPrintSet == null) return null;
            if (DateTime.Now >= _reverseLastPrint.ExpiresAt) return null;
            if (!_reverseLastPrintSet.SetEquals(selected)) return null;

            AppLogger.Info(
                $"[TabPrint] In Reverse: dùng lại bản in gần nhất cho {selected.Count} đơn " +
                "— không gọi lại JMS.");
            return _reverseLastPrint;
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

    // ─────────────────────────────────────────────────────────────────────────────
    // Hai control tự vẽ của tab In Reverse. Dựng ở đây chứ không tái dùng DkchDropDown:
    // cụm Đăng ký chuyển hoàn vẽ ô nhập LẪN giá trị bên trong, còn tab này cần một cái
    // khung rỗng để nhét TextBox/DateTimePicker thật vào — gõ và chọn lịch vẫn phải chạy.
    // Hình học bo góc thì dùng chung DkchPaint.RoundRect, không vẽ lại.
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Khung nhập bo góc: tự vẽ nền, viền và biểu tượng bên trái, còn control nhập thật
    /// (<see cref="TextBox"/> hoặc <see cref="DateTimePicker"/>) nằm lọt bên trong.
    ///
    /// <see cref="DateTimePicker"/> là control của Windows: nó luôn tự vẽ viền vuông và bỏ
    /// qua <c>BackColor</c>. Cắt viền đó bằng <see cref="Control.Region"/> — vùng cửa sổ do
    /// hệ điều hành cắt nên viền không còn đường nào lọt ra ngoài.
    /// </summary>
    internal sealed class ReverseInputBox : Panel
    {
        internal enum Glyph { None, Clock, Search, Calendar }

        private const int Radius = 6;
        private const int TextPad = 9;
        private const int GlyphGutter = 26;
        private const int TrailingWidth = 22;

        private readonly Control _input;
        private readonly Control _trailing;
        private readonly Glyph _glyph;
        private bool _hot;
        private Rectangle _clip;

        public Color FieldBackColor { get; set; } = Color.White;
        public Color BorderColor { get; set; } = Color.Gainsboro;
        public Color FocusBorderColor { get; set; } = Color.DodgerBlue;
        public Color GlyphColor { get; set; } = Color.Gray;

        public ReverseInputBox(Control input, Glyph glyph, Control trailing)
        {
            _input = input;
            _glyph = glyph;
            _trailing = trailing;

            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.ResizeRedraw | ControlStyles.UserPaint
                | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;

            Controls.Add(input);
            if (trailing != null) Controls.Add(trailing);

            // Viền sáng lên khi con trỏ đang ở trong ô. DateTimePicker không phát Enter/Leave
            // cho control cha nên bắt thẳng trên chính nó.
            input.GotFocus += (s, e) => SetHot(true);
            input.LostFocus += (s, e) => SetHot(false);
        }

        /// <summary>
        /// Bề ngang vừa đủ cho một chuỗi mẫu: máng biểu tượng trái (hoặc lề trái) + chữ + lề
        /// phải, cộng chỗ cho nút phụ và cho nút xổ lịch của <see cref="DateTimePicker"/>. Đo
        /// bằng chính font sẽ dùng nên đổi DPI hay đổi cỡ chữ là tự khớp, không phải sửa số.
        /// Các lề là số 96-DPI, quy đổi theo <paramref name="ctx"/> và cộng từng số một y như
        /// <see cref="OnLayout"/> — quy đổi cả tổng thì làm tròn lệch, chữ hụt 1px.
        /// </summary>
        public static int MeasureWidth(Control ctx, string sample, Font font, Glyph glyph, bool hasTrailing, bool isPicker)
        {
            int width = DpiHelper.Scale(ctx, glyph == Glyph.None ? TextPad : GlyphGutter)
                + TextRenderer.MeasureText(sample, font).Width + DpiHelper.Scale(ctx, TextPad);
            if (hasTrailing) width += DpiHelper.Scale(ctx, TrailingWidth);
            if (isPicker) width += SystemInformation.VerticalScrollBarWidth; // bề ngang nút xổ lịch
            return width;
        }

        private int S(int value) => DpiHelper.Scale(this, value);

        private void SetHot(bool hot)
        {
            if (_hot == hot) return;
            _hot = hot;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            var box = new Rectangle(0, 0, Width - 1, Height - 1);
            using (var path = DkchPaint.RoundRect(box, S(Radius)))
            using (var fill = new SolidBrush(FieldBackColor))
            using (var pen = new Pen(_hot ? FocusBorderColor : BorderColor, _hot ? 1.4f : 1f))
            {
                g.FillPath(fill, path);
                g.DrawPath(pen, path);
            }

            if (_glyph == Glyph.None) return;

            // Trước đây ba biểu tượng này dựng bằng Pen vì font hệ thống thiếu glyph thì ra ô
            // vuông tofu. Nay lucide.ttf đi kèm assembly và ASymbols đăng ký nó cho cả GDI, nên
            // vẽ thẳng glyph thật. Hằng ASymbols là số codepoint, không phải ký tự trong mã
            // nguồn, nên cũng không dính chuyện \uXXXX bị công cụ sửa file biến thành byte thật.
            int side = S(14);
            var cell = new Rectangle(S(8), (Height - side) / 2, side, side);
            int symbol = _glyph switch
            {
                Glyph.Clock => ASymbols.Clock,
                Glyph.Calendar => ASymbols.Calendar,
                _ => ASymbols.Search
            };
            ASymbols.Draw(g, symbol, side, GlyphColor, cell);
        }

        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            if (Width <= 0 || Height <= 0) return;

            int left = S(_glyph == Glyph.None ? TextPad : GlyphGutter);
            int right = Width - S(TextPad);

            if (_trailing != null)
            {
                right -= S(TrailingWidth);
                _trailing.Bounds = new Rectangle(right + S(2), S(3), S(TrailingWidth), Height - S(3) * 2);
            }

            int width = Math.Max(right - left, S(8));
            if (_input is DateTimePicker)
            {
                // Cắt 2px mỗi phía: phần bị cắt đúng là viền vuông của control, chữ bên trong
                // vẫn nguyên. Region cũ phải Dispose, nếu không mỗi lượt layout lại rò một
                // handle vùng của GDI.
                //
                // DateTimePicker tự ép chiều cao theo font y như TextBox một dòng (28 đặt vào
                // thành 26), nên căn giữa theo chiều cao THẬT của nó — đặt Top = 0 là chữ bị lệch
                // lên trên trong khung.
                //
                // Đặt Bounds MỘT lần với đúng chiều cao nó sẽ giữ, và chỉ thay Region khi khung
                // cắt đổi: bounds không đổi thì WinForms bỏ qua SetWindowPos, còn gán Region thì
                // luôn gọi SetWindowRgn - mỗi lần đổi sang tab IN ĐƠN trước đây tốn 3 lệnh này
                // cho mỗi ô ngày giờ.
                _input.Bounds = new Rectangle(left - 2, Math.Max((Height - _input.Height) / 2, 0), width + 4, _input.Height);
                var clip = new Rectangle(2, 2, _input.Width - 4, _input.Height - 4);
                if (clip != _clip || _input.Region == null)
                {
                    _clip = clip;
                    var old = _input.Region;
                    _input.Region = new Region(clip);
                    old?.Dispose();
                }
            }
            else
            {
                // TextBox một dòng tự ép chiều cao theo font — căn giữa theo chiều cao thật của
                // nó, đặt Height ở đây là bị nó ghi đè ngay.
                _input.Bounds = new Rectangle(left, Math.Max((Height - _input.Height) / 2, 0), width, _input.Height);
            }
        }

        /// <summary>Bấm vào khoảng trống trong khung cũng đưa con trỏ vào ô nhập.</summary>
        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (_input != null && !_input.IsDisposed) _input.Focus();
        }
    }

    /// <summary>
    /// Nút bo góc tô đặc. <c>BackColor</c> ở đây là màu NGOÀI bốn góc bo (màu nền của panel
    /// chứa nút), <see cref="Fill"/> mới là màu thân nút — control WinForms không có nền thật
    /// trong suốt nên bốn góc phải tô bằng đúng màu panel.
    /// </summary>
    internal sealed class ReverseRoundButton : Button
    {
        private const int Radius = 6;
        private bool _hover;

        public Color Fill { get; set; } = Color.DodgerBlue;
        public Color HoverFill { get; set; } = Color.CornflowerBlue;
        public Color DisabledFill { get; set; } = Color.Gainsboro;
        public Color DisabledForeColor { get; set; } = Color.Gray;
        /// <summary>Viền khi còn bật. Empty = không viền (nút X trong ô nhập).</summary>
        public Color Border { get; set; } = Color.Empty;
        /// <summary>Màu chữ khi hover. Empty = giữ ForeColor.</summary>
        public Color HoverForeColor { get; set; } = Color.Empty;

        /// <summary>
        /// Icon Lucide vẽ THAY cho <see cref="Control.Text"/>. <c>ASymbols.None</c> (mặc định)
        /// thì nút vẽ chữ như cũ. Nút chỉ có icon vẫn nên đặt <c>Text</c> rỗng chứ không đặt ký
        /// tự mô phỏng — <c>Text</c> còn là tên nút với trình đọc màn hình.
        /// </summary>
        public int Symbol { get; set; } = ASymbols.None;

        /// <summary>Cạnh ô icon, pixel 96-DPI (lúc vẽ tự quy đổi). Nút toolbar cao 29 nên 16 là vừa.</summary>
        public int SymbolSize { get; set; } = 16;

        private int S(int value) => DpiHelper.Scale(this, value);

        public ReverseRoundButton()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            UseVisualStyleBackColor = false;
            Cursor = Cursors.Hand;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            // BackColor trong suốt thì Clear không xoá được bộ đệm đen — lấy màu cha thật sự vẽ.
            g.Clear(BackColor.A == 255 ? BackColor : ControlStyler.SurfaceBehind(Parent));
            g.SmoothingMode = SmoothingMode.AntiAlias;

            // Tô thân trên trọn W x H với PixelOffsetMode.Half (pixel i phủ [i, i+1]). Chế độ mặc
            // định + khung W-1/H-1 chỉ phủ nửa hàng/cột pixel mép nên thân nút có vành mờ quanh viền.
            var body = new Rectangle(0, 0, Width - 1, Height - 1);
            var tone = !Enabled ? DisabledFill : _hover ? HoverFill : Fill;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            using (var path = DkchPaint.RoundRect(ClientRectangle, S(Radius)))
            using (var brush = new SolidBrush(tone))
                g.FillPath(brush, path);
            if (Enabled) ControlStyler.DrawBorder(g, ClientRectangle, Border, S(1), S(Radius));
            g.PixelOffsetMode = PixelOffsetMode.Default;

            var ink = !Enabled ? DisabledForeColor : _hover && HoverForeColor.A != 0 ? HoverForeColor : ForeColor;
            if (Symbol != ASymbols.None)
            {
                ASymbols.Draw(g, Symbol, S(SymbolSize), ink, body);
            }
            else
            {
                // SingleLine bắt buộc đi kèm VerticalCenter — thiếu nó thì DrawText chuyển sang chế
                // độ nhiều dòng và bỏ qua luôn việc căn giữa theo chiều dọc.
                TextRenderer.DrawText(g, Text, Font, body, ink,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter
                    | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
            }

            // Khung chấm chỉ khi focus đến từ bàn phím — bấm chuột xong không để lại khung đen.
            if (Focused && Enabled && ShowFocusCues)
                ControlPaint.DrawFocusRectangle(g, Rectangle.Inflate(body, -S(4), -S(4)));
        }

        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
    }

    /// <summary>
    /// <see cref="DateTimePicker"/> theo tông theme tối. Control này của Windows bỏ qua
    /// <c>BackColor</c>, luôn vẽ nền trắng chữ đen — lọt thỏm một mảng sáng giữa khung nhập tối.
    /// Chặn WM_PAINT: để nó tự vẽ vào bitmap rồi chép ra màn hình qua ma trận màu đưa trắng về
    /// nền, đen về chữ. Ánh xạ tuyến tính TỪNG kênh nên viền ClearType của chữ vẫn đúng điểm
    /// ảnh con; cái giá là ô đang chọn đổi từ xanh sang cam. Nút tăng giảm (ShowUpDown) là cửa
    /// sổ con riêng nên móc thêm một lớp y hệt. Lịch xổ xuống là popup của hệ thống - vẫn sáng.
    /// </summary>
    internal sealed class ReverseDateTimePicker : DateTimePicker
    {
        private const int WM_PAINT = 0x000F;
        private const int WM_ERASEBKGND = 0x0014;
        private const int WM_PRINTCLIENT = 0x0318, PRF_CLIENT = 0x0004;

        private ImageAttributes _remap;
        private UpDownHook _upDown;

        /// <summary>Nền sáng thì để Windows vẽ nguyên bản; nền tối thì đổi tông.</summary>
        public void SetTone(Color back, Color ink)
        {
            _remap?.Dispose();
            _remap = null;
            if (back.GetBrightness() < 0.5f)
            {
                // out = ink + in * (back - ink), từng kênh: trắng (1) -> back, đen (0) -> ink.
                _remap = new ImageAttributes();
                _remap.SetColorMatrix(new ColorMatrix
                {
                    Matrix00 = (back.R - ink.R) / 255f,
                    Matrix11 = (back.G - ink.G) / 255f,
                    Matrix22 = (back.B - ink.B) / 255f,
                    Matrix40 = ink.R / 255f,
                    Matrix41 = ink.G / 255f,
                    Matrix42 = ink.B / 255f
                });
            }
            Invalidate(true);
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            IntPtr upDown = FindWindowEx(Handle, IntPtr.Zero, "msctls_updown32", null);
            _upDown = upDown == IntPtr.Zero ? null : new UpDownHook(this, upDown);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { _remap?.Dispose(); _remap = null; }
            base.Dispose(disposing);
        }

        protected override void WndProc(ref Message m)
        {
            if (_remap != null && Remaps(m)) Remap(ref m, _remap, msg => DefWndProc(ref msg));
            else base.WndProc(ref m);
        }

        private static bool Remaps(Message m) =>
            m.Msg == WM_ERASEBKGND || (m.Msg == WM_PAINT && m.WParam == IntPtr.Zero);

        /// <summary>
        /// Xử lý thay WM_ERASEBKGND / WM_PAINT khi đang đổi tông. <paramref name="native"/> là thủ
        /// tục cửa sổ gốc: nó được gọi xoá nền rồi vẽ vào DC của bitmap bằng WM_PRINTCLIENT - đường
        /// vẽ-vào-HDC-có-sẵn mà common controls được tài liệu hoá là hỗ trợ (WM_PAINT kèm HDC ở
        /// wParam chỉ là quy ước ngầm). Bitmap 32bppRgb chứ không ARGB: GDI để kênh alpha bằng 0.
        /// </summary>
        private static void Remap(ref Message m, ImageAttributes remap, Action<Message> native)
        {
            m.Result = (IntPtr)1;
            if (m.Msg == WM_ERASEBKGND) return;

            var ps = new PaintStruct();
            IntPtr hdc = BeginPaint(m.HWnd, ref ps);
            try
            {
                GetClientRect(m.HWnd, out var rc);
                if (rc.Right > 0 && rc.Bottom > 0)
                {
                    using var bmp = new Bitmap(rc.Right, rc.Bottom, PixelFormat.Format32bppRgb);
                    using (var g = Graphics.FromImage(bmp))
                    {
                        IntPtr mem = g.GetHdc();
                        try
                        {
                            native(Message.Create(m.HWnd, WM_ERASEBKGND, mem, IntPtr.Zero));
                            native(Message.Create(m.HWnd, WM_PRINTCLIENT, mem, (IntPtr)PRF_CLIENT));
                        }
                        finally { g.ReleaseHdc(mem); }
                    }
                    using var screen = Graphics.FromHdc(hdc);
                    screen.DrawImage(bmp, new Rectangle(0, 0, rc.Right, rc.Bottom),
                        0, 0, rc.Right, rc.Bottom, GraphicsUnit.Pixel, remap);
                }
            }
            finally { EndPaint(m.HWnd, ref ps); }
            m.Result = IntPtr.Zero;
        }

        private sealed class UpDownHook : NativeWindow
        {
            private readonly ReverseDateTimePicker _owner;

            public UpDownHook(ReverseDateTimePicker owner, IntPtr handle)
            {
                _owner = owner;
                AssignHandle(handle);
            }

            protected override void WndProc(ref Message m)
            {
                var remap = _owner._remap;
                if (remap != null && Remaps(m)) Remap(ref m, remap, msg => DefWndProc(ref msg));
                else base.WndProc(ref m);
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct PaintStruct
        {
            public IntPtr Hdc;
            public int Erase;
            public Rect Paint;
            public int Restore;
            public int IncUpdate;
            public long Reserved0, Reserved1, Reserved2, Reserved3; // BYTE rgbReserved[32]
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct Rect { public int Left, Top, Right, Bottom; }

        [DllImport("user32.dll")] private static extern IntPtr BeginPaint(IntPtr hWnd, ref PaintStruct ps);
        [DllImport("user32.dll")] private static extern bool EndPaint(IntPtr hWnd, ref PaintStruct ps);
        [DllImport("user32.dll")] private static extern bool GetClientRect(IntPtr hWnd, out Rect rect);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string className, string windowName);
    }
}

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Printing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Sunny.UI;

namespace AutoJMS
{
    /// <summary>
    /// "In lại đơn" (tabPrint_inLaiDon).
    ///
    /// Flow the owner asked for:
    ///   nhập mã -> Tìm kiếm -> preview bản in (bắt buộc) -> tuỳ chọn bật sửa -> Print.
    ///
    /// The label PDF is fetched once, at search time, through the JMS
    /// <c>expressPrint/batchPrintPDF</c> endpoint. The (optionally edited) bytes are then
    /// parked in the normal print-job cache under the exact key <see cref="ExecutePrintAsync"/>
    /// computes, so pressing IN prints what the preview showed without a second JMS call.
    ///
    /// The whole UI is built here in code — Main.Designer.cs is not touched.
    /// </summary>
    public partial class Main
    {
        // batchPrintPDF wants the JMS breadcrumb header verbatim (already URL-encoded).
        private const string ReprintRouterNameList =
            "%E7%BB%BC%E5%90%88%E4%B8%9A%E5%8A%A1%3E%E9%9D%A2%E5%8D%95%E8%A1%A5%E6%89%93%3E%E9%9D%A2%E5%8D%95%E8%A1%A5%E6%89%93";
        private const string ReprintRouteName = "Centerforplay";
        private const string ReprintEndpoint = "operatingplatform/expressPrint/batchPrintPDF";

        // Long enough that the user can read, edit and re-check the preview before printing.
        private static readonly TimeSpan ReprintJobTtl = TimeSpan.FromMinutes(30);
        private static readonly TimeSpan ReprintApiTimeout = TimeSpan.FromSeconds(25);
        private static readonly TimeSpan ReprintDownloadTimeout = TimeSpan.FromSeconds(60);
        private const int ReprintDebounceMs = 600;
        private const int ReprintPreviewFilesKept = 5;

        // FontAwesome: con mắt mở / con mắt gạch chéo.
        private const int ReprintSymbolEyeOpen = 61550;
        private const int ReprintSymbolEyeClosed = 61552;

        // ── controls (all created in BuildTabPrintInLaiDonSection) ──
        private TableLayoutPanel _reprintRoot;
        private UICheckBox _reprintChkReceiver;
        private UICheckBox _reprintChkRoute;
        private UICheckBox _reprintChkNotes;
        private UICheckBox _reprintChkPrintCount;
        private UITextBox _reprintTxtName;
        private UITextBox _reprintTxtPhone;
        private UISymbolButton _reprintBtnRevealPhone;
        private UITextBox _reprintTxtAddress;
        private UITextBox _reprintTxtRoute1;
        private UITextBox _reprintTxtRoute2;
        private UITextBox _reprintTxtRoute3;
        private UITextBox _reprintTxtNote;
        private UITextBox _reprintTxtCod;
        private UITextBox _reprintTxtDeadline;
        private UITextBox _reprintTxtPrintCode;
        private UITextBox _reprintTxtPrintTimes;
        private UITextBox _reprintTxtPrintTime;
        private UILabel _reprintStatus;
        private ToolTip _reprintTip;

        // ── state ──
        private byte[] _reprintOriginalPdf;
        private string _reprintCacheKey = "";
        private string _reprintFirstWaybill = "";
        private List<string> _reprintWaybills = new();
        private System.Windows.Forms.Timer _reprintDebounce;
        private readonly SemaphoreSlim _reprintGate = new(1, 1);
        private bool _reprintSuppressEvents;
        private int _reprintPreviewSeq;

        /// <summary>Tên + SĐT người nhận vừa lấy từ JMS cho đơn đang xem trước.</summary>
        private ReceiverContact _reprintReceiver;

        /// <summary>Owner đã bấm nút con mắt: ô SĐT — và bản in — dùng số đầy đủ.</summary>
        private bool _reprintPhoneRevealed;

        private bool IsReprintModeActive =>
            _printService != null && _printService.CurrentMode == PrintMode.InLaiDon;

        // ==================================================================================
        // UI
        // ==================================================================================

        /// <summary>
        /// Builds the whole "In lại đơn" editor inside the (empty) designer tab page.
        /// Called from the Main constructor, before AppTheme re-applies, so the controls
        /// pick up the current theme like every other dynamically created control.
        /// </summary>
        private void BuildTabPrintInLaiDonSection()
        {
            if (tabPrint_inLaiDon == null || tabPrint_inLaiDon.IsDisposed) return;
            if (tabPrint_inLaiDon.Controls.Find("tabPrint_reprintRoot", false).Length > 0) return;

            _reprintTip = new ToolTip { InitialDelay = 350, ReshowDelay = 120 };

            _reprintRoot = new TableLayoutPanel
            {
                Name = "tabPrint_reprintRoot",
                Dock = DockStyle.Fill,
                ColumnCount = 4,
                RowCount = 2,
                Margin = new Padding(0),
                Padding = new Padding(4, 2, 4, 2),
                BackColor = Color.Transparent
            };
            _reprintRoot.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 30F));
            _reprintRoot.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 15F));
            _reprintRoot.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 28F));
            _reprintRoot.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 27F));
            _reprintRoot.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            _reprintRoot.RowStyles.Add(new RowStyle(SizeType.Absolute, 22F));

            _reprintRoot.Controls.Add(BuildReprintReceiverCard(), 0, 0);
            _reprintRoot.Controls.Add(BuildReprintRouteCard(), 1, 0);
            _reprintRoot.Controls.Add(BuildReprintNotesCard(), 2, 0);
            _reprintRoot.Controls.Add(BuildReprintPrintCountCard(), 3, 0);

            _reprintStatus = new UILabel
            {
                Name = "tabPrint_reprintStatus",
                Dock = DockStyle.Fill,
                Text = "Nhập mã vận đơn rồi bấm Tìm kiếm để xem trước bản in.",
                TextAlign = ContentAlignment.MiddleLeft,
                Font = new Font("Segoe UI", 9F, FontStyle.Regular),
                Margin = new Padding(2, 0, 2, 0)
            };
            _reprintRoot.Controls.Add(_reprintStatus, 0, 1);
            _reprintRoot.SetColumnSpan(_reprintStatus, 4);

            tabPrint_inLaiDon.Controls.Add(_reprintRoot);

            _reprintDebounce = new System.Windows.Forms.Timer { Interval = ReprintDebounceMs };
            _reprintDebounce.Tick += ReprintDebounce_Tick;

            ApplyReprintEditingState();
            ApplyReprintPhoneVisibility();
        }

        /// <summary>
        /// Thẻ 1 — Người nhận: tên (chỉ đọc), SĐT + nút con mắt, địa chỉ.
        /// </summary>
        private Control BuildReprintReceiverCard()
        {
            var card = NewReprintCard("tabPrint_reprintCardReceiver", 3, out var body);
            body.RowStyles.Add(new RowStyle(SizeType.Absolute, 23F));
            body.RowStyles.Add(new RowStyle(SizeType.Absolute, 27F));
            body.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

            _reprintChkReceiver = NewReprintCheckBox("tabPrint_reprintChkReceiver", "Sửa Người nhận");

            // Tên lấy thẳng từ JMS và cố ý KHÔNG cho sửa: nó chỉ có mặt để miếng vá in lại
            // đúng tên của nhãn gốc. Trước đây ô này luôn rỗng nên tick "Sửa" là mất tên.
            _reprintTxtName = NewReprintTextBox("tabPrint_reprintTxtName", "Tên người nhận", false);
            _reprintTxtPhone = NewReprintTextBox("tabPrint_reprintTxtPhone", "Số điện thoại", false);
            _reprintTxtPhone.Margin = new Padding(4, 1, 0, 2);
            _reprintBtnRevealPhone = NewReprintRevealButton();
            _reprintTxtAddress = NewReprintTextBox("tabPrint_reprintTxtAddress", "Địa chỉ người nhận", true);

            var contact = NewReprintFieldRow();
            AddReprintCell(contact, _reprintTxtName, 0);
            AddReprintCell(contact, _reprintTxtPhone, 104);
            AddReprintCell(contact, _reprintBtnRevealPhone, 30);

            body.Controls.Add(_reprintChkReceiver, 0, 0);
            body.Controls.Add(contact, 0, 1);
            body.Controls.Add(_reprintTxtAddress, 0, 2);
            return card;
        }

        /// <summary>Thẻ 2 — Mã tuyến: ba dòng ngắn, không cần rộng.</summary>
        private Control BuildReprintRouteCard()
        {
            var card = NewReprintCard("tabPrint_reprintCardRoute", 5, out var body);
            body.RowStyles.Add(new RowStyle(SizeType.Absolute, 23F));
            body.RowStyles.Add(new RowStyle(SizeType.Absolute, 25F));
            body.RowStyles.Add(new RowStyle(SizeType.Absolute, 25F));
            body.RowStyles.Add(new RowStyle(SizeType.Absolute, 25F));
            body.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

            _reprintChkRoute = NewReprintCheckBox("tabPrint_reprintChkRoute", "Sửa Mã tuyến");
            _reprintTxtRoute1 = NewReprintTextBox("tabPrint_reprintTxtRoute1", "Mã tuyến 1", false);
            _reprintTxtRoute2 = NewReprintTextBox("tabPrint_reprintTxtRoute2", "Mã tuyến 2", false);
            _reprintTxtRoute3 = NewReprintTextBox("tabPrint_reprintTxtRoute3", "Mã tuyến 3", false);

            body.Controls.Add(_reprintChkRoute, 0, 0);
            body.Controls.Add(_reprintTxtRoute1, 0, 1);
            body.Controls.Add(_reprintTxtRoute2, 0, 2);
            body.Controls.Add(_reprintTxtRoute3, 0, 3);
            return card;
        }

        /// <summary>Thẻ 3 — Ghi chú & COD.</summary>
        private Control BuildReprintNotesCard()
        {
            var card = NewReprintCard("tabPrint_reprintCardNotes", 3, out var body);
            body.RowStyles.Add(new RowStyle(SizeType.Absolute, 23F));
            body.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            body.RowStyles.Add(new RowStyle(SizeType.Absolute, 27F));

            _reprintChkNotes = NewReprintCheckBox("tabPrint_reprintChkNotes", "Sửa Ghi chú & COD");
            _reprintTxtNote = NewReprintTextBox("tabPrint_reprintTxtNote", "Ghi chú", true);
            _reprintTxtCod = NewReprintTextBox("tabPrint_reprintTxtCod", "Tiền thu hộ", false);
            _reprintTxtDeadline = NewReprintTextBox("tabPrint_reprintTxtDeadline", "Giao trước", false);
            _reprintTxtDeadline.Margin = new Padding(4, 1, 0, 2);

            // Ô COD chứa "1,234,000" còn "Giao trước" chứa một mốc ngày: cả hai đều ngắn,
            // nên khoá bề rộng thay vì để chúng kéo hết chiều ngang của thẻ.
            var bottom = NewReprintFieldRow();
            AddReprintCell(bottom, _reprintTxtCod, 108);
            AddReprintCell(bottom, _reprintTxtDeadline, 108);
            AddReprintCell(bottom, null, 0);

            body.Controls.Add(_reprintChkNotes, 0, 0);
            body.Controls.Add(_reprintTxtNote, 0, 1);
            body.Controls.Add(bottom, 0, 2);
            return card;
        }

        /// <summary>
        /// Thẻ 4 — dòng đếm lần in ở cột phải của nhãn: "214A03 in lần 11:" rồi "22:17 12-09-2026".
        /// </summary>
        private Control BuildReprintPrintCountCard()
        {
            var card = NewReprintCard("tabPrint_reprintCardPrintCount", 4, out var body);
            body.RowStyles.Add(new RowStyle(SizeType.Absolute, 23F));
            body.RowStyles.Add(new RowStyle(SizeType.Absolute, 27F));
            body.RowStyles.Add(new RowStyle(SizeType.Absolute, 27F));
            body.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

            _reprintChkPrintCount = NewReprintCheckBox("tabPrint_reprintChkPrintCount", "Sửa dòng đếm lần in");
            _reprintTxtPrintCode = NewReprintTextBox("tabPrint_reprintTxtPrintCode", "Mã bưu cục", false);
            _reprintTxtPrintTimes = NewReprintTextBox("tabPrint_reprintTxtPrintTimes", "Lần in", false);
            _reprintTxtPrintTimes.Margin = new Padding(4, 1, 0, 2);
            _reprintTxtPrintTime = NewReprintTextBox("tabPrint_reprintTxtPrintTime", "Giờ & ngày in", false);

            var header = NewReprintFieldRow();
            AddReprintCell(header, _reprintTxtPrintCode, 96);
            AddReprintCell(header, _reprintTxtPrintTimes, 62);
            AddReprintCell(header, null, 0);

            var stamp = NewReprintFieldRow();
            AddReprintCell(stamp, _reprintTxtPrintTime, 162);
            AddReprintCell(stamp, null, 0);

            body.Controls.Add(_reprintChkPrintCount, 0, 0);
            body.Controls.Add(header, 0, 1);
            body.Controls.Add(stamp, 0, 2);
            return card;
        }

        /// <summary>
        /// Một "thẻ": khung bo góc do AppTheme tô (nhánh <c>UIPanel</c>) bọc một bảng dọc.
        /// Ô tick nằm ở dòng đầu và đóng luôn vai tiêu đề, nên không tốn thêm dòng cho chữ
        /// tiêu đề riêng — chiều cao khả dụng của dải này chỉ khoảng 120px.
        /// </summary>
        private static UIPanel NewReprintCard(string name, int rowCount, out TableLayoutPanel body)
        {
            var card = new UIPanel
            {
                Name = name,
                Dock = DockStyle.Fill,
                Margin = new Padding(3, 0, 3, 0),
                Padding = new Padding(6, 4, 6, 4),
                Radius = 8
            };

            body = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = rowCount,
                Margin = new Padding(0),
                BackColor = Color.Transparent
            };
            body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));

            card.Controls.Add(body);
            return card;
        }

        /// <summary>
        /// Dòng ngang trong thẻ. Các ô được nhồi bằng <see cref="AddReprintCell"/> theo bề rộng
        /// cố định, đúng yêu cầu "thu gọn các trường đang quá rộng so với dữ liệu".
        /// </summary>
        private static TableLayoutPanel NewReprintFieldRow()
        {
            var row = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 0,
                RowCount = 1,
                Margin = new Padding(0),
                BackColor = Color.Transparent
            };
            row.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            return row;
        }

        /// <summary>
        /// Thêm một cột vào <paramref name="row"/>. <paramref name="fixedWidth"/> ≤ 0 nghĩa là
        /// cột co giãn; <paramref name="control"/> null tạo cột đệm để đẩy phần dư sang phải.
        /// </summary>
        private static void AddReprintCell(TableLayoutPanel row, Control control, int fixedWidth)
        {
            int index = row.ColumnCount;
            row.ColumnCount = index + 1;
            row.ColumnStyles.Add(fixedWidth > 0
                ? new ColumnStyle(SizeType.Absolute, fixedWidth)
                : new ColumnStyle(SizeType.Percent, 100F));

            if (control != null) row.Controls.Add(control, index, 0);
        }

        private UISymbolButton NewReprintRevealButton()
        {
            var button = new UISymbolButton
            {
                Name = "tabPrint_reprintBtnRevealPhone",
                Text = "",
                Dock = DockStyle.Fill,
                Symbol = ReprintSymbolEyeOpen,
                SymbolSize = 16,
                Radius = 6,
                Margin = new Padding(4, 1, 0, 2),
                MinimumSize = new Size(1, 1)
            };
            button.Click += Reprint_RevealPhoneClicked;
            return button;
        }

        private UICheckBox NewReprintCheckBox(string name, string text)
        {
            var box = new UICheckBox
            {
                Name = name,
                Text = text,
                Dock = DockStyle.Fill,
                Checked = false,
                Font = new Font("Segoe UI", 9F, FontStyle.Regular),
                Margin = new Padding(0, 1, 0, 1)
            };
            box.CheckedChanged += Reprint_EditToggleChanged;
            return box;
        }

        private UITextBox NewReprintTextBox(string name, string watermark, bool multiline)
        {
            var box = new UITextBox
            {
                Name = name,
                Dock = DockStyle.Fill,
                Multiline = multiline,
                Watermark = watermark,
                ShowText = false,
                Font = new Font("Segoe UI", 9F, FontStyle.Regular),
                Margin = new Padding(0, 1, 0, 2),
                MinimumSize = new Size(1, 1),
                Padding = new Padding(2),
                TextAlignment = multiline ? ContentAlignment.TopLeft : ContentAlignment.MiddleLeft
            };
            box.TextChanged += Reprint_FieldTextChanged;
            return box;
        }

        // ==================================================================================
        // UI events
        // ==================================================================================

        private void Reprint_EditToggleChanged(object sender, EventArgs e)
        {
            if (_reprintSuppressEvents) return;

            ApplyReprintEditingState();
            _reprintDebounce?.Stop();
            _ = RenderReprintPreviewAsync();
        }

        private void Reprint_FieldTextChanged(object sender, EventArgs e)
        {
            if (_reprintSuppressEvents) return;
            if (_reprintDebounce == null) return;

            _reprintDebounce.Stop();
            _reprintDebounce.Start();
        }

        private void ReprintDebounce_Tick(object sender, EventArgs e)
        {
            _reprintDebounce.Stop();
            _ = RenderReprintPreviewAsync();
        }

        /// <summary>
        /// Bấm con mắt: đổi giữa bản che và số đầy đủ. Owner đã chốt là nút này vừa lộ số trên
        /// app vừa đưa chính số đó lên bản in, nên phải dựng lại preview ngay sau khi đổi.
        /// </summary>
        private void Reprint_RevealPhoneClicked(object sender, EventArgs e)
        {
            if (_reprintReceiver == null || !_reprintReceiver.HasUnmaskedPhone)
            {
                SetReprintStatus("JMS không trả số điện thoại đầy đủ cho đơn này.", true);
                return;
            }

            _reprintPhoneRevealed = !_reprintPhoneRevealed;
            ApplyReprintPhoneVisibility();

            _reprintDebounce?.Stop();
            _ = RenderReprintPreviewAsync();
        }

        /// <summary>
        /// Đồng bộ ô SĐT + biểu tượng nút theo <see cref="_reprintPhoneRevealed"/>. Nút chỉ sáng
        /// khi thực sự có số đầy đủ để lộ — không có thì bấm cũng chẳng đổi được gì.
        /// </summary>
        private void ApplyReprintPhoneVisibility()
        {
            var contact = _reprintReceiver;
            bool canReveal = contact != null && contact.HasUnmaskedPhone;
            bool revealed = canReveal && _reprintPhoneRevealed;

            if (_reprintBtnRevealPhone != null && !_reprintBtnRevealPhone.IsDisposed)
            {
                _reprintBtnRevealPhone.Enabled = canReveal;
                _reprintBtnRevealPhone.Symbol = revealed ? ReprintSymbolEyeClosed : ReprintSymbolEyeOpen;
                _reprintTip?.SetToolTip(_reprintBtnRevealPhone, canReveal
                    ? (revealed ? "Ẩn lại số điện thoại (in bản che)" : "Hiện số điện thoại đầy đủ (in cả số)")
                    : "Đơn này JMS chỉ trả số đã che");
            }

            if (contact == null) return;

            _reprintSuppressEvents = true;
            try
            {
                SetReprintText(_reprintTxtPhone, revealed ? contact.Phone : contact.MaskedPhone);
            }
            finally
            {
                _reprintSuppressEvents = false;
            }
        }

        /// <summary>
        /// Fields stay locked until their checkbox is ticked — the owner asked for
        /// "mặc định không bật sửa" on every region.
        /// </summary>
        private void ApplyReprintEditingState()
        {
            bool receiver = _reprintChkReceiver?.Checked == true;
            bool route = _reprintChkRoute?.Checked == true;
            bool notes = _reprintChkNotes?.Checked == true;
            bool printCount = _reprintChkPrintCount?.Checked == true;

            // "Tên người nhận" chỉ để hiển thị: Owner chốt là không bật sửa. Vẫn giữ Enabled
            // để đọc và copy được, chỉ chặn gõ.
            SetReprintFieldReadOnly(_reprintTxtName);

            SetReprintFieldEnabled(_reprintTxtPhone, receiver);
            SetReprintFieldEnabled(_reprintTxtAddress, receiver);
            SetReprintFieldEnabled(_reprintTxtRoute1, route);
            SetReprintFieldEnabled(_reprintTxtRoute2, route);
            SetReprintFieldEnabled(_reprintTxtRoute3, route);
            SetReprintFieldEnabled(_reprintTxtNote, notes);
            SetReprintFieldEnabled(_reprintTxtCod, notes);
            SetReprintFieldEnabled(_reprintTxtDeadline, notes);
            SetReprintFieldEnabled(_reprintTxtPrintCode, printCount);
            SetReprintFieldEnabled(_reprintTxtPrintTimes, printCount);
            SetReprintFieldEnabled(_reprintTxtPrintTime, printCount);
        }

        private static void SetReprintFieldEnabled(UITextBox box, bool enabled)
        {
            if (box == null || box.IsDisposed) return;
            box.ReadOnly = !enabled;
            box.Enabled = enabled;
        }

        private static void SetReprintFieldReadOnly(UITextBox box)
        {
            if (box == null || box.IsDisposed) return;
            box.ReadOnly = true;
            box.Enabled = true;
        }

        private void SetReprintStatus(string message, bool isError = false)
        {
            if (_reprintStatus == null || _reprintStatus.IsDisposed) return;
            if (_reprintStatus.InvokeRequired)
            {
                _reprintStatus.BeginInvoke((MethodInvoker)(() => SetReprintStatus(message, isError)));
                return;
            }

            bool isDark = UI.AppTheme.CurrentTheme == UI.ThemeMode.Dark;
            _reprintStatus.Text = message ?? "";
            _reprintStatus.ForeColor = isError
                ? (isDark ? Color.FromArgb(252, 115, 115) : Color.Red)
                : (isDark ? Color.White : Color.FromArgb(48, 48, 48));
        }

        // ==================================================================================
        // State lifecycle
        // ==================================================================================

        /// <summary>Drops the fetched label and (optionally) empties the editor.</summary>
        private void ResetTabPrintReprintState(bool clearInputs)
        {
            _reprintDebounce?.Stop();
            _reprintOriginalPdf = null;
            _reprintCacheKey = "";
            _reprintFirstWaybill = "";
            _reprintWaybills = new List<string>();

            if (!clearInputs) return;

            _reprintReceiver = null;
            _reprintPhoneRevealed = false;

            _reprintSuppressEvents = true;
            try
            {
                SetReprintText(_reprintTxtName, "");
                SetReprintText(_reprintTxtPhone, "");
                SetReprintText(_reprintTxtAddress, "");
                SetReprintText(_reprintTxtRoute1, "");
                SetReprintText(_reprintTxtRoute2, "");
                SetReprintText(_reprintTxtRoute3, "");
                SetReprintText(_reprintTxtNote, "");
                SetReprintText(_reprintTxtCod, "");
                SetReprintText(_reprintTxtDeadline, "");
                SetReprintText(_reprintTxtPrintCode, "");
                SetReprintText(_reprintTxtPrintTimes, "");
                SetReprintText(_reprintTxtPrintTime, "");
            }
            finally
            {
                _reprintSuppressEvents = false;
            }

            ApplyReprintPhoneVisibility();
            SetReprintStatus("Nhập mã vận đơn rồi bấm Tìm kiếm để xem trước bản in.");
        }

        /// <summary>
        /// Called after a successful print so the next IN cannot re-send the same stale
        /// label bytes — the flow has to go through Tìm kiếm (and a new preview) again.
        /// </summary>
        private void InvalidateReprintJobAfterPrint()
        {
            if (string.IsNullOrWhiteSpace(_reprintCacheKey)) return;

            lock (_printJobCacheLock)
            {
                _printJobCacheBySignature.Remove(_reprintCacheKey);
            }
            ResetTabPrintReprintState(clearInputs: false);
            SetReprintStatus("Đã in. Bấm Tìm kiếm lại nếu muốn in lại đơn này.");
        }

        private static void SetReprintText(UITextBox box, string value)
        {
            if (box == null || box.IsDisposed) return;
            box.Text = value ?? "";
        }

        private static string ReadReprintText(UITextBox box) => (box?.Text ?? "").Trim();

        // ==================================================================================
        // Pipeline
        // ==================================================================================

        /// <summary>
        /// Search is done: pull the label from JMS, prefill the editor from the grid row
        /// and show the preview. Never throws — failures only degrade to the legacy path.
        /// </summary>
        private async Task PrepareReprintPreviewAsync()
        {
            if (!IsReprintModeActive) return;

            var waybills = _printService.GetSelectedWaybills() ?? new List<string>();
            if (waybills.Count == 0)
            {
                ResetTabPrintReprintState(clearInputs: false);
                SetReprintStatus("Không có vận đơn nào được chọn để in lại.", true);
                return;
            }

            _reprintDebounce?.Stop();
            _reprintWaybills = waybills;
            _reprintFirstWaybill = waybills[0];
            _reprintCacheKey = BuildPrintPdfCacheKey(waybills, 1, 4);
            _reprintOriginalPdf = null;

            PrefillReprintEditor(waybills);
            SetReprintStatus($"Đang lấy bản in từ JMS ({waybills.Count} đơn)...");

            try
            {
                byte[] pdf = await FetchReprintLabelAsync(waybills).ConfigureAwait(true);
                if (pdf == null || pdf.Length == 0)
                {
                    SetReprintStatus("Không lấy được PDF bản in từ JMS. Bấm IN để dùng luồng in mặc định.", true);
                    return;
                }

                _reprintOriginalPdf = pdf;

                // Lấy sau khi có PDF, trước lần dựng preview đầu tiên: bảng TRACKING không có
                // tên/SĐT người nhận nên phải hỏi JMS, và vá lại lượt sau thì preview sẽ nháy.
                await LoadReprintReceiverAsync().ConfigureAwait(true);

                await RenderReprintPreviewAsync().ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                AppLogger.Error($"In lại đơn: chuẩn bị preview thất bại waybill={_reprintFirstWaybill}", ex);
                SetReprintStatus($"Lỗi lấy bản in: {ex.Message}", true);
            }
        }

        /// <summary>
        /// Hỏi JMS tên + SĐT người nhận rồi đổ vào hai ô của thẻ "Người nhận". Không ném:
        /// thiếu dữ liệu chỉ làm mất tiện lợi, còn bản in thì vẫn dựng được.
        /// </summary>
        private async Task LoadReprintReceiverAsync()
        {
            SetReprintStatus("Đang lấy thông tin người nhận...");

            ReceiverContact contact;
            using (var timeoutCts = new CancellationTokenSource(ReprintApiTimeout))
            {
                contact = await ReceiverContactService
                    .FetchAsync(_reprintFirstWaybill, timeoutCts.Token)
                    .ConfigureAwait(true);
            }

            _reprintReceiver = contact;
            _reprintPhoneRevealed = false;

            _reprintSuppressEvents = true;
            try
            {
                SetReprintText(_reprintTxtName, contact?.Name ?? "");
                SetReprintText(_reprintTxtPhone, contact?.MaskedPhone ?? "");
            }
            finally
            {
                _reprintSuppressEvents = false;
            }

            ApplyReprintPhoneVisibility();
        }

        /// <summary>Fills the editor from the tracking row behind the first selected waybill.</summary>
        private void PrefillReprintEditor(List<string> waybills)
        {
            var rows = _printService?.GetLoadedPrintRows();
            TrackingRow row = null;
            if (rows != null && rows.Count > 0 && waybills.Count > 0)
            {
                string first = GetBaseWaybill(waybills[0]);
                row = rows.FirstOrDefault(r => string.Equals(GetBaseWaybill(r?.WaybillNo), first, StringComparison.OrdinalIgnoreCase))
                      ?? rows[0];
            }

            // Tên/SĐT do LoadReprintReceiverAsync điền — ở đây chỉ dọn sạch phần của đơn trước.
            _reprintReceiver = null;
            _reprintPhoneRevealed = false;

            _reprintSuppressEvents = true;
            try
            {
                SetReprintText(_reprintTxtName, "");
                SetReprintText(_reprintTxtPhone, "");
                SetReprintText(_reprintTxtAddress, BuildReceiverAddress(row));
                SetReprintText(_reprintTxtRoute1, Dash2Empty(row?.MaDoan1));
                SetReprintText(_reprintTxtRoute2, Dash2Empty(row?.MaDoan2));
                SetReprintText(_reprintTxtRoute3, Dash2Empty(row?.MaDoan3));
                SetReprintText(_reprintTxtNote, Dash2Empty(row?.NoiDungHangHoa));
                SetReprintText(_reprintTxtCod, Dash2Empty(row?.CODThucTe));
                SetReprintText(_reprintTxtDeadline, "");
                SetReprintText(_reprintTxtPrintCode, ResolveReprintNetworkCode(row));
                SetReprintText(_reprintTxtPrintTimes, ResolveReprintPrintCount(row));

                // Nhãn vừa được JMS sinh ra vài giây trước, nên "giữ như cũ" chính là lúc này.
                SetReprintText(_reprintTxtPrintTime, DateTime.Now.ToString("HH:mm dd-MM-yyyy"));
            }
            finally
            {
                _reprintSuppressEvents = false;
            }

            ApplyReprintPhoneVisibility();
        }

        /// <summary>
        /// Mã bưu cục mở đầu dòng đếm lần in. Cùng thứ tự ưu tiên với
        /// <c>PrintService.ResolveSenderNetworkCode</c> — hàm đó private nên chép lại tại đây
        /// thay vì nới rộng bề mặt public của PrintService cho một ô nhập liệu.
        /// </summary>
        private static string ResolveReprintNetworkCode(TrackingRow row)
        {
            if (row == null) return "";

            foreach (var candidate in new[]
                     {
                         row.PrintSenderNetworkCode, row.NewTerminalDispatchCode,
                         row.MaDoanFull, row.MaDoan2
                     })
            {
                string text = Dash2Empty(candidate);
                if (text.Length > 0) return text;
            }
            return "";
        }

        /// <summary>Số lần in JMS đang ghi nhận; cùng quy tắc với <c>PrintService.ResolvePrintCount</c>.</summary>
        private static string ResolveReprintPrintCount(TrackingRow row)
        {
            if (row == null) return "";
            if (row.PrintApprovalPrintCount.HasValue)
                return row.PrintApprovalPrintCount.Value.ToString(CultureInfo.InvariantCulture);

            return row.PrintCount > 0 ? row.PrintCount.ToString(CultureInfo.InvariantCulture) : "";
        }

        private static string BuildReceiverAddress(TrackingRow row)
        {
            if (row == null) return "";
            var parts = new[] { Dash2Empty(row.DiaChiNhanHang), Dash2Empty(row.Phuong) }
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .ToList();
            return string.Join(", ", parts);
        }

        private static string Dash2Empty(string value)
        {
            string text = (value ?? "").Trim();
            return text == "-" ? "" : text;
        }

        /// <summary>POSTs batchPrintPDF and downloads the returned label.</summary>
        private async Task<byte[]> FetchReprintLabelAsync(List<string> waybills)
        {
            await RefreshAuthTokenAsync().ConfigureAwait(true);
            if (!JmsAuthStateService.HasToken)
                throw new InvalidOperationException("Không tìm thấy Token xác thực.");

            var payload = new Dictionary<string, object>
            {
                { "type", 1 },
                { "fileType", 1 },
                { "waybillNos", waybills },
                { "mold", 1 },
                { "printOptType", 2 },
                { "countryId", "1" }
            };
            string json = JsonSerializer.Serialize(payload);
            string url = AppConfig.Current.BuildJmsApiUrl(ReprintEndpoint);

            string body;
            int statusCode;
            using (var timeoutCts = new CancellationTokenSource(ReprintApiTimeout))
            using (var response = await JmsApiClient.PostJsonAsync(
                       url, json, ReprintRouteName, ReprintRouterNameList, ct: timeoutCts.Token).ConfigureAwait(true))
            {
                if (response == null)
                    throw new InvalidOperationException("JMS không phản hồi batchPrintPDF.");

                statusCode = (int)response.StatusCode;
                body = await response.Content.ReadAsStringAsync(timeoutCts.Token).ConfigureAwait(true) ?? "";
            }

            AppLogger.Info($"REPRINT_API_RESPONSE waybill={_reprintFirstWaybill} http={statusCode} bodyLength={body.Length}");
            if (!JmsResponseClassifier.IsSuccess(statusCode, body))
                throw new InvalidOperationException($"JMS từ chối batchPrintPDF (HTTP {statusCode}).");

            string pdfUrl = ExtractReprintPdfUrl(body);
            if (string.IsNullOrWhiteSpace(pdfUrl))
                throw new InvalidOperationException("Phản hồi JMS không có pdfUrl.");

            using var downloadCts = new CancellationTokenSource(ReprintDownloadTimeout);
            var bytes = await JmsApiClient.Instance.GetByteArrayAsync(pdfUrl, downloadCts.Token).ConfigureAwait(true);
            AppLogger.Info($"REPRINT_PDF_DOWNLOADED waybill={_reprintFirstWaybill} bytes={bytes?.Length ?? 0}");
            return bytes;
        }

        private static string ExtractReprintPdfUrl(string rawJson)
        {
            if (string.IsNullOrWhiteSpace(rawJson)) return "";
            try
            {
                using var doc = JsonDocument.Parse(rawJson);
                if (!doc.RootElement.TryGetProperty("data", out var data)) return "";

                if (data.ValueKind == JsonValueKind.Object
                    && data.TryGetProperty("pdfUrl", out var urlElement)
                    && urlElement.ValueKind == JsonValueKind.String)
                {
                    return (urlElement.GetString() ?? "").Trim();
                }

                if (data.ValueKind == JsonValueKind.String)
                    return (data.GetString() ?? "").Trim();

                return "";
            }
            catch (Exception ex)
            {
                AppLogger.Warning($"In lại đơn: không đọc được pdfUrl: {ex.Message}");
                return "";
            }
        }

        /// <summary>
        /// Re-applies the overlay to the fetched label, parks the result in the print-job
        /// cache and points the WebView2 preview at it.
        /// </summary>
        private async Task RenderReprintPreviewAsync()
        {
            if (_reprintOriginalPdf == null || _reprintOriginalPdf.Length == 0) return;
            if (!await _reprintGate.WaitAsync(0).ConfigureAwait(true))
                return;

            try
            {
                var original = _reprintOriginalPdf;
                var content = BuildReprintOverlayContent();
                string appliedError = "";

                byte[] finalPdf = original;
                if (content.HasAnyEdit)
                {
                    finalPdf = await Task.Run(() =>
                    {
                        var produced = PdfReprintModifier.TryApply(original, content, out string err);
                        appliedError = err;
                        return produced;
                    }).ConfigureAwait(true);
                }

                string previewPath = await WriteReprintPreviewFileAsync(finalPdf).ConfigureAwait(true);
                CacheReprintPrintJob(finalPdf, previewPath);
                NavigatePreviewTo(previewPath);

                if (!string.IsNullOrEmpty(appliedError))
                    SetReprintStatus($"Không đè được nội dung ({appliedError}) — đang xem bản gốc.", true);
                else if (content.EditReceiver && content.ReceiverName.Length == 0)
                    SetReprintStatus(
                        $"Đã xem trước bản sửa cho {_reprintFirstWaybill}, NHƯNG không lấy được tên người nhận " +
                        "— vùng này sẽ in thiếu tên. Bỏ tick \"Sửa Người nhận\" nếu không muốn vậy.", true);
                else if (content.HasAnyEdit)
                    SetReprintStatus($"Đã xem trước bản sửa cho {_reprintFirstWaybill}. Bấm IN để in đúng bản này.");
                else
                    SetReprintStatus($"Đã xem trước bản gốc cho {_reprintFirstWaybill}. Tick ô \"Sửa\" nếu cần chỉnh.");
            }
            catch (Exception ex)
            {
                AppLogger.Error($"In lại đơn: dựng preview thất bại waybill={_reprintFirstWaybill}", ex);
                SetReprintStatus($"Lỗi dựng bản xem trước: {ex.Message}", true);
            }
            finally
            {
                _reprintGate.Release();
            }
        }

        private ReprintOverlayContent BuildReprintOverlayContent() => new()
        {
            EditReceiver = _reprintChkReceiver?.Checked == true,
            EditRoute = _reprintChkRoute?.Checked == true,
            EditNotes = _reprintChkNotes?.Checked == true,
            EditPrintCount = _reprintChkPrintCount?.Checked == true,
            ReceiverName = ReadReprintText(_reprintTxtName),

            // Đúng thứ đang hiển thị: bản che khi chưa bấm con mắt, số đầy đủ khi đã bấm.
            ReceiverPhone = ReadReprintText(_reprintTxtPhone),
            ReceiverAddress = ReadReprintText(_reprintTxtAddress),
            Route1 = ReadReprintText(_reprintTxtRoute1),
            Route2 = ReadReprintText(_reprintTxtRoute2),
            Route3 = ReadReprintText(_reprintTxtRoute3),
            Note = ReadReprintText(_reprintTxtNote),
            CodAmount = ReadReprintText(_reprintTxtCod),
            Deadline = ReadReprintText(_reprintTxtDeadline),
            PrintCountNetworkCode = ReadReprintText(_reprintTxtPrintCode),
            PrintCountTimes = ReadReprintText(_reprintTxtPrintTimes),
            PrintCountTimestamp = ReadReprintText(_reprintTxtPrintTime),
            WaybillNo = _reprintFirstWaybill
        };

        /// <summary>
        /// Stores the previewed bytes under the key ExecutePrintAsync will look up, with a
        /// TTL long enough to survive an edit session (the normal one is 60s).
        /// </summary>
        private void CacheReprintPrintJob(byte[] pdfBytes, string localPath)
        {
            if (pdfBytes == null || pdfBytes.Length == 0) return;
            if (string.IsNullOrWhiteSpace(_reprintCacheKey)) return;

            RememberPrintJob(_reprintCacheKey, new PrintJobCacheEntry
            {
                CacheKey = _reprintCacheKey,
                WaybillNo = _reprintFirstWaybill,
                PdfBytes = pdfBytes,
                LocalPdfPath = localPath ?? "",
                CreatedAt = DateTime.Now,
                ExpiresAt = DateTime.Now.Add(ReprintJobTtl),
                PdfHash = ComputeSha256(pdfBytes)
            });
        }

        /// <summary>
        /// Each render goes to its own file: WebView2 keeps the previous PDF open, so
        /// overwriting one path would fail with a sharing violation.
        /// </summary>
        private async Task<string> WriteReprintPreviewFileAsync(byte[] pdfBytes)
        {
            string dir = Path.Combine(AppPaths.CacheDir, "reprint-preview");
            Directory.CreateDirectory(dir);

            int seq = Interlocked.Increment(ref _reprintPreviewSeq);
            string safeWaybill = string.Join("_", (_reprintFirstWaybill ?? "label").Split(Path.GetInvalidFileNameChars()));
            string path = Path.Combine(dir, $"{safeWaybill}-{seq:D4}-{DateTime.Now:HHmmssfff}.pdf");

            await File.WriteAllBytesAsync(path, pdfBytes).ConfigureAwait(true);
            CleanupReprintPreviewFiles(dir, path);
            return path;
        }

        private static void CleanupReprintPreviewFiles(string dir, string keepPath)
        {
            try
            {
                var stale = new DirectoryInfo(dir)
                    .GetFiles("*.pdf")
                    .OrderByDescending(f => f.CreationTimeUtc)
                    .Skip(ReprintPreviewFilesKept)
                    .ToList();

                foreach (var file in stale)
                {
                    if (string.Equals(file.FullName, keepPath, StringComparison.OrdinalIgnoreCase)) continue;
                    try { file.Delete(); } catch { /* still open in WebView2 — next pass gets it */ }
                }
            }
            catch (Exception ex)
            {
                AppLogger.Warning($"In lại đơn: dọn file preview thất bại: {ex.Message}");
            }
        }

        private void NavigatePreviewTo(string localPdfPath)
        {
            if (tabPrint_printPreview == null || tabPrint_printPreview.IsDisposed) return;
            if (string.IsNullOrWhiteSpace(localPdfPath) || !File.Exists(localPdfPath)) return;

            try
            {
                string uri = new Uri(localPdfPath).AbsoluteUri;
                if (tabPrint_printPreview.CoreWebView2 != null)
                    tabPrint_printPreview.CoreWebView2.Navigate(uri);
                else
                    tabPrint_printPreview.Source = new Uri(uri);
            }
            catch (Exception ex)
            {
                AppLogger.Warning($"In lại đơn: không mở được preview: {ex.Message}");
            }
        }

        // ==================================================================================
        // Printing — chống lệch khi khổ giấy không phải 3"x3"
        // ==================================================================================

        /// <summary>
        /// Bản in riêng cho "In lại đơn".
        ///
        /// <c>CreatePrintDocument()</c> mặc định chạy chế độ CutMargin: nó dịch gốc toạ độ ra
        /// <c>-HardMargin</c> rồi canh giữa theo cả hai chiều. Trên máy in nhiệt có lề cứng, và
        /// nhất là khi khổ giấy vật lý không phải 3"x3" (75x100mm, 76x130mm...), phần đè bị đẩy
        /// lệch khỏi đúng vị trí trên nhãn.
        ///
        /// Bản này luôn:
        ///   • vẽ trong vùng in được (đã trừ lề cứng), không tràn ra ngoài,
        ///   • giữ nguyên tỷ lệ 210:227 của nhãn JMS — co theo cạnh chật hơn, không kéo giãn X/Y,
        ///   • canh giữa theo chiều ngang và ghim sát mép trên (CenterTop).
        /// </summary>
        private PrintDocument CreateReprintPrintDocument(PdfiumViewer.PdfDocument pdf)
        {
            var document = new PrintDocument();
            int page = 0;

            document.BeginPrint += (_, _) => page = 0;
            document.PrintPage += (_, e) =>
            {
                if (pdf == null || page >= pdf.PageCount)
                {
                    e.HasMorePages = false;
                    return;
                }

                var pdfSize = pdf.PageSizes[page];
                e.PageSettings.Landscape = pdfSize.Width > pdfSize.Height;

                // Mọi phép tính dưới đây theo đơn vị 1/100 inch — đúng đơn vị của PageBounds.
                double width = e.PageBounds.Width - e.PageSettings.HardMarginX * 2;
                double height = e.PageBounds.Height - e.PageSettings.HardMarginY * 2;

                // Driver báo trang nằm ngang trong khi nhãn dựng đứng (hoặc ngược lại).
                bool pdfPortrait = pdfSize.Height > pdfSize.Width;
                bool pagePortrait = height > width;
                if (pdfPortrait != pagePortrait)
                    (width, height) = (height, width);

                if (width <= 0 || height <= 0 || pdfSize.Width <= 0 || pdfSize.Height <= 0)
                {
                    e.HasMorePages = false;
                    return;
                }

                double pdfRatio = pdfSize.Height / pdfSize.Width;
                double pageRatio = height / width;

                double drawWidth = width;
                double drawHeight = height;
                if (pdfRatio > pageRatio) drawWidth = width * (pageRatio / pdfRatio);
                else drawHeight = height * (pdfRatio / pageRatio);

                double left = (width - drawWidth) / 2.0;   // canh giữa ngang
                const double top = 0;                      // ghim mép trên

                pdf.Render(
                    page,
                    e.Graphics,
                    e.Graphics.DpiX,
                    e.Graphics.DpiY,
                    new Rectangle(
                        ToPrinterDots(left, e.Graphics.DpiX),
                        ToPrinterDots(top, e.Graphics.DpiY),
                        ToPrinterDots(drawWidth, e.Graphics.DpiX),
                        ToPrinterDots(drawHeight, e.Graphics.DpiY)),
                    PdfiumViewer.PdfRenderFlags.ForPrinting | PdfiumViewer.PdfRenderFlags.Annotations);

                page++;
                e.HasMorePages = page < pdf.PageCount;
            };

            return document;
        }

        private static int ToPrinterDots(double hundredthsOfInch, float dpi) =>
            (int)(hundredthsOfInch / 100.0 * dpi);

        /// <summary>
        /// Lề 0 cho "In lại đơn": phần canh lề do <see cref="CreateReprintPrintDocument"/> lo,
        /// lề của driver chồng thêm chỉ làm nhãn tụt xuống.
        /// </summary>
        private void ApplyReprintPageSettings(PrintDocument printDocument, PdfiumViewer.PdfDocument pdf)
        {
            if (printDocument == null || !IsReprintModeActive) return;

            try
            {
                printDocument.DefaultPageSettings.Margins = new Margins(0, 0, 0, 0);
                printDocument.PrinterSettings.DefaultPageSettings.Margins = new Margins(0, 0, 0, 0);

                var pdfSize = pdf != null && pdf.PageCount > 0 ? pdf.PageSizes[0] : SizeF.Empty;
                var paper = printDocument.DefaultPageSettings.PaperSize;
                AppLogger.Info(
                    $"[Reprint] margins=0 pdfPage={pdfSize.Width:0.#}x{pdfSize.Height:0.#}pt " +
                    $"paper={paper?.Width ?? 0}x{paper?.Height ?? 0}(1/100in) " +
                    $"landscape={printDocument.DefaultPageSettings.Landscape}");
            }
            catch (Exception ex)
            {
                AppLogger.Warning($"[Reprint] không đặt được lề 0: {ex.Message}");
            }
        }

        /// <summary>
        /// Makes sure a pending debounced re-render has landed before IN reads the cache,
        /// so the printed bytes always match what the preview shows.
        /// </summary>
        private async Task FlushPendingReprintRenderAsync()
        {
            if (!IsReprintModeActive) return;
            if (_reprintOriginalPdf == null || _reprintOriginalPdf.Length == 0) return;

            if (_reprintDebounce != null && _reprintDebounce.Enabled)
            {
                _reprintDebounce.Stop();
                await RenderReprintPreviewAsync().ConfigureAwait(true);
                return;
            }

            // Nothing queued — just wait out any render still in flight.
            await _reprintGate.WaitAsync().ConfigureAwait(true);
            _reprintGate.Release();
        }
    }
}

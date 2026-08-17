using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

// Lớp Main nằm ở namespace "AutoJMS" (không phải "AutoJMS.Forms") dù file ở thư mục Forms/.
namespace AutoJMS
{
    /// <summary>
    /// Panel "Newbill" — toàn bộ vùng dưới mục DATA của tab DKCH, dựng theo
    /// docs/layout/tabDKCH/Newbill Panel Redesign.html.
    ///
    /// Thứ tự từ trên xuống:
    ///   1. Hai ô danh sách cạnh nhau: MÃ VẬN ĐƠN (đang chờ) · ĐANG THỰC HIỆN (đã xong)
    ///   2. Thẻ kết quả: mã đơn + nút chép, chip trạng thái, tên bưu tá, nguyên nhân,
    ///      huy hiệu ngày tồn, dải vi phạm, chip ĐKCH/Phát lại
    ///   3. Dòng GỢI Ý (chỉ chế độ Newbie khi nhập lẻ 1 mã)
    ///   4. Thanh Tiến trình n/x
    ///   5. Danh sách Hành trình
    ///
    /// Vẫn theo đúng bài học của mục DATA: mọi control đều tự vẽ với
    /// UserPaint + OptimizedDoubleBuffer nên không nháy khi rê chuột, và bố cục
    /// được tính lại sau mỗi AppTheme.Apply() nên theme không phá được.
    /// Hai ô danh sách giữ RichTextBox thật bên trong vì người dùng cần gõ/dán mã vào đó.
    /// </summary>
    public partial class Main
    {
        // Hai ô nhập/kết quả cũ nay là RichTextBox thuần nằm bên trong DkchListCard.
        // Giữ NGUYÊN tên field để ~26 chỗ đọc/ghi trong Main.cs không phải sửa.
        private RichTextBox tabDKCH_inputNewBill;
        private RichTextBox tabDKCH_newBillDone;

        private Panel tabDKCH_newbillHost;
        // Pill chế độ KHÔNG còn là control con của uiTitlePanel2. UITitlePanel là
        // ScrollableControl nên toạ độ control con bị dời/kẹp khó lường — đã hai lần
        // pill rơi xuống đè ô danh sách. Vẽ thẳng trong Paint của nó thì toạ độ là
        // toạ độ client, không ai dời được.
        private string _dkchModeText = "NORMAL";
        private DkchListCard tabDKCH_cardInput;
        private DkchListCard tabDKCH_cardDone;
        private DkchResultCard tabDKCH_cardResult;
        private DkchTipBar tabDKCH_tipBar;
        private DkchProgressCard tabDKCH_cardProgress;
        private DkchJourneyCard tabDKCH_cardJourney;

        private bool _dkchNbBusy;
        private bool _dkchNbPending;

        private const int DkchNbGap = 7;      // khe giữa hai ô danh sách
        private const int DkchNbPad = 6;      // lề trong của panel (thu từ 8 để rộng thêm)
        private const int DkchNbListH = 104;  // khi có mã: header 24px + ~4 dòng 11pt
        private const int DkchNbListMinH = 76; // khi cả hai ô còn rỗng — trả chỗ cho Hành trình

        /// <summary>Dựng lại toàn bộ panel Newbill. Gọi ngay sau BuildDkchDataSection().</summary>
        private void BuildDkchNewbillSection()
        {
            if (uiTitlePanel2 == null || uiTitlePanel2.IsDisposed) return;

            uiTitlePanel2.SuspendLayout();
            for (int i = uiTitlePanel2.Controls.Count - 1; i >= 0; i--)
            {
                Control stale = uiTitlePanel2.Controls[i];
                uiTitlePanel2.Controls.RemoveAt(i);
                stale.Dispose();
            }

            tabDKCH_newbillHost = new Panel
            {
                Name = "tabDKCH_newbillHost",
                Dock = DockStyle.Fill,
                BackColor = Color.Transparent,
                Margin = new Padding(0),
                Padding = new Padding(0),
                TabStop = false
            };

            // Hai ô danh sách. RichTextBox bên trong giữ nguyên tên field cũ nên
            // toàn bộ code đọc/ghi ở Main.cs không phải sửa.
            tabDKCH_cardInput = new DkchListCard("tabDKCH_cardInput", "Mã vận đơn", editable: true);
            tabDKCH_inputNewBill = tabDKCH_cardInput.Body;
            tabDKCH_inputNewBill.Name = "tabDKCH_inputNewBill";

            tabDKCH_cardDone = new DkchListCard("tabDKCH_cardDone", "Đang thực hiện", editable: false);
            // Phải đặt TRƯỚC khi gán Skin: OnSkinChanged đọc cờ này để chọn màu chữ.
            tabDKCH_cardDone.BodyIsDone = true;
            tabDKCH_newBillDone = tabDKCH_cardDone.Body;
            tabDKCH_newBillDone.Name = "tabDKCH_newBillDone";

            tabDKCH_cardResult = new DkchResultCard { Name = "tabDKCH_cardResult" };
            tabDKCH_cardResult.CopyRequested += (s, e) => CopyDkchWaybillToClipboard();

            tabDKCH_tipBar = new DkchTipBar { Name = "tabDKCH_tipBar", Visible = false };
            tabDKCH_cardProgress = new DkchProgressCard { Name = "tabDKCH_cardProgress" };
            tabDKCH_cardJourney = new DkchJourneyCard { Name = "tabDKCH_cardJourney" };

            tabDKCH_newbillHost.Controls.AddRange(new Control[]
            {
                tabDKCH_cardInput, tabDKCH_cardDone, tabDKCH_cardResult,
                tabDKCH_tipBar, tabDKCH_cardProgress, tabDKCH_cardJourney
            });

            uiTitlePanel2.Controls.Add(tabDKCH_newbillHost);
            uiTitlePanel2.Paint -= PaintDkchModePill;
            uiTitlePanel2.Paint += PaintDkchModePill;
            uiTitlePanel2.ResumeLayout(false);

            tabDKCH_newbillHost.Resize += (s, e) => LayoutDkchNewbill();
            LayoutDkchNewbill();
        }

        /// <summary>Xếp lại panel Newbill. An toàn khi gọi nhiều lần.</summary>
        private void LayoutDkchNewbill()
        {
            // Bỏ qua lời gọi lồng nhau nhưng GHI NHỚ lại: đổi theme làm mục DATA cao/thấp
            // đi, kéo theo panel này resize NGAY GIỮA một lượt xếp đang chạy. Nếu chỉ chặn
            // mà không xếp lại thì lượt ngoài (số đo cũ) thắng — đó là lúc dải GỢI Ý nằm
            // chồng lên thẻ Hành trình.
            if (_dkchNbBusy) { _dkchNbPending = true; return; }
            if (tabDKCH_newbillHost == null || tabDKCH_newbillHost.IsDisposed) return;
            if (tabDKCH_cardInput == null || tabDKCH_cardJourney == null) return;

            _dkchNbBusy = true;
            try
            {
                for (int pass = 0; pass < 3; pass++)
                {
                _dkchNbPending = false;
                ApplyDkchNewbillSkin();

                int w = tabDKCH_newbillHost.ClientSize.Width;
                int h = tabDKCH_newbillHost.ClientSize.Height;
                if (w < 80) w = 270;
                if (h < 80) h = 420;

                int x = DkchNbPad;
                int inner = Math.Max(60, w - DkchNbPad * 2);
                int half = (inner - DkchNbGap) / 2;
                int y = DkchNbPad;

                // Ở cửa sổ nhỏ nhất (MinimumSize 1024x700) tổng chiều cao mong muốn vượt
                // chỗ có thật, nên hai ô danh sách chịu co trước — chúng có thanh cuộn,
                // còn thẻ kết quả và hành trình thì không.
                // Hai ô rỗng chiếm 104px chỉ để hiện hai số 0, trong khi Hành trình —
                // thứ nhìn nhiều nhất — lại bị cắt. Rỗng thì co, có mã thì giãn.
                bool hasCodes = (tabDKCH_inputNewBill != null && tabDKCH_inputNewBill.TextLength > 0)
                             || (tabDKCH_newBillDone != null && tabDKCH_newBillDone.TextLength > 0);
                int listH = hasCodes ? DkchNbListH : DkchNbListMinH;
                int wantH = DkchNbPad * 2 + listH + DkchNbGap * 3 + 135 + 54 + 90;
                if (h < wantH) listH = Math.Max(64, listH - (wantH - h));

                // Hai ô ghép SÁT thành một khối: chồng nhau 1px để hai đường viền
                // giáp nhau trùng làm một, mỗi ô chỉ bo phía ngoài cùng.
                tabDKCH_cardInput.RoundLeft = true; tabDKCH_cardInput.RoundRight = false;
                tabDKCH_cardDone.RoundLeft = false; tabDKCH_cardDone.RoundRight = true;
                Place(tabDKCH_cardInput, x, y, half, listH);
                Place(tabDKCH_cardDone, x + half - 1, y, inner - half + 1, listH);
                y += listH + DkchNbGap;

                int resultH = tabDKCH_cardResult.MeasureHeight(inner);
                Place(tabDKCH_cardResult, x, y, inner, resultH);
                y += resultH + DkchNbGap;

                // Đặt VÔ ĐIỀU KIỆN. Bản trước bỏ qua khi đang ẩn nên nó giữ nguyên toạ độ
                // của lượt xếp trước; lúc hiện lại thì nằm chồng lên thẻ Hành trình.
                int tipH = tabDKCH_tipBar.Visible ? tabDKCH_tipBar.MeasureHeight(inner) : 1;
                Place(tabDKCH_tipBar, x, y, inner, tipH);
                if (tabDKCH_tipBar.Visible) y += tipH + DkchNbGap;

                int progH = tabDKCH_cardProgress.MeasureHeight();
                Place(tabDKCH_cardProgress, x, y, inner, progH);
                y += progH + DkchNbGap;

                // Hành trình ăn hết phần còn lại; tối thiểu 70px để không bẹp thành gạch.
                int journeyH = Math.Max(70, h - y - DkchNbPad);
                Place(tabDKCH_cardJourney, x, y, inner, journeyH);

                if (!_dkchNbPending) break;   // không ai yêu cầu xếp lại → xong
                }
            }
            catch (Exception ex)
            {
                AppLogger.Warning($"[DKCH] không xếp được panel Newbill: {ex.Message}");
            }
            finally
            {
                _dkchNbBusy = false;
                _dkchNbPending = false;
            }
        }

        /// <summary>P1 — vẽ pill chế độ ở góc phải thanh tiêu đề NEWBILL.</summary>
        private void PaintDkchModePill(object sender, PaintEventArgs e)
        {
            if (uiTitlePanel2 == null || uiTitlePanel2.IsDisposed) return;

            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            var skin = DkchSkin.For(UI.AppTheme.CurrentTheme);
            // Hai pill DỰNG GIỐNG HỆT NHAU — cùng cỡ chữ, cùng đệm, cùng bo 3px — chỉ
            // khác đúng cặp màu. Không lấy từ skin.TipBg nữa vì màu đó đã được làm mềm
            // cho dải GỢI Ý, dùng lại thì pill Newbie nhạt đi so với yêu cầu.
            bool newbie = string.Equals(_dkchModeText, "NEWBIE", StringComparison.OrdinalIgnoreCase);
            Color pillBg = newbie ? ColorTranslator.FromHtml("#FFD166") : Color.White;
            Color pillInk = newbie ? ColorTranslator.FromHtml("#5A3A00") : skin.Accent;
            using (var f = new Font(DkchCardBase.UiFamily, 7.5f, FontStyle.Bold))
            {
                int w = TextRenderer.MeasureText(_dkchModeText, f,
                            new Size(int.MaxValue, int.MaxValue), TextFormatFlags.SingleLine).Width + 14;
                int h = 15;
                int bar = Math.Max(h, uiTitlePanel2.TitleHeight);
                var box = new Rectangle(Math.Max(2, uiTitlePanel2.ClientSize.Width - w - 7),
                                        (bar - h) / 2, w, h);
                using (var path = DkchPaint.RoundRect(box, 3))
                using (var brush = new SolidBrush(pillBg))
                {
                    g.FillPath(brush, path);
                }
                TextRenderer.DrawText(g, _dkchModeText, f, box, pillInk,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
            }
        }

        /// <summary>Bơm bảng màu của theme hiện tại xuống mọi thẻ.</summary>
        private void ApplyDkchNewbillSkin()
        {
            var skin = DkchSkin.For(UI.AppTheme.CurrentTheme);

            if (tabDKCH_cardInput != null) { tabDKCH_cardInput.Skin = skin; tabDKCH_cardInput.RefreshBodyStyle(); }
            if (tabDKCH_cardDone != null) { tabDKCH_cardDone.Skin = skin; tabDKCH_cardDone.RefreshBodyStyle(); }
            if (tabDKCH_cardResult != null) tabDKCH_cardResult.Skin = skin;
            if (tabDKCH_tipBar != null) tabDKCH_tipBar.Skin = skin;
            if (tabDKCH_cardProgress != null) tabDKCH_cardProgress.Skin = skin;
            if (tabDKCH_cardJourney != null) tabDKCH_cardJourney.Skin = skin;
        }

        /// <summary>Chép mã đơn đang hiển thị trên thẻ kết quả vào clipboard.</summary>
        private void CopyDkchWaybillToClipboard()
        {
            string code = tabDKCH_cardResult?.Waybill ?? "";
            // "—" là chỗ giữ chỗ lúc chưa xử lý mã nào, không phải mã đơn.
            if (string.IsNullOrWhiteSpace(code) || code == "—") return;
            try
            {
                Clipboard.SetText(code);
                // Chỉ tô xanh SAU KHI ghi clipboard thành công — bấm mà lỗi thì báo xanh
                // là nói dối, người dùng dán ra lại thấy nội dung cũ.
                tabDKCH_cardResult.FlashCopied();
                AppLogger.Info($"[DKCH] đã chép mã {code} vào clipboard.");
            }
            catch (Exception ex)
            {
                AppLogger.Warning($"[DKCH] không chép được mã: {ex.Message}");
            }
        }
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Bảng màu — ba biến thể lấy thẳng từ file thiết kế, chọn theo AppTheme.CurrentTheme.
    // ─────────────────────────────────────────────────────────────────────────────

    internal sealed class DkchSkin
    {
        public Color Accent, AccentText;
        public Color CardBg, CardBorder, ListHeaderBg, ListLabel, ListText, ListDoneText;
        public Color ResultBg, ResultBorder, ResultTitle, ResultMuted, ResultSub;
        public Color RowBg, RowBorder;   // hai tấm mã đơn / thao tác cuối
        public Color ChipBg, ChipText, ChipDot;
        public Color ChipOkBg, ChipOkText, ChipWarnBg, ChipWarnText;
        public Color ActBlueBg, ActBlueInk, ActNeutralBg, ActNeutralInk, ActNeutralDot;
        public Color StockBg, StockBorder, StockText, StockLabel;
        public Color ViolationBg, ViolationBar, ViolationTitle, ViolationText;
        public Color CountChipBg, CountChipBorder, CountChipText, CountChipValue;
        public Color TipBg, TipBorder, TipBar, TipLabel, TipText;
        public Color BoxBg, BoxBorder, BoxLabel;
        public Color StepDone, StepCurrent, StepCurrentAlt, StepPending, StepLabel, StepLabelCurrent;
        public Color JourneyDot, JourneyDotMuted, JourneyTitle, JourneyTitleMuted;
        public Color JourneyName, JourneyNote, JourneyTime, JourneyDivider;
        public Color CopyBg, CopyBorder, CopyFore;
        // Panel trái: thanh tiêu đề khung nhóm + 4 nút CONTROL.
        public Color TitleFore = Color.White;
        public Color BtnPrimary, BtnSuccess, BtnWarning, BtnDanger, BtnFore;

        private static Color Hex(string s) => ColorTranslator.FromHtml(s);

        // Dựng một lần cho mỗi theme. Trước đây mỗi lần layout lại tạo bảng màu mới,
        // khiến setter Skin luôn coi là "đã đổi" và chạy lại OnSkinChanged vô ích.
        private static readonly DkchSkin _red = Red();
        private static readonly DkchSkin _dark = Dark();
        private static readonly DkchSkin _light = Light();

        public static DkchSkin For(UI.ThemeMode mode)
        {
            switch (mode)
            {
                case UI.ThemeMode.Dark: return _dark;
                case UI.ThemeMode.Red: return _red;
                default: return _light;
            }
        }

        private static DkchSkin Red() => new DkchSkin
        {
            ActBlueBg = Hex("#E9EFFD"),
            ActBlueInk = Hex("#2B5FD9"),
            ActNeutralBg = Hex("#EDF0F4"),
            ActNeutralInk = Hex("#12161C"),
            ActNeutralDot = Hex("#5C6675"),
            RowBg = Hex("#FFFFFF"),
            RowBorder = Hex("#EBD2D3"),
            ChipOkBg = Hex("#E7F6EC"), ChipOkText = Hex("#15803D"),
            ChipWarnBg = Hex("#FFF6E3"), ChipWarnText = Hex("#B26B00"),
            TitleFore = Hex("#FFFFFF"),
            BtnPrimary = Hex("#D6242B"),
            BtnSuccess = Hex("#16A34A"),
            BtnWarning = Hex("#F59E0B"),
            BtnDanger = Hex("#B91C1C"),
            BtnFore = Hex("#FFFFFF"),
            Accent = Hex("#D6242B"),
            AccentText = Hex("#C81E25"),
            CardBg = Hex("#FFFFFF"),
            CardBorder = Hex("#EBD2D3"),
            ListHeaderBg = Hex("#FCF6F6"),
            ListLabel = Hex("#12161C"),
            ListText = Hex("#12161C"),
            ListDoneText = Hex("#C81E25"),
            ResultBg = Hex("#FFFFFF"),
            ResultBorder = Hex("#EBD2D3"),
            ResultTitle = Hex("#12161C"),
            ResultMuted = Hex("#3F4855"),
            ResultSub = Hex("#3F4855"),
            ChipBg = Hex("#FDECEC"),
            ChipText = Hex("#C81E25"),
            ChipDot = Hex("#D6242B"),
            StockBg = Hex("#FFF6E3"),
            StockBorder = Hex("#F0C36A"),
            StockText = Hex("#B26B00"),
            StockLabel = Hex("#B26B00"),
            ViolationBg = Hex("#FDECEC"),
            ViolationBar = Hex("#D6242B"),
            ViolationTitle = Hex("#C81E25"),
            ViolationText = Hex("#7E1418"),
            CountChipBg = Hex("#FFFFFF"),
            CountChipBorder = Hex("#F3B9BB"),
            CountChipText = Hex("#A8161C"),
            CountChipValue = Hex("#A8161C"),
            TipBg = Hex("#FFF6E3"),
            TipBorder = Hex("#F0C36A"),
            TipBar = Hex("#F2A007"),
            TipLabel = Hex("#B26B00"),
            TipText = Hex("#4A3410"),
            BoxBg = Hex("#FFFFFF"),
            BoxBorder = Hex("#E2E6EC"),
            BoxLabel = Hex("#12161C"),
            StepDone = Hex("#16A34A"),
            StepCurrent = Hex("#D6242B"),
            StepCurrentAlt = Hex("#F0575D"),
            StepPending = Hex("#DFE3E9"),
            StepLabel = Hex("#3F4855"),
            StepLabelCurrent = Hex("#C81E25"),
            JourneyDot = Hex("#D6242B"),
            JourneyDotMuted = Hex("#5C6675"),
            JourneyTitle = Hex("#C81E25"),
            JourneyTitleMuted = Hex("#12161C"),
            JourneyName = Hex("#12161C"),
            JourneyNote = Hex("#3F4855"),
            JourneyTime = Hex("#3F4855"),
            JourneyDivider = Hex("#E3E7ED"),
            CopyBg = Hex("#FDECEC"),
            CopyBorder = Hex("#F3B9BB"),
            CopyFore = Hex("#C81E25")
        };

        private static DkchSkin Dark() => new DkchSkin
        {
            ActBlueBg = Hex("#1B2A3F"),
            ActBlueInk = Hex("#7FA9F0"),
            ActNeutralBg = Hex("#2A323E"),
            ActNeutralInk = Hex("#E9EEF6"),
            ActNeutralDot = Hex("#8C99AC"),
            RowBg = Hex("#1B2230"),
            RowBorder = Hex("#2A323E"),
            ChipOkBg = Hex("#16301F"), ChipOkText = Hex("#7BE0A4"),
            ChipWarnBg = Hex("#2E2A1E"), ChipWarnText = Hex("#FFC65C"),
            TitleFore = Hex("#FFFFFF"),
            BtnPrimary = Hex("#C13239"),
            BtnSuccess = Hex("#22A75B"),
            BtnWarning = Hex("#D08A16"),
            BtnDanger = Hex("#E03A40"),
            BtnFore = Hex("#FFFFFF"),
            Accent = Hex("#C13239"),
            AccentText = Hex("#FF7A7F"),
            CardBg = Hex("#12161C"),
            CardBorder = Hex("#2A323E"),
            ListHeaderBg = Hex("#1A2029"),
            ListLabel = Hex("#E9EEF6"),
            ListText = Hex("#E9EEF6"),
            ListDoneText = Hex("#FF7A7F"),
            ResultBg = Hex("#1A2029"),
            ResultBorder = Hex("#2A323E"),
            ResultTitle = Hex("#FFFFFF"),
            ResultMuted = Hex("#C9D3E2"),
            ResultSub = Hex("#E9EEF6"),
            ChipBg = Hex("#3B2429"),
            ChipText = Hex("#FFB4B7"),
            ChipDot = Hex("#FF5A5F"),
            StockBg = Hex("#2E2A1E"),
            StockBorder = Hex("#6B5320"),
            StockText = Hex("#FFC65C"),
            StockLabel = Hex("#E0B266"),
            ViolationBg = Hex("#331A1E"),
            ViolationBar = Hex("#FF5A5F"),
            ViolationTitle = Hex("#FFB4B7"),
            ViolationText = Hex("#FFE3E4"),
            CountChipBg = Hex("#2B313B"),
            CountChipBorder = Hex("#2B313B"),
            CountChipText = Hex("#D7DEE9"),
            CountChipValue = Hex("#FFFFFF"),
            TipBg = Hex("#3D2800"),
            TipBorder = Hex("#6B4A0A"),
            TipBar = Hex("#FFD166"),
            TipLabel = Hex("#FFD166"),
            TipText = Hex("#FFF3D6"),
            BoxBg = Hex("#1A2029"),
            BoxBorder = Hex("#2A323E"),
            BoxLabel = Hex("#E9EEF6"),
            StepDone = Hex("#22A75B"),
            StepCurrent = Hex("#E03A40"),
            StepCurrentAlt = Hex("#7A2126"),
            StepPending = Hex("#2F3946"),
            StepLabel = Hex("#C9D3E2"),
            StepLabelCurrent = Hex("#FF7A7F"),
            JourneyDot = Hex("#FF5A5F"),
            JourneyDotMuted = Hex("#8C99AC"),
            JourneyTitle = Hex("#FF7A7F"),
            JourneyTitleMuted = Hex("#E9EEF6"),
            JourneyName = Hex("#E9EEF6"),
            JourneyNote = Hex("#E9EEF6"),
            JourneyTime = Hex("#C9D3E2"),
            JourneyDivider = Hex("#2A2E35"),
            CopyBg = Hex("#2B313B"),
            CopyBorder = Hex("#3A4250"),
            CopyFore = Hex("#E9EEF6")
        };

        private static DkchSkin Light() => new DkchSkin
        {
            ActBlueBg = Hex("#E9EFFD"),
            ActBlueInk = Hex("#2B5FD9"),
            ActNeutralBg = Hex("#EDF0F4"),
            ActNeutralInk = Hex("#12161C"),
            ActNeutralDot = Hex("#5C6675"),
            RowBg = Hex("#FFFFFF"),
            RowBorder = Hex("#DDE2EA"),
            ChipOkBg = Hex("#E7F6EC"), ChipOkText = Hex("#15803D"),
            ChipWarnBg = Hex("#FFF6E3"), ChipWarnText = Hex("#B26B00"),
            TitleFore = Hex("#FFFFFF"),
            BtnPrimary = Hex("#1C6DD0"),
            BtnSuccess = Hex("#16A34A"),
            BtnWarning = Hex("#F59E0B"),
            BtnDanger = Hex("#D6242B"),
            BtnFore = Hex("#FFFFFF"),
            Accent = Hex("#1C6DD0"),
            AccentText = Hex("#1C6DD0"),
            CardBg = Hex("#FFFFFF"),
            CardBorder = Hex("#DDE2EA"),
            ListHeaderBg = Hex("#F7F9FC"),
            ListLabel = Hex("#12161C"),
            ListText = Hex("#12161C"),
            ListDoneText = Hex("#C81E25"),
            ResultBg = Hex("#FFFFFF"),
            ResultBorder = Hex("#DDE2EA"),
            ResultTitle = Hex("#12161C"),
            ResultMuted = Hex("#3F4855"),
            ResultSub = Hex("#3F4855"),
            ChipBg = Hex("#FDECEC"),
            ChipText = Hex("#C81E25"),
            ChipDot = Hex("#D6242B"),
            StockBg = Hex("#FFF6E3"),
            StockBorder = Hex("#F0C36A"),
            StockText = Hex("#B26B00"),
            StockLabel = Hex("#B26B00"),
            ViolationBg = Hex("#FDECEC"),
            ViolationBar = Hex("#D6242B"),
            ViolationTitle = Hex("#C81E25"),
            ViolationText = Hex("#7E1418"),
            CountChipBg = Hex("#FFFFFF"),
            CountChipBorder = Hex("#F3B9BB"),
            CountChipText = Hex("#A8161C"),
            CountChipValue = Hex("#A8161C"),
            TipBg = Hex("#FFF6E3"),
            TipBorder = Hex("#F0C36A"),
            TipBar = Hex("#F2A007"),
            TipLabel = Hex("#B26B00"),
            TipText = Hex("#4A3410"),
            BoxBg = Hex("#FFFFFF"),
            BoxBorder = Hex("#E2E6EC"),
            BoxLabel = Hex("#12161C"),
            StepDone = Hex("#16A34A"),
            StepCurrent = Hex("#D6242B"),
            StepCurrentAlt = Hex("#F0575D"),
            StepPending = Hex("#DFE3E9"),
            StepLabel = Hex("#3F4855"),
            StepLabelCurrent = Hex("#C81E25"),
            JourneyDot = Hex("#D6242B"),
            JourneyDotMuted = Hex("#5C6675"),
            JourneyTitle = Hex("#C81E25"),
            JourneyTitleMuted = Hex("#12161C"),
            JourneyName = Hex("#12161C"),
            JourneyNote = Hex("#3F4855"),
            JourneyTime = Hex("#3F4855"),
            JourneyDivider = Hex("#E3E7ED"),
            CopyBg = Hex("#EDF2F9"),
            CopyBorder = Hex("#C7D8EE"),
            CopyFore = Hex("#1C6DD0")
        };
    }

    /// <summary>Nền tảng chung cho các thẻ của panel Newbill.</summary>
    internal enum DkchActRole { Neutral = 0, Blue, Red, Green, Orange }

    internal abstract class DkchCardBase : Control
    {
        private DkchSkin _skin = DkchSkin.For(UI.ThemeMode.Light);

        protected DkchCardBase()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                   | ControlStyles.ResizeRedraw | ControlStyles.UserPaint
                   | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            TabStop = false;
        }

        public DkchSkin Skin
        {
            get { return _skin; }
            set { if (value == null || ReferenceEquals(value, _skin)) return; _skin = value; OnSkinChanged(); Invalidate(); }
        }

        protected virtual void OnSkinChanged() { }

        /// <summary>
        /// Cùng họ chữ với mục DATA ("Segoe UI Semibold") để hai khối trong panel trái
        /// nhìn như một, thay vì DATA một kiểu NEWBILL một kiểu.
        /// </summary>
        internal const string UiFamily = "Segoe UI Semibold";

        /// <summary>
        /// Trước đây mã đơn/giờ/số đếm dùng Consolas cho thẳng cột, nhưng mục DATA
        /// không có chữ đều nét nào nên hai khối nhìn lệch hẳn nhau. Nay dùng CHUNG
        /// một họ chữ với DATA. Chữ số của Segoe UI vốn đã đều bề rộng (tabular figures)
        /// nên mã vận đơn vẫn thẳng cột như cũ.
        /// </summary>
        internal const string MonoFamily = UiFamily;

        /// <summary>
        /// Thang cỡ chữ lấy ĐÚNG của mục DATA (DkchLabelPtMax/PtMin = 10.5 / 8pt) để hai
        /// khối trong panel trái không lệch nhau. Riêng mã vận đơn to hơn một bậc vì nó
        /// là dòng tiêu đề của thẻ, đúng như bản mẫu.
        /// </summary>
        internal const float UiPtMax = 10.5f;
        internal const float UiPtMin = 8f;
        internal const float UiPtLead = 12.5f;

        protected static Font Ui(float pt, FontStyle style = FontStyle.Regular)
            => new Font(UiFamily, pt, style);

        protected static Font Mono(float pt, FontStyle style = FontStyle.Regular)
            => new Font(MonoFamily, pt, style);

        protected static void Draw(Graphics g, string s, Font f, Rectangle r, Color c,
                                   TextFormatFlags extra = TextFormatFlags.Left)
        {
            if (string.IsNullOrEmpty(s)) return;
            // PreserveGraphicsClipping: mặc định TextRenderer vẽ bằng GDI và BỎ QUA
            // Graphics.Clip — chính vì thế danh sách hành trình từng tràn chữ ra ngoài.
            // Cờ này bảo nó lấy vùng cắt của Graphics áp vào HDC trước khi vẽ.
            TextRenderer.DrawText(g, s, f, r, c,
                extra | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix
                      | TextFormatFlags.EndEllipsis | TextFormatFlags.PreserveGraphicsClipping);
        }

        /// <summary>
        /// Màu theo TÊN THAO TÁC. Thao tác không có trong bảng thì để MÀU ĐEN — đúng
        /// quy ước của chủ dự án, thà trung tính còn hơn tô nhầm ý nghĩa nghiệp vụ.
        /// Bảng này nên chuyển sang tab2config.json để thêm thao tác không phải build lại.
        /// Xếp từ cụ thể tới chung: "in đơn chuyển hoàn" phải đứng trước "chuyển hoàn".
        /// </summary>
        private static readonly (string Key, DkchActRole Role)[] ActRules =
        {
            // CHỈ bốn thao tác này có màu. Mọi thao tác khác — Giao lại hàng, Chuyển hoàn
            // lần 2, Xuống hàng kiện đến, Gỡ bao, In đơn chuyển hoàn… — đều để ĐEN.
            // "đang chuyển hoàn" đứng trước "đăng ký chuyển hoàn" cho chắc thứ tự khớp.
            ("đang chuyển hoàn",    DkchActRole.Orange),
            ("đăng ký chuyển hoàn", DkchActRole.Green),
            ("退件登记",             DkchActRole.Green),
            ("ký nhận cpn",         DkchActRole.Green),
            ("快件签收",             DkchActRole.Green),
            ("quét phát hàng",      DkchActRole.Blue),
            ("出仓扫描",             DkchActRole.Blue),
            ("kiện vấn đề",         DkchActRole.Red),
            ("问题件扫描",           DkchActRole.Red),
        };

        protected static DkchActRole ActRoleOf(string action)
        {
            if (string.IsNullOrWhiteSpace(action)) return DkchActRole.Neutral;
            string t = action.Trim().ToLowerInvariant();
            foreach (var (key, role) in ActRules)
                if (t.Contains(key)) return role;
            return DkchActRole.Neutral;
        }

        protected Color ActInk(DkchActRole r)
        {
            switch (r)
            {
                case DkchActRole.Blue: return Skin.ActBlueInk;
                case DkchActRole.Red: return Skin.ChipText;
                case DkchActRole.Green: return Skin.ChipOkText;
                case DkchActRole.Orange: return Skin.ChipWarnText;
                default: return Skin.ActNeutralInk;
            }
        }

        protected Color ActBg(DkchActRole r)
        {
            switch (r)
            {
                case DkchActRole.Blue: return Skin.ActBlueBg;
                case DkchActRole.Red: return Skin.ChipBg;
                case DkchActRole.Green: return Skin.ChipOkBg;
                case DkchActRole.Orange: return Skin.ChipWarnBg;
                default: return Skin.ActNeutralBg;
            }
        }

        protected Color ActDot(DkchActRole r)
        {
            switch (r)
            {
                case DkchActRole.Blue: return Skin.ActBlueInk;
                case DkchActRole.Red: return Skin.ChipDot;
                case DkchActRole.Green: return Skin.StepDone;
                case DkchActRole.Orange: return Skin.StockText;
                default: return Skin.ActNeutralDot;
            }
        }

        protected static int Measure(string s, Font f)
            => string.IsNullOrEmpty(s) ? 0
             : TextRenderer.MeasureText(s, f, new Size(int.MaxValue, int.MaxValue), TextFormatFlags.SingleLine).Width;

        /// <summary>
        /// Tự ngắt dòng theo TỪ. Đã hai lần tin vào DT_WORDBREAK của GDI mà chữ vẫn bị
        /// cắt giữa từ ("Khách h / àng"), nên chỗ nào được phép xuống dòng thì tự cắt ở
        /// khoảng trắng rồi vẽ từng dòng một — GDI không còn cơ hội cắt sai.
        /// Từ nào dài hơn cả một dòng thì đứng riêng một dòng.
        /// </summary>
        protected static List<string> WrapWords(string text, Font font, int width)
        {
            var lines = new List<string>();
            if (string.IsNullOrWhiteSpace(text) || width < 10) return lines;

            string cur = "";
            foreach (var word in text.Split(' '))
            {
                if (word.Length == 0) continue;
                string probe = cur.Length == 0 ? word : cur + " " + word;
                if (Measure(probe, font) <= width) { cur = probe; continue; }
                if (cur.Length > 0) lines.Add(cur);
                cur = word;
            }
            if (cur.Length > 0) lines.Add(cur);
            return lines;
        }

        protected void FillCard(Graphics g, Color back, Color border, int radius = 6)
        {
            var box = new Rectangle(0, 0, Math.Max(1, Width - 1), Math.Max(1, Height - 1));
            using (var path = DkchPaint.RoundRect(box, radius))
            {
                using (var brush = new SolidBrush(back)) g.FillPath(brush, path);
                using (var pen = new Pen(border, 1f)) g.DrawPath(pen, path);
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            PaintCard(e.Graphics);
        }

        protected abstract void PaintCard(Graphics g);
    }

    /// <summary>Huy hiệu chế độ (NORMAL / NEWBIE) ở góc phải thanh tiêu đề.</summary>
    internal sealed class DkchModeBadge : DkchCardBase
    {
        public Size PreferredBadgeSize()
        {
            using (var f = Ui(7.5f, FontStyle.Bold))
                return new Size(Measure(Text, f) + 14, 16);
        }

        protected override void PaintCard(Graphics g)
        {
            var box = new Rectangle(0, 0, Math.Max(1, Width - 1), Math.Max(1, Height - 1));
            using (var path = DkchPaint.RoundRect(box, 3))
            using (var brush = new SolidBrush(Skin.TipBg))
            {
                g.FillPath(brush, path);
            }
            using (var f = Ui(7.5f, FontStyle.Bold))
                Draw(g, Text, f, box, Skin.TipText, TextFormatFlags.HorizontalCenter);
        }
    }

    /// <summary>
    /// Ô danh sách: thanh nhãn + số đếm ở trên, RichTextBox thật ở dưới.
    /// Giữ RichTextBox vì người dùng cần gõ/dán mã, và 26 chỗ trong Main.cs đang đọc/ghi nó.
    /// </summary>
    internal sealed class DkchListCard : DkchCardBase
    {
        private const int HeaderH = 24;

        private bool _focused;

        /// <summary>Bo góc phía nào. Hai ô ghép sát nhau nên mỗi ô chỉ bo một bên.</summary>
        public bool RoundLeft { get; set; } = true;
        public bool RoundRight { get; set; } = true;

        public RichTextBox Body { get; }
        public string Caption { get; set; }
        public bool BodyIsDone { get; set; }

        public DkchListCard(string name, string caption, bool editable)
        {
            Name = name;
            Caption = caption ?? "";
            Body = new RichTextBox
            {
                BorderStyle = BorderStyle.None,
                Multiline = true,
                WordWrap = false,
                ReadOnly = !editable,
                ScrollBars = RichTextBoxScrollBars.Vertical,
                Font = ListFont,
                Margin = new Padding(0),
                TabStop = editable
            };
            Body.TextChanged += (s, e) => Invalidate();
            // Ô đang nhập được viền sáng để người dùng biết súng quét sẽ bắn vào đâu.
            Body.Enter += (s, e) => { _focused = true; Invalidate(); };
            Body.Leave += (s, e) => { _focused = false; Invalidate(); };
            Controls.Add(Body);
        }

        /// <summary>
        /// Ô "Đang thực hiện" dùng font đều nét cho dễ soi mã; ô nhập thì KHÔNG đụng tới,
        /// để <c>ApplyWaybillInputBoldFonts</c> giữ đúng font "Segoe UI Semibold" 12 như cũ.
        /// </summary>
        // 9.5pt: ô rộng ~115px, trừ lề và thanh cuộn dọc còn ~90px cho chữ. Mã 12 chữ số
        // ở cỡ này chiếm ~88px nên vừa khít; để 10.5pt là mã bị đẩy ngang khi có thanh cuộn.
        private static readonly Font ListFont = new Font(MonoFamily, 9.5f, FontStyle.Bold);

        protected override void OnSkinChanged() => RefreshBodyStyle();

        /// <summary>
        /// Áp màu + font cho ô nhập. Phải gọi VÔ ĐIỀU KIỆN sau mỗi AppTheme.Apply:
        /// bảng màu nay được cache theo theme nên setter Skin thoát sớm khi theme không
        /// đổi, mà AppTheme thì vẫn kịp gán Font = "Segoe UI" 10F cho mọi control.
        /// </summary>
        public void RefreshBodyStyle()
        {
            if (Body == null || Body.IsDisposed) return;
            Body.BackColor = Skin.CardBg;
            Body.ForeColor = BodyIsDone ? Skin.ListDoneText : Skin.ListText;
            if (!ReferenceEquals(Body.Font, ListFont)) Body.Font = ListFont;
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            // Lề mỏng để ô nhập rộng rãi hơn — thiết kế cần cảm giác thoáng, không viền dày.
            Body.Bounds = new Rectangle(4, HeaderH + 3, Math.Max(10, Width - 8), Math.Max(10, Height - HeaderH - 7));
        }

        private int LineCount()
        {
            string t = Body.Text ?? "";
            if (t.Trim().Length == 0) return 0;
            int n = 0;
            foreach (var line in t.Split('\n'))
                if (line.Trim().Length > 0) n++;
            return n;
        }

        protected override void PaintCard(Graphics g)
        {
            var box = new Rectangle(0, 0, Math.Max(1, Width - 1), Math.Max(1, Height - 1));
            using (var path = DkchPaint.RoundSide(box, 3, RoundLeft, RoundRight))
            {
                using (var brush = new SolidBrush(Skin.CardBg)) g.FillPath(brush, path);
                using (var pen = new Pen(_focused ? Skin.Accent : Skin.CardBorder, _focused ? 1.6f : 1f))
                {
                    g.DrawPath(pen, path);
                }
            }

            var header = new Rectangle(1, 1, Math.Max(1, Width - 3), HeaderH);
            using (var clip = DkchPaint.RoundSide(box, 3, RoundLeft, RoundRight))
            {
                var old = g.Clip;
                g.SetClip(clip, CombineMode.Intersect);
                using (var brush = new SolidBrush(Skin.ListHeaderBg)) g.FillRectangle(brush, header);
                g.Clip = old;
            }
            using (var pen = new Pen(Skin.CardBorder, 1f))
                g.DrawLine(pen, 1, header.Bottom, Width - 2, header.Bottom);

            using (var fn = Mono(10.5f, FontStyle.Bold))
            {
                string count = LineCount().ToString();
                int cw = Measure(count, fn) + 4;
                Draw(g, count, fn,
                     new Rectangle(header.Right - cw - 5, header.Y, cw, header.Height),
                     // Ở theme RED màu nhấn trùng màu lỗi, nên "0" ở ô chờ trông như báo lỗi.
                     BodyIsDone ? Skin.ListDoneText : Skin.ListLabel, TextFormatFlags.Right);

                // Nhãn hạ cỡ chữ cho tới khi vừa: "ĐANG THỰC HIỆN" dài hơn "MÃ VẬN ĐƠN"
                // nên ở 7pt bị cắt thành "ĐANG THỰC HI…" trong ô rộng ~123px.
                string caption = Caption.ToUpperInvariant();
                int room = Math.Max(1, header.Width - cw - 12);
                for (float pt = 9f; ; pt -= 0.25f)
                {
                    using (var fl = Ui(pt, FontStyle.Bold))
                    {
                        if (pt <= 6f || Measure(caption, fl) <= room)
                        {
                            Draw(g, caption, fl,
                                 new Rectangle(header.X + 5, header.Y, room, header.Height), Skin.ListLabel);
                            break;
                        }
                    }
                }
            }
        }
    }

    /// <summary>Thẻ kết quả: mã đơn, chip trạng thái, tên bưu tá, ngày tồn, dải vi phạm, chip đếm.</summary>
    internal sealed class DkchResultCard : DkchCardBase
    {
        private Rectangle _copyBox;
        private bool _copyHot;

        public event EventHandler CopyRequested;

        /// <summary>Xanh báo "đã chép" — giống nhau ở mọi theme.</summary>
        private static readonly Color DkchOkGreen = ColorTranslator.FromHtml("#15803D");

        // Hàng mã: đệm 7 + nút 26 + 7 = 40. Hàng trạng thái: đệm 6 + huy hiệu 35 + 6 = 47.
        /// <summary>Lề trong hai tấm nền tối (bản mẫu 9px, thu còn 7 để chữ rộng hơn).</summary>
        private const int DkchRowPad = 7;
        private const int DkchCodeRowH = 40;
        private const int DkchStateRowH = 47;
        /// <summary>
        /// Chiều cao dùng chung cho chip "thao tác cuối" VÀ huy hiệu NGÀY TỒN — chủ dự án
        /// muốn hai khối cao bằng nhau, nên để một hằng số thay vì hai số 35 rời rạc.
        /// </summary>
        private const int DkchChipH = 35;
        /// <summary>
        /// Cỡ chữ thao tác cuối. Cố tình DÙNG CHUNG với số ngày tồn để hai khối cạnh nhau
        /// cân nhau — chủ dự án muốn chữ chip bằng chữ NGÀY TỒN. Sửa một chỗ là đổi cả hai.
        /// </summary>
        private const float DkchChipPt = UiPtMax;
        // Lề trong chip. Trước đây đo bằng 44 nhưng vẽ bằng 36 → đo và vẽ lệch nhau, chữ
        // bị cắt sớm hơn cần thiết. Nay tách thành từng phần và cộng lại, hết đường lệch.
        private const int DkchChipDotLeft = 4;
        private const int DkchChipDotD = 6;
        private const int DkchChipDotGap = 4;
        private const int DkchChipTextLeft = DkchChipDotLeft + DkchChipDotD + DkchChipDotGap;
        private const int DkchChipPadRight = 8;
        /// <summary>Phần bề ngang chip KHÔNG dành cho chữ — dùng chung cho đo và vẽ.</summary>
        private const int DkchChipPad = DkchChipTextLeft + DkchChipPadRight;

        /// <summary>Ok = xanh lá · Problem = đỏ · Pending = vàng (theo tài liệu P4).</summary>

        /// <summary>Ok = xanh lá · Problem = đỏ · Pending = vàng (theo tài liệu P4).</summary>
        public enum StatusKind { Pending = 0, Ok, Problem }

        public StatusKind Kind { get; set; } = StatusKind.Pending;

        /// <summary>Có tên bưu tá hoặc nguyên nhân thì mới vẽ khối dưới đường kẻ.</summary>
        // Ngày tồn nay nằm ở hàng chip nên KHÔNG còn là lý do để mở khối chi tiết.
        private bool HasDetails =>
            !string.IsNullOrWhiteSpace(OperatorName)
            || !string.IsNullOrWhiteSpace(NoteText)
            || !string.IsNullOrWhiteSpace(StampText);

        public string Waybill { get; set; } = "";
        public string StatusText { get; set; } = "";
        public string StampText { get; set; } = "";
        public string OperatorName { get; set; } = "";
        public string NoteText { get; set; } = "";
        public int? DaysInStock { get; set; }

        /// <summary>Nội dung dải vi phạm. Rỗng thì dải chuyển sang hiển thị kết quả.</summary>
        public string ViolationText { get; set; } = "";

        /// <summary>Thông điệp kết quả/lỗi từ JMS — hiện ở dải P6 khi không có vi phạm.</summary>
        public string ResultText { get; set; } = "";

        private bool IsViolation => !string.IsNullOrWhiteSpace(ViolationText);
        private string StripTag => IsViolation ? "⛔ VI PHẠM QUY TRÌNH"
                                 : Kind == StatusKind.Ok ? "✓ KẾT QUẢ"
                                 : Kind == StatusKind.Problem ? "⚠ KHÔNG THỰC HIỆN ĐƯỢC"
                                 : "• KẾT QUẢ";
        private string StripText => IsViolation ? ViolationText : ResultText;
        private bool HasStrip => !string.IsNullOrWhiteSpace(StripText)
                              || RegisterCount > 0 || RedeliverCount > 0;

        public int RegisterCount { get; set; }
        public int RedeliverCount { get; set; }

        private readonly System.Windows.Forms.Timer _copyTimer;
        private bool _copyOk;

        public DkchResultCard()
        {
            Cursor = Cursors.Default;
            _copyTimer = new System.Windows.Forms.Timer { Interval = 1000 };
            _copyTimer.Tick += (s, e) => { _copyTimer.Stop(); _copyOk = false; Invalidate(); };
        }

        /// <summary>
        /// Báo cho nút biết đã chép XONG để tô xanh 1 giây. Gọi từ nơi thực sự ghi
        /// clipboard, không tự bật lúc bấm — bấm mà clipboard lỗi thì báo xanh là nói dối.
        /// </summary>
        public void FlashCopied()
        {
            _copyOk = true;
            _copyTimer.Stop();
            _copyTimer.Start();
            Invalidate();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _copyTimer?.Dispose();
            base.Dispose(disposing);
        }

        /// <summary>Hai hàng đầu + khe 6px giữa chúng.</summary>
        private const int DkchTopRowsH = DkchCodeRowH + 6 + DkchStateRowH;

        /// <summary>Bề rộng dành cho dấu thời gian ở hàng tên bưu tá, kèm khe 8px.</summary>
        private int StampGutter()
        {
            if (string.IsNullOrWhiteSpace(StampText)) return 0;
            using (var f = Mono(UiPtMin, FontStyle.Bold)) return Measure(StampText, f) + 8;
        }


        /// <summary>
        /// Chiều cao THẬT của dòng "↳ nguyên nhân" sau khi xuống dòng theo TỪ.
        /// Đo trước rồi mới vẽ nên chữ không bao giờ bị cắt cụt.
        /// </summary>
        /// <summary>Dòng nguyên nhân: đậm, cỡ 9pt, màu chữ chính — đây là thông tin
        /// nhân viên đọc nhiều nhất nên không để mờ như chú thích.</summary>
        private const float DkchNotePt = 9f;
        private const int DkchNoteLineH = 17;

        /// <summary>Bề rộng dòng nguyên nhân — dùng TRỌN bề ngang, dấu thời gian chỉ
        /// chiếm dòng đầu nên không việc gì phải chừa chỗ cho nó ở các dòng sau.</summary>
        private int NoteWidth(int width) => Math.Max(60, width - 20);

        private int NoteHeight(int width)
        {
            if (string.IsNullOrWhiteSpace(NoteText)) return 0;
            using (var f = Ui(DkchNotePt, FontStyle.Bold))
            {
                return WrapWords("↳ " + DkchRemark.Translate(NoteText), f, NoteWidth(width)).Count * DkchNoteLineH;
            }
        }

        /// <summary>Chiều cao khối tên bưu tá + nguyên nhân, tối thiểu bằng huy hiệu ngày tồn.</summary>
        private int DetailHeight(int width)
            => HasDetails ? 7 + Math.Max(22, 21 + NoteHeight(width) + 4) : 0;

        /// <summary>
        /// Cỡ chữ dải kết quả/vi phạm. Chủ dự án yêu cầu 14px; Font của WinForms nhận
        /// POINT nên 14px ÷ (96/72) = 10.5pt. Nhãn phía trên để nhỏ hơn 2.5pt cho còn ra
        /// dáng nhãn — kéo nhãn lên bằng nội dung thì "⚠ KHÔNG THỰC HIỆN ĐƯỢC" chiếm
        /// gần hết một dòng và át luôn thông điệp.
        /// </summary>
        private const float DkchStripPt = 10.5f;
        private const float DkchStripTagPt = DkchStripPt - 2.5f;
        /// <summary>Chiều cao hàng chip đếm ĐKCH/Phát lại ở đáy dải.</summary>
        private const int DkchStripChipH = 17;

        /// <summary>
        /// Bề rộng chữ trong dải. Trước đây chỗ đo ghi "width - 24" còn chỗ vẽ tự tính
        /// lại từ toạ độ — hai bên tình cờ bằng nhau, đổi một bên là lệch. Nay một hàm.
        /// </summary>
        private static int StripTextWidth(int width) => Math.Max(40, width - 24);

        /// <summary>
        /// Ngắt nội dung dải thành các dòng TRỌN TỪ. Bỏ DT_WORDBREAK của GDI vì nó ngắt
        /// cả giữa từ ("chuyển / hoàn" thành "chuy / ển hoàn") — chủ dự án không nhận.
        /// Dùng CHUNG cho lúc đo chiều cao và lúc vẽ: hai bên phải ra đúng một danh sách,
        /// lệch nhau là chữ bị cắt đáy hoặc dải hở một khoảng trắng.
        /// </summary>
        private List<string> StripLines(int width, Font f)
            => WrapWords(StripText ?? "", f, StripTextWidth(width));

        private int ViolationHeight(int width)
        {
            if (!HasStrip) return 0;
            // Chiều cao suy ra TỪ CHÍNH FONT sẽ vẽ, không còn số 12/18/40 cắm cứng —
            // đổi cỡ chữ là chiều cao tự theo, không phải sửa tay bốn con số rời rạc.
            using (var fTag = Ui(DkchStripTagPt, FontStyle.Bold))
            using (var f = Ui(DkchStripPt))
            {
                // Chỉ có hai chip đếm mà không có thông điệp thì bỏ luôn cả khe dưới,
                // đừng chừa một khoảng trống không chứa gì.
                int n = StripLines(width, f).Count;
                int textH = n > 0 ? n * f.Height + 6 : 0;
                return 8 + fTag.Height + 4 + textH + DkchStripChipH + 8;
            }
        }

        public int MeasureHeight(int width)
        {
            // Lúc chưa xử lý mã nào thì bỏ hẳn đường kẻ và khối tên/nguyên nhân —
            // bản trước luôn cộng đủ nên thẻ rỗng thành một khối tối cao lêu nghêu.
            int body = 8 + DkchTopRowsH + 9;
            return body + DetailHeight(width) + ViolationHeight(width);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            bool hot = _copyBox.Contains(e.Location);
            Cursor = hot ? Cursors.Hand : Cursors.Default;
            if (hot != _copyHot) { _copyHot = hot; Invalidate(_copyBox); }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (_copyHot) { _copyHot = false; Invalidate(); }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button == MouseButtons.Left && _copyBox.Contains(e.Location))
            {
                var handler = CopyRequested;
                if (handler != null) handler(this, EventArgs.Empty);
            }
        }

        protected override void PaintCard(Graphics g)
        {
            FillCard(g, Skin.ResultBg, Skin.ResultBorder);

            int pad = 10;
            int right = Width - pad;
            int y = 8;

            // ── Hai hàng đầu: mỗi hàng là một TẤM NỀN TỐI riêng, đúng nguồn tham khảo.
            // Nền tối giữ nguyên ở cả ba theme vì bản mẫu vẽ nó trên trang sáng.
            // Hai tấm nền tối trải gần sát mép thẻ (chỉ chừa 4px cho viền bo) thay vì
            // thụt vào 10px như phần chữ bên dưới — lấy thêm 12px bề ngang cho nội dung.
            const int rowInset = 4;
            int rowLeft = rowInset;
            int rowW = Math.Max(60, Width - 1 - rowInset * 2);

            // Hàng 1 — mã vận đơn + nút chép neo phải trong tấm nền.
            var box1 = new Rectangle(rowLeft, y, rowW, DkchCodeRowH);
            using (var path = DkchPaint.RoundRect(box1, 6))
            using (var brush = new SolidBrush(Skin.RowBg))
            using (var pen = new Pen(Skin.RowBorder, 1f))
            {
                g.FillPath(brush, path);
                g.DrawPath(pen, path);   // nền trắng thì phải có viền mới thấy tấm
            }
            using (var fCode = Mono(UiPtLead, FontStyle.Bold))
            using (var fIcon = Ui(UiPtMax, FontStyle.Bold))
            {
                _copyBox = new Rectangle(box1.Right - DkchRowPad - 26, box1.Y + (box1.Height - 26) / 2, 26, 26);

                Color btnBg = _copyOk ? DkchOkGreen : (_copyHot ? Skin.ChipBg : Skin.CopyBg);
                Color btnLine = _copyOk ? DkchOkGreen : Skin.CopyBorder;
                Color btnInk = _copyOk ? Color.White : Skin.CopyFore;

                int codeW = Math.Max(20, _copyBox.X - box1.X - DkchRowPad - 8);
                Draw(g, Waybill, fCode, new Rectangle(box1.X + DkchRowPad, box1.Y, codeW, box1.Height), Skin.ResultTitle);

                using (var path = DkchPaint.RoundRect(_copyBox, 5))
                using (var brush = new SolidBrush(btnBg))
                using (var pen = new Pen(btnLine, 1f))
                {
                    g.FillPath(brush, path);
                    g.DrawPath(pen, path);
                }
                Draw(g, _copyOk ? "✓" : "⧉", fIcon, _copyBox, btnInk, TextFormatFlags.HorizontalCenter);
            }
            y += DkchCodeRowH + 6;

            // Hàng 2 — chip thao tác cuối (ôm sát chữ) + huy hiệu NGÀY TỒN neo phải.
            var box2 = new Rectangle(rowLeft, y, rowW, DkchStateRowH);
            using (var path = DkchPaint.RoundRect(box2, 6))
            using (var brush = new SolidBrush(Skin.RowBg))
            using (var pen = new Pen(Skin.RowBorder, 1f))
            {
                g.FillPath(brush, path);
                g.DrawPath(pen, path);
            }

            int badgeW = 0;
            if (DaysInStock.HasValue)
            {
                // Cùng DkchChipPt với chip thao tác cuối — hai khối phải cân chữ nhau.
                using (var fNum = Mono(DkchChipPt, FontStyle.Bold))
                using (var fLbl = Ui(UiPtMin, FontStyle.Bold))
                {
                    string num = DaysInStock.Value.ToString();
                    badgeW = Math.Max(Measure(num, fNum), Measure("NGÀY TỒN", fLbl)) + 14;
                    var badge = new Rectangle(box2.Right - DkchRowPad - badgeW, box2.Y + (box2.Height - DkchChipH) / 2, badgeW, DkchChipH);
                    using (var path = DkchPaint.RoundRect(badge, 6))
                    using (var brush = new SolidBrush(Skin.StockBg))
                    using (var pen = new Pen(Skin.StockBorder, 1f))
                    {
                        g.FillPath(brush, path);
                        g.DrawPath(pen, path);
                    }
                    Draw(g, num, fNum, new Rectangle(badge.X, badge.Y + 2, badge.Width, 18),
                         Skin.StockText, TextFormatFlags.HorizontalCenter);
                    Draw(g, "NGÀY TỒN", fLbl, new Rectangle(badge.X, badge.Y + 20, badge.Width, 13),
                         Skin.StockLabel, TextFormatFlags.HorizontalCenter);
                    badgeW += 8;
                }
            }

            // Tên thao tác dài ("Đăng ký chuyển hoàn") có thể không vừa chỗ còn lại sau
            // huy hiệu — hạ cỡ chữ tới khi vừa, hết cỡ mới chịu cắt.
            int chipRoom;
            {
                int bw = badgeW;
                chipRoom = Math.Max(40, rowW - DkchRowPad * 2 - bw);
            }
            // Chip LUÔN một dòng. Bắt đầu từ DkchChipPt (bằng cỡ số ngày tồn) rồi chỉ hạ
            // khi thật sự không vừa — đệm trong chip nay còn 25px thay vì 44px đo hụt của
            // bản cũ, nên phần lớn tên thao tác giữ nguyên cỡ tối đa.
            float chipPt = DkchChipPt;
            if (!string.IsNullOrWhiteSpace(StatusText))
            {
                for (; chipPt > UiPtMin; chipPt -= 0.25f)
                {
                    using (var f = Ui(chipPt, FontStyle.Bold))
                        if (Measure(StatusText, f) + DkchChipPad <= chipRoom) break;
                }
            }
            using (var fChip = Ui(chipPt, FontStyle.Bold))
            {
                // Lúc chưa xử lý mã nào: GIỮ chip làm dấu hiệu nhìn thấy được, nhưng
                // không ghi chữ — "Chưa xử lý mã nào" chỉ là tiếng ồn.
                string status = StatusText ?? "";
                bool blank = string.IsNullOrWhiteSpace(status);
                // Màu lấy theo TÊN THAO TÁC, không theo mức độ kết quả nữa — người dùng
                // nhìn chip là biết đơn vừa qua bước nào, còn kết quả đã có dải P6 riêng.
                var role = ActRoleOf(StatusText);
                Color chipBg = ActBg(role), chipInk = ActInk(role), chipDot = ActDot(role);

                int chipW = blank ? 40 : Math.Min(chipRoom, Measure(status, fChip) + DkchChipPad);
                var chip = new Rectangle(box2.X + DkchRowPad, box2.Y + (box2.Height - DkchChipH) / 2,
                                         chipW, DkchChipH);
                // Bo góc 6 để khớp với huy hiệu NGÀY TỒN bên cạnh — góc 4 của chip thấp cũ
                // trông lệch hẳn khi khối cao lên.
                using (var path = DkchPaint.RoundRect(chip, 6))
                using (var brush = new SolidBrush(chipBg))
                {
                    g.FillPath(brush, path);
                }
                using (var brush = new SolidBrush(chipDot))
                {
                    g.FillEllipse(brush, chip.X + DkchChipDotLeft,
                                  chip.Y + (chip.Height - DkchChipDotD) / 2, DkchChipDotD, DkchChipDotD);
                }
                if (!blank)
                {
                    Draw(g, status, fChip,
                         new Rectangle(chip.X + DkchChipTextLeft, chip.Y,
                                       Math.Max(1, chip.Width - DkchChipPad), chip.Height), chipInk);
                }
            }
            y += DkchStateRowH + 9;

            // Bọc trong if chứ KHÔNG return: return sẽ cắt luôn dải P6 phía dưới,
            // làm mất thông điệp kết quả ở những ca không tra được bưu tá.
            if (HasDetails)
            {
            // ── Đường kẻ mảnh rồi tới tên bưu tá + nguyên nhân + DẤU THỜI GIAN.
            // Ngày tồn đã chuyển lên hàng chip; chỗ này nay dành cho thời gian thao tác.
            using (var pen = new Pen(Skin.ResultBorder, 1f))
                g.DrawLine(pen, pad, y, right, y);
            y += 7;

            int stampW = StampGutter();
            using (var fStamp = Mono(UiPtMin, FontStyle.Bold))
            {
                Draw(g, StampText, fStamp,
                     new Rectangle(right - stampW + 8, y, Math.Max(1, stampW - 8), 20),
                     Skin.ResultSub, TextFormatFlags.Right);
            }

            int textW = Math.Max(40, right - pad - stampW);
            using (var fName = Ui(UiPtMax, FontStyle.Bold))
            using (var fNote = Ui(DkchNotePt, FontStyle.Bold))
            {
                Draw(g, OperatorName, fName, new Rectangle(pad, y, textW, 20), Skin.ResultTitle);
                if (!string.IsNullOrWhiteSpace(NoteText))
                {
                    // Vẽ từng dòng đã tự ngắt — mỗi dòng là một lần vẽ một dòng, GDI
                    // không còn cơ hội cắt giữa từ.
                    var lines = WrapWords("↳ " + DkchRemark.Translate(NoteText), fNote, NoteWidth(Width));
                    for (int li = 0; li < lines.Count; li++)
                    {
                        Draw(g, lines[li], fNote,
                             new Rectangle(pad, y + 21 + li * DkchNoteLineH, NoteWidth(Width), DkchNoteLineH),
                             ActInk(ActRoleOf(StatusText)));
                    }
                }
            }
            }

            // ── Dải P6 — bám đáy thẻ. Có vi phạm thì đỏ; không thì hiện KẾT QUẢ
            // theo màu của trạng thái, vì trước đây không vi phạm là mất luôn cả
            // thông điệp kết quả lẫn hai chip đếm.
            if (HasStrip)
            {
                Color stripBg, stripBar, stripTagInk, stripInk;
                if (IsViolation || Kind == StatusKind.Problem)
                { stripBg = Skin.ViolationBg; stripBar = Skin.ViolationBar; stripTagInk = Skin.ViolationTitle; stripInk = Skin.ViolationText; }
                else if (Kind == StatusKind.Ok)
                { stripBg = Skin.ChipOkBg; stripBar = Skin.StepDone; stripTagInk = Skin.ChipOkText; stripInk = Skin.ChipOkText; }
                else
                { stripBg = Skin.ChipWarnBg; stripBar = Skin.StockText; stripTagInk = Skin.ChipWarnText; stripInk = Skin.ChipWarnText; }

                int vh = ViolationHeight(Width);
                var strip = new Rectangle(1, Height - vh - 1, Math.Max(1, Width - 3), vh);
                using (var clip = DkchPaint.RoundRect(new Rectangle(0, 0, Math.Max(1, Width - 1), Math.Max(1, Height - 1)), 6))
                {
                    var old = g.Clip;
                    g.SetClip(clip, CombineMode.Intersect);
                    using (var brush = new SolidBrush(stripBg)) g.FillRectangle(brush, strip);
                    using (var brush = new SolidBrush(stripBar))
                        g.FillRectangle(brush, new Rectangle(strip.X, strip.Y, 3, strip.Height));
                    g.Clip = old;
                }

                int vx = strip.X + 11;
                int vw = StripTextWidth(Width);
                using (var fTitle = Ui(DkchStripTagPt, FontStyle.Bold))
                using (var fText = Ui(DkchStripPt))
                using (var fChip = Ui(7.1f, FontStyle.Bold))
                {
                    // Cùng thứ tự cộng dồn với ViolationHeight: 8 · nhãn · 4 · nội dung
                    // · 6 · chip · 8. Lệch một bước là chữ bị cắt hoặc dải hở đáy.
                    int ty = strip.Y + 8;
                    Draw(g, StripTag, fTitle, new Rectangle(vx, ty, vw, fTitle.Height), stripTagInk);
                    ty += fTitle.Height + 4;

                    foreach (string ln in StripLines(Width, fText))
                    {
                        Draw(g, ln, fText, new Rectangle(vx, ty, vw, fText.Height), stripInk);
                        ty += fText.Height;
                    }

                    int chipY = strip.Bottom - 8 - DkchStripChipH;

                    // Chip ghi số 0 không nói lên điều gì, chỉ thêm nhiễu.
                    if (RegisterCount > 0) PaintCountChip(g, ref vx, chipY, fChip, "ĐKCH", RegisterCount);
                    if (RedeliverCount > 0) PaintCountChip(g, ref vx, chipY, fChip, "Phát lại", RedeliverCount);
                }
            }
        }

        private void PaintCountChip(Graphics g, ref int x, int y, Font font, string label, int value)
        {
            string text = label + "  " + value;
            int w = Measure(text, font) + 14;
            var box = new Rectangle(x, y, w, 17);
            using (var path = DkchPaint.RoundRect(box, 3))
            using (var brush = new SolidBrush(Skin.CountChipBg))
            using (var pen = new Pen(Skin.CountChipBorder, 1f))
            {
                g.FillPath(brush, path);
                g.DrawPath(pen, path);
            }
            Draw(g, text, font, box, Skin.CountChipText, TextFormatFlags.HorizontalCenter);
            x += w + 5;
        }
    }

    /// <summary>Dòng GỢI Ý màu vàng — chỉ hiện ở chế độ Newbie khi nhập lẻ 1 mã.</summary>
    internal sealed class DkchTipBar : DkchCardBase
    {
        public string Tip { get; set; } = "";

        public int MeasureHeight(int width)
        {
            using (var f = Ui(8.5f, FontStyle.Bold))
            {
                var size = TextRenderer.MeasureText(Tip ?? "", f,
                    new Size(Math.Max(40, width - 26), int.MaxValue), TextFormatFlags.WordBreak);
                return Math.Max(38, size.Height + 26);
            }
        }

        protected override void PaintCard(Graphics g)
        {
            FillCard(g, Skin.TipBg, Skin.TipBorder);
            using (var clip = DkchPaint.RoundRect(new Rectangle(0, 0, Math.Max(1, Width - 1), Math.Max(1, Height - 1)), 6))
            {
                var old = g.Clip;
                g.SetClip(clip, CombineMode.Intersect);
                using (var brush = new SolidBrush(Skin.TipBar))
                    g.FillRectangle(brush, new Rectangle(0, 0, 4, Height));
                g.Clip = old;
            }

            int x = 12;
            using (var fMark = Ui(9f, FontStyle.Bold))
            {
                Draw(g, "▶", fMark, new Rectangle(x, 6, 12, 13), Skin.TipLabel);
            }
            x += 14;
            int w = Math.Max(30, Width - x - 9);
            using (var fLabel = Ui(6.4f, FontStyle.Bold))
            using (var fText = Ui(9f, FontStyle.Bold))
            {
                Draw(g, "GỢI Ý:", fLabel, new Rectangle(x, 5, w, 11), Skin.TipLabel);
                TextRenderer.DrawText(g, Tip ?? "", fText, new Rectangle(x, 17, w, Height - 22),
                    Skin.TipText, TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
            }
        }
    }

    /// <summary>Thanh "Tiến trình n/x" với các mốc và nhãn bên dưới.</summary>
    internal sealed class DkchProgressCard : DkchCardBase
    {
        public List<DkchStep> Steps { get; set; } = new List<DkchStep>();

        /// <summary>Chỉ khi bị chặn mới tô đỏ; "đang ở bước n" là bình thường, không phải sự cố.</summary>
        public bool IsBlocked { get; set; }

        public int MeasureHeight() => 48;

        protected override void PaintCard(Graphics g)
        {
            FillCard(g, Skin.BoxBg, Skin.BoxBorder);

            int pad = 9;
            int w = Math.Max(20, Width - pad * 2);
            // Steps có thể null nếu nơi gọi quên khởi tạo — ném lỗi trong OnPaint là treo app.
            var steps = Steps ?? new List<DkchStep>();
            int done = 0;
            foreach (var s in steps) if (s.State == DkchStepState.Done) done++;

            using (var fLabel = Ui(6.75f, FontStyle.Bold))
            using (var fCount = Ui(7.9f, FontStyle.Bold))
            {
                Draw(g, "TIẾN TRÌNH", fLabel, new Rectangle(pad, 5, w - 40, 12), Skin.BoxLabel);
                Draw(g, $"{done}/{Math.Max(1, steps.Count)}", fCount,
                     new Rectangle(Width - pad - 40, 4, 40, 13),
                     IsBlocked ? Skin.StepLabelCurrent : Skin.BoxLabel, TextFormatFlags.Right);
            }

            if (steps.Count == 0) return;

            int gap = 3;
            int cellW = Math.Max(6, (w - gap * (steps.Count - 1)) / steps.Count);
            int y = 20;

            for (int i = 0; i < steps.Count; i++)
            {
                var bar = new Rectangle(pad + i * (cellW + gap), y, cellW, 7);
                Color fill;
                switch (steps[i].State)
                {
                    case DkchStepState.Done: fill = Skin.StepDone; break;
                    case DkchStepState.Current: fill = IsBlocked ? Skin.StepCurrent : Skin.StepPending; break;
                    default: fill = Skin.StepPending; break;
                }
                using (var path = DkchPaint.RoundRect(bar, 2))
                using (var brush = new SolidBrush(fill))
                {
                    g.FillPath(brush, path);
                }
                // Gạch chéo đỏ chỉ dành cho đơn ĐANG BỊ CHẶN; chưa tới bước thì để trống.
                if (steps[i].State == DkchStepState.Current && IsBlocked)
                {
                    using (var path = DkchPaint.RoundRect(bar, 2))
                    using (var brush = new HatchBrush(HatchStyle.LightUpwardDiagonal, Skin.StepCurrentAlt, Skin.StepCurrent))
                    {
                        g.FillPath(brush, path);
                    }
                }
            }

            // Nhãn nay là MỘT TỪ (ĐẾN · PHÁT · KIỆN · ĐKCH · XNCH · PL · IN · KÝ) nên
            // vẽ gọn một dòng; chỉ hạ cỡ chữ nếu ô quá hẹp, không xuống dòng, không cắt.
            float stepPt = 6.5f;
            for (; stepPt > 5f; stepPt -= 0.25f)
            {
                using (var probe = Ui(stepPt, FontStyle.Bold))
                {
                    int widest = 0;
                    foreach (var st in steps) widest = Math.Max(widest, Measure(st.Label, probe));
                    if (widest <= cellW) break;
                }
            }

            using (var fStep = Ui(stepPt, FontStyle.Bold))
            {
                for (int i = 0; i < steps.Count; i++)
                {
                    var cell = new Rectangle(pad + i * (cellW + gap), y + 10, cellW, 14);
                    bool current = steps[i].State == DkchStepState.Current;
                    Draw(g, steps[i].Label, fStep, cell,
                         current && IsBlocked ? Skin.StepLabelCurrent : Skin.StepLabel,
                         TextFormatFlags.HorizontalCenter);
                }
            }
        }
    }

    /// <summary>
    /// Dịch ghi chú tiếng Trung của JMS sang tiếng Việt.
    /// <para>
    /// CHỈ chứa những chuỗi đã thấy trong dữ liệu thật — không đoán nghĩa. Chuỗi lạ
    /// giữ nguyên văn để còn nhận ra mà bổ sung, thay vì dịch bừa rồi hiểu sai nghiệp vụ.
    /// Danh sách này nên chuyển sang tab2config.json để thêm mới không phải build lại.
    /// </para>
    /// </summary>
    internal static class DkchRemark
    {
        private static readonly (string Cn, string Vi)[] Map =
        {
            ("退回寄件地址", "Trả về địa chỉ người gửi"),
        };

        public static string Translate(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return raw ?? "";
            string s = raw.Trim();
            foreach (var (cn, vi) in Map)
            {
                if (s == cn) return vi;
                if (s.Contains(cn)) s = s.Replace(cn, vi);
            }
            return s;
        }

        /// <summary>
        /// Tên người thao tác. JMS đôi khi trả MÃ nhân viên ("01525852") thay vì tên —
        /// hiện một dãy số ở chỗ tên chỉ gây rối, nên bỏ trống để dòng đó tự thu lại.
        /// </summary>
        public static string PersonName(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return "";
            string s = raw.Trim();
            foreach (char c in s) if (!char.IsDigit(c)) return s;
            return "";
        }
    }

    /// <summary>Danh sách "Hành trình" — mốc mới nhất ở trên, có chấm, tên bưu tá và nguyên nhân.</summary>
    internal sealed class DkchJourneyCard : DkchCardBase
    {
        private readonly VScrollBar _bar;

        public DkchJourneyCard()
        {
            // Thanh cuộn thật thay vì âm thầm bỏ bớt mốc. Dùng VScrollBar của hệ thống
            // vì nó tự xử lý chuột, không phải cướp focus của ô nhập mã.
            _bar = new VScrollBar { Width = 12, Visible = false, SmallChange = 16, TabStop = false };
            _bar.Scroll += (s, e) => Invalidate();
            Controls.Add(_bar);
        }

        public List<DkchJourneyEntry> Entries { get; set; } = new List<DkchJourneyEntry>();
        public string EmptyText { get; set; } = "Chưa có dữ liệu hành trình.";

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            // OnResize có thể chạy TRƯỚC khi constructor xong (Control nền đổi kích thước
            // trong lúc dựng), nên phải kiểm null chứ không tin là _bar đã có.
            if (_bar == null) return;
            _bar.Bounds = new Rectangle(Math.Max(1, Width - 13), 22, 12, Math.Max(10, Height - 26));
        }

        private int RowHeightOf(DkchJourneyEntry it)
            => string.IsNullOrWhiteSpace(it?.Note) ? 32 : 46;

        protected override void PaintCard(Graphics g)
        {
            FillCard(g, Skin.BoxBg, Skin.BoxBorder);

            int pad = 10;
            int w = Math.Max(20, Width - pad * 2);
            int y = 6;

            int headerBottom = y + 16;
            y = headerBottom;

            if (Entries == null || Entries.Count == 0)
            {
                using (var f = Ui(8f))
                    Draw(g, EmptyText, f, new Rectangle(pad, y, w, 16), Skin.JourneyTime);
                return;
            }

            // Tổng chiều cao nội dung để biết có cần cuộn không.
            int total = 0;
            foreach (var e2 in Entries) total += RowHeightOf(e2);
            int viewH = Math.Max(10, Height - y - 4);
            bool needBar = total > viewH;
            if (_bar.Visible != needBar) _bar.Visible = needBar;
            if (needBar)
            {
                _bar.Minimum = 0;
                _bar.LargeChange = viewH;
                _bar.Maximum = Math.Max(viewH, total);
                if (_bar.Value > _bar.Maximum - _bar.LargeChange)
                    _bar.Value = Math.Max(0, _bar.Maximum - _bar.LargeChange);
            }
            int scroll = needBar ? _bar.Value : 0;
            int listRight = needBar ? Width - pad - 14 : Width - pad;

            // Vẽ THẲNG lên bề mặt thật (không qua ảnh đệm) để chữ giữ nguyên độ nét của
            // ClearType. Vùng cắt cắt gọn dòng chạm mép, nên vẫn cuộn mượt từng pixel.
            var view = new Rectangle(pad, y, Math.Max(10, listRight - pad), viewH);
            var oldClip = g.Clip;
            g.SetClip(view, CombineMode.Intersect);
            y -= scroll;

            using (var fTitle = Ui(9f, FontStyle.Bold))
            using (var fName = Ui(7.9f, FontStyle.Bold))
            using (var fNote = Ui(8.25f))
            using (var fTime = Mono(7.1f))
            {
                for (int i = 0; i < Entries.Count; i++)
                {
                    var it = Entries[i];
                    bool hasNote = !string.IsNullOrWhiteSpace(it.Note);
                    int rowH = RowHeightOf(it);
                    if (y + rowH <= view.Y) { y += rowH; continue; }   // đã cuộn qua
                    if (y >= view.Bottom) break;                       // ngoài tầm nhìn

                    var role = ActRoleOf(it.Type);
                    using (var brush = new SolidBrush(ActDot(role)))
                        g.FillEllipse(brush, pad, y + 4, 8, 8);

                    // Dấu thời gian ĐẦY ĐỦ ngày + giờ, không rút gọn. Chỉ dòng tiêu đề
                    // phải nhường chỗ cho nó; dòng tên và ghi chú dùng trọn bề ngang.
                    string stamp = string.IsNullOrWhiteSpace(it.Date)
                        ? (it.Time ?? "")
                        : it.Date + " | " + it.Time;
                    int tw = Measure(stamp, fTime) + 4;
                    int tx = pad + 14;
                    int fullW = Math.Max(30, listRight - tx);
                    int contentW = Math.Max(30, fullW - tw - 6);

                    Draw(g, it.Type, fTitle, new Rectangle(tx, y, contentW, 14), ActInk(role));
                    Draw(g, stamp, fTime, new Rectangle(listRight - tw, y, tw, 14),
                         Skin.JourneyTime, TextFormatFlags.Right);
                    Draw(g, DkchRemark.PersonName(it.Operator), fName,
                         new Rectangle(tx, y + 14, fullW, 13), Skin.JourneyName);
                    if (hasNote)
                        Draw(g, "↳ " + DkchRemark.Translate(it.Note), fNote,
                             new Rectangle(tx, y + 27, fullW, 14), ActInk(role));

                    y += rowH;
                    if (i < Entries.Count - 1)
                    {
                        using (var pen = new Pen(Skin.JourneyDivider, 1f) { DashStyle = DashStyle.Dash })
                            g.DrawLine(pen, pad, y - 5, listRight, y - 5);
                    }
                }
            }
            g.Clip = oldClip;

            // Nhãn là TIÊU ĐỀ cố định của thẻ: vẽ sau cùng trên nền đặc. Ảnh đệm đã bắt
            // đầu ngay dưới nhãn nên không thể chồng lên, đây là lớp chắn thứ hai.
            var headerBand = new Rectangle(1, 1, Math.Max(1, Width - 3), headerBottom - 1);
            using (var clipTop = DkchPaint.RoundRect(new Rectangle(0, 0, Math.Max(1, Width - 1), Math.Max(1, Height - 1)), 6))
            {
                var old2 = g.Clip;
                g.SetClip(clipTop, CombineMode.Intersect);
                using (var brush = new SolidBrush(Skin.BoxBg)) g.FillRectangle(brush, headerBand);
                g.Clip = old2;
            }
            using (var fLabel = Ui(6.75f, FontStyle.Bold))
                Draw(g, "HÀNH TRÌNH", fLabel, new Rectangle(pad, 6, w, 12), Skin.BoxLabel);
        }
    }
}

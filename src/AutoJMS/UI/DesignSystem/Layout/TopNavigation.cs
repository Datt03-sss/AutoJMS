using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace AutoJMS.UI.DesignSystem
{
    /// <summary>
    /// Thanh điều hướng của AutoJMS — một lớp cho cả thanh tab chính (Main.topNav), dải tab CON
    /// 4 chế độ in của tab IN ĐƠN và dải tab của FullStackOperation.
    ///
    /// Kiểu "sliding pill" theo index.html của Owner (2026-10-01): dải SurfaceRaised phủ hết bề
    /// ngang, các nút xếp từ mép TRÁI theo đúng bề rộng chữ (không giãn lấp đầy - phần còn trống để
    /// dành cho tab mới); giữa các nút là vách 1px; một viên pill nằm DƯỚI chữ trượt 100ms tới đúng
    /// Left/Width thật của nút đang chọn. Màu lấy theo theme. Animation là ngoại lệ có chủ đích so
    /// với DESIGN.md (Owner yêu cầu).
    ///
    /// KHÔNG GIỮ TRẠNG THÁI CHỌN. Nguồn sự thật duy nhất là <see cref="Target"/>.
    /// Bấm nav thì ghi vào <c>Target.SelectedIndex</c>; Target đổi thì nav vẽ lại.
    /// Giữ một bản sao thứ hai ở đây là tự tạo ra hai chỗ có thể lệch nhau — chính
    /// là lỗi mà ThemeManager đã cố ý tránh (xem DesignSystem/README.md § Theme).
    ///
    /// Nav chỉ VẼ nhãn của TabPage. Không control nghiệp vụ nào bị di chuyển vào đây.
    /// </summary>
    public sealed class TopNavigation : AControl
    {
        private const int ItemHeight = 30;     // .nav-tab height
        private const int ItemPaddingX = 12;   // button padding: 0 12px
        private const int BarPadding = 3;      // từ mép thanh tới nút, cả bốn phía
        private const int PillRadius = 6;
        private const int PillTint = 20;       // % Primary trộn lên nền thanh
        private const int DividerAlpha = 71;   // chữ 28%
        private const int DividerHeight = 12;
        private const int SlideMs = 100;

        // 12px Medium. Segoe UI không có bậc Medium (500); Semibold là bậc gần nhất còn đọc ra
        // "đậm vừa" trên chữ in hoa nhỏ. 9pt = 12px ở 96 DPI, GDI tự nhân theo DPI. Font tĩnh vẽ
        // tay, KHÔNG gán vào Control.Font - AppTheme kéo mọi font không phải token về Body.
        private static readonly Font NavFont = new Font(ThemeTypography.FamilySemibold, 9F, FontStyle.Regular);

        private const TextFormatFlags MeasureFlags = TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine;
        private const TextFormatFlags DrawFlags = ControlStyler.TextLeft | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine;

        private readonly List<Rectangle> _itemRects = new List<Rectangle>();
        private readonly System.Windows.Forms.Timer _slideTimer = new System.Windows.Forms.Timer { Interval = 15 };
        private int[] _textWidths = Array.Empty<int>();
        private TabControl _target;
        private bool _layoutDirty = true;
        private int _hotIndex = -1;
        private int[] _symbols;
        private Rectangle _band;        // khung bao các nút - vùng vẽ lại khi pill trượt
        private Rectangle _pill;        // pill của lần vẽ gần nhất = điểm xuất phát khi lựa chọn đổi
        private Rectangle _slideFrom;
        private long _slideStart;       // Stopwatch timestamp; 0 = chưa tick lần nào

        public TopNavigation()
        {
            // PHẢI qua S(). Lượt auto-scale của Form chỉ chạy một lần, ở ResumeLayout cuối
            // InitializeComponent; control vào cây SAU đó - kể cả ngay trong constructor - KHÔNG
            // được nhân (đã đo bằng app thử: trong InitializeComponent 40 -> 80 ở hệ số 2, thêm
            // trong constructor hay OnLoad vẫn 40). Main.topNav và dải tab của FullStackOperation
            // đều dựng bằng code nên cần S(). Bản đặt trong Designer (tabPrint_printTabs) gán lại
            // Height bên trong InitializeComponent, nên vẫn được nhân như mọi control Designer.
            Height = S(ThemeMetrics.NavHeight);
            Dock = DockStyle.Top;
            TabStop = true;
            _slideTimer.Tick += OnSlideTick;
        }

        /// <summary>
        /// Lưới tab mà thanh này điều khiển. Nhãn hiển thị lấy từ <c>Target.TabPages[i].Text</c>,
        /// nên thứ tự tab — kể cả ràng buộc ABOUT phải là tab cuối (CLAUDE.md § Tab Boundary Rule)
        /// — vẫn do Designer quyết định, không phải do thanh này.
        /// </summary>
        public TabControl Target
        {
            get => _target;
            set
            {
                if (_target == value) return;

                if (_target != null)
                {
                    _target.SelectedIndexChanged -= OnTargetSelectionChanged;
                    _target.ControlAdded -= OnTargetPagesChanged;
                    _target.ControlRemoved -= OnTargetPagesChanged;
                }

                _target = value;

                if (_target != null)
                {
                    _target.SelectedIndexChanged += OnTargetSelectionChanged;
                    _target.ControlAdded += OnTargetPagesChanged;
                    _target.ControlRemoved += OnTargetPagesChanged;
                }

                _slideTimer.Stop();
                _pill = Rectangle.Empty;
                _layoutDirty = true;
                Invalidate();
            }
        }

        /// <summary>Icon <see cref="ASymbols"/> từng tab, cùng thứ tự Target.TabPages. null = chỉ chữ.</summary>
        [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public int[] Symbols
        {
            get => _symbols;
            set { _symbols = value; _layoutDirty = true; Invalidate(); }
        }

        private int SymbolAt(int index) => _symbols != null && index < _symbols.Length ? _symbols[index] : ASymbols.None;

        private int SelectedIndex => _target?.SelectedIndex ?? -1;

        private int ItemCount => _target?.TabPages.Count ?? 0;

        private void OnTargetSelectionChanged(object sender, EventArgs e)
        {
            // Trượt từ chỗ pill đang đứng - kể cả giữa chừng một lần trượt khác. Nav chưa vẽ lần
            // nào (dải tab con trên trang chưa mở) thì không có điểm xuất phát: hiện thẳng ở đích.
            if (!_pill.IsEmpty)
            {
                _slideFrom = _pill;
                _slideStart = 0;
                _slideTimer.Start();
            }
            Invalidate();
        }

        private void OnTargetPagesChanged(object sender, ControlEventArgs e)
        {
            _layoutDirty = true;
            Invalidate();
        }

        protected override void OnSizeChanged(EventArgs e)
        {
            base.OnSizeChanged(e);
            _layoutDirty = true;
        }

        private void OnSlideTick(object sender, EventArgs e)
        {
            // Đồng hồ bắt đầu ở tick ĐẦU chứ không lúc bấm: đổi sang trang nặng (WebView2, lưới
            // lớn) giữ luồng UI quá 100ms, tính giờ từ lúc bấm thì pill nhảy thẳng tới đích.
            if (_slideStart == 0) _slideStart = Stopwatch.GetTimestamp();
            else if (SlideProgress() >= 1f) _slideTimer.Stop();
            Invalidate(_band);
        }

        private float SlideProgress()
            => _slideStart == 0 ? 0f : Math.Min(1f, (float)Stopwatch.GetElapsedTime(_slideStart).TotalMilliseconds / SlideMs);

        // ---- Bố cục ----------------------------------------------------------

        private void EnsureLayout(Graphics g)
        {
            if (!_layoutDirty) return;
            _layoutDirty = false;

            _itemRects.Clear();
            int count = ItemCount;
            _textWidths = new int[count];
            if (count == 0) return;

            int padX = S(ItemPaddingX);
            int barPad = S(BarPadding);
            var widths = new int[count];
            int total = 0;
            for (int i = 0; i < count; i++)
            {
                _textWidths[i] = TextRenderer.MeasureText(g, _target.TabPages[i].Text, NavFont, Size.Empty, MeasureFlags).Width;
                int icon = SymbolAt(i) != ASymbols.None ? S(ThemeMetrics.IconSizeDense) + S(ThemeSpacing.Xs) : 0;
                widths[i] = _textWidths[i] + icon + padX * 2;
                total += widths[i];
            }

            // Không đủ chỗ thì co đều thay vì để tab cuối tràn ra ngoài mép phải - chữ hụt "…"
            // nhưng tab vẫn bấm được.
            int available = Width - barPad * 2;
            if (total > available && available > 0)
            {
                int shrunk = 0;
                for (int i = 0; i < count; i++)
                {
                    widths[i] = widths[i] * available / total;
                    shrunk += widths[i];
                }
                total = shrunk;
            }

            int itemHeight = S(ItemHeight);
            _band = new Rectangle(barPad, barPad, total, itemHeight);

            int x = barPad;
            for (int i = 0; i < count; i++)
            {
                _itemRects.Add(new Rectangle(x, barPad, widths[i], itemHeight));
                x += widths[i];
            }
        }

        private int HitTest(Point location)
        {
            for (int i = 0; i < _itemRects.Count; i++)
                if (_itemRects[i].Contains(location)) return i;
            return -1;
        }

        // ---- Vẽ --------------------------------------------------------------

        protected override void OnPaint(PaintEventArgs e)
        {
            var c = Theme;
            var g = e.Graphics;
            EnsureLayout(g);

            using (var back = new SolidBrush(c.SurfaceRaised))
                g.FillRectangle(back, ClientRectangle);

            // Đáy 1px tách thanh khỏi trang bên dưới (cũng trắng ở Light/Red), như đáy AppTitleBar.
            using (var border = new Pen(c.Border))
                g.DrawLine(border, 0, Height - 1, Width, Height - 1);
            if (_itemRects.Count == 0) return;

            ControlStyler.Prepare(g, PillRadius);

            int selected = SelectedIndex;
            _pill = Rectangle.Empty;
            if (selected >= 0 && selected < _itemRects.Count)
            {
                Rectangle to = _itemRects[selected];
                _pill = _slideTimer.Enabled ? Lerp(_slideFrom, to, Ease(SlideProgress())) : to;
                ControlStyler.FillSurface(g, _pill, ThemeColors.Blend(c.Primary, c.SurfaceRaised, PillTint), S(PillRadius));
            }

            // Vách nằm trên pill như ::after của index.html; chỉ là nét vẽ nên không cản click.
            int dividerHeight = S(DividerHeight);
            using (var divider = new SolidBrush(Color.FromArgb(DividerAlpha, c.Text)))
            {
                for (int i = 0; i < _itemRects.Count; i++)
                {
                    var rect = _itemRects[i];
                    DrawItem(g, i, rect, c.Text);
                    if (i < _itemRects.Count - 1)
                        g.FillRectangle(divider, rect.Right - S(1), rect.Y + (rect.Height - dividerHeight) / 2, S(1), dividerHeight);
                }
            }

            // Vòng focus chỉ khi focus đến từ bàn phím (←/→ đổi tab), bấm chuột không để lại viền.
            if (Focused && ShowFocusCues && selected >= 0 && selected < _itemRects.Count)
                ControlStyler.DrawFocusRing(g, _itemRects[selected], c, S(PillRadius));
        }

        private void DrawItem(Graphics g, int index, Rectangle rect, Color ink)
        {
            int symbol = SymbolAt(index);
            int icon = symbol != ASymbols.None ? S(ThemeMetrics.IconSizeDense) : 0;
            int gap = icon > 0 ? S(ThemeSpacing.Xs) : 0;
            int textWidth = Math.Min(_textWidths[index], Math.Max(0, rect.Width - S(ItemPaddingX) * 2 - icon - gap));
            int x = rect.X + (rect.Width - (icon + gap + textWidth)) / 2;

            if (icon > 0)
                ASymbols.Draw(g, symbol, icon, ink, new Rectangle(x, rect.Y + (rect.Height - icon) / 2, icon, icon));

            int textX = x + icon + gap;
            TextRenderer.DrawText(g, _target.TabPages[index].Text, NavFont,
                new Rectangle(textX, rect.Y, rect.Right - textX, rect.Height), ink, DrawFlags);
        }

        private static float Ease(float t) => 1f - (1f - t) * (1f - t) * (1f - t);   // ease-out cubic

        private static Rectangle Lerp(Rectangle a, Rectangle b, float t) => new Rectangle(
            a.X + (int)Math.Round((b.X - a.X) * t), b.Y,
            a.Width + (int)Math.Round((b.Width - a.Width) * t), b.Height);

        // ---- Chuột / bàn phím ------------------------------------------------

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);

            int hot = HitTest(e.Location);
            if (hot == _hotIndex) return;

            _hotIndex = hot;
            Cursor = hot >= 0 ? Cursors.Hand : Cursors.Default;
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (_hotIndex == -1) return;

            _hotIndex = -1;
            Cursor = Cursors.Default;
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left || _target == null) return;

            int index = HitTest(e.Location);
            if (index < 0 || index == _target.SelectedIndex) return;

            // Không Focus() nav: TabControl tự lấy focus (WmSelChanging) rồi chuyển vào control
            // đầu của trang mới (UpdateTabSelection), nên focus ở nav mất ngay - chỉ tốn thêm một
            // vòng SetFocus/KillFocus (rời WebView2 là gọi chéo tiến trình) và hai lần vẽ lại nav.
            // Ghi vào Target là đủ: OnTargetSelectionChanged sẽ vẽ lại, và MỌI handler
            // nghiệp vụ đang nghe tabControl.SelectedIndexChanged vẫn chạy y như cũ.
            SelectPage(index);
        }

        private const int WmSetRedraw = 0x000B;
        private const uint RdwInvalidate = 0x0001, RdwErase = 0x0004, RdwAllChildren = 0x0080, RdwFrame = 0x0400;
        [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll")] private static extern bool RedrawWindow(IntPtr hWnd, IntPtr rect, IntPtr rgn, uint flags);

        /// <summary>
        /// Đổi trang với khung chứa Target tắt vẽ (WM_SETREDRAW). Không tắt thì hiện trang mới là
        /// Windows xử lý vùng hiển thị + vẽ ngay từng control con, còn handler nghiệp vụ đổi
        /// text/cột lại vẽ thêm lượt nữa. Tắt trên Parent chứ không trên Target: SysTabControl32
        /// tự giữ cờ redraw của nó mà không ẩn cây con. Bật lại xong vẽ lại cả khung một lượt,
        /// kể cả viền (RDW_FRAME) vì trong lúc tắt không ai nhận WM_NCPAINT.
        /// </summary>
        private void SelectPage(int index)
        {
            Control host = _target.Parent ?? _target;
            if (!host.IsHandleCreated)
            {
                _target.SelectedIndex = index;
                return;
            }

            SendMessage(host.Handle, WmSetRedraw, IntPtr.Zero, IntPtr.Zero);
            try
            {
                _target.SelectedIndex = index;
            }
            finally
            {
                SendMessage(host.Handle, WmSetRedraw, (IntPtr)1, IntPtr.Zero);
                RedrawWindow(host.Handle, IntPtr.Zero, IntPtr.Zero, RdwInvalidate | RdwErase | RdwFrame | RdwAllChildren);
            }
        }

        protected override bool IsInputKey(Keys keyData)
            => keyData == Keys.Left || keyData == Keys.Right || base.IsInputKey(keyData);

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (_target == null || ItemCount == 0) return;

            int delta = e.KeyCode == Keys.Left ? -1 : e.KeyCode == Keys.Right ? 1 : 0;
            if (delta == 0) return;

            int next = _target.SelectedIndex + delta;
            if (next < 0 || next >= ItemCount) return;

            SelectPage(next);
            e.Handled = true;
        }

        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                Target = null;   // gỡ handler khỏi TabControl
                _slideTimer.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}

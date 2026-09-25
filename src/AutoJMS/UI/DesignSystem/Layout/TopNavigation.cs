using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace AutoJMS.UI.DesignSystem
{
    /// <summary>
    /// Thanh điều hướng chính của AutoJMS. Xem DesignReference/AutoJMS.DESIGN.md §M.
    ///
    /// KHÔNG GIỮ TRẠNG THÁI CHỌN. Nguồn sự thật duy nhất là <see cref="Target"/>.
    /// Bấm nav thì ghi vào <c>Target.SelectedIndex</c>; Target đổi thì nav vẽ lại.
    /// Giữ một bản sao thứ hai ở đây là tự tạo ra hai chỗ có thể lệch nhau — chính
    /// là lỗi mà ThemeManager đã cố ý tránh (xem DesignSystem/README.md § Theme).
    ///
    /// Nav chỉ VẼ nhãn của TabPage. Không control nghiệp vụ nào bị di chuyển vào đây.
    ///
    /// CHỈ vẽ đầu tab, KHÔNG vẽ icon + tên sản phẩm ở góc trái: thanh tiêu đề (AppTitleBar ở
    /// Main, thanh của Windows ở FullStackOperation) đã mang sẵn cả hai, vẽ lại ở đây là hai lần
    /// "AutoJMS" chồng nhau và chữ dính sát tab đầu tiên. Nhờ vậy cùng một lớp dùng được cho cả
    /// thanh nav chính lẫn dải tab CON bên trong một trang (4 chế độ in của tab IN ĐƠN) — một
    /// thanh chứ không phải hai lớp gần giống nhau.
    /// </summary>
    public sealed class TopNavigation : AControl
    {
        private const int ItemPaddingX = 16;  // đệm trái/phải trong một tab
        private const int EdgePaddingX = 12;  // lề trái của cả thanh

        private readonly List<Rectangle> _itemRects = new List<Rectangle>();
        private TabControl _target;
        private bool _layoutDirty = true;
        private int _hotIndex = -1;

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

                _layoutDirty = true;
                Invalidate();
            }
        }

        private int SelectedIndex => _target?.SelectedIndex ?? -1;

        private int ItemCount => _target?.TabPages.Count ?? 0;

        private void OnTargetSelectionChanged(object sender, EventArgs e) => Invalidate();

        private void OnTargetPagesChanged(object sender, ControlEventArgs e)
        {
            _layoutDirty = true;
            Invalidate();
        }

        protected override void OnThemeChanged()
        {
            _layoutDirty = true;   // BodyStrong rộng hơn Body → bề rộng tab đổi theo theme/font
            Invalidate();
        }

        protected override void OnSizeChanged(EventArgs e)
        {
            base.OnSizeChanged(e);
            _layoutDirty = true;
        }

        // ---- Bố cục ----------------------------------------------------------

        /// <summary>
        /// Đo lại bề rộng từng tab. Đo bằng <see cref="ThemeTypography.BodyStrong"/> cho MỌI tab,
        /// kể cả tab chưa chọn: nếu đo bằng font của trạng thái hiện tại thì tab sẽ nhảy ngang
        /// mỗi lần đổi lựa chọn.
        /// </summary>
        private void EnsureLayout(Graphics g)
        {
            if (!_layoutDirty) return;

            _itemRects.Clear();

            int left = S(EdgePaddingX);

            var widths = new int[ItemCount];
            int total = 0;
            for (int i = 0; i < ItemCount; i++)
            {
                widths[i] = TextRenderer.MeasureText(g, _target.TabPages[i].Text, ThemeTypography.BodyStrong).Width
                          + (S(ItemPaddingX) * 2);
                total += widths[i];
            }

            // Không đủ chỗ thì co đều thay vì để tab cuối tràn ra ngoài mép phải hoặc
            // giấu sau cặp mũi tên ‹ › — giấu là "In Reverse" biến mất hẳn trên màn hẹp.
            // Co lại thì chữ hụt nhưng tab vẫn bấm được.
            int available = Width - left - S(EdgePaddingX);
            int x = left;
            for (int i = 0; i < ItemCount; i++)
            {
                int width = total > available && available > 0 ? widths[i] * available / total : widths[i];
                _itemRects.Add(new Rectangle(x, 0, width, Height));
                x += width;
            }

            _layoutDirty = false;
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

            using (var back = new SolidBrush(c.Surface))
                g.FillRectangle(back, ClientRectangle);

            int selected = SelectedIndex;
            for (int i = 0; i < _itemRects.Count; i++)
                DrawItem(g, c, i, i == selected);

            // Đường phân cách nav / nội dung. 1px, không đổ bóng (DESIGN.md §I).
            using (var border = new Pen(c.Border))
                g.DrawLine(border, 0, Height - 1, Width, Height - 1);
        }

        private void DrawItem(Graphics g, ThemeColors c, int index, bool isSelected)
        {
            var rect = _itemRects[index];
            bool isHot = index == _hotIndex && !isSelected;

            // Tab đang chọn KHÔNG tô nền Primary — DESIGN.md §M nói rõ vì sao:
            // một vệt đặc cao 40px cạnh vùng dữ liệu kéo mắt khỏi chính dữ liệu.
            if (isHot)
            {
                using (var hot = new SolidBrush(c.SurfaceAlt))
                    g.FillRectangle(hot, rect);
            }

            Color fore = isSelected ? c.Text : (isHot ? c.Text : c.TextSecondary);
            Font font = isSelected ? ThemeTypography.BodyStrong : ThemeTypography.Body;

            // EndEllipsis: khi thanh phải co lại (cửa sổ hẹp) thì cắt ĐUÔI rồi thêm "…".
            // Không có cờ này, HorizontalCenter gặm đều cả hai đầu - "In chuyển hoàn" hiện
            // ra "n chuyển hoà" và dính liền tab kế bên, không đọc ra tab nào nữa.
            TextRenderer.DrawText(g, _target.TabPages[index].Text, font, rect, fore,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter
                | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);

            if (isSelected)
            {
                int indicator = S(ThemeMetrics.TabIndicatorHeight);
                using (var accent = new SolidBrush(c.Primary))
                    g.FillRectangle(accent, rect.X, Height - indicator, rect.Width, indicator);
            }

            // Viền focus bàn phím: cố ý dùng Focus, KHÔNG dùng Primary (DESIGN.md §S).
            if (Focused && index == SelectedIndex)
            {
                using (var focus = new Pen(c.Focus) { DashStyle = System.Drawing.Drawing2D.DashStyle.Dot })
                    g.DrawRectangle(focus, rect.X + 2, 2, rect.Width - 5, Height - 6);
            }
        }

        // ---- Chuột / bàn phím ------------------------------------------------

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);

            int hot = HitTest(e.Location);
            if (hot == _hotIndex) return;

            _hotIndex = hot;
            Cursor = hot >= 0 ? Cursors.Hand : Cursors.Default;
            Invalidate();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (_hotIndex == -1) return;

            _hotIndex = -1;
            Cursor = Cursors.Default;
            Invalidate();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left || _target == null) return;

            int index = HitTest(e.Location);
            if (index < 0 || index == _target.SelectedIndex) return;

            Focus();
            // Ghi vào Target là đủ: OnTargetSelectionChanged sẽ vẽ lại, và MỌI handler
            // nghiệp vụ đang nghe tabControl.SelectedIndexChanged vẫn chạy y như cũ.
            _target.SelectedIndex = index;
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

            _target.SelectedIndex = next;
            e.Handled = true;
        }

        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }

        protected override void Dispose(bool disposing)
        {
            if (disposing) Target = null;   // gỡ handler khỏi TabControl
            base.Dispose(disposing);
        }
    }
}

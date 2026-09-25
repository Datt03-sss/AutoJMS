using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;

namespace AutoJMS.UI.DesignSystem
{
    /// <summary>
    /// Thanh điều hướng chính của AutoJMS. Xem DesignReference/AutoJMS.DESIGN.md §M.
    ///
    /// Thay cho dải tab của Sunny.UI <c>UITabControl</c>, vốn <c>sealed</c> và không bắn
    /// <c>DrawItem</c> (đã ghi lại bằng thực nghiệm ở UI/PremiumTabAccent.cs:11-20).
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
        private const int IdentityGap = 12;   // khoảng cách chữ "AutoJMS" tới tab đầu tiên
        private const int ItemPaddingX = 16;  // đệm trái/phải trong một tab
        private const int EdgePaddingX = 12;  // lề trái của cả thanh

        private readonly List<Rectangle> _itemRects = new List<Rectangle>();
        private TabControl _target;
        private bool _layoutDirty = true;
        private bool _showIdentity = true;
        private int _hotIndex = -1;

        public TopNavigation()
        {
            // Giá trị 96-DPI thô, KHÔNG qua DpiHelper.Scale().
            // Cả Main đang chạy AutoScaleMode.None (Main.Designer.cs) nên mọi toạ độ khác
            // cũng là 96-DPI. Scale riêng thanh này sẽ làm nó cao lệch so với phần còn lại.
            // Khi phase DPI bật AutoScaleMode.Dpi thì đổi sang S(...) ở đây và bỏ chú thích này.
            Height = ThemeMetrics.NavHeight;
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

        /// <summary>Tên sản phẩm ở góc trái. Nhận diện, KHÔNG phải nút (DESIGN.md §M).</summary>
        public string ProductTitle { get; set; } = "AutoJMS";

        /// <summary>
        /// Tắt để dùng thanh này làm dải tab CON bên trong một trang (ví dụ 4 chế độ in
        /// của tab IN ĐƠN): bỏ icon + tên sản phẩm, chỉ còn các đầu tab. Một thanh chứ
        /// không phải hai lớp gần giống nhau — cùng cách chọn, cùng cách vẽ, cùng
        /// "không giữ trạng thái, Target là nguồn sự thật".
        /// </summary>
        [DefaultValue(true)]
        public bool ShowIdentity
        {
            get => _showIdentity;
            set
            {
                if (_showIdentity == value) return;
                _showIdentity = value;
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

            int left = EdgePaddingX;
            if (_showIdentity)
                left += TextRenderer.MeasureText(g, ProductTitle, ThemeTypography.H2).Width + IdentityGap;

            var widths = new int[ItemCount];
            int total = 0;
            for (int i = 0; i < ItemCount; i++)
            {
                widths[i] = TextRenderer.MeasureText(g, _target.TabPages[i].Text, ThemeTypography.BodyStrong).Width
                          + (ItemPaddingX * 2);
                total += widths[i];
            }

            // Không đủ chỗ thì co đều thay vì để tab cuối tràn ra ngoài mép phải.
            // Bản SunnyUI giấu tab thừa sau cặp mũi tên ‹ › — tức là "In Reverse"
            // biến mất hẳn trên màn hẹp. Co lại thì chữ hụt nhưng tab vẫn bấm được.
            int available = Width - left - EdgePaddingX;
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

            if (_showIdentity) DrawIdentity(g, c);

            int selected = SelectedIndex;
            for (int i = 0; i < _itemRects.Count; i++)
                DrawItem(g, c, i, i == selected);

            // Đường phân cách nav / nội dung. 1px, không đổ bóng (DESIGN.md §I).
            using (var border = new Pen(c.Border))
                g.DrawLine(border, 0, Height - 1, Width, Height - 1);
        }

        private void DrawIdentity(Graphics g, ThemeColors c)
        {
            var icon = FindForm()?.Icon;
            int x = EdgePaddingX;

            if (icon != null)
            {
                int size = ThemeMetrics.IconSizeNav;
                using (var bmp = icon.ToBitmap())
                    g.DrawImage(bmp, new Rectangle(x, (Height - size) / 2, size, size));
                x += size + 8;
            }

            TextRenderer.DrawText(g, ProductTitle, ThemeTypography.H2,
                new Rectangle(x, 0, Width - x, Height), c.Text,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
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

            TextRenderer.DrawText(g, _target.TabPages[index].Text, font, rect, fore,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);

            if (isSelected)
            {
                using (var accent = new SolidBrush(c.Primary))
                    g.FillRectangle(accent,
                        rect.X, Height - ThemeMetrics.TabIndicatorHeight,
                        rect.Width, ThemeMetrics.TabIndicatorHeight);
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

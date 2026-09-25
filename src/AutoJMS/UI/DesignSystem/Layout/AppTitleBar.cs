using System;
using System.Drawing;
using System.Windows.Forms;

namespace AutoJMS.UI.DesignSystem
{
    /// <summary>
    /// Thanh tiêu đề tự vẽ cho cửa sổ bỏ viền (<c>FormBorderStyle.None</c>). Xem
    /// DesignReference/AutoJMS.DESIGN.md §M.
    ///
    /// Trái: icon + <c>Text</c> của cửa sổ — đọc lại mỗi lần vẽ, nên chuỗi "Đang tải cập nhật
    /// 45%" mà Main ghi vào <c>Text</c> vẫn hiện ra như trên thanh tiêu đề cũ của Windows.
    /// Phải: Thu nhỏ và Đóng. KHÔNG có nút phóng to, KHÔNG kéo, KHÔNG nhấp đúp: cửa sổ luôn
    /// phóng to, và giữ trạng thái đó là việc của Form chứ không phải của thanh này.
    ///
    /// Control con thả vào với <c>Dock = Right</c> (nhãn trạng thái mạng) tự nằm sát bên trái
    /// cặp nút, vì <see cref="DisplayRectangle"/> đã chừa chỗ cho hai nút.
    /// </summary>
    public sealed class AppTitleBar : AControl
    {
        private const int CaptionButtonWidth = 46;   // đúng bề rộng nút tiêu đề của Windows 10/11
        private const int MinimizeButton = 0;
        private const int CloseButton = 1;

        private readonly Form _window;
        private Icon _icon;        // icon cửa sổ ở đúng cỡ đang vẽ — tạo một lần, không tạo trong mỗi lần vẽ
        private int _iconSize;
        private bool _active;
        private int _hot = -1;
        private int _pressed = -1;

        public AppTitleBar(Form window)
        {
            _window = window;
            // Qua S(): thanh này do code dựng SAU InitializeComponent, mà lượt auto-scale của
            // Form không nhân control vào cây muộn như vậy (xem TopNavigation).
            Height = S(ThemeMetrics.TitleBarHeight);
            Dock = DockStyle.Top;
            TabStop = false;
            AccessibleRole = AccessibleRole.TitleBar;

            // Thanh là con của chính cửa sổ này nên sống đúng bằng nó — không cần gỡ handler.
            window.TextChanged += (s, e) => Invalidate();
            window.Activated += (s, e) => { _active = true; Invalidate(); };
            window.Deactivate += (s, e) => { _active = false; Invalidate(); };
        }

        private int ButtonWidth => S(CaptionButtonWidth);

        private Rectangle ButtonRect(int button)
            => new Rectangle(Width - (2 - button) * ButtonWidth, 0, ButtonWidth, Height);

        /// <summary>Vùng dock cho control con: trừ cặp nút bên phải và một khoảng Sm trước chúng.</summary>
        public override Rectangle DisplayRectangle
        {
            get
            {
                var r = base.DisplayRectangle;
                r.Width = Math.Max(0, r.Width - 2 * ButtonWidth - S(ThemeSpacing.Sm));
                return r;
            }
        }

        // ---- Vẽ --------------------------------------------------------------

        protected override void OnPaint(PaintEventArgs e)
        {
            var c = Theme;
            var g = e.Graphics;

            using (var back = new SolidBrush(c.Surface))
                g.FillRectangle(back, ClientRectangle);

            int x = S(ThemeSpacing.Md);
            int iconSize = S(ThemeMetrics.IconSizeDefault);
            var icon = WindowIcon(iconSize);
            if (icon != null)
            {
                g.DrawIcon(icon, new Rectangle(x, (Height - iconSize) / 2, iconSize, iconSize));
                x += iconSize + S(ThemeSpacing.Sm);
            }

            // Chữ dừng trước control con đầu tiên bên phải: nhãn nền trong suốt vẽ lại nền của
            // thanh này, nên tiêu đề dài mà không cắt sẽ lộ ra ngay sau nhãn.
            int right = DisplayRectangle.Right;
            foreach (Control child in Controls)
                if (child.Visible) right = Math.Min(right, child.Left);

            TextRenderer.DrawText(g, _window.Text, ThemeTypography.BodyStrong,
                new Rectangle(x, 0, Math.Max(0, right - S(ThemeSpacing.Md) - x), Height),
                _active ? c.Text : c.TextMuted,
                TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine
                | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);

            DrawButton(g, c, MinimizeButton, ASymbols.Minus);
            DrawButton(g, c, CloseButton, ASymbols.X);

            // Đường phân cách thanh tiêu đề / thanh nav, vẽ sau cùng như đáy TopNavigation.
            // 1px, không đổ bóng (DESIGN.md §I).
            using (var border = new Pen(c.Border))
                g.DrawLine(border, 0, Height - 1, Width, Height - 1);
        }

        private void DrawButton(Graphics g, ThemeColors c, int button, int symbol)
        {
            var rect = ButtonRect(button);
            bool hot = _hot == button;
            bool pressed = hot && _pressed == button;

            // Đóng theo biến thể Danger của AButton — nền đỏ chỉ hiện khi rê chuột, như nút đóng
            // của Windows. Thu nhỏ KHÔNG lấy biến thể Ghost: Ghost tính cho nền SurfaceRaised,
            // còn thanh này nền Surface, và SurfaceAlt (hover của Ghost) lại sáng hơn Surface ở
            // theme sáng nên rê chuột không thấy gì. Border/BorderStrong thấy rõ ở cả ba theme.
            Color back = Color.Empty;
            Color fore = c.TextSecondary;
            if (hot && button == CloseButton)
            {
                back = pressed ? ThemeColors.Blend(Color.Black, c.Danger, 12) : c.Danger;
                fore = c.OnPrimary;
            }
            else if (hot)
            {
                back = pressed ? c.BorderStrong : c.Border;
                fore = c.Text;
            }

            if (!back.IsEmpty)
            {
                using (var brush = new SolidBrush(back))
                    g.FillRectangle(brush, rect);
            }

            ASymbols.Draw(g, symbol, S(ThemeMetrics.IconSizeDefault), fore, rect);
        }

        private Icon WindowIcon(int size)
        {
            if (_window.Icon == null) return null;
            if (_icon == null || _iconSize != size)
            {
                _icon?.Dispose();
                _icon = new Icon(_window.Icon, size, size);   // chọn ảnh vừa cỡ trong .ico, không kéo giãn ảnh 32px
                _iconSize = size;
            }
            return _icon;
        }

        // ---- Chuột -----------------------------------------------------------

        private int HitButton(Point location)
        {
            if (ButtonRect(MinimizeButton).Contains(location)) return MinimizeButton;
            if (ButtonRect(CloseButton).Contains(location)) return CloseButton;
            return -1;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);

            int hot = HitButton(e.Location);
            if (hot == _hot) return;

            _hot = hot;
            Invalidate();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (_hot == -1) return;

            _hot = -1;
            Invalidate();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) return;

            _pressed = HitButton(e.Location);
            Invalidate();
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button != MouseButtons.Left) return;

            int button = _pressed;
            _pressed = -1;
            // Xoá hover TRƯỚC khi hành động: Thu nhỏ đưa cửa sổ đi mà không có MouseLeave, còn
            // Đóng mở hộp xác nhận modal — bấm Hủy bỏ quay về thì nút không được kẹt màu đỏ.
            _hot = -1;
            Invalidate();

            // Nhấn một nút rồi thả ở chỗ khác là huỷ, như nút tiêu đề của Windows.
            if (button < 0 || HitButton(e.Location) != button) return;

            // Close() đi cùng đường CloseReason.UserClosing với nút X cũ của Windows, nên
            // FormClosing của cửa sổ (hộp "Đóng ứng dụng") vẫn chạy y như trước.
            if (button == CloseButton) _window.Close();
            else _window.WindowState = FormWindowState.Minimized;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _icon?.Dispose();
                _icon = null;
            }
            base.Dispose(disposing);
        }
    }
}

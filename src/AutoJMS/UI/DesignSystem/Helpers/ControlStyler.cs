using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace AutoJMS.UI.DesignSystem
{
    /// <summary>
    /// Nguyên thuỷ vẽ dùng chung cho mọi control A* tự vẽ.
    ///
    /// Quy tắc hiệu năng: chỉ bật khử răng cưa khi thật sự có góc bo.
    /// Bật AntiAlias cho hình chữ nhật vuông vừa tốn CPU vừa làm nhoè đường kẻ 1px.
    /// </summary>
    public static class ControlStyler
    {
        /// <summary>Cờ vẽ chữ chuẩn: canh giữa dọc, không cắt ký tự, tôn trọng prefix &amp;.</summary>
        public const TextFormatFlags TextCenter =
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
            TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding;

        public const TextFormatFlags TextLeft =
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter |
            TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding;

        public const TextFormatFlags TextRight =
            TextFormatFlags.Right | TextFormatFlags.VerticalCenter |
            TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding;

        /// <summary>
        /// Đặt chế độ vẽ theo bán kính. Gọi đầu OnPaint.
        /// radius == 0 -> giữ nguyên chế độ nhanh nhất.
        /// </summary>
        public static void Prepare(Graphics g, int radius)
        {
            g.SmoothingMode = radius > 0 ? SmoothingMode.AntiAlias : SmoothingMode.None;
            g.PixelOffsetMode = PixelOffsetMode.Half;
        }

        /// <summary>
        /// Đường bao chữ nhật bo góc. Người gọi phải Dispose().
        /// radius &lt;= 0 trả về đường bao chữ nhật vuông.
        /// </summary>
        public static GraphicsPath RoundedRect(Rectangle r, int radius)
        {
            var path = new GraphicsPath();
            if (r.Width <= 0 || r.Height <= 0)
            {
                path.AddRectangle(r);
                return path;
            }

            radius = Math.Min(radius, Math.Min(r.Width, r.Height) / 2);
            if (radius <= 0)
            {
                path.AddRectangle(r);
                return path;
            }

            int d = radius * 2;
            path.AddArc(r.X, r.Y, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        public static void FillSurface(Graphics g, Rectangle r, Color back, int radius)
        {
            if (back.A == 0 || r.Width <= 0 || r.Height <= 0) return;

            using (var brush = new SolidBrush(back))
            {
                if (radius <= 0)
                {
                    g.FillRectangle(brush, r);
                    return;
                }
                using (var path = RoundedRect(r, radius))
                    g.FillPath(brush, path);
            }
        }

        /// <summary>
        /// Vẽ viền NẰM GỌN bên trong <paramref name="r"/>.
        /// GDI+ căn nét bút vào giữa đường bao, nên phải thu hình lại nửa bề dày -
        /// thiếu bước này thì viền bị cắt mất một nửa ở mép control.
        /// </summary>
        public static void DrawBorder(Graphics g, Rectangle r, Color color, int width, int radius)
        {
            if (color.A == 0 || width <= 0) return;

            int half = width / 2;
            var inner = new Rectangle(r.X + half, r.Y + half, r.Width - width, r.Height - width);
            if (inner.Width <= 0 || inner.Height <= 0) return;

            using (var pen = new Pen(color, width))
            {
                pen.Alignment = PenAlignment.Center;
                if (radius <= 0)
                {
                    g.DrawRectangle(pen, inner);
                    return;
                }
                using (var path = RoundedRect(inner, Math.Max(0, radius - half)))
                    g.DrawPath(pen, path);
            }
        }

        /// <summary>
        /// Vòng focus bàn phím hai lớp (DESIGN.md §S): vành ngoài 2px màu Focus,
        /// khe sáng 1px bên trong.
        ///
        /// Khe sáng mới là thứ làm vòng focus nhìn thấy được trên nền accent ĐẶC.
        /// Thiếu nó, focus trên một nút Primary xanh ở theme Light là vô hình.
        /// Nội dung control phải chừa ThemeBorders.FocusInset px.
        /// </summary>
        public static void DrawFocusRing(Graphics g, Rectangle client, ThemeColors c, int radius)
        {
            DrawBorder(g, client, c.Focus, ThemeBorders.Focus, radius);
            DrawBorder(g,
                Rectangle.Inflate(client, -ThemeBorders.Focus, -ThemeBorders.Focus),
                c.SurfaceRaised,
                ThemeBorders.FocusGap,
                Math.Max(0, radius - ThemeBorders.Focus));
        }

        /// <summary>
        /// Bật double buffer cho control không lộ thuộc tính DoubleBuffered.
        /// Dùng cho control của bên thứ ba (SunnyUI, DataGridView); control A*
        /// tự bật bằng SetStyle trong constructor.
        /// </summary>
        public static void EnableDoubleBuffer(Control ctrl)
        {
            if (ctrl == null) return;
            try
            {
                typeof(Control)
                    .GetProperty("DoubleBuffered",
                        System.Reflection.BindingFlags.Instance |
                        System.Reflection.BindingFlags.NonPublic)
                    ?.SetValue(ctrl, true, null);
            }
            catch { }
        }
    }
}

using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using Sunny.UI;

namespace AutoJMS.UI
{
    /// <summary>
    /// Viền vàng-cam "premium" quanh đầu tab con đang chọn, để không nhầm mode đang mở.
    /// <para>
    /// Phải bám vào hàng đợi thông điệp vì không còn lối nào khác:
    /// <see cref="UITabControl"/> là <c>sealed</c> nên không kế thừa để override
    /// <c>OnDrawItem</c> được; nó cũng không gọi <c>base.OnDrawItem</c> nên sự kiện
    /// <c>DrawItem</c> không bao giờ bắn; và nó vẽ đầu tab SAU khi sự kiện <c>Paint</c> đã
    /// chạy xong, nên nét vẽ trong <c>Paint</c> bị chính nó phủ lên (đo bằng
    /// <c>CopyFromScreen</c>: tô đỏ trong Paint vẫn ra pixel xanh). Vẽ sau khi
    /// <c>WM_PAINT</c> hoàn tất là thời điểm sớm nhất mà nét vẽ còn giữ được.
    /// </para>
    /// </summary>
    internal sealed class PremiumTabAccent : NativeWindow
    {
        private const int WM_PAINT = 0x000F;

        /// <summary>Bề dày nét vàng ở 96 DPI; màn scaling cao được nhân lên theo DeviceDpi.</summary>
        private const int BaseBorderWidth = 2;

        private readonly UITabControl _tab;
        private bool _drawFailureLogged;

        private PremiumTabAccent(UITabControl tab)
        {
            _tab = tab;

            if (tab.IsHandleCreated) AssignHandle(tab.Handle);
            tab.HandleCreated += (_, __) => AssignHandle(_tab.Handle);
            tab.HandleDestroyed += (_, __) => ReleaseHandle();

            // Đổi tab: ép vẽ lại cả control để viền vàng của tab cũ bị xoá hẳn, không phụ
            // thuộc vào việc SunnyUI có tự vẽ lại đúng ô đó hay không.
            tab.SelectedIndexChanged += (_, __) =>
            {
                if (!_tab.IsDisposed && _tab.IsHandleCreated) _tab.Invalidate();
            };
        }

        /// <summary>
        /// Gắn viền vào một <see cref="UITabControl"/>. Người gọi phải GIỮ lại giá trị trả về:
        /// thả ra là GC dọn mất đối tượng và viền lặng lẽ biến mất sau lần thu gom đầu tiên.
        /// </summary>
        internal static PremiumTabAccent Attach(UITabControl tab)
        {
            if (tab == null || tab.IsDisposed) return null;
            return new PremiumTabAccent(tab);
        }

        protected override void WndProc(ref Message m)
        {
            base.WndProc(ref m);
            if (m.Msg != WM_PAINT) return;

            // Ném từ WndProc là giết cả app, mà đây chỉ là nét trang trí.
            try
            {
                DrawAccent();
            }
            catch (Exception ex)
            {
                if (_drawFailureLogged) return;
                _drawFailureLogged = true;
                AppLogger.Warning($"Không vẽ được viền tab IN ĐƠN: {ex.Message}");
            }
        }

        private void DrawAccent()
        {
            if (_tab.IsDisposed || !_tab.IsHandleCreated) return;

            int index = _tab.SelectedIndex;
            if (index < 0 || index >= _tab.TabCount) return;

            // SunnyUI vẽ đầu tab từ gốc client, BỎ QUA cái lề mà tab control gốc của Windows
            // chừa ra quanh dải đầu tab; GetTabRect thì lại trả toạ độ CÓ lề đó. Lấy thẳng
            // GetTabRect là viền lệch đúng bằng bề rộng lề: đo trên máy Owner được 2px — mép
            // trái hở 3px xanh, mép phải tràn hẳn ra ngoài ô. GetTabRect(0).Location chính là
            // cái lề ấy, nên trừ đi là khớp, và khớp ở mọi mức scaling vì lề cũng co giãn theo.
            var inlay = _tab.GetTabRect(0).Location;
            var painted = _tab.GetTabRect(index);
            painted.Offset(-inlay.X, -inlay.Y);

            // Nét và khoảng lùi tính theo DPI: 2px cố định ở màn 150%/200% mảnh như sợi chỉ.
            double dpiScale = _tab.DeviceDpi / 96.0;
            int thickness = Math.Max(
                BaseBorderWidth,
                (int)Math.Round(BaseBorderWidth * dpiScale, MidpointRounding.AwayFromZero));
            int inset = Math.Max(1, (int)Math.Round(dpiScale, MidpointRounding.AwayFromZero));

            // Lùi vào để nét nằm gọn trong ô tab, không liếm sang ô bên cạnh.
            var border = Rectangle.Inflate(painted, -inset, -inset);

            // Trang là một HWND con nên nó vẽ SAU control cha — cạnh dưới rơi xuống vùng trang
            // là mất trắng. Kẹp lại để hình chữ nhật khép kín hẳn trong dải đầu tab.
            border.Height = Math.Min(border.Height, _tab.DisplayRectangle.Top - 1 - border.Y);

            if (border.Width <= thickness * 2 || border.Height <= thickness * 2) return;

            var palette = Palette();

            using var g = Graphics.FromHwnd(_tab.Handle);

            using (var gradient = new LinearGradientBrush(
                       border, palette.Top, palette.Bottom, LinearGradientMode.Vertical))
            using (var pen = new Pen(gradient, thickness) { Alignment = PenAlignment.Inset })
            {
                g.DrawRectangle(pen, border);
            }

            // Gờ sáng 1px nằm sát phía trong: chính nó tạo cảm giác dập nổi của viền kim loại.
            var bevel = Rectangle.Inflate(border, -thickness, -thickness);
            if (bevel.Width <= 0 || bevel.Height <= 0) return;

            using var bevelPen = new Pen(palette.Bevel, 1f);
            g.DrawRectangle(bevelPen, bevel);
        }

        /// <summary>
        /// Vàng ánh kim: sáng ở trên, trầm ở dưới. Nền tối dùng tông trầm hơn hẳn — vàng
        /// rực trên nền tối bị loé và trông rẻ, chứ không "premium".
        /// </summary>
        private static (Color Top, Color Bottom, Color Bevel) Palette()
        {
            switch (AppTheme.CurrentTheme)
            {
                case ThemeMode.Dark:
                    return (Color.FromArgb(0xF2, 0xC2, 0x6B),
                            Color.FromArgb(0x9C, 0x6B, 0x12),
                            Color.FromArgb(70, 255, 240, 205));

                case ThemeMode.Red:
                    return (Color.FromArgb(0xF7, 0xC9, 0x6E),
                            Color.FromArgb(0xB8, 0x73, 0x0F),
                            Color.FromArgb(90, 255, 255, 255));

                default: // Light — tông cam vàng Owner chốt
                    return (Color.FromArgb(0xFF, 0xD2, 0x7A),
                            Color.FromArgb(0xC9, 0x86, 0x1B),
                            Color.FromArgb(120, 255, 255, 255));
            }
        }
    }
}

using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace AutoJMS.UI.DesignSystem
{
    /// <summary>
    /// Icon dạng chữ cho A*. Xem DesignReference/AutoJMS.DESIGN.md §H.
    ///
    /// Dùng "Segoe MDL2 Assets", font có sẵn trong Windows 10/11, nên không thêm tệp
    /// font nào vào bản cài. Máy thiếu font thì Windows thay bằng font mặc định và vẽ ra
    /// ô vuông — chữ trên nút vẫn đọc được, không có ngoại lệ nào bị ném.
    ///
    /// Mã ở đây là codepoint MDL2, KHÔNG phải mã FontAwesome. Đặt thẳng số của bộ icon
    /// khác vào <c>AButton.Symbol</c> sẽ ra hình khác — luôn dùng hằng trong lớp này.
    /// </summary>
    public static class ASymbols
    {
        public const int None = 0;

        public const int Home = 0xE80F;
        public const int Back = 0xE72B;
        public const int Forward = 0xE72A;
        public const int Refresh = 0xE72C;
        public const int Search = 0xE721;
        public const int Download = 0xE896;
        public const int Upload = 0xE898;
        public const int Export = 0xEDE1;
        public const int Page = 0xE7C3;
        public const int Copy = 0xE8C8;
        public const int Settings = 0xE713;
        public const int More = 0xE712;
        public const int Calendar = 0xE787;
        public const int Send = 0xE724;
        public const int View = 0xE890;
        public const int Hide = 0xED1A;
        public const int Warning = 0xE7BA;
        public const int Inbox = 0xE8A8;
        public const int Print = 0xE749;

        private const string IconFamily = "Segoe MDL2 Assets";

        // Một Font cho mỗi cỡ, dùng lại suốt phiên. Tạo font trong OnPaint thì mỗi lần
        // vẽ lại xin một handle GDI mới — trên máy yếu (mục tiêu của bản thiết kế này)
        // đó là thứ làm cuộn bảng bị khựng.
        private static readonly Dictionary<int, Font> Fonts = new Dictionary<int, Font>();

        private static Font FontFor(int size)
        {
            lock (Fonts)
            {
                if (!Fonts.TryGetValue(size, out var font))
                {
                    font = new Font(IconFamily, size, GraphicsUnit.Pixel);
                    Fonts[size] = font;
                }
                return font;
            }
        }

        /// <summary>Vẽ icon căn giữa trong <paramref name="bounds"/>. symbol = 0 thì không vẽ gì.</summary>
        public static void Draw(Graphics g, int symbol, int size, Color color, Rectangle bounds)
        {
            if (symbol == None || size <= 0) return;

            TextRenderer.DrawText(g, char.ConvertFromUtf32(symbol), FontFor(size), bounds, color,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        }
    }
}

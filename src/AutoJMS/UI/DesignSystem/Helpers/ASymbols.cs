using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Text;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace AutoJMS.UI.DesignSystem
{
    /// <summary>
    /// Icon dạng chữ cho A*. Xem DesignReference/AutoJMS.DESIGN.md §J
    /// và .agent/rules/11-icon-and-animation-rules.md.
    ///
    /// Dùng "lucide" — font vector nhúng sẵn trong Assembly, nạp qua
    /// <see cref="PrivateFontCollection"/> từ embedded resource lúc khởi động.
    /// Không phụ thuộc vào font Windows trên máy người dùng, hiển thị sắc nét
    /// ở mọi DPI (100%–200%), hoạt động 100% offline.
    ///
    /// Fallback: nếu embedded resource không tải được (ví dụ assembly bị strip),
    /// hệ thống rơi về font "Segoe MDL2 Assets" của Windows 10/11 để nút vẫn vẽ
    /// được thay vì ném lỗi — nhưng các codepoint dưới đây là của Lucide, không
    /// trùng bảng mã MDL2, nên glyph sẽ SAI (hoặc ra ô vuông). Đây là đường thoát
    /// hiểm, không phải chế độ hỗ trợ: thấy icon lạ trên diện rộng thì nghi
    /// lucide.ttf không còn trong assembly trước khi nghi từng mã một.
    ///
    /// Mã ở đây là codepoint Lucide, KHÔNG phải mã FontAwesome hay MDL2.
    /// Đặt thẳng số của bộ icon khác vào <c>AButton.Symbol</c> sẽ ra hình khác
    /// — luôn dùng hằng trong lớp này.
    /// </summary>
    public static class ASymbols
    {
        public const int None = 0;

        // ──────────────────────────────────────────────────
        //  Lucide Icons — codepoints từ lucide-static font
        //  Tên theo PascalCase khớp với lucide.dev/icons
        //  Tra cứu: https://lucide.dev/icons/
        // ──────────────────────────────────────────────────

        // Navigation & Actions
        public const int Home         = 0xE0F5;
        public const int Back         = 0xE048; // arrow-left
        public const int Forward      = 0xE049; // arrow-right
        public const int Refresh      = 0xE145; // refresh-cw
        public const int Search       = 0xE151;
        public const int Download     = 0xE0B2;
        public const int Upload       = 0xE19E;
        public const int Export       = 0xE0B9; // external-link
        public const int Share        = 0xE155;
        public const int Send         = 0xE152;

        // Content & Editing
        public const int Copy         = 0xE09E;
        public const int Check        = 0xE06C;
        public const int Plus         = 0xE13D;
        public const int Minus        = 0xE11C;
        public const int Filter       = 0xE0DC;
        public const int X            = 0xE1B2;
        public const int Trash        = 0xE18E; // trash-2

        // UI & Layout
        public const int Settings     = 0xE154;
        public const int More         = 0xE0B6; // ellipsis
        public const int Menu         = 0xE115;
        public const int Calendar     = 0xE063;
        public const int Clock        = 0xE087; // clock-4 — bí danh của "clock" trong lucide.ttf
        public const int Inbox        = 0xE0F7;
        public const int Page         = 0xE129; // package

        // Status & Feedback
        public const int View         = 0xE0BA; // eye
        public const int Hide         = 0xE0BB; // eye-off
        public const int Warning      = 0xE193; // triangle-alert
        public const int Print        = 0xE141; // printer
        public const int TrendingUp   = 0xE191;
        public const int TrendingDown = 0xE190;

        // Expand / Collapse
        public const int ChevronDown  = 0xE06D;
        public const int ChevronUp    = 0xE070;
        public const int ChevronLeft  = 0xE06E;
        public const int ChevronRight = 0xE06F;

        // Media
        public const int Play         = 0xE13C;
        public const int Pause        = 0xE12E;

        // ──────────────────────────────────────────────────
        //  Font loading
        // ──────────────────────────────────────────────────

        private const string LucideFontResourceName = "AutoJMS.Resources.Fonts.lucide.ttf";
        private const string FallbackFontFamily = "Segoe MDL2 Assets";

        private static readonly PrivateFontCollection _privateCollection = new PrivateFontCollection();
        private static readonly string _iconFontFamily;

        // AddMemoryFont không copy: PrivateFontCollection đọc thẳng vùng nhớ này suốt
        // vòng đời của nó. Giữ mảng ở field static và KHÔNG gọi handle.Free() —
        // unpin rồi để biến cục bộ ra khỏi phạm vi là mở đường cho GC dọn/di chuyển
        // vùng nhớ mà font vẫn đang trỏ tới (glyph rác hoặc AccessViolation ngẫu
        // nhiên, thường chỉ lộ ra khi máy chịu áp lực bộ nhớ). Lớp static này sống
        // hết tiến trình nên ghim vĩnh viễn là đúng, không phải rò rỉ.
        private static byte[] _fontData;
        private static GCHandle _fontHandle;

        // PrivateFontCollection chỉ tồn tại với GDI+. Draw() lại vẽ bằng
        // TextRenderer, tức GDI — GDI tra tên family "lucide" trong bảng font hệ
        // thống, không thấy, rồi ÂM THẦM thay bằng font mặc định. Font mặc định
        // không có glyph nào trong Private Use Area, nên mọi icon ra ô vuông
        // trong khi IsEmbeddedFontLoaded vẫn true và build vẫn 0 Warning.
        // AddFontMemResourceEx đăng ký đúng buffer đó cho GDI, phạm vi tiến trình.
        // Không gọi RemoveFontMemResourceEx: lớp static này sống hết tiến trình.
        [DllImport("gdi32.dll", ExactSpelling = true)]
        private static extern IntPtr AddFontMemResourceEx(IntPtr pbFont, uint cbFont, IntPtr pdv, out uint pcFonts);

        static ASymbols()
        {
            _iconFontFamily = LoadLucideFont() ?? FallbackFontFamily;
        }

        /// <summary>
        /// Font family thật đang vẽ icon. Bằng <c>"Segoe MDL2 Assets"</c> nghĩa là
        /// lucide.ttf KHÔNG nạp được và mọi icon đang ra glyph sai — đây là chỗ duy
        /// nhất quan sát được việc đó, vì <see cref="Draw"/> vẫn vẽ bình thường.
        /// </summary>
        public static string IconFontFamily => _iconFontFamily;

        /// <summary>true khi đang dùng font Lucide nhúng (đường chạy đúng).</summary>
        public static bool IsEmbeddedFontLoaded => _iconFontFamily != FallbackFontFamily;

        /// <summary>
        /// Nạp lucide.ttf từ embedded resource vào PrivateFontCollection.
        /// Trả về tên font family nếu thành công, null nếu thất bại.
        /// </summary>
        private static string LoadLucideFont()
        {
            try
            {
                var assembly = Assembly.GetExecutingAssembly();
                using var stream = assembly.GetManifestResourceStream(LucideFontResourceName);
                if (stream == null) return null;

                // ReadExactly, không phải Read: Read được phép trả về ít hơn số byte
                // yêu cầu, và bỏ qua giá trị trả về là cách nạp một font cụt.
                _fontData = new byte[stream.Length];
                stream.ReadExactly(_fontData, 0, _fontData.Length);

                _fontHandle = GCHandle.Alloc(_fontData, GCHandleType.Pinned);
                var ptr = _fontHandle.AddrOfPinnedObject();

                _privateCollection.AddMemoryFont(ptr, _fontData.Length);          // GDI+
                AddFontMemResourceEx(ptr, (uint)_fontData.Length, IntPtr.Zero, out _); // GDI

                return _privateCollection.Families.Length > 0
                    ? _privateCollection.Families[0].Name
                    : null;
            }
            catch
            {
                return null;
            }
        }

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
                    // Ưu tiên PrivateFontCollection (Lucide font nhúng)
                    if (_privateCollection.Families.Length > 0)
                    {
                        font = new Font(_privateCollection.Families[0], size, FontStyle.Regular, GraphicsUnit.Pixel);
                    }
                    else
                    {
                        // Fallback: Segoe MDL2 Assets
                        font = new Font(_iconFontFamily, size, GraphicsUnit.Pixel);
                    }
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

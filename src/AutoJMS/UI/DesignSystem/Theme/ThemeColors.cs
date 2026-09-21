using System.Drawing;

namespace AutoJMS.UI.DesignSystem
{
    /// <summary>
    /// Toàn bộ từ vựng màu của AutoJMS. Không có màu nào ngoài lớp này.
    /// Xem DesignReference/AutoJMS.DESIGN.md §B.
    ///
    /// Giá trị khớp 1:1 với AppTheme.LightColors/RedColors/DarkColors đang chạy,
    /// trừ Warning ở Light/Red (#F59E0B -> #D97706) vì giá trị cũ chỉ đạt 2,2:1
    /// trên nền trắng, dưới ngưỡng WCAG AA 4,5:1.
    /// </summary>
    public sealed class ThemeColors
    {
        public ThemeMode Mode { get; private set; }

        // Accent - đúng MỘT màu bão hoà cho cả app
        public Color Primary { get; private set; }
        public Color PrimaryHover { get; private set; }
        public Color PrimaryPressed { get; private set; }
        public Color PrimaryDisabled { get; private set; }
        public Color PrimaryTint { get; private set; }
        public Color OnPrimary { get; private set; }

        // Bề mặt
        public Color Surface { get; private set; }
        public Color SurfaceAlt { get; private set; }
        public Color SurfaceRaised { get; private set; }

        // Viền
        public Color Border { get; private set; }
        public Color BorderStrong { get; private set; }

        // Chữ
        public Color Text { get; private set; }
        public Color TextSecondary { get; private set; }
        public Color TextMuted { get; private set; }

        // Trạng thái
        public Color Success { get; private set; }
        public Color Warning { get; private set; }
        public Color Danger { get; private set; }
        public Color Info { get; private set; }

        // Focus bàn phím - cố ý KHÁC Primary, xem AutoJMS.DESIGN.md §S
        public Color Focus { get; private set; }

        // Lớp phủ sau modal (có alpha)
        public Color Scrim { get; private set; }

        public static readonly ThemeColors Light = new ThemeColors
        {
            Mode = ThemeMode.Light,

            Primary = ColorTranslator.FromHtml("#3B82F6"),
            PrimaryHover = ColorTranslator.FromHtml("#60A5FA"),
            PrimaryPressed = ColorTranslator.FromHtml("#2563EB"),
            PrimaryDisabled = ColorTranslator.FromHtml("#BFDBFE"),
            PrimaryTint = ColorTranslator.FromHtml("#EFF6FF"),
            OnPrimary = ColorTranslator.FromHtml("#FFFFFF"),

            Surface = ColorTranslator.FromHtml("#F5F7FA"),
            SurfaceAlt = ColorTranslator.FromHtml("#F9FAFB"),
            SurfaceRaised = ColorTranslator.FromHtml("#FFFFFF"),

            Border = ColorTranslator.FromHtml("#E5E7EB"),
            BorderStrong = ColorTranslator.FromHtml("#D1D5DB"),

            Text = ColorTranslator.FromHtml("#1F2937"),
            TextSecondary = ColorTranslator.FromHtml("#6B7280"),
            TextMuted = ColorTranslator.FromHtml("#9CA3AF"),

            Success = ColorTranslator.FromHtml("#16A34A"),
            Warning = ColorTranslator.FromHtml("#D97706"),
            Danger = ColorTranslator.FromHtml("#DC2626"),
            Info = ColorTranslator.FromHtml("#2563EB"),

            Focus = ColorTranslator.FromHtml("#111827"),
            Scrim = Color.FromArgb(0x66, 0x00, 0x00, 0x00)
        };

        public static readonly ThemeColors Red = new ThemeColors
        {
            Mode = ThemeMode.Red,

            Primary = ColorTranslator.FromHtml("#E53935"),
            PrimaryHover = ColorTranslator.FromHtml("#EF5350"),
            PrimaryPressed = ColorTranslator.FromHtml("#C62828"),
            PrimaryDisabled = ColorTranslator.FromHtml("#F5B7B5"),
            PrimaryTint = ColorTranslator.FromHtml("#FFEBEE"),
            OnPrimary = ColorTranslator.FromHtml("#FFFFFF"),

            Surface = ColorTranslator.FromHtml("#F5F7FA"),
            SurfaceAlt = ColorTranslator.FromHtml("#F9FAFB"),
            SurfaceRaised = ColorTranslator.FromHtml("#FFFFFF"),

            Border = ColorTranslator.FromHtml("#E5E7EB"),
            BorderStrong = ColorTranslator.FromHtml("#D1D5DB"),

            Text = ColorTranslator.FromHtml("#1F2937"),
            TextSecondary = ColorTranslator.FromHtml("#6B7280"),
            TextMuted = ColorTranslator.FromHtml("#9CA3AF"),

            Success = ColorTranslator.FromHtml("#16A34A"),
            Warning = ColorTranslator.FromHtml("#D97706"),
            Danger = ColorTranslator.FromHtml("#DC2626"),
            Info = ColorTranslator.FromHtml("#2563EB"),

            Focus = ColorTranslator.FromHtml("#111827"),
            Scrim = Color.FromArgb(0x66, 0x00, 0x00, 0x00)
        };

        public static readonly ThemeColors Dark = new ThemeColors
        {
            Mode = ThemeMode.Dark,

            Primary = ColorTranslator.FromHtml("#E53935"),
            PrimaryHover = ColorTranslator.FromHtml("#EF5350"),
            PrimaryPressed = ColorTranslator.FromHtml("#B71C1C"),
            PrimaryDisabled = ColorTranslator.FromHtml("#5A2422"),
            PrimaryTint = ColorTranslator.FromHtml("#2A1414"),
            OnPrimary = ColorTranslator.FromHtml("#FFFFFF"),

            Surface = ColorTranslator.FromHtml("#101012"),
            SurfaceAlt = ColorTranslator.FromHtml("#131316"),
            SurfaceRaised = ColorTranslator.FromHtml("#18181B"),

            Border = ColorTranslator.FromHtml("#27272A"),
            BorderStrong = ColorTranslator.FromHtml("#3F3F46"),

            Text = ColorTranslator.FromHtml("#E4E4E7"),
            TextSecondary = ColorTranslator.FromHtml("#A1A1AA"),
            TextMuted = ColorTranslator.FromHtml("#71717A"),

            Success = ColorTranslator.FromHtml("#22C55E"),
            Warning = ColorTranslator.FromHtml("#F59E0B"),
            Danger = ColorTranslator.FromHtml("#EF4444"),
            Info = ColorTranslator.FromHtml("#3B82F6"),

            Focus = ColorTranslator.FromHtml("#FAFAFA"),
            Scrim = Color.FromArgb(0x99, 0x00, 0x00, 0x00)
        };

        /// <summary>
        /// Trộn <paramref name="fore"/> lên <paramref name="back"/> theo tỉ lệ phần trăm,
        /// trả về màu ĐỤC.
        ///
        /// Dùng cho nền badge ("Success @ 12%", DESIGN.md §C). Không dùng
        /// Color.FromArgb(alpha, c) cho BackColor: WinForms bỏ qua alpha của BackColor
        /// nên control sẽ ra màu đặc 100% chứ không phải màu nhạt như mong đợi.
        /// </summary>
        public static Color Blend(Color fore, Color back, int percent)
        {
            if (percent <= 0) return back;
            if (percent >= 100) return fore;

            double f = percent / 100.0;
            return Color.FromArgb(
                (int)(fore.R * f + back.R * (1 - f)),
                (int)(fore.G * f + back.G * (1 - f)),
                (int)(fore.B * f + back.B * (1 - f)));
        }

        /// <summary>Nền badge trạng thái: màu trạng thái 12% trên SurfaceRaised.</summary>
        public Color StatusTint(Color status) => Blend(status, SurfaceRaised, 12);
    }
}

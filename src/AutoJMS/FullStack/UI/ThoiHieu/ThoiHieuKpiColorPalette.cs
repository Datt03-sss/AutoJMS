using System.Drawing;

namespace AutoJMS.FullStack.UI.ThoiHieu
{
    /// <summary>
    /// Bảng màu của sheet. Light là bảng gốc kiểu Excel và là bảng duy nhất của ảnh xuất
    /// (ThoiHieuKpiImageExporter tự tạo renderer mới, mặc định Light). Dark chỉ dùng khi
    /// vẽ lên màn hình ở theme Dark: giữ nghĩa từng màu (xanh/vàng/đỏ) nhưng hạ độ sáng.
    /// </summary>
    public sealed class ThoiHieuKpiColorPalette
    {
        public static readonly ThoiHieuKpiColorPalette Light = new();

        public static readonly ThoiHieuKpiColorPalette Dark = new()
        {
            Canvas = Color.FromArgb(16, 16, 18),
            TitleGreen = Color.FromArgb(56, 87, 35),
            HeaderGreen = Color.FromArgb(42, 58, 36),
            Yellow = Color.FromArgb(74, 64, 0),
            TotalRed = Color.FromArgb(127, 29, 29),
            DangerRed = Color.FromArgb(99, 36, 38),
            LightBlue = Color.FromArgb(30, 58, 95),
            HourHighBlue = Color.FromArgb(37, 78, 128),
            HourLowBlue = Color.FromArgb(28, 35, 48),
            KpiBodyYellow = Color.FromArgb(42, 40, 20),
            LightGray = Color.FromArgb(39, 39, 42),
            CellBack = Color.FromArgb(24, 24, 27),
            Border = Color.FromArgb(82, 82, 91),
            ThinBorder = Color.FromArgb(63, 63, 70),
            Text = Color.FromArgb(228, 228, 231),
            RedText = Color.FromArgb(255, 150, 150),
            TotalHighlightText = Color.FromArgb(253, 224, 71),
            RateGreen = Color.FromArgb(31, 92, 51),
            RateLight = Color.FromArgb(30, 46, 32),
            RateLow = Color.FromArgb(74, 31, 37),
            GlyphBack = Color.FromArgb(39, 39, 42),
            GlyphBorder = Color.FromArgb(82, 82, 91),
            GlyphFill = Color.FromArgb(161, 161, 170),
        };

        public Color Canvas { get; private init; } = Color.White;
        public Color TitleGreen { get; private init; } = Color.FromArgb(146, 208, 80);
        public Color TitleText { get; private init; } = Color.White;
        public Color HeaderGreen { get; private init; } = Color.FromArgb(198, 224, 180);
        public Color Yellow { get; private init; } = Color.FromArgb(255, 255, 0);
        public Color TotalRed { get; private init; } = Color.FromArgb(255, 0, 0);
        public Color DangerRed { get; private init; } = Color.FromArgb(248, 105, 107);
        public Color LightBlue { get; private init; } = Color.FromArgb(189, 215, 238);
        public Color HourHighBlue { get; private init; } = Color.FromArgb(142, 180, 227);
        public Color HourLowBlue { get; private init; } = Color.FromArgb(235, 242, 252);
        public Color KpiBodyYellow { get; private init; } = Color.FromArgb(255, 255, 210);
        public Color LightGray { get; private init; } = Color.FromArgb(217, 217, 217);
        public Color CellBack { get; private init; } = Color.White;

        public Color Border { get; private init; } = Color.FromArgb(64, 64, 64);
        public Color ThinBorder { get; private init; } = Color.FromArgb(165, 165, 165);
        public Color Text { get; private init; } = Color.FromArgb(20, 20, 20);
        public Color RedText { get; private init; } = Color.FromArgb(192, 0, 0);
        public Color TotalText { get; private init; } = Color.White;
        public Color TotalHighlightText { get; private init; } = Color.Yellow;
        public Color RateGreen { get; private init; } = Color.FromArgb(99, 190, 123);
        public Color RateLight { get; private init; } = Color.FromArgb(214, 236, 210);
        public Color RateLow { get; private init; } = Color.FromArgb(255, 199, 206);

        public Color GlyphBack { get; private init; } = Color.FromArgb(242, 242, 242);
        public Color GlyphBorder { get; private init; } = Color.FromArgb(190, 190, 190);
        public Color GlyphFill { get; private init; } = Color.FromArgb(90, 90, 90);
    }
}

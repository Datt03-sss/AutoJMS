using System.Drawing;

namespace AutoJMS.UI.DesignSystem
{
    /// <summary>
    /// Mô hình độ nổi. Xem DesignReference/AutoJMS.DESIGN.md §I.
    ///
    /// AutoJMS KHÔNG dùng đổ bóng - kể cả bóng nhẹ, kể cả một chỗ.
    /// Đổ bóng thật trong WinForms cần WS_EX_LAYERED hoặc vẽ alpha thủ công mỗi WM_PAINT;
    /// cả hai đều buộc composite trên CPU cho mọi lần vẽ lại, và đó là nguồn nháy hình
    /// khi cuộn bảng trên máy cấu hình thấp - đúng đối tượng người dùng AutoJMS.
    ///
    /// Phân tầng làm bằng nền + viền. Tầng modal dùng scrim, không dùng bóng.
    /// Lớp này cố ý không có API vẽ bóng nào.
    /// </summary>
    public static class ThemeShadows
    {
        public enum Elevation
        {
            /// <summary>Nền trang. Surface, không viền.</summary>
            Background = 0,

            /// <summary>Card, panel, ô nhập. SurfaceRaised + Hairline. Chiếm đa số.</summary>
            Flat = 1,

            /// <summary>Card hover/chọn, popover. SurfaceRaised + BorderStrong.</summary>
            Raised = 2,

            /// <summary>Dialog. SurfaceRaised + Hairline, nền cha phủ Scrim.</summary>
            Modal = 3
        }

        public static Color BackColorFor(Elevation level, ThemeColors c)
            => level == Elevation.Background ? c.Surface : c.SurfaceRaised;

        /// <summary>Color.Empty nghĩa là không vẽ viền.</summary>
        public static Color BorderColorFor(Elevation level, ThemeColors c)
        {
            switch (level)
            {
                case Elevation.Raised: return c.BorderStrong;
                case Elevation.Flat:
                case Elevation.Modal: return c.Border;
                default: return Color.Empty;
            }
        }
    }
}

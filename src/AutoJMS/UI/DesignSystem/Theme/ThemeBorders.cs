namespace AutoJMS.UI.DesignSystem
{
    /// <summary>
    /// Độ dày viền. Xem DesignReference/AutoJMS.DESIGN.md §H.
    ///
    /// Viền là công cụ phân tầng CHÍNH của AutoJMS (thay cho đổ bóng - xem ThemeShadows).
    /// Chỉ có 1px và 2px. Viền 3px+ trông như lỗi hiển thị ở 100% DPI.
    ///
    /// Không cache Pen ở đây: Pen là đối tượng GDI có trạng thái, dùng chung giữa
    /// các luồng vẽ sẽ hỏng, và Pen tạo trong OnPaint rồi using-dispose là đủ rẻ.
    /// </summary>
    public static class ThemeBorders
    {
        /// <summary>1px - viền card, divider, đường kẻ bảng. Màu: ThemeColors.Border.</summary>
        public const int Hairline = 1;

        /// <summary>1px - viền ô nhập và control tương tác. Màu: ThemeColors.BorderStrong.</summary>
        public const int Control = 1;

        /// <summary>2px - vòng focus bàn phím. Màu: ThemeColors.Focus.</summary>
        public const int Focus = 2;

        /// <summary>1px khe sáng giữa vòng focus và control (DESIGN.md §S).</summary>
        public const int FocusGap = 1;

        /// <summary>Tổng bề dày vòng focus - phần nội dung phải chừa ra ngần này.</summary>
        public const int FocusInset = Focus + FocusGap;
    }
}

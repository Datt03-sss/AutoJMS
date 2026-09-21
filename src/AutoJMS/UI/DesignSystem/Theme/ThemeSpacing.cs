namespace AutoJMS.UI.DesignSystem
{
    /// <summary>
    /// Thang khoảng cách, cơ sở 4px. Xem DesignReference/AutoJMS.DESIGN.md §F.
    ///
    /// Giá trị ở 96 DPI. Đưa qua DpiHelper.Scale() trước khi dùng làm toạ độ/kích thước.
    ///
    /// Nhịp hai tốc độ: chrome (nav, card, dialog) thở ở Md-Xl;
    /// bề mặt dữ liệu (ô bảng, hàng danh sách) chật ở Xs-Sm.
    /// </summary>
    public static class ThemeSpacing
    {
        /// <summary>4px - nhãn ↔ control của nó, padding dọc ô bảng.</summary>
        public const int Xs = 4;

        /// <summary>8px - giữa control cùng nhóm, padding ngang ô bảng.</summary>
        public const int Sm = 8;

        /// <summary>12px - padding trong card, giữa các nhóm control.</summary>
        public const int Md = 12;

        /// <summary>16px - padding panel, lề nội dung trang.</summary>
        public const int Lg = 16;

        /// <summary>24px - giữa các section lớn.</summary>
        public const int Xl = 24;

        /// <summary>32px - padding dialog.</summary>
        public const int Xxl = 32;

        // Bề mặt dữ liệu - nhịp chật
        public const int CellPadX = Sm;
        public const int CellPadY = Xs;
    }
}

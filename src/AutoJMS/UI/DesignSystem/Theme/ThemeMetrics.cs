namespace AutoJMS.UI.DesignSystem
{
    /// <summary>
    /// Kích thước chuẩn của phần tử. Xem DesignReference/AutoJMS.DESIGN.md §F, §Q, §T.
    /// Giá trị ở 96 DPI - LUÔN đưa qua DpiHelper.Scale() trước khi gán.
    /// </summary>
    public static class ThemeMetrics
    {
        // Chrome
        public const int TitleBarHeight = 32;
        public const int NavHeight = 40;
        public const int ToolbarHeight = 32;
        public const int SubTabHeight = 30;

        // Control tương tác
        public const int ControlHeight = 28;
        public const int BadgeHeight = 18;

        /// <summary>Vùng bấm nhỏ nhất (DESIGN.md §Y). Không control tương tác nào nhỏ hơn.</summary>
        public const int MinTouchTarget = 24;

        // Bảng - chiều cao hàng CỐ ĐỊNH, xem DESIGN.md §Q
        public const int GridRowHeight = 26;
        public const int GridHeaderHeight = 30;

        /// <summary>Số dòng vượt ngưỡng này thì grid phải bật VirtualMode.</summary>
        public const int GridVirtualModeThreshold = 5000;

        /// <summary>Quá 8 cột mới được kẻ đường dọc trong grid.</summary>
        public const int GridVerticalLineColumnThreshold = 8;

        // Dialog - ba bề rộng, không tự do
        public const int DialogWidthCompact = 360;
        public const int DialogWidthDefault = 480;
        public const int DialogWidthWide = 640;

        // Icon — thang 5 bậc, không có cỡ nào ngoài năm giá trị này
        // (.agent/rules/11-icon-and-animation-rules.md §2). Truyền qua DpiHelper.Scale.
        /// <summary>Nhãn/tag, badge đếm số.</summary>
        public const int IconSizeTag = 12;
        public const int IconSizeDense = 14;
        public const int IconSizeDefault = 16;
        public const int IconSizeNav = 20;
        /// <summary>Icon dẫn dắt của trạng thái rỗng, dialog xác nhận.</summary>
        public const int IconSizeHero = 24;

        /// <summary>Thanh tiến trình mảnh ở đỉnh vùng đang tải (DESIGN.md §V).</summary>
        public const int LoadingBarHeight = 2;

        /// <summary>Gạch dưới tab đang chọn (DESIGN.md §M, §N).</summary>
        public const int TabIndicatorHeight = 2;
    }
}

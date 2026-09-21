using System;

namespace AutoJMS.UI.DesignSystem
{
    /// <summary>
    /// Điểm vào duy nhất để đọc theme hiện tại và đổi theme.
    ///
    /// CỐ Ý không giữ state riêng: <see cref="Mode"/> uỷ quyền thẳng cho
    /// <see cref="AppTheme.CurrentTheme"/>. AppTheme mới là nơi Settings đang đọc/ghi;
    /// nếu DesignSystem giữ một bản sao thì hai bên sẽ lệch nhau ngay lần đầu người dùng
    /// đổi theme từ màn hình cũ. Khi AppTheme cũ được gỡ hẳn, chuyển field về đây.
    /// </summary>
    public static class ThemeManager
    {
        /// <summary>Bắn sau khi theme đổi. Control tự vẽ nên Invalidate() trong handler.</summary>
        public static event EventHandler ThemeChanged;

        public static ThemeMode Mode
        {
            get => AppTheme.CurrentTheme;
            set
            {
                if (AppTheme.CurrentTheme == value) return;
                AppTheme.CurrentTheme = value;
                ThemeChanged?.Invoke(null, EventArgs.Empty);
            }
        }

        public static ThemeColors Current
        {
            get
            {
                switch (AppTheme.CurrentTheme)
                {
                    case ThemeMode.Red: return ThemeColors.Red;
                    case ThemeMode.Dark: return ThemeColors.Dark;
                    default: return ThemeColors.Light;
                }
            }
        }

        public static bool IsDark => AppTheme.CurrentTheme == ThemeMode.Dark;

        /// <summary>
        /// Báo cho DesignSystem biết theme đã bị đổi qua đường cũ
        /// (code gán thẳng AppTheme.CurrentTheme). Gọi sau khi gán để control tự vẽ
        /// kịp repaint.
        /// </summary>
        public static void NotifyChanged() => ThemeChanged?.Invoke(null, EventArgs.Empty);
    }
}

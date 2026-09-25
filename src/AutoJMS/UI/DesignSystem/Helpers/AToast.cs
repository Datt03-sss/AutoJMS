using System.Windows.Forms;

namespace AutoJMS.UI.DesignSystem
{
    /// <summary>
    /// Báo ngắn KHÔNG chặn thao tác.
    ///
    /// Dựng trên <see cref="ToolTip"/> của WinForms chứ không phải một Form tự vẽ:
    /// ToolTip đã là cửa sổ topmost tự tắt sau thời gian đặt trước, không nhận focus
    /// và không bơm message loop — đúng ba tính chất khiến UIMessageTip được chọn
    /// ban đầu. Đổi sang <see cref="AMessageDialog"/> ở đây là đổi hành vi: modal sẽ
    /// chặn luồng UI, đúng thứ <c>GoogleSheetService.ShowSheetToast</c> cố tránh.
    ///
    /// Tip mang màu hệ thống, không lấy token theme. Cố tình: đây là cửa sổ native
    /// của Windows, vẽ lại nó bằng OwnerDraw chỉ để đúng bảng màu là việc không ai
    /// yêu cầu, cho một thứ hiện 3 giây.
    /// </summary>
    public static class AToast
    {
        private const int DurationMs = 3000;

        // Một ToolTip dùng chung cho cả app: mỗi instance là một cửa sổ native, tạo
        // mới ở mỗi lần báo thì rò handle vì không có chỗ nào Dispose.
        private static readonly ToolTip Tip = new ToolTip
        {
            ToolTipTitle = "AutoJMS",
            UseAnimation = false,
            UseFading = false
        };

        /// <summary>
        /// Hiện tip cạnh con trỏ chuột. Phải gọi trên UI thread của <paramref name="owner"/>.
        /// Owner không phải Control (hoặc đã dispose) thì bỏ qua trong im lặng.
        /// </summary>
        public static void Show(IWin32Window owner, string message, bool warning = false)
            => Show(owner, message, warning ? ToolTipIcon.Warning : ToolTipIcon.Info);

        /// <summary>Tip cảnh báo (icon tam giác vàng).</summary>
        public static void Warning(IWin32Window owner, string message) => Show(owner, message, ToolTipIcon.Warning);

        /// <summary>Tip lỗi (icon chữ thập đỏ).</summary>
        public static void Error(IWin32Window owner, string message) => Show(owner, message, ToolTipIcon.Error);

        private static void Show(IWin32Window owner, string message, ToolTipIcon icon)
        {
            if (owner is not Control host || host.IsDisposed || !host.IsHandleCreated) return;

            Tip.ToolTipIcon = icon;

            // Hide trước: gọi Show hai lần liên tiếp trên cùng một ToolTip thì lần sau
            // không hiện lại, tip cũ cứ đứng đó cho đến khi hết giờ của lần đầu.
            Tip.Hide(host);
            Tip.Show(message, host, host.PointToClient(Cursor.Position), DurationMs);
        }
    }
}

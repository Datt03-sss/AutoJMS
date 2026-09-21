using System.Windows.Forms;

namespace AutoJMS.UI.DesignSystem
{
    /// <summary>
    /// Hỏi trước khi làm. Xem DesignReference/AutoJMS.DESIGN.md §T.
    ///
    /// Việc phá huỷ (xoá, dừng, huỷ đơn) dùng nút Danger và KHÔNG phải nút mặc định:
    /// Enter sẽ rơi vào "Huỷ". Người dùng gõ nhanh không xoá nhầm dữ liệu.
    /// </summary>
    public class AConfirmDialog : ADialog
    {
        public AConfirmDialog()
        {
            Width = ThemeMetrics.DialogWidthCompact;
            Text = "Xác nhận";
        }

        /// <returns>true nếu người dùng đồng ý.</returns>
        public static bool Confirm(IWin32Window owner, string message,
            string title = "Xác nhận", string confirmText = "Đồng ý",
            string cancelText = "Huỷ", bool destructive = false)
        {
            using (var dialog = new AConfirmDialog { Text = title, Message = message })
            {
                // Thêm trái sang phải: Huỷ trước, nút chính sau -> nút chính ngoài cùng phải.
                dialog.AddButton(cancelText, AButtonVariant.Secondary, DialogResult.Cancel,
                    isDefault: destructive);
                dialog.AddButton(confirmText,
                    destructive ? AButtonVariant.Danger : AButtonVariant.Primary,
                    DialogResult.OK, isDefault: !destructive);

                return dialog.ShowDialog(owner) == DialogResult.OK;
            }
        }
    }
}

using System.Windows.Forms;

namespace AutoJMS.UI.DesignSystem
{
    /// <summary>
    /// Báo một việc đã xong hoặc một thông tin. Một nút, không có lựa chọn.
    /// Xem DesignReference/AutoJMS.DESIGN.md §T.
    ///
    /// Thay cho MessageBox.Show: MessageBox lấy màu của Windows nên ở theme Dark
    /// nó bật ra một ô trắng.
    /// </summary>
    public class AMessageDialog : ADialog
    {
        public AMessageDialog()
        {
            Width = ThemeMetrics.DialogWidthCompact;
            Text = "Thông báo";
            AddButton("Đóng", AButtonVariant.Primary, DialogResult.OK, isDefault: true);
        }

        public static void Show(IWin32Window owner, string message, string title = "Thông báo")
        {
            using (var dialog = new AMessageDialog { Text = title, Message = message })
                dialog.ShowDialog(owner);
        }
    }
}

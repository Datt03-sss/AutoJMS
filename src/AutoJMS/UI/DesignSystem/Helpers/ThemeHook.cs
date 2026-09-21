using System;
using System.Windows.Forms;

namespace AutoJMS.UI.DesignSystem
{
    /// <summary>
    /// Nối một control vào ThemeManager.ThemeChanged và bảo đảm gỡ ra khi Dispose.
    ///
    /// ThemeChanged là event TĨNH. Control nào đăng ký mà quên gỡ thì cả cây control
    /// của form đã đóng vẫn bị giữ sống tới hết tiến trình. Gom vào một chỗ để
    /// chỗ duy nhất có thể sai là chỗ này.
    /// </summary>
    internal sealed class ThemeHook : IDisposable
    {
        private Control _owner;
        private Action _onChanged;

        /// <param name="onChanged">
        /// Việc cần làm khi theme đổi. Bỏ trống thì chỉ Invalidate().
        /// Control nào có control con của WinForms (ATextBox, AComboBox) phải truyền
        /// hàm gán lại màu cho ruột - vẽ lại vỏ không đổi được BackColor của ruột.
        /// </param>
        public ThemeHook(Control owner, Action onChanged = null)
        {
            _owner = owner ?? throw new ArgumentNullException(nameof(owner));
            _onChanged = onChanged;
            ThemeManager.ThemeChanged += OnThemeChanged;
        }

        private void OnThemeChanged(object sender, EventArgs e)
        {
            var owner = _owner;
            if (owner == null || owner.IsDisposed || !owner.IsHandleCreated) return;

            Action act = _onChanged ?? owner.Invalidate;
            if (owner.InvokeRequired) owner.BeginInvoke(act);
            else act();
        }

        public void Dispose()
        {
            if (_owner == null) return;
            ThemeManager.ThemeChanged -= OnThemeChanged;
            _owner = null;
            _onChanged = null;
        }
    }
}

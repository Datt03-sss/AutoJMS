using System;
using System.Windows.Forms;

namespace AutoJMS.UI.DesignSystem
{
    /// <summary>
    /// Khung chứa các trang nội dung của AutoJMS. Xem DesignReference/AutoJMS.DESIGN.md §N.
    ///
    /// Kế thừa thẳng <see cref="TabControl"/> — ngoại lệ có chủ ý của quy tắc "A* kế thừa
    /// AControl", cùng lý do với <c>ADataGridView</c>: viết lại việc chứa trang, chuyển trang,
    /// và thứ tự tab là hàng trăm dòng để đổi lấy đúng con số không.
    ///
    /// Quan trọng hơn: giữ nguyên <see cref="TabControl"/> nghĩa là <c>SelectedTab</c>,
    /// <c>TabPages</c> và <c>SelectedIndexChanged</c> vẫn còn, nên 30 chỗ gọi trong
    /// Main.cs / UserActionCaptureService.cs biên dịch và chạy KHÔNG đổi một dòng nào.
    /// Năm <c>TabPage</c> chính là năm panel nội dung — không cần dựng thêm panel mới.
    ///
    /// Dải tab gốc bị ẩn: phần nhìn do <see cref="TopNavigation"/> đảm nhiệm.
    /// </summary>
    public sealed class ATabControl : TabControl
    {
        /// <summary>TCM_ADJUSTRECT — TabControl hỏi "trang nằm ở đâu trong vùng client".</summary>
        private const int TcmAdjustRect = 0x1328;

        private ThemeHook _themeHook;

        public ATabControl()
        {
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            ApplyTheme();
            _themeHook ??= new ThemeHook(this, ApplyTheme);
        }

        protected override void OnControlAdded(ControlEventArgs e)
        {
            base.OnControlAdded(e);
            if (e.Control is TabPage page) page.BackColor = ThemeManager.Current.Surface;
        }

        /// <summary>Nền khung và nền mọi trang lấy từ token, không có màu chết nào ở đây.</summary>
        public void ApplyTheme()
        {
            var c = ThemeManager.Current;

            BackColor = c.Surface;
            foreach (TabPage page in TabPages)
                page.BackColor = c.Surface;
        }

        /// <summary>
        /// Trả 1 cho TCM_ADJUSTRECT = "trang chiếm trọn vùng client", nên dải tab gốc của
        /// comctl32 bị trang phủ kín. Đây là cách ẩn dải tab mà KHÔNG phải tự vẽ lại
        /// TabControl — tránh đúng cái bẫy mà PremiumTabAccent đã phải đi vòng qua WM_PAINT.
        ///
        /// Vẫn để dải tab hiện trong Designer của Visual Studio, nếu không thì không ai
        /// kéo-thả sửa được 5 trang nữa.
        /// </summary>
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == TcmAdjustRect && !DesignMode)
            {
                m.Result = (IntPtr)1;
                return;
            }

            base.WndProc(ref m);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _themeHook?.Dispose();
                _themeHook = null;
            }
            base.Dispose(disposing);
        }
    }
}

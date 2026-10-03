using System;
using System.Drawing;
using System.Windows.Forms;
using AutoJMS.UI.DesignSystem;

namespace AutoJMS
{
    public partial class FullStackOperation
    {
        private static readonly Color FullStackBackColor = Color.FromArgb(244, 246, 249);
        private static readonly Color PanelBackColor = Color.White;
        private static readonly Color AccentGreen = Color.FromArgb(22, 163, 74);
        private static readonly Color AccentBlue = Color.FromArgb(47, 111, 237); // #2f6fed
        private static readonly Color AccentRed = Color.FromArgb(210, 58, 46); // #d23a2e
        private static readonly Color AccentPurple = Color.FromArgb(124, 58, 237);
        private static readonly Color HeaderDark = Color.FromArgb(17, 36, 63); // #11243f
        // Dark grid palette (data grids render dark bg + white text for every cell).
        private static readonly Color GridDarkBg = Color.FromArgb(24, 24, 27);    // #18181b
        private static readonly Color GridDarkAltBg = Color.FromArgb(32, 33, 37); // #202125 (row stripe)
        private static readonly Color GridDarkLine = Color.FromArgb(55, 55, 60);  // #37373c (grid lines)
        private static readonly Color BorderColor = Color.FromArgb(228, 232, 239); // #e4e8ef
        private static readonly Color TextPrimary = Color.FromArgb(31, 41, 55); // #1f2937
        private static readonly Color TextSecondary = Color.FromArgb(107, 117, 136); // #6b7588
        private static readonly Font UiFont = new("Segoe UI", 10F, FontStyle.Regular);
        private static readonly Font UiBoldFont = new("Segoe UI Semibold", 10F, FontStyle.Bold);

        // Màu nội dung đi theo theme. Chỉ Dark đổi màu: Light và Red (IsDark = false) giữ đúng
        // các màu cố định từ trước, nên hai theme đó không lệch một pixel nào.
        private static Color ContentLabelText => ThemeManager.IsDark ? ThemeManager.Current.TextSecondary : Color.FromArgb(70, 70, 70);

        private ThemeHook _contentThemeHook;

        [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

        /// <summary>
        /// Tô lại phần nội dung tự đặt màu (nhãn CHATBOT, trang Dashboard).
        /// Form này không qua AppTheme.Apply nên không ai khác tô hộ. Gọi lần đầu ở Load khi UI đã
        /// dựng xong; sau đó ThemeHook gọi mỗi lần đổi theme (form chưa có handle thì hook bỏ qua,
        /// lần Load kế tiếp sẽ tô đúng).
        /// </summary>
        private void ApplyFullStackContentTheme()
        {
            // Thanh tiêu đề native không theo theme của app (như TermsDialog). 20 = DWMWA_USE_IMMERSIVE_DARK_MODE;
            // Light/Red truyền 0 = mặc định, đúng như trước.
            if (IsHandleCreated)
            {
                int darkCaption = ThemeManager.IsDark ? 1 : 0;
                DwmSetWindowAttribute(Handle, 20, ref darkCaption, sizeof(int));
            }

            foreach (var label in new[] { uiLabel5, uiLabel3, tabChat_sumFollow, tabChat_hasKVD, tabChat_hasXNCH })
                if (label != null) label.ForeColor = ContentLabelText;
            if (tabChat_userName != null)
            {
                // Light/Red để trống = màu link mặc định của WinForms (xanh 0,0,255), như trước.
                // Xanh đó gần như chìm trên nền Dark nên Dark dùng Info.
                var link = ThemeManager.IsDark ? ThemeManager.Current.Info : Color.Empty;
                tabChat_userName.LinkColor = link;
                tabChat_userName.ActiveLinkColor = link;
            }

            _ = ApplyDashboardThemeAsync();
        }
    }
}

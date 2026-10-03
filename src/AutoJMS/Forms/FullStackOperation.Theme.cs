using System;
using System.Drawing;
using System.Windows.Forms;
using AutoJMS.FullStack.UI.ThoiHieu;
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
        private static Color ThoiHieuToolbarBack => ThemeManager.IsDark ? ThemeManager.Current.SurfaceAlt : Color.FromArgb(245, 245, 245);
        private static Color ThoiHieuButtonBack => ThemeManager.IsDark ? ThemeManager.Current.SurfaceRaised : Color.White;
        private static Color ThoiHieuButtonText => ThemeManager.IsDark ? ThemeManager.Current.Text : Color.FromArgb(45, 45, 45);
        private static Color ThoiHieuStatusText => ThemeManager.IsDark ? ThemeManager.Current.TextSecondary : Color.FromArgb(80, 80, 80);
        private static Color ContentLabelText => ThemeManager.IsDark ? ThemeManager.Current.TextSecondary : Color.FromArgb(70, 70, 70);
        private static ThoiHieuKpiColorPalette ThoiHieuPalette => ThemeManager.IsDark ? ThoiHieuKpiColorPalette.Dark : ThoiHieuKpiColorPalette.Light;

        private ThemeHook _contentThemeHook;

        [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

        /// <summary>
        /// Tô lại phần nội dung tự đặt màu (toolbar + sheet Thời hiệu, nhãn CHATBOT, trang Dashboard).
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

            if (_thoiHieuToolbar != null) _thoiHieuToolbar.BackColor = ThoiHieuToolbarBack;
            foreach (var button in new[] { _thoiHieuNormalViewButton, _thoiHieuFitWidthButton, _thoiHieuFitOnePageButton, _thoiHieuExportImageButton, _thoiHieuOpenExportFolderButton })
            {
                if (button == null) continue;
                button.BackColor = ThoiHieuButtonBack;
                button.ForeColor = ThoiHieuButtonText;
            }
            if (_thoiHieuKpiSheet != null)
            {
                _thoiHieuKpiSheet.Palette = ThoiHieuPalette;
                ControlStyler.ApplyNativeScrollTheme(_thoiHieuKpiSheet);
                UpdateThoiHieuModeButtons(_thoiHieuKpiSheet.ViewMode);
            }
            if (_thoiHieuStatusLabel != null) UpdateThoiHieuStatus(_thoiHieuStatusLabel.Text);

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

        /// <summary>
        /// Button phẳng theo luật nút của 8ce0caf (như AButton Secondary): nghỉ = viền Primary;
        /// hover/nhấn = tô Primary/PrimaryPressed, chữ OnPrimary; disable = viền xám, chữ xám (Dark tự vẽ, xem OnPaint).
        /// Không dùng AButton vì AButton Secondary tô SurfaceRaised theo theme cả ở Light, còn toolbar
        /// Thời hiệu ở Light giữ nền trắng cũ. Nền lúc nghỉ và màu "đang chọn" do chỗ gọi gán
        /// BackColor/ForeColor (xem ThoiHieuButtonBack / ApplyFullStackContentTheme).
        /// </summary>
        private sealed class ThemedFlatButton : Button
        {
            private readonly ThemeHook _themeHook;
            private bool _hover;

            public ThemedFlatButton()
            {
                FlatStyle = FlatStyle.Flat;
                UseVisualStyleBackColor = false;
                FlatAppearance.BorderSize = 1;
                ApplyTheme();
                _themeHook = new ThemeHook(this, ApplyTheme);
            }

            // Flat Button không có ForeColor riêng cho hover; ButtonBase vẽ chữ bằng getter này.
            public override Color ForeColor
            {
                get => _hover && Enabled ? ThemeManager.Current.OnPrimary : base.ForeColor;
                set => base.ForeColor = value;
            }

            private void ApplyTheme()
            {
                var c = ThemeManager.Current;
                FlatAppearance.BorderColor = Enabled ? c.Primary : ThemeManager.IsDark ? c.BorderStrong : FullStackOperation.BorderColor;
                FlatAppearance.MouseOverBackColor = c.Primary;
                FlatAppearance.MouseDownBackColor = c.PrimaryPressed;
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                // Chữ disable của WinForms là ControlPaint.Dark(BackColor): trên nền Dark thành đen trên đen.
                if (Enabled || !ThemeManager.IsDark) { base.OnPaint(e); return; }
                e.Graphics.Clear(BackColor);
                using (var pen = new Pen(FlatAppearance.BorderColor))
                    e.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
                TextRenderer.DrawText(e.Graphics, Text, Font, ClientRectangle, ThemeManager.Current.TextMuted,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
            }

            protected override void OnMouseEnter(EventArgs e) { _hover = true; base.OnMouseEnter(e); }
            protected override void OnMouseLeave(EventArgs e) { _hover = false; base.OnMouseLeave(e); }
            protected override void OnEnabledChanged(EventArgs e) { _hover = false; ApplyTheme(); base.OnEnabledChanged(e); }

            protected override void Dispose(bool disposing)
            {
                if (disposing) _themeHook.Dispose();
                base.Dispose(disposing);
            }
        }
    }
}

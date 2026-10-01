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
        private static readonly Color AccentSlate = Color.FromArgb(75, 85, 99);
        private static readonly Color AccentWarning = Color.FromArgb(245, 158, 11);
        private static readonly Color HeaderDark = Color.FromArgb(17, 36, 63); // #11243f
        // Dark grid palette (data grids render dark bg + white text for every cell).
        private static readonly Color GridDarkBg = Color.FromArgb(24, 24, 27);    // #18181b
        private static readonly Color GridDarkAltBg = Color.FromArgb(32, 33, 37); // #202125 (row stripe)
        private static readonly Color GridDarkLine = Color.FromArgb(55, 55, 60);  // #37373c (grid lines)
        private static readonly Color WorkspaceBackColor = Color.FromArgb(244, 246, 249); // #f4f6f9
        private static readonly Color BorderColor = Color.FromArgb(228, 232, 239); // #e4e8ef
        private static readonly Color TextPrimary = Color.FromArgb(31, 41, 55); // #1f2937
        private static readonly Color TextSecondary = Color.FromArgb(107, 117, 136); // #6b7588
        private static readonly Font UiFont = new("Segoe UI", 10F, FontStyle.Regular);
        private static readonly Font UiBoldFont = new("Segoe UI Semibold", 10F, FontStyle.Bold);

        /// <summary>
        /// Button phẳng theo luật nút của 8ce0caf (như AButton Secondary): nghỉ = viền Primary;
        /// hover/nhấn = tô Primary/PrimaryPressed, chữ OnPrimary; disable = viền xám, chữ xám của WinForms.
        /// Không dùng AButton vì form này nền sáng cố định, còn AButton Secondary tô SurfaceRaised theo
        /// theme (Dark = #18181B). Nền lúc nghỉ và màu "đang chọn" vẫn do chỗ gọi gán BackColor/ForeColor.
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
                FlatAppearance.BorderColor = Enabled ? c.Primary : FullStackOperation.BorderColor;
                FlatAppearance.MouseOverBackColor = c.Primary;
                FlatAppearance.MouseDownBackColor = c.PrimaryPressed;
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

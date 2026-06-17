using System;
using System.Drawing;
using System.Windows.Forms;
using Sunny.UI;

namespace AutoJMS
{
    public static class AppTheme
    {
        // J&T Express light brand styling colors
        public static readonly Color PrimaryRed = Color.FromArgb(225, 41, 46);      // #E1292E
        public static readonly Color PrimaryHover = Color.FromArgb(243, 65, 70);    // Light hover red
        public static readonly Color PrimaryPress = Color.FromArgb(190, 25, 29);    // Deep red
        public static readonly Color Background = Color.FromArgb(245, 246, 248);     // Clean grey/white base
        public static readonly Color CardBackground = Color.White;
        public static readonly Color BorderColor = Color.FromArgb(225, 228, 232);    // Soft borders
        public static readonly Color TextPrimary = Color.FromArgb(48, 49, 51);       // Readable dark grey
        public static readonly Color TextSecondary = Color.FromArgb(144, 147, 153);
        public static readonly Color GridHeaderBack = Color.FromArgb(245, 247, 250);
        public static readonly Color GridAlternating = Color.FromArgb(250, 251, 253);

        /// <summary>
        /// Apply theme styles and recursive double buffering to a target UIForm.
        /// </summary>
        public static void Apply(UIForm form)
        {
            if (form == null) return;

            form.SuspendLayout();
            form.Style = UIStyle.Custom;
            form.BackColor = Background;
            form.ForeColor = TextPrimary;
            form.RectColor = PrimaryRed;
            form.TitleColor = PrimaryRed;
            form.TitleForeColor = Color.White;

            ApplyToControls(form.Controls);
            form.ResumeLayout(true);
        }

        private static void ApplyToControls(Control.ControlCollection controls)
        {
            if (controls == null) return;

            foreach (Control ctrl in controls)
            {
                // Force double buffering on the control to prevent redraw flickers
                try
                {
                    var prop = typeof(Control).GetProperty("DoubleBuffered", 
                        System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                    prop?.SetValue(ctrl, true, null);
                }
                catch { }

                ApplyStyleToControl(ctrl);

                if (ctrl.Controls.Count > 0)
                {
                    ApplyToControls(ctrl.Controls);
                }
            }
        }

        private static void ApplyStyleToControl(Control ctrl)
        {
            if (ctrl is UIButton btn)
            {
                btn.Style = UIStyle.Custom;
                btn.FillColor = PrimaryRed;
                btn.FillHoverColor = PrimaryHover;
                btn.FillPressColor = PrimaryPress;
                btn.FillSelectedColor = PrimaryPress;
                btn.RectColor = PrimaryRed;
                btn.RectHoverColor = PrimaryHover;
                btn.RectPressColor = PrimaryPress;
                btn.RectSelectedColor = PrimaryPress;
                btn.ForeColor = Color.White;
            }
            else if (ctrl is UILabel lbl)
            {
                lbl.Style = UIStyle.Custom;
                lbl.ForeColor = TextPrimary;
                // Avoid visual artifact overlapping
                if (lbl.Parent is Panel || lbl.Parent is TabPage)
                {
                    lbl.BackColor = Color.Transparent;
                }
            }
            else if (ctrl is UITabControl tab)
            {
                tab.Style = UIStyle.Custom;
                tab.TabSelectedColor = PrimaryRed;
                tab.TabSelectedForeColor = Color.White;
                tab.TabSelectedHighColor = PrimaryRed;
                tab.TabUnSelectedColor = Color.FromArgb(230, 232, 235);
                tab.TabUnSelectedForeColor = TextPrimary;
                tab.TabBackColor = Background;
                tab.FillColor = Background;
            }
            else if (ctrl is TabPage page)
            {
                page.BackColor = Background;
            }
            else if (ctrl is UIDataGridView dgv)
            {
                dgv.Style = UIStyle.Custom;
                dgv.BackgroundColor = CardBackground;
                dgv.GridColor = BorderColor;
                dgv.StripeEvenColor = GridAlternating;
                dgv.StripeOddColor = CardBackground;
                
                // Column headers style
                dgv.ColumnHeadersDefaultCellStyle.BackColor = GridHeaderBack;
                dgv.ColumnHeadersDefaultCellStyle.ForeColor = TextPrimary;
                dgv.ColumnHeadersDefaultCellStyle.SelectionBackColor = GridHeaderBack;
                dgv.ColumnHeadersDefaultCellStyle.SelectionForeColor = TextPrimary;
                
                // Cells style
                dgv.DefaultCellStyle.BackColor = CardBackground;
                dgv.DefaultCellStyle.ForeColor = TextPrimary;
                dgv.DefaultCellStyle.SelectionBackColor = Color.FromArgb(254, 235, 235); // soft brand red selection
                dgv.DefaultCellStyle.SelectionForeColor = PrimaryRed;
            }
            else if (ctrl is UITextBox txt)
            {
                txt.Style = UIStyle.Custom;
                txt.FillColor = Color.White;
                txt.RectColor = BorderColor;
                txt.ForeColor = TextPrimary;
            }
            else if (ctrl is UIPanel pnl)
            {
                pnl.Style = UIStyle.Custom;
                pnl.FillColor = CardBackground;
                pnl.RectColor = BorderColor;
                pnl.ForeColor = TextPrimary;
            }
        }
    }
}

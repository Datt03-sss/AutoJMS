using System;
using System.Drawing;
using System.Windows.Forms;
using Sunny.UI;

namespace AutoJMS
{
    public static class AppTheme
    {
        // Professional J&T Express Light Theme Colors
        public static readonly Color PrimaryRed = Color.FromArgb(225, 41, 46);      // #E1292E
        public static readonly Color PrimaryHover = Color.FromArgb(243, 65, 70);    // Hover light red
        public static readonly Color PrimaryPress = Color.FromArgb(190, 25, 29);    // Press dark red
        public static readonly Color Background = Color.FromArgb(245, 246, 248);     // Clean workspace background
        public static readonly Color CardBackground = Color.White;
        public static readonly Color BorderColor = Color.FromArgb(225, 228, 232);    // Soft border
        public static readonly Color TextPrimary = Color.FromArgb(48, 49, 51);       // Dark gray text
        public static readonly Color TextSecondary = Color.FromArgb(144, 147, 153);  // Muted text
        public static readonly Color GridHeaderBack = Color.FromArgb(245, 247, 250);
        public static readonly Color GridAlternating = Color.FromArgb(250, 251, 253);

        /// <summary>
        /// Applies theme styles and recursive double buffering to the entire UIForm and its children.
        /// </summary>
        public static void Apply(UIForm form)
        {
            if (form == null) return;

            form.SuspendLayout();
            
            // Set native form custom styling
            form.Style = UIStyle.Custom;
            form.BackColor = Background;
            form.ForeColor = TextPrimary;
            form.RectColor = PrimaryRed;
            form.TitleColor = PrimaryRed;
            form.TitleForeColor = Color.White;

            // Apply double buffering to the form itself
            EnableDoubleBuffer(form);

            // Apply to all children controls
            ApplyToControls(form.Controls);

            form.ResumeLayout(true);
        }

        /// <summary>
        /// Recurse controls collection to apply styles and force double-buffering.
        /// </summary>
        private static void ApplyToControls(Control.ControlCollection controls)
        {
            if (controls == null) return;

            foreach (Control ctrl in controls)
            {
                EnableDoubleBuffer(ctrl);
                ApplyStyleToControl(ctrl);

                if (ctrl.Controls.Count > 0)
                {
                    ApplyToControls(ctrl.Controls);
                }
            }
        }

        /// <summary>
        /// Forces double-buffering on a control using reflection to prevent redraw flickering.
        /// </summary>
        private static void EnableDoubleBuffer(Control ctrl)
        {
            if (ctrl == null) return;
            try
            {
                var prop = typeof(Control).GetProperty("DoubleBuffered", 
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                prop?.SetValue(ctrl, true, null);
            }
            catch { }
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
                btn.ForeHoverColor = Color.White;
                btn.ForePressColor = Color.White;
                btn.ForeSelectedColor = Color.White;
            }
            else if (ctrl is UISymbolButton sbtn)
            {
                sbtn.Style = UIStyle.Custom;
                sbtn.FillColor = PrimaryRed;
                sbtn.FillHoverColor = PrimaryHover;
                sbtn.FillPressColor = PrimaryPress;
                sbtn.FillSelectedColor = PrimaryPress;
                sbtn.RectColor = PrimaryRed;
                sbtn.RectHoverColor = PrimaryHover;
                sbtn.RectPressColor = PrimaryPress;
                sbtn.RectSelectedColor = PrimaryPress;
                sbtn.ForeColor = Color.White;
                sbtn.ForeHoverColor = Color.White;
                sbtn.ForePressColor = Color.White;
                sbtn.ForeSelectedColor = Color.White;
                sbtn.SymbolColor = Color.White;
                sbtn.SymbolHoverColor = Color.White;
                sbtn.SymbolPressColor = Color.White;
                sbtn.SymbolSelectedColor = Color.White;
            }
            else if (ctrl is UIImageButton imgBtn)
            {
                imgBtn.Style = UIStyle.Custom;
                imgBtn.ForeColor = TextPrimary;
            }
            else if (ctrl is UILabel lbl)
            {
                lbl.Style = UIStyle.Custom;
                lbl.ForeColor = TextPrimary;
                if (lbl.Parent is Panel || lbl.Parent is TabPage || lbl.Parent is TableLayoutPanel)
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
                dgv.DefaultCellStyle.SelectionBackColor = Color.FromArgb(254, 235, 235); // Soft J&T Red highlight
                dgv.DefaultCellStyle.SelectionForeColor = PrimaryRed;
            }
            else if (ctrl is UITextBox txt)
            {
                txt.Style = UIStyle.Custom;
                txt.FillColor = Color.White;
                txt.RectColor = BorderColor;
                txt.ForeColor = TextPrimary;
            }
            else if (ctrl is UIRichTextBox rtxt)
            {
                rtxt.Style = UIStyle.Custom;
                rtxt.FillColor = Color.White;
                rtxt.RectColor = BorderColor;
                rtxt.ForeColor = TextPrimary;
            }
            else if (ctrl is UIPanel pnl)
            {
                pnl.Style = UIStyle.Custom;
                pnl.FillColor = CardBackground;
                pnl.RectColor = BorderColor;
                pnl.ForeColor = TextPrimary;
            }
            else if (ctrl is UITitlePanel tpnl)
            {
                tpnl.Style = UIStyle.Custom;
                tpnl.TitleColor = PrimaryRed;
                tpnl.TitleForeColor = Color.White;
                tpnl.RectColor = PrimaryRed;
                tpnl.FillColor = CardBackground;
                tpnl.ForeColor = TextPrimary;
            }
            else if (ctrl is UIComboBox cb)
            {
                cb.Style = UIStyle.Custom;
                cb.FillColor = Color.White;
                cb.RectColor = BorderColor;
                cb.ForeColor = TextPrimary;
            }
            else if (ctrl is UIIntegerUpDown iud)
            {
                iud.Style = UIStyle.Custom;
                iud.FillColor = Color.White;
                iud.RectColor = BorderColor;
                iud.ForeColor = TextPrimary;
            }
            else if (ctrl is UIDatetimePicker dtp)
            {
                dtp.Style = UIStyle.Custom;
                dtp.FillColor = Color.White;
                dtp.RectColor = BorderColor;
                dtp.ForeColor = TextPrimary;
            }
            else if (ctrl is UISwitch sw)
            {
                sw.Style = UIStyle.Custom;
                sw.ActiveColor = PrimaryRed;
                sw.InActiveColor = Color.FromArgb(220, 223, 230);
            }
            else if (ctrl is UICheckBox chk)
            {
                chk.Style = UIStyle.Custom;
                chk.CheckBoxColor = PrimaryRed;
                chk.ForeColor = TextPrimary;
            }
            else if (ctrl is UIProcessBar pb)
            {
                pb.Style = UIStyle.Custom;
                pb.ForeColor = PrimaryRed;
                pb.FillColor = Color.FromArgb(235, 238, 245);
            }
            else if (ctrl is UIFlowLayoutPanel flp)
            {
                flp.Style = UIStyle.Custom;
                flp.FillColor = CardBackground;
                flp.RectColor = BorderColor;
            }
        }
    }
}

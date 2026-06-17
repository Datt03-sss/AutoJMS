using System;
using System.Drawing;
using System.Windows.Forms;
using Sunny.UI;
using AutoJMS.UI;

namespace AutoJMS
{
    public static class AppTheme
    {
        /// <summary>
        /// Applies theme styles and recursive double buffering to the entire UIForm and its children.
        /// </summary>
        public static void Apply(UIForm form)
        {
            if (form == null) return;

            form.SuspendLayout();
            
            // Set native form custom styling
            form.Style = UIStyle.Custom;
            form.BackColor = AppPalette.AppBackground;
            form.ForeColor = AppPalette.TextPrimary;
            form.RectColor = AppPalette.SubtleBorder;
            form.TitleColor = AppPalette.CardBackground;
            form.TitleForeColor = AppPalette.TextPrimary;
            
            // Modern UI form styling
            form.Font = new Font("Segoe UI", 10F, FontStyle.Regular); // Slightly smaller base font for modern look
            try 
            {
                form.ShowRadius = true;
                form.ShowShadow = true;
            }
            catch { }

            // Apply double buffering to the form itself
            EnableDoubleBuffer(form);

            // Apply to all children controls
            ApplyToControls(form.Controls);

            form.ResumeLayout(true);
        }

        /// <summary>
        /// Recurse controls collection to apply styles and force double-buffering.
        /// </summary>
        public static void ApplyToControls(Control.ControlCollection controls)
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
            // Apply modern font globally (skip WebView2 to avoid COM issues)
            if (!(ctrl is Microsoft.Web.WebView2.WinForms.WebView2))
            {
                // Only override if not explicitly set to something else, or just force Segoe UI
                ctrl.Font = new Font("Segoe UI", 10F, FontStyle.Regular);
            }

            if (ctrl is UIButton btn)
            {
                btn.Style = UIStyle.Custom;
                btn.Radius = 6; // Softer radius for buttons
                btn.FillColor = AppPalette.PrimaryAccent;
                btn.FillHoverColor = AppPalette.PrimaryHoverTint;
                btn.FillPressColor = AppPalette.PrimaryPress;
                btn.FillSelectedColor = AppPalette.PrimaryPress;
                btn.RectColor = AppPalette.PrimaryAccent;
                btn.RectHoverColor = AppPalette.PrimaryHoverTint;
                btn.RectPressColor = AppPalette.PrimaryPress;
                btn.RectSelectedColor = AppPalette.PrimaryPress;
                btn.ForeColor = AppPalette.TextInverse;
                btn.ForeHoverColor = AppPalette.PrimaryAccent; // Inverted on hover tint
                btn.ForePressColor = AppPalette.TextInverse;
                btn.ForeSelectedColor = AppPalette.TextInverse;
            }
            else if (ctrl is UISymbolButton sbtn)
            {
                sbtn.Style = UIStyle.Custom;
                sbtn.Radius = 6;
                sbtn.FillColor = AppPalette.PrimaryAccent;
                sbtn.FillHoverColor = AppPalette.PrimaryHoverTint;
                sbtn.FillPressColor = AppPalette.PrimaryPress;
                sbtn.FillSelectedColor = AppPalette.PrimaryPress;
                sbtn.RectColor = AppPalette.PrimaryAccent;
                sbtn.RectHoverColor = AppPalette.PrimaryHoverTint;
                sbtn.RectPressColor = AppPalette.PrimaryPress;
                sbtn.RectSelectedColor = AppPalette.PrimaryPress;
                sbtn.ForeColor = AppPalette.TextInverse;
                sbtn.ForeHoverColor = AppPalette.PrimaryAccent;
                sbtn.ForePressColor = AppPalette.TextInverse;
                sbtn.ForeSelectedColor = AppPalette.TextInverse;
                sbtn.SymbolColor = AppPalette.TextInverse;
                sbtn.SymbolHoverColor = AppPalette.PrimaryAccent;
                sbtn.SymbolPressColor = AppPalette.TextInverse;
                sbtn.SymbolSelectedColor = AppPalette.TextInverse;
            }
            else if (ctrl is UIImageButton imgBtn)
            {
                imgBtn.Style = UIStyle.Custom;
                imgBtn.ForeColor = AppPalette.TextPrimary;
            }
            else if (ctrl is UILabel lbl)
            {
                lbl.Style = UIStyle.Custom;
                lbl.ForeColor = AppPalette.TextPrimary;
                if (lbl.Parent is Panel || lbl.Parent is TabPage || lbl.Parent is TableLayoutPanel || lbl.Parent is UITitlePanel)
                {
                    lbl.BackColor = Color.Transparent;
                }
            }
            else if (ctrl is UITabControl tab)
            {
                tab.Style = UIStyle.Custom;
                tab.TabSelectedColor = AppPalette.PrimaryHoverTint;
                tab.TabSelectedForeColor = AppPalette.PrimaryAccent;
                tab.TabSelectedHighColor = AppPalette.PrimaryAccent;
                tab.TabUnSelectedColor = AppPalette.AppBackground;
                tab.TabUnSelectedForeColor = AppPalette.TextSecondary;
                tab.TabBackColor = AppPalette.AppBackground;
                tab.FillColor = AppPalette.AppBackground;
            }
            else if (ctrl is TabPage page)
            {
                page.BackColor = AppPalette.AppBackground;
            }
            else if (ctrl is UIDataGridView dgv)
            {
                dgv.Style = UIStyle.Custom;
                dgv.BackgroundColor = AppPalette.CardBackground;
                dgv.GridColor = AppPalette.SubtleBorder;
                dgv.StripeEvenColor = AppPalette.GridAlternating;
                dgv.StripeOddColor = AppPalette.CardBackground;
                
                // Column headers style
                dgv.ColumnHeadersDefaultCellStyle.BackColor = AppPalette.GridHeaderBack;
                dgv.ColumnHeadersDefaultCellStyle.ForeColor = AppPalette.TextSecondary;
                dgv.ColumnHeadersDefaultCellStyle.Font = new Font("Segoe UI", 10F, FontStyle.Bold);
                dgv.ColumnHeadersDefaultCellStyle.SelectionBackColor = AppPalette.GridHeaderBack;
                dgv.ColumnHeadersDefaultCellStyle.SelectionForeColor = AppPalette.TextSecondary;
                
                // Cells style
                dgv.DefaultCellStyle.BackColor = AppPalette.CardBackground;
                dgv.DefaultCellStyle.ForeColor = AppPalette.TextPrimary;
                dgv.DefaultCellStyle.SelectionBackColor = AppPalette.GridSelectedBack;
                dgv.DefaultCellStyle.SelectionForeColor = AppPalette.PrimaryAccent;
                dgv.RowHeadersVisible = false; // Modern tables often hide row headers
                dgv.EnableHeadersVisualStyles = false;
                dgv.BorderStyle = BorderStyle.None;
            }
            else if (ctrl is UITextBox txt)
            {
                txt.Style = UIStyle.Custom;
                txt.Radius = 6;
                txt.FillColor = AppPalette.CardBackground;
                txt.RectColor = AppPalette.InputBorder;
                txt.ForeColor = AppPalette.TextPrimary;
            }
            else if (ctrl is UIRichTextBox rtxt)
            {
                rtxt.Style = UIStyle.Custom;
                rtxt.Radius = 6;
                rtxt.FillColor = AppPalette.CardBackground;
                rtxt.RectColor = AppPalette.InputBorder;
                rtxt.ForeColor = AppPalette.TextPrimary;
            }
            else if (ctrl is UIPanel pnl)
            {
                pnl.Style = UIStyle.Custom;
                pnl.Radius = 8;
                pnl.FillColor = AppPalette.CardBackground;
                pnl.RectColor = AppPalette.SubtleBorder;
                pnl.ForeColor = AppPalette.TextPrimary;
            }
            else if (ctrl is UITitlePanel tpnl)
            {
                tpnl.Style = UIStyle.Custom;
                tpnl.Radius = 8;
                tpnl.TitleColor = AppPalette.CardBackground;
                tpnl.TitleForeColor = AppPalette.TextPrimary;
                tpnl.RectColor = AppPalette.SubtleBorder;
                tpnl.FillColor = AppPalette.CardBackground;
                tpnl.ForeColor = AppPalette.TextPrimary;
            }
            else if (ctrl is UIComboBox cb)
            {
                cb.Style = UIStyle.Custom;
                cb.Radius = 6;
                cb.FillColor = AppPalette.CardBackground;
                cb.RectColor = AppPalette.InputBorder;
                cb.ForeColor = AppPalette.TextPrimary;
            }
            else if (ctrl is UIIntegerUpDown iud)
            {
                iud.Style = UIStyle.Custom;
                iud.Radius = 6;
                iud.FillColor = AppPalette.CardBackground;
                iud.RectColor = AppPalette.InputBorder;
                iud.ForeColor = AppPalette.TextPrimary;
            }
            else if (ctrl is UIDatetimePicker dtp)
            {
                dtp.Style = UIStyle.Custom;
                dtp.Radius = 6;
                dtp.FillColor = AppPalette.CardBackground;
                dtp.RectColor = AppPalette.InputBorder;
                dtp.ForeColor = AppPalette.TextPrimary;
            }
            else if (ctrl is UISwitch sw)
            {
                sw.Style = UIStyle.Custom;
                sw.ActiveColor = AppPalette.Success;
                sw.InActiveColor = ColorTranslator.FromHtml("#E5E7EB");
            }
            else if (ctrl is UICheckBox chk)
            {
                chk.Style = UIStyle.Custom;
                chk.CheckBoxColor = AppPalette.PrimaryAccent;
                chk.ForeColor = AppPalette.TextPrimary;
            }
            else if (ctrl is UIProcessBar pb)
            {
                pb.Style = UIStyle.Custom;
                pb.ForeColor = AppPalette.PrimaryAccent;
                pb.FillColor = ColorTranslator.FromHtml("#F3F4F6");
                pb.RectColor = Color.Transparent;
            }
            else if (ctrl is UIFlowLayoutPanel flp)
            {
                flp.Style = UIStyle.Custom;
                flp.FillColor = Color.Transparent;
                flp.RectColor = Color.Transparent;
            }
        }
    }
}

using System;
using System.Drawing;
using System.Windows.Forms;
using Sunny.UI;

namespace AutoJMS.UI
{
    public enum ThemeMode
    {
        Light,
        Red,
        Dark
    }

    public static class AppTheme
    {
        public static ThemeMode CurrentTheme { get; set; } = ThemeMode.Light;

        public class ThemeColors
        {
            public Color AppBackground { get; set; }
            public Color CardBackground { get; set; }
            public Color InputBackground { get; set; }
            public Color SubtleBorder { get; set; }
            public Color InputBorder { get; set; }
            public Color TextPrimary { get; set; }
            public Color TextSecondary { get; set; }
            public Color TextInverse { get; set; }

            public Color PrimaryAccent { get; set; }
            public Color PrimaryHover { get; set; }
            public Color PrimaryPress { get; set; }
            public Color PrimaryHoverTint { get; set; }

            public Color Success { get; set; }
            public Color Warning { get; set; }
            public Color Danger { get; set; }

            public Color GridHeaderBack { get; set; }
            public Color GridAlternating { get; set; }
            public Color GridSelectedBack { get; set; }

            public Color TitleColor { get; set; }
            public Color TitleForeColor { get; set; }
            public Color RectColor { get; set; }
        }

        public static readonly ThemeColors LightColors = new ThemeColors
        {
            AppBackground = ColorTranslator.FromHtml("#F5F7FA"),
            CardBackground = ColorTranslator.FromHtml("#FFFFFF"),
            InputBackground = ColorTranslator.FromHtml("#FFFFFF"),
            SubtleBorder = ColorTranslator.FromHtml("#E5E7EB"),
            InputBorder = ColorTranslator.FromHtml("#D1D5DB"),
            TextPrimary = ColorTranslator.FromHtml("#1F2937"),
            TextSecondary = ColorTranslator.FromHtml("#6B7280"),
            TextInverse = ColorTranslator.FromHtml("#FFFFFF"),

            PrimaryAccent = ColorTranslator.FromHtml("#3B82F6"), // Blue
            PrimaryHover = ColorTranslator.FromHtml("#60A5FA"),
            PrimaryPress = ColorTranslator.FromHtml("#2563EB"),
            PrimaryHoverTint = ColorTranslator.FromHtml("#EFF6FF"),

            Success = ColorTranslator.FromHtml("#16A34A"),
            Warning = ColorTranslator.FromHtml("#F59E0B"),
            Danger = ColorTranslator.FromHtml("#DC2626"),

            GridHeaderBack = ColorTranslator.FromHtml("#F9FAFB"),
            GridAlternating = ColorTranslator.FromHtml("#F9FAFB"),
            GridSelectedBack = ColorTranslator.FromHtml("#EFF6FF"),

            TitleColor = ColorTranslator.FromHtml("#3B82F6"),
            TitleForeColor = Color.White,
            RectColor = ColorTranslator.FromHtml("#3B82F6")
        };

        public static readonly ThemeColors RedColors = new ThemeColors
        {
            AppBackground = ColorTranslator.FromHtml("#F5F7FA"),
            CardBackground = ColorTranslator.FromHtml("#FFFFFF"),
            InputBackground = ColorTranslator.FromHtml("#FFFFFF"),
            SubtleBorder = ColorTranslator.FromHtml("#E5E7EB"),
            InputBorder = ColorTranslator.FromHtml("#D1D5DB"),
            TextPrimary = ColorTranslator.FromHtml("#1F2937"),
            TextSecondary = ColorTranslator.FromHtml("#6B7280"),
            TextInverse = ColorTranslator.FromHtml("#FFFFFF"),

            PrimaryAccent = ColorTranslator.FromHtml("#E53935"), // Red
            PrimaryHover = ColorTranslator.FromHtml("#EF5350"),
            PrimaryPress = ColorTranslator.FromHtml("#C62828"),
            PrimaryHoverTint = ColorTranslator.FromHtml("#FFEBEE"),

            Success = ColorTranslator.FromHtml("#16A34A"),
            Warning = ColorTranslator.FromHtml("#F59E0B"),
            Danger = ColorTranslator.FromHtml("#DC2626"),

            GridHeaderBack = ColorTranslator.FromHtml("#F9FAFB"),
            GridAlternating = ColorTranslator.FromHtml("#F9FAFB"),
            GridSelectedBack = ColorTranslator.FromHtml("#FFEBEE"),

            TitleColor = ColorTranslator.FromHtml("#E53935"),
            TitleForeColor = Color.White,
            RectColor = ColorTranslator.FromHtml("#E53935")
        };

        public static readonly ThemeColors DarkColors = new ThemeColors
        {
            AppBackground = ColorTranslator.FromHtml("#101012"), // Deeper black/charcoal background matching WebView dark theme
            CardBackground = ColorTranslator.FromHtml("#18181B"), // Matches WebView cards and panel containers
            InputBackground = ColorTranslator.FromHtml("#121214"), // Darker input field background
            SubtleBorder = ColorTranslator.FromHtml("#27272A"), // Very thin/sleek dark border lines
            InputBorder = ColorTranslator.FromHtml("#3F3F46"),
            TextPrimary = ColorTranslator.FromHtml("#FAFAFA"), // High contrast off-white text
            TextSecondary = ColorTranslator.FromHtml("#A1A1AA"),
            TextInverse = ColorTranslator.FromHtml("#0A0A0C"),

            PrimaryAccent = ColorTranslator.FromHtml("#E53935"), // Red J&T/JMS Accent
            PrimaryHover = ColorTranslator.FromHtml("#EF5350"),
            PrimaryPress = ColorTranslator.FromHtml("#B71C1C"),
            PrimaryHoverTint = ColorTranslator.FromHtml("#2A1414"), // Dark red highlight/hover tint

            Success = ColorTranslator.FromHtml("#22C55E"),
            Warning = ColorTranslator.FromHtml("#F59E0B"),
            Danger = ColorTranslator.FromHtml("#EF4444"),

            GridHeaderBack = ColorTranslator.FromHtml("#18181B"),
            GridAlternating = ColorTranslator.FromHtml("#131316"),
            GridSelectedBack = ColorTranslator.FromHtml("#2A1414"),

            TitleColor = ColorTranslator.FromHtml("#121214"), // Premium dark title panel background
            TitleForeColor = ColorTranslator.FromHtml("#FAFAFA"),
            RectColor = ColorTranslator.FromHtml("#27272A")
        };

        public static ThemeColors Colors
        {
            get
            {
                switch (CurrentTheme)
                {
                    case ThemeMode.Red: return RedColors;
                    case ThemeMode.Dark: return DarkColors;
                    default: return LightColors;
                }
            }
        }

        public static void Apply(UIForm form)
        {
            if (form == null) return;

            form.SuspendLayout();

            var colors = Colors;

            // Apply base form style
            form.Style = UIStyle.Custom;
            form.StyleCustomMode = true;
            form.TitleColor = colors.TitleColor;
            form.TitleForeColor = colors.TitleForeColor;
            form.RectColor = colors.RectColor;
            form.ControlBoxForeColor = colors.TitleForeColor;
            form.ControlBoxFillHoverColor = (CurrentTheme == ThemeMode.Dark) ? colors.CardBackground : Color.FromArgb(232, 244, 255);
            form.BackColor = colors.AppBackground;

            EnableDoubleBuffer(form);
            ApplyToControls(form.Controls, colors);

            form.ResumeLayout(true);
        }

        public static void ApplyToControls(Control.ControlCollection controls, ThemeColors colors)
        {
            if (controls == null) return;

            foreach (Control ctrl in controls)
            {
                // Skip WebViews entirely to avoid breaking them
                if (ctrl.GetType().FullName.Contains("WebView2"))
                    continue;

                EnableDoubleBuffer(ctrl);
                ApplyStyleToControl(ctrl, colors);

                if (ctrl.Controls.Count > 0)
                {
                    ApplyToControls(ctrl.Controls, colors);
                }
            }
        }

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

        private static void ApplyStyleToControl(Control ctrl, ThemeColors colors)
        {
            if (ctrl == null) return;

            // Apply modern font globally (skip WebView2)
            ctrl.Font = new Font("Segoe UI", 10F, FontStyle.Regular);

            if (ctrl is UISymbolButton sbtn)
            {
                sbtn.Style = UIStyle.Custom;
                sbtn.Radius = 6;
                sbtn.StyleCustomMode = true;

                if (sbtn.Name == "tabDKCH_Home")
                {
                    if (CurrentTheme == ThemeMode.Dark)
                    {
                        sbtn.FillColor = colors.InputBackground;
                        sbtn.FillHoverColor = colors.PrimaryHoverTint;
                        sbtn.FillPressColor = colors.PrimaryPress;
                        sbtn.FillSelectedColor = colors.PrimaryPress;
                        sbtn.RectColor = colors.SubtleBorder;
                        sbtn.RectHoverColor = colors.PrimaryAccent;
                        sbtn.RectPressColor = colors.PrimaryPress;
                        sbtn.RectSelectedColor = colors.PrimaryPress;
                        sbtn.ForeColor = colors.TextPrimary;
                        sbtn.ForeHoverColor = colors.PrimaryAccent;
                        sbtn.ForePressColor = Color.White;
                        sbtn.ForeSelectedColor = Color.White;
                        sbtn.SymbolColor = colors.TextPrimary;
                        sbtn.SymbolHoverColor = colors.PrimaryAccent;
                        sbtn.SymbolPressColor = Color.White;
                        sbtn.SymbolSelectedColor = Color.White;
                    }
                    else // Light/Red theme
                    {
                        sbtn.FillColor = Color.FromArgb(255, 192, 192);
                        sbtn.FillHoverColor = Color.FromArgb(255, 210, 210);
                        sbtn.FillPressColor = Color.FromArgb(224, 160, 160);
                        sbtn.RectColor = Color.FromArgb(224, 160, 160);
                        sbtn.ForeColor = Color.Black;
                        sbtn.ForeHoverColor = Color.Black;
                        sbtn.ForePressColor = Color.Black;
                        sbtn.ForeSelectedColor = Color.Black;
                        sbtn.SymbolColor = Color.Black;
                        sbtn.SymbolHoverColor = Color.Black;
                        sbtn.SymbolPressColor = Color.Black;
                        sbtn.SymbolSelectedColor = Color.Black;
                    }
                }
                else if (IsIconButton(sbtn.Name))
                {
                    sbtn.FillColor = Color.Transparent;
                    sbtn.FillHoverColor = colors.AppBackground;
                    sbtn.FillPressColor = colors.InputBorder;
                    sbtn.FillSelectedColor = colors.InputBorder;
                    sbtn.RectColor = Color.Transparent;
                    sbtn.RectHoverColor = Color.Transparent;
                    sbtn.RectPressColor = Color.Transparent;
                    sbtn.RectSelectedColor = Color.Transparent;
                    sbtn.ForeColor = colors.TextPrimary;
                    sbtn.ForeHoverColor = colors.PrimaryAccent;
                    sbtn.ForePressColor = colors.TextPrimary;
                    sbtn.ForeSelectedColor = colors.TextPrimary;
                    sbtn.SymbolColor = colors.TextSecondary;
                    sbtn.SymbolHoverColor = colors.PrimaryAccent;
                    sbtn.SymbolPressColor = colors.TextPrimary;
                    sbtn.SymbolSelectedColor = colors.TextPrimary;
                }
                else
                {
                    sbtn.FillColor = colors.PrimaryAccent;
                    sbtn.FillHoverColor = colors.PrimaryPress;
                    sbtn.FillPressColor = colors.PrimaryPress;
                    sbtn.FillSelectedColor = colors.PrimaryPress;
                    sbtn.RectColor = colors.PrimaryAccent;
                    sbtn.RectHoverColor = colors.PrimaryPress;
                    sbtn.RectPressColor = colors.PrimaryPress;
                    sbtn.RectSelectedColor = colors.PrimaryPress;
                    sbtn.ForeColor = Color.White;
                    sbtn.ForeHoverColor = Color.White;
                    sbtn.ForePressColor = Color.White;
                    sbtn.ForeSelectedColor = Color.White;
                    sbtn.SymbolColor = Color.White;
                    sbtn.SymbolHoverColor = Color.White;
                    sbtn.SymbolPressColor = Color.White;
                    sbtn.SymbolSelectedColor = Color.White;
                }
            }
            else if (ctrl is UIButton btn)
            {
                btn.Style = UIStyle.Custom;
                btn.Radius = 6;
                btn.StyleCustomMode = true;

                // Special treatment for tabDKCH buttons (Home, DKCH1, DKCH2, Stop)
                if (btn.Name == "tabDKCH_btnDKCH1" || btn.Name == "tabDKCH_btnDKCH2" || btn.Name == "tabDKCH_btnStop")
                {
                    if (CurrentTheme == ThemeMode.Dark)
                    {
                        if (btn.Name == "tabDKCH_btnStop")
                        {
                            btn.FillColor = ColorTranslator.FromHtml("#7F1D1D");
                            btn.FillHoverColor = ColorTranslator.FromHtml("#991B1B");
                            btn.FillPressColor = ColorTranslator.FromHtml("#B91C1C");
                            btn.RectColor = ColorTranslator.FromHtml("#EF4444");
                            btn.RectHoverColor = ColorTranslator.FromHtml("#F87171");
                            btn.RectPressColor = ColorTranslator.FromHtml("#B91C1C");
                            btn.ForeColor = Color.White;
                            btn.ForeHoverColor = Color.White;
                            btn.ForePressColor = Color.White;
                            btn.ForeSelectedColor = Color.White;
                        }
                        else // DKCH1, DKCH2
                        {
                            btn.FillColor = colors.InputBackground;
                            btn.FillHoverColor = colors.PrimaryHoverTint;
                            btn.FillPressColor = colors.PrimaryPress;
                            btn.FillSelectedColor = colors.PrimaryPress;
                            btn.RectColor = colors.SubtleBorder;
                            btn.RectHoverColor = colors.PrimaryAccent;
                            btn.RectPressColor = colors.PrimaryPress;
                            btn.RectSelectedColor = colors.PrimaryPress;
                            btn.ForeColor = colors.TextPrimary;
                            btn.ForeHoverColor = colors.PrimaryAccent;
                            btn.ForePressColor = Color.White;
                            btn.ForeSelectedColor = Color.White;
                        }
                    }
                    else // Light/Red theme: keep original designer/custom colors
                    {
                        if (btn.Name == "tabDKCH_btnDKCH1")
                        {
                            btn.FillColor = Color.FromArgb(128, 255, 128);
                            btn.FillHoverColor = Color.FromArgb(160, 255, 160);
                            btn.FillPressColor = Color.FromArgb(96, 224, 96);
                            btn.RectColor = Color.FromArgb(96, 224, 96);
                            btn.ForeColor = Color.Black;
                            btn.ForeHoverColor = Color.Black;
                            btn.ForePressColor = Color.Black;
                            btn.ForeSelectedColor = Color.Black;
                        }
                        else if (btn.Name == "tabDKCH_btnDKCH2")
                        {
                            btn.FillColor = Color.FromArgb(255, 192, 128);
                            btn.FillHoverColor = Color.FromArgb(255, 210, 160);
                            btn.FillPressColor = Color.FromArgb(224, 160, 96);
                            btn.RectColor = Color.FromArgb(224, 160, 96);
                            btn.ForeColor = Color.Black;
                            btn.ForeHoverColor = Color.Black;
                            btn.ForePressColor = Color.Black;
                            btn.ForeSelectedColor = Color.Black;
                        }
                        else if (btn.Name == "tabDKCH_btnStop")
                        {
                            btn.FillColor = Color.FromArgb(255, 128, 128);
                            btn.FillHoverColor = Color.FromArgb(255, 160, 160);
                            btn.FillPressColor = Color.FromArgb(224, 96, 96);
                            btn.RectColor = Color.FromArgb(224, 96, 96);
                            btn.ForeColor = Color.Black;
                            btn.ForeHoverColor = Color.Black;
                            btn.ForePressColor = Color.Black;
                            btn.ForeSelectedColor = Color.Black;
                        }
                    }
                }
                else
                {
                    btn.FillColor = colors.PrimaryAccent;
                    btn.FillHoverColor = colors.PrimaryPress;
                    btn.FillPressColor = colors.PrimaryPress;
                    btn.FillSelectedColor = colors.PrimaryPress;
                    btn.RectColor = colors.PrimaryAccent;
                    btn.RectHoverColor = colors.PrimaryPress;
                    btn.RectPressColor = colors.PrimaryPress;
                    btn.RectSelectedColor = colors.PrimaryPress;
                    btn.ForeColor = Color.White;
                    btn.ForeHoverColor = Color.White;
                    btn.ForePressColor = Color.White;
                    btn.ForeSelectedColor = Color.White;
                }
            }
            else if (ctrl is UIImageButton imgBtn)
            {
                imgBtn.Style = UIStyle.Custom;
                imgBtn.ForeColor = colors.TextPrimary;
            }
            else if (ctrl is UILabel lbl)
            {
                lbl.Style = UIStyle.Custom;
                if (lbl.Name != null && lbl.Name.StartsWith("tabDKCH_") && lbl.Name.EndsWith("_title"))
                {
                    lbl.ForeColor = colors.PrimaryAccent;
                }
                else if (lbl.Name != null && (lbl.Name.ToLower().Contains("secondary") || lbl.Name.ToLower().Contains("subtitle") || lbl.Name.ToLower().Contains("body")))
                {
                    lbl.ForeColor = colors.TextSecondary;
                }
                else
                {
                    lbl.ForeColor = colors.TextPrimary;
                }
                lbl.BackColor = Color.Transparent;
            }
            else if (ctrl is UITabControl tab)
            {
                tab.Style = UIStyle.Custom;
                tab.StyleCustomMode = true;

                if (tab.Name == "tabPrint_printFunc")
                {
                    if (CurrentTheme == ThemeMode.Dark)
                    {
                        tab.TabBackColor = colors.AppBackground;
                        tab.FillColor = colors.AppBackground;
                        tab.TabSelectedColor = colors.PrimaryAccent;
                        tab.TabSelectedForeColor = Color.White;
                        tab.TabSelectedHighColor = colors.PrimaryAccent;
                        tab.TabUnSelectedColor = colors.CardBackground;
                        tab.TabUnSelectedForeColor = colors.TextSecondary;
                    }
                    else if (CurrentTheme == ThemeMode.Light)
                    {
                        tab.TabBackColor = colors.AppBackground;
                        tab.FillColor = colors.AppBackground;
                        tab.TabSelectedColor = colors.PrimaryAccent;
                        tab.TabSelectedForeColor = Color.White;
                        tab.TabSelectedHighColor = colors.PrimaryAccent;
                        tab.TabUnSelectedColor = colors.CardBackground;
                        tab.TabUnSelectedForeColor = colors.TextSecondary;
                    }
                    else // Red theme
                    {
                        tab.TabBackColor = colors.AppBackground;
                        tab.FillColor = colors.AppBackground;
                        tab.TabSelectedColor = colors.PrimaryAccent;
                        tab.TabSelectedForeColor = Color.White;
                        tab.TabSelectedHighColor = colors.PrimaryAccent;
                        tab.TabUnSelectedColor = colors.CardBackground;
                        tab.TabUnSelectedForeColor = colors.TextSecondary;
                    }
                }
                else
                {
                    if (CurrentTheme == ThemeMode.Dark)
                    {
                        tab.TabBackColor = colors.AppBackground;
                        tab.FillColor = colors.AppBackground;
                        tab.TabSelectedColor = colors.CardBackground;
                        tab.TabSelectedForeColor = colors.PrimaryAccent;
                        tab.TabSelectedHighColor = colors.PrimaryAccent;
                        tab.TabUnSelectedColor = colors.AppBackground;
                        tab.TabUnSelectedForeColor = ColorTranslator.FromHtml("#D4D4D8");
                    }
                    else if (CurrentTheme == ThemeMode.Light)
                    {
                        tab.TabBackColor = Color.Azure;
                        tab.FillColor = colors.AppBackground;
                        tab.TabSelectedColor = Color.White;
                        tab.TabSelectedForeColor = SystemColors.ControlText;
                        tab.TabSelectedHighColor = Color.Black;
                        tab.TabUnSelectedColor = Color.FromArgb(115, 179, 255);
                        tab.TabUnSelectedForeColor = Color.FromArgb(240, 240, 240);
                    }
                    else // Red theme
                    {
                        tab.TabBackColor = Color.FromArgb(254, 242, 242);
                        tab.FillColor = colors.AppBackground;
                        tab.TabSelectedColor = Color.White;
                        tab.TabSelectedForeColor = Color.FromArgb(185, 28, 28);
                        tab.TabSelectedHighColor = Color.FromArgb(185, 28, 28);
                        tab.TabUnSelectedColor = Color.FromArgb(254, 202, 202);
                        tab.TabUnSelectedForeColor = Color.FromArgb(185, 28, 28);
                    }
                }
            }
            else if (ctrl is TabPage page)
            {
                page.BackColor = colors.AppBackground;
            }
            else if (ctrl is UIDataGridView dgv)
            {
                dgv.Style = UIStyle.Custom;
                dgv.StyleCustomMode = true;
                dgv.BackgroundColor = colors.CardBackground;
                dgv.GridColor = colors.SubtleBorder;
                dgv.StripeEvenColor = colors.GridAlternating;
                dgv.StripeOddColor = colors.CardBackground;

                dgv.ColumnHeadersDefaultCellStyle.BackColor = colors.GridHeaderBack;
                dgv.ColumnHeadersDefaultCellStyle.ForeColor = colors.TextSecondary;
                dgv.ColumnHeadersDefaultCellStyle.SelectionBackColor = colors.GridHeaderBack;
                dgv.ColumnHeadersDefaultCellStyle.SelectionForeColor = colors.TextSecondary;

                dgv.DefaultCellStyle.BackColor = colors.CardBackground;
                dgv.DefaultCellStyle.ForeColor = colors.TextPrimary;
                dgv.DefaultCellStyle.SelectionBackColor = colors.GridSelectedBack;
                dgv.DefaultCellStyle.SelectionForeColor = (CurrentTheme == ThemeMode.Dark) ? Color.White : colors.PrimaryAccent;
                dgv.RowHeadersVisible = false;
                dgv.EnableHeadersVisualStyles = false;
                dgv.BorderStyle = BorderStyle.None;
            }
            else if (ctrl is UIRichTextBox rtxt)
            {
                rtxt.Style = UIStyle.Custom;
                rtxt.StyleCustomMode = true;
                rtxt.Radius = 6;
                rtxt.FillColor = colors.InputBackground;
                rtxt.RectColor = colors.InputBorder;
                rtxt.ForeColor = colors.TextPrimary;
            }
            else if (ctrl is UITextBox txt)
            {
                txt.Style = UIStyle.Custom;
                txt.StyleCustomMode = true;
                txt.Radius = 6;
                txt.FillColor = colors.InputBackground;
                txt.RectColor = colors.InputBorder;
                txt.ForeColor = colors.TextPrimary;
            }
            else if (ctrl is UITitlePanel tpnl)
            {
                tpnl.Style = UIStyle.Custom;
                tpnl.StyleCustomMode = true;
                tpnl.Radius = 8;
                tpnl.TitleColor = colors.TitleColor;
                // Use red accent for title text in Dark theme to match J&T WebView header red accents
                tpnl.TitleForeColor = (CurrentTheme == ThemeMode.Dark) ? colors.PrimaryAccent : colors.TitleForeColor;
                tpnl.RectColor = colors.SubtleBorder;
                tpnl.FillColor = colors.CardBackground;
                tpnl.ForeColor = colors.TextPrimary;
                tpnl.BackColor = Color.Transparent;
            }
            else if (ctrl is UIFlowLayoutPanel flp)
            {
                flp.Style = UIStyle.Custom;
                flp.StyleCustomMode = true;
                flp.FillColor = Color.Transparent;
                flp.RectColor = Color.Transparent;
                flp.BackColor = Color.Transparent;
            }
            else if (ctrl is UIPanel pnl)
            {
                pnl.Style = UIStyle.Custom;
                pnl.StyleCustomMode = true;
                pnl.ControlAdded -= Panel_ControlAdded;
                pnl.ControlAdded += Panel_ControlAdded;
                if (pnl.Name == "tabHome_pnlLeft")
                {
                    pnl.Radius = 0;
                    pnl.FillColor = (CurrentTheme == ThemeMode.Dark) ? colors.AppBackground : colors.CardBackground;
                    pnl.RectColor = (CurrentTheme == ThemeMode.Dark) ? Color.Transparent : colors.SubtleBorder;
                }
                else if (pnl.Name == "uiPanel19" || pnl.Name == "uiPanel20")
                {
                    pnl.Radius = 0;
                    pnl.FillColor = colors.CardBackground;
                    pnl.RectColor = colors.SubtleBorder;
                }
                else
                {
                    pnl.Radius = 8;
                    pnl.FillColor = colors.CardBackground;
                    pnl.RectColor = colors.SubtleBorder;
                }
                pnl.ForeColor = colors.TextPrimary;
                pnl.BackColor = Color.Transparent;
            }
            else if (ctrl is UIComboBox cb)
            {
                cb.Style = UIStyle.Custom;
                cb.StyleCustomMode = true;
                cb.Radius = 6;
                cb.FillColor = colors.InputBackground;
                cb.RectColor = colors.InputBorder;
                cb.ForeColor = colors.TextPrimary;
                if (CurrentTheme == ThemeMode.Dark)
                {
                    cb.ItemHoverColor = colors.PrimaryHoverTint; // #2A1414 (dark red hover)
                    cb.ItemSelectForeColor = Color.White;
                    cb.ItemSelectBackColor = colors.PrimaryPress; // #B71C1C (red selected)
                }
                else if (CurrentTheme == ThemeMode.Red)
                {
                    cb.ItemHoverColor = ColorTranslator.FromHtml("#FFEBEE");
                    cb.ItemSelectForeColor = Color.Black;
                    cb.ItemSelectBackColor = colors.PrimaryHover;
                }
                else
                {
                    cb.ItemHoverColor = Color.FromArgb(155, 200, 255);
                    cb.ItemSelectForeColor = SystemColors.ControlText;
                    cb.ItemSelectBackColor = Color.FromArgb(80, 160, 255);
                }
            }
            else if (ctrl is UIIntegerUpDown iud)
            {
                iud.Style = UIStyle.Custom;
                iud.StyleCustomMode = true;
                iud.Radius = 6;
                iud.FillColor = colors.InputBackground;
                iud.RectColor = colors.InputBorder;
                iud.ForeColor = colors.TextPrimary;
            }
            else if (ctrl is UIDatetimePicker dtp)
            {
                dtp.Style = UIStyle.Custom;
                dtp.StyleCustomMode = true;
                dtp.Radius = 6;
                dtp.FillColor = colors.InputBackground;
                dtp.RectColor = colors.InputBorder;
                dtp.ForeColor = colors.TextPrimary;
            }
            else if (ctrl is UISwitch sw)
            {
                sw.Style = UIStyle.Custom;
                sw.StyleCustomMode = true;
                sw.ActiveColor = (CurrentTheme == ThemeMode.Dark) ? colors.PrimaryAccent : colors.Success;
                sw.InActiveColor = colors.SubtleBorder;
                sw.BackColor = Color.Transparent;
            }
            else if (ctrl is UICheckBox chk)
            {
                chk.Style = UIStyle.Custom;
                chk.StyleCustomMode = true;
                chk.CheckBoxColor = colors.PrimaryAccent;
                chk.ForeColor = colors.TextPrimary;
                chk.BackColor = Color.Transparent;
            }
            else if (ctrl is UIProcessBar pb)
            {
                pb.Style = UIStyle.Custom;
                pb.StyleCustomMode = true;
                pb.ForeColor = colors.PrimaryAccent;
                pb.FillColor = colors.InputBackground;
                pb.RectColor = Color.Transparent;
            }
            else if (ctrl is TableLayoutPanel tlp)
            {
                tlp.BackColor = Color.Transparent;
            }
            else if (ctrl is SplitContainer sc)
            {
                sc.BackColor = Color.Transparent;
                sc.Panel1.BackColor = Color.Transparent;
                sc.Panel2.BackColor = Color.Transparent;
            }
        }

        private static bool IsPrimaryButton(string name)
        {
            if (string.IsNullOrEmpty(name)) return true;
            name = name.ToLower();
            return name.Contains("search") || 
                   name.Contains("timkiem") || 
                   name.Contains("print") || 
                   name.Contains("stop") || 
                   name.Contains("checkupdate");
        }

        private static void Panel_ControlAdded(object sender, ControlEventArgs e)
        {
            if (e.Control != null)
            {
                EnableDoubleBuffer(e.Control);
                ApplyStyleToControl(e.Control, Colors);
                if (e.Control.Controls.Count > 0)
                {
                    ApplyToControls(e.Control.Controls, Colors);
                }
            }
        }

        private static bool IsIconButton(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            name = name.ToLower();
            return name.Contains("btnback") || 
                   name.Contains("btnforward") || 
                   name.Contains("btnreload") || 
                   name.Contains("btnhome") || 
                   name.Contains("btnmenu");
        }
    }
}

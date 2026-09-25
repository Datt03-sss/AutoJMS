using System;
using System.Drawing;
using System.Windows.Forms;

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

        /// <summary>Font mặc định cho control WinForms chuẩn. Tạo một lần, không dispose.</summary>
        private static readonly Font DefaultControlFont = new Font("Segoe UI", 10F, FontStyle.Regular);

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
            TextPrimary = ColorTranslator.FromHtml("#E4E4E7"), // Slightly dimmed off-white text (reduced glare)
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

        /// <summary>
        /// Mọi Form của app nay đều là <see cref="Form"/> chuẩn. Nhánh tô thanh tiêu đề
        /// của UIForm bỏ hẳn: thanh tiêu đề giờ là của Windows, không có thuộc tính nào
        /// để gán. TitleColor/TitleForeColor/RectColor trong bảng màu vì thế chỉ còn
        /// phục vụ các nhánh khác.
        /// </summary>
        public static void Apply(Form form)
        {
            if (form == null) return;

            form.SuspendLayout();

            var colors = Colors;

            form.BackColor = colors.AppBackground;

            EnableDoubleBuffer(form);
            ApplyToControls(form.Controls, colors);

            // Control design system bị ApplyToControls bỏ qua có chủ ý, nên phải được
            // báo riêng. Chúng đọc màu trực tiếp từ CurrentTheme - chỉ thiếu tín hiệu vẽ lại.
            DesignSystem.ThemeManager.NotifyChanged();

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

                // Control của design system tự lấy màu/cỡ chữ từ ThemeManager và tự vẽ lại
                // khi đổi theme. ApplyStyleToControl đè Font 10F lên chúng thì mọi token
                // trong ThemeTypography/ThemeColors thành vô nghĩa — nên bỏ qua.
                //
                // Chỉ bỏ qua CHÍNH control đó, KHÔNG bỏ qua cây con: từ khi tabControl là
                // ATabControl, cả 5 tab nằm trong nó. Bỏ cây con thì mọi TabPage, Label và
                // TableLayoutPanel bên trong mất sạch theme mà không báo lỗi gì.
                bool isDesignSystem = ctrl.GetType().Namespace == "AutoJMS.UI.DesignSystem";

                if (!isDesignSystem)
                {
                    EnableDoubleBuffer(ctrl);
                    ApplyStyleToControl(ctrl, colors);
                }

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

            // Apply modern font globally (skip WebView2).
            // Dùng một instance dùng chung: dòng này chạy cho MỌI control ở MỖI lần đổi
            // theme, nên `new Font(...)` tại đây rò một handle GDI mỗi control mỗi lần.
            //
            // Control đã chọn một token của ThemeTypography thì giữ nguyên: không có
            // điều kiện này, mọi nhãn vừa di trú sang bảng chữ bị kéo hết về 10F.
            if (!DesignSystem.ThemeTypography.IsToken(ctrl.Font))
                ctrl.Font = DefaultControlFont;

            // Nhánh cho control Sunny.UI (UISymbolButton/UIButton/UIImageButton/
            // UITabControl/UIDataGridView/UIRichTextBox/UITextBox/UITitlePanel/
            // UIFlowLayoutPanel/UIPanel/UIComboBox/UIIntegerUpDown/UIDatetimePicker/
            // UISwitch/UICheckBox/UIProcessBar) bỏ hết cùng gói SunnyUI. Control A* tự
            // đọc token qua ThemeManager; ProgressBar chuẩn bỏ qua ForeColor/BackColor
            // khi visual styles bật nên cũng không cần nhánh nào.
            if (ctrl is LinkLabel link)
            {
                // Phải đứng TRƯỚC nhánh Label: LinkLabel kế thừa Label.
                link.ForeColor = colors.TextPrimary;
                link.LinkColor = colors.PrimaryAccent;
                link.ActiveLinkColor = colors.PrimaryPress;
                link.VisitedLinkColor = colors.PrimaryAccent;
                link.BackColor = Color.Transparent;
            }
            else if (ctrl is Label lbl)
            {
                if (lbl.Name == "tabTracking_countSum")
                {
                    // Bigger count + no outer frame (the global 10F font above + the
                    // designer's FixedSingle border made it tiny and boxed).
                    lbl.Font = new Font("Segoe UI Semibold", 26F, FontStyle.Bold);
                    lbl.BorderStyle = BorderStyle.None;
                    lbl.ForeColor = colors.TextPrimary;
                }
                else if (lbl.Name == "lblNetworkStatus")
                {
                    // Ignore, managed by Main.cs
                }
                else if (lbl.Name != null && lbl.Name.StartsWith("tabDKCH_") && lbl.Name.EndsWith("_title"))
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
            else if (ctrl is TabPage page)
            {
                page.BackColor = colors.AppBackground;
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
    }
}

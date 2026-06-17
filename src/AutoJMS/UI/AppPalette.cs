using System.Drawing;

namespace AutoJMS.UI
{
    public static class AppPalette
    {
        // Primary Brand Colors
        public static readonly Color PrimaryAccent = ColorTranslator.FromHtml("#E53935");
        public static readonly Color PrimaryHoverTint = ColorTranslator.FromHtml("#FDECEC");
        public static readonly Color PrimaryPress = ColorTranslator.FromHtml("#C62828");

        // Backgrounds
        public static readonly Color AppBackground = ColorTranslator.FromHtml("#F5F7FA");
        public static readonly Color CardBackground = ColorTranslator.FromHtml("#FFFFFF");
        public static readonly Color SidebarBackground = ColorTranslator.FromHtml("#FFFFFF");

        // Borders and Dividers
        public static readonly Color SubtleBorder = ColorTranslator.FromHtml("#E5E7EB");
        public static readonly Color InputBorder = ColorTranslator.FromHtml("#D1D5DB");

        // Text Colors
        public static readonly Color TextPrimary = ColorTranslator.FromHtml("#1F2937");
        public static readonly Color TextSecondary = ColorTranslator.FromHtml("#6B7280");
        public static readonly Color TextInverse = ColorTranslator.FromHtml("#FFFFFF");

        // Semantic Colors
        public static readonly Color Success = ColorTranslator.FromHtml("#16A34A");
        public static readonly Color Warning = ColorTranslator.FromHtml("#F59E0B");
        public static readonly Color Info = ColorTranslator.FromHtml("#3B82F6");
        public static readonly Color Danger = ColorTranslator.FromHtml("#DC2626");
        
        // DataGrid Specific
        public static readonly Color GridHeaderBack = ColorTranslator.FromHtml("#F9FAFB");
        public static readonly Color GridAlternating = ColorTranslator.FromHtml("#F9FAFB");
        public static readonly Color GridSelectedBack = ColorTranslator.FromHtml("#FEF2F2");
    }
}

using System.Drawing;

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
        private static readonly Color WorkspaceBackColor = Color.FromArgb(244, 246, 249); // #f4f6f9
        private static readonly Color BorderColor = Color.FromArgb(228, 232, 239); // #e4e8ef
        private static readonly Color TextPrimary = Color.FromArgb(31, 41, 55); // #1f2937
        private static readonly Color TextSecondary = Color.FromArgb(107, 117, 136); // #6b7588
        private static readonly Font UiFont = new("Segoe UI", 10F, FontStyle.Regular);
        private static readonly Font UiBoldFont = new("Segoe UI Semibold", 10F, FontStyle.Bold);
    }
}

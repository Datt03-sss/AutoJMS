using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;

namespace AutoJMS.UI.DesignSystem
{
    /// <summary>
    /// Chấm trạng thái + chữ, ví dụ "● Online" / "● Mất kết nối".
    /// Xem DesignReference/AutoJMS.DESIGN.md §S.
    ///
    /// Thay cho các chuỗi màu literal đang nằm rải trong UpdateNetworkUI (Main.cs).
    /// Chấm KHÔNG bao giờ đứng một mình - luôn kèm chữ.
    /// </summary>
    [ToolboxItem(true)]
    public class AStatusIndicator : AControl
    {
        private AStatus _status = AStatus.Neutral;
        private const int DotSize = 8;

        public AStatusIndicator()
        {
            Font = ThemeTypography.Small;
            AutoSize = true;
            Height = ThemeMetrics.BadgeHeight;
        }

        [DefaultValue(AStatus.Neutral)]
        public AStatus Status
        {
            get => _status;
            set { if (_status == value) return; _status = value; Invalidate(); }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var c = Theme;
            var g = e.Graphics;

            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            PaintParentBackground(e);

            Color dot = ABadge.ColorFor(_status, c);
            int d = S(DotSize);
            int y = (Height - d) / 2;

            using (var brush = new SolidBrush(dot))
                g.FillEllipse(brush, 0, y, d, d);

            int textX = d + S(ThemeSpacing.Xs) + S(ThemeSpacing.Xs) / 2;
            TextRenderer.DrawText(g, Text, Font,
                new Rectangle(textX, 0, Width - textX, Height),
                c.TextSecondary, ControlStyler.TextLeft);
        }

        public override Size GetPreferredSize(Size proposedSize)
        {
            int w = S(DotSize) + S(ThemeSpacing.Sm)
                  + TextRenderer.MeasureText(Text ?? string.Empty, Font).Width;
            return new Size(w, S(ThemeMetrics.BadgeHeight));
        }

        protected override void OnTextChanged(System.EventArgs e)
        {
            if (AutoSize) PerformLayout();
            Invalidate();
            base.OnTextChanged(e);
        }
    }
}

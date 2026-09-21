using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;

namespace AutoJMS.UI.DesignSystem
{
    /// <summary>
    /// Trạng thái nghiệp vụ dùng chung cho ABadge, AStatusIndicator và các component trạng thái.
    /// </summary>
    public enum AStatus
    {
        Neutral,
        Success,
        Warning,
        Danger,
        Info
    }

    /// <summary>
    /// Nhãn trạng thái hình viên thuốc. Xem DesignReference/AutoJMS.DESIGN.md §S.
    ///
    /// LUÔN có chữ. Không bao giờ chỉ là một chấm màu - khoảng 8% nam giới Việt Nam
    /// mù màu đỏ-lục, mà đỏ và lục là hai màu trạng thái quan trọng nhất của AutoJMS.
    /// </summary>
    [ToolboxItem(true)]
    public class ABadge : AControl
    {
        private AStatus _status = AStatus.Neutral;

        public ABadge()
        {
            Font = ThemeTypography.Badge;
            AutoSize = true;
            Height = ThemeMetrics.BadgeHeight;
        }

        [DefaultValue(AStatus.Neutral)]
        public AStatus Status
        {
            get => _status;
            set { if (_status == value) return; _status = value; Invalidate(); }
        }

        internal static Color ColorFor(AStatus status, ThemeColors c)
        {
            switch (status)
            {
                case AStatus.Success: return c.Success;
                case AStatus.Warning: return c.Warning;
                case AStatus.Danger: return c.Danger;
                case AStatus.Info: return c.Info;
                default: return c.TextSecondary;
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var c = Theme;
            var g = e.Graphics;
            int radius = ThemeRadius.Resolve(ThemeRadius.Pill, Height);

            ControlStyler.Prepare(g, radius);
            PaintParentBackground(e);

            Color fore = ColorFor(_status, c);
            Color back = _status == AStatus.Neutral ? c.SurfaceAlt : c.StatusTint(fore);

            ControlStyler.FillSurface(g, ClientRectangle, back, radius);
            TextRenderer.DrawText(g, Text, Font, ClientRectangle, fore, ControlStyler.TextCenter);
        }

        public override Size GetPreferredSize(Size proposedSize)
        {
            int w = TextRenderer.MeasureText(Text ?? string.Empty, Font).Width + S(ThemeSpacing.Sm) * 2;
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

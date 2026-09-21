using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;

namespace AutoJMS.UI.DesignSystem
{
    /// <summary>
    /// Dải công cụ ngang. Xem DesignReference/AutoJMS.DESIGN.md §F, §R.
    ///
    /// Góc vuông (bề mặt cấu trúc) và CHỈ kẻ một đường dưới, không đóng khung:
    /// toolbar dính liền với nội dung bên dưới nó, viền bốn phía sẽ cắt rời hai thứ
    /// vốn là một.
    /// </summary>
    [ToolboxItem(true)]
    public class AToolbar : APanel
    {
        public AToolbar()
        {
            Radius = ThemeRadius.None;
            Elevation = ThemeShadows.Elevation.Flat;
            Dock = DockStyle.Top;
            Height = ThemeMetrics.ToolbarHeight;
            Padding = new Padding(ThemeSpacing.Sm, 0, ThemeSpacing.Sm, 0);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var c = Theme;

            using (var brush = new SolidBrush(c.SurfaceAlt))
                e.Graphics.FillRectangle(brush, ClientRectangle);

            int w = S(ThemeBorders.Hairline);
            using (var pen = new Pen(c.Border, w))
                e.Graphics.DrawLine(pen, 0, Height - w, Width, Height - w);
        }
    }
}

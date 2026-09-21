using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;

namespace AutoJMS.UI.DesignSystem
{
    /// <summary>Ô đánh dấu tự vẽ. Xem DesignReference/AutoJMS.DESIGN.md §L.</summary>
    [ToolboxItem(true)]
    [DefaultEvent(nameof(CheckedChanged))]
    public class ACheckBox : ACheckControl
    {
        protected override void DrawGlyph(Graphics g, Rectangle glyph, ThemeColors c)
        {
            int radius = S(ThemeRadius.Sm);

            Color back = !Enabled ? c.SurfaceAlt
                       : Checked ? c.Primary
                       : c.SurfaceRaised;

            Color border = !Enabled ? c.Border
                         : Checked ? c.Primary
                         : Hovered ? c.Primary
                         : c.BorderStrong;

            ControlStyler.FillSurface(g, glyph, back, radius);
            ControlStyler.DrawBorder(g, glyph, border, S(ThemeBorders.Control), radius);

            if (!Checked) return;

            // Dấu tick: ba điểm, vẽ bằng bút dày 2px theo DPI.
            using (var pen = new Pen(Enabled ? c.OnPrimary : c.TextMuted, S(2)))
            {
                pen.StartCap = System.Drawing.Drawing2D.LineCap.Round;
                pen.EndCap = System.Drawing.Drawing2D.LineCap.Round;
                pen.LineJoin = System.Drawing.Drawing2D.LineJoin.Round;

                float w = glyph.Width, h = glyph.Height;
                g.DrawLines(pen, new[]
                {
                    new PointF(glyph.X + w * 0.24f, glyph.Y + h * 0.52f),
                    new PointF(glyph.X + w * 0.43f, glyph.Y + h * 0.71f),
                    new PointF(glyph.X + w * 0.76f, glyph.Y + h * 0.30f)
                });
            }
        }
    }
}

using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;

namespace AutoJMS.UI.DesignSystem
{
    /// <summary>
    /// Công tắc bật/tắt, tự vẽ. Xem DesignReference/AutoJMS.DESIGN.md §L.
    ///
    /// Núm nhảy thẳng sang vị trí mới, KHÔNG trượt. Animation trượt cần Timer chạy
    /// vài chục khung hình cho mỗi lần bấm - vi phạm §A.5 và là thứ đầu tiên thấy giật
    /// trên máy cấu hình thấp.
    /// </summary>
    [ToolboxItem(true)]
    [DefaultEvent(nameof(CheckedChanged))]
    public class AToggleSwitch : ACheckControl
    {
        protected override int GlyphWidth => 36;
        protected override int GlyphHeight => 20;

        protected override void DrawGlyph(Graphics g, Rectangle glyph, ThemeColors c)
        {
            int radius = glyph.Height / 2;

            Color track = !Enabled ? c.Border
                        : Checked ? (Hovered ? c.PrimaryHover : c.Primary)
                        : (Hovered ? c.BorderStrong : c.Border);

            ControlStyler.FillSurface(g, glyph, track, radius);

            if (!Checked)
                ControlStyler.DrawBorder(g, glyph, c.BorderStrong, S(ThemeBorders.Control), radius);

            int pad = S(2);
            int d = glyph.Height - pad * 2;
            int x = Checked ? glyph.Right - pad - d : glyph.X + pad;

            using (var brush = new SolidBrush(Enabled ? c.SurfaceRaised : c.SurfaceAlt))
                g.FillEllipse(brush, x, glyph.Y + pad, d, d);
        }
    }
}

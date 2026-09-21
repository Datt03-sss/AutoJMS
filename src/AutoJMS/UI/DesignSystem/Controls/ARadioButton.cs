using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;

namespace AutoJMS.UI.DesignSystem
{
    /// <summary>
    /// Nút chọn một-trong-nhiều, tự vẽ. Xem DesignReference/AutoJMS.DESIGN.md §L.
    ///
    /// Gom nhóm theo control cha, giống RadioButton của WinForms: mọi ARadioButton
    /// cùng Parent là một nhóm. Muốn nhiều nhóm thì đặt mỗi nhóm trong một APanel riêng.
    /// </summary>
    [ToolboxItem(true)]
    [DefaultEvent(nameof(CheckedChanged))]
    public class ARadioButton : ACheckControl
    {
        /// <summary>Bấm lại nút đang chọn không tắt nó - đúng ngữ nghĩa radio.</summary>
        protected override void Toggle()
        {
            if (!Checked) Checked = true;
        }

        protected override void OnCheckedTrue()
        {
            if (Parent == null) return;

            foreach (Control sibling in Parent.Controls)
            {
                if (!ReferenceEquals(sibling, this) && sibling is ARadioButton radio && radio.Checked)
                    radio.Checked = false;
            }
        }

        protected override void DrawGlyph(Graphics g, Rectangle glyph, ThemeColors c)
        {
            Color border = !Enabled ? c.Border
                         : Checked || Hovered ? c.Primary
                         : c.BorderStrong;

            using (var brush = new SolidBrush(Enabled ? c.SurfaceRaised : c.SurfaceAlt))
                g.FillEllipse(brush, glyph);

            int w = S(ThemeBorders.Control);
            using (var pen = new Pen(border, w))
                g.DrawEllipse(pen, glyph.X + w / 2f, glyph.Y + w / 2f, glyph.Width - w, glyph.Height - w);

            if (!Checked) return;

            var dot = Rectangle.Inflate(glyph, -glyph.Width / 3, -glyph.Height / 3);
            using (var brush = new SolidBrush(Enabled ? c.Primary : c.TextMuted))
                g.FillEllipse(brush, dot);
        }
    }
}

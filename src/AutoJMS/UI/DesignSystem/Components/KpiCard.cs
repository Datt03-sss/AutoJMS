using System;
using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;

namespace AutoJMS.UI.DesignSystem
{
    /// <summary>
    /// Thẻ số liệu. Xem DesignReference/AutoJMS.DESIGN.md §P.
    ///
    /// Phần tử "quét trong 1-3 giây" quan trọng nhất của AutoJMS:
    ///  - SỐ là thứ to nhất; nhãn nhỏ, nằm trên, màu TextSecondary.
    ///  - Số dùng font ĐỀU để nhiều thẻ xếp cạnh nhau thẳng cột.
    ///  - Delta luôn kèm ký hiệu tam giác, không chỉ dựa vào màu.
    ///  - Không viền accent, không nền màu - viền hairline như mọi card khác.
    /// </summary>
    [ToolboxItem(true)]
    public class KpiCard : APanel
    {
        private string _label = string.Empty;
        private string _value = "0";
        private string _delta = string.Empty;
        private AStatus _deltaStatus = AStatus.Neutral;
        private DeltaDirection _direction = DeltaDirection.None;

        public enum DeltaDirection { None, Up, Down, Flat }

        public KpiCard()
        {
            Radius = ThemeRadius.Md;
            Elevation = ThemeShadows.Elevation.Flat;
            Padding = new Padding(ThemeSpacing.Md);
            Size = new Size(160, 92);
        }

        [DefaultValue("")]
        public string Label
        {
            get => _label;
            set { _label = value ?? string.Empty; Invalidate(); }
        }

        [DefaultValue("0")]
        public string Value
        {
            get => _value;
            set { _value = value ?? string.Empty; Invalidate(); }
        }

        [DefaultValue("")]
        public string Delta
        {
            get => _delta;
            set { _delta = value ?? string.Empty; Invalidate(); }
        }

        [DefaultValue(AStatus.Neutral)]
        public AStatus DeltaStatus
        {
            get => _deltaStatus;
            set { _deltaStatus = value; Invalidate(); }
        }

        [DefaultValue(DeltaDirection.None)]
        public DeltaDirection Direction
        {
            get => _direction;
            set { _direction = value; Invalidate(); }
        }

        private string DirectionGlyph()
        {
            switch (_direction)
            {
                case DeltaDirection.Up: return "▲ ";
                case DeltaDirection.Down: return "▼ ";
                case DeltaDirection.Flat: return "— ";
                default: return string.Empty;
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);

            var c = Theme;
            var g = e.Graphics;
            int x = Padding.Left;
            int w = Math.Max(1, Width - Padding.Left - Padding.Right);
            int y = Padding.Top;

            // Nhãn - IN HOA, nhỏ, phía trên
            int labelH = ThemeTypography.Small.Height;
            TextRenderer.DrawText(g, (_label ?? string.Empty).ToUpperInvariant(), ThemeTypography.Small,
                new Rectangle(x, y, w, labelH), c.TextSecondary, ControlStyler.TextLeft);
            y += labelH + S(ThemeSpacing.Xs);

            // Số - to nhất trên thẻ
            int valueH = ThemeTypography.MonoDisplay.Height;
            TextRenderer.DrawText(g, _value, ThemeTypography.MonoDisplay,
                new Rectangle(x, y, w, valueH), c.Text, ControlStyler.TextLeft);
            y += valueH;

            if (string.IsNullOrEmpty(_delta)) return;

            y += S(ThemeSpacing.Xs);
            TextRenderer.DrawText(g, DirectionGlyph() + _delta, ThemeTypography.Small,
                new Rectangle(x, y, w, ThemeTypography.Small.Height),
                ABadge.ColorFor(_deltaStatus, c), ControlStyler.TextLeft);
        }

        public override Size GetPreferredSize(Size proposedSize)
        {
            int h = Padding.Top + Padding.Bottom
                  + ThemeTypography.Small.Height + S(ThemeSpacing.Xs)
                  + ThemeTypography.MonoDisplay.Height;

            if (!string.IsNullOrEmpty(_delta))
                h += S(ThemeSpacing.Xs) + ThemeTypography.Small.Height;

            return new Size(Math.Max(Width, S(140)), h);
        }
    }
}

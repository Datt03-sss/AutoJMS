using System;
using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;

namespace AutoJMS.UI.DesignSystem
{
    /// <summary>
    /// Bề mặt chứa control con. Xem DesignReference/AutoJMS.DESIGN.md §I.
    ///
    /// Kế thừa Panel (BCL) chứ không phải Control: Panel đã có sẵn AutoScroll,
    /// Padding và quản lý control con. Viết lại cuộn bằng tay chỉ để "tự vẽ 100%"
    /// là đổi một thứ chạy tốt lấy một thứ phải bảo trì. Panel không phải SunnyUI
    /// nên vẫn đúng hướng bỏ dần SunnyUI.
    /// </summary>
    [ToolboxItem(true)]
    public class APanel : Panel
    {
        private ThemeHook _themeHook;
        private ThemeShadows.Elevation _elevation = ThemeShadows.Elevation.Flat;
        private int _radius = ThemeRadius.Md;

        public APanel()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw, true);
            Font = ThemeTypography.Body;
        }

        protected static ThemeColors Theme => ThemeManager.Current;

        protected int S(int value) => DpiHelper.Scale(this, value);

        [DefaultValue(ThemeShadows.Elevation.Flat)]
        public ThemeShadows.Elevation Elevation
        {
            get => _elevation;
            set { if (_elevation == value) return; _elevation = value; Invalidate(); }
        }

        [DefaultValue(ThemeRadius.Md)]
        public int Radius
        {
            get => _radius;
            set { if (_radius == value) return; _radius = value; Invalidate(); }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            _themeHook ??= new ThemeHook(this);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { _themeHook?.Dispose(); _themeHook = null; }
            base.Dispose(disposing);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var c = Theme;
            int radius = S(_radius);

            ControlStyler.Prepare(e.Graphics, radius);

            if (radius > 0)
            {
                // Góc bo để lộ nền cha - phải tô nền cha trước, nếu không sẽ có
                // bốn chấm tối ở bốn góc.
                using (var brush = new SolidBrush(Parent?.BackColor ?? c.Surface))
                    e.Graphics.FillRectangle(brush, ClientRectangle);
            }

            ControlStyler.FillSurface(e.Graphics, ClientRectangle,
                ThemeShadows.BackColorFor(_elevation, c), radius);
            ControlStyler.DrawBorder(e.Graphics, ClientRectangle,
                ThemeShadows.BorderColorFor(_elevation, c), S(ThemeBorders.Hairline), radius);

            base.OnPaint(e);
        }
    }
}

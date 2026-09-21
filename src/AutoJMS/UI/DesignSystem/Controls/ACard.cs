using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;

namespace AutoJMS.UI.DesignSystem
{
    /// <summary>
    /// Card có tiêu đề tuỳ chọn. Xem DesignReference/AutoJMS.DESIGN.md §O.
    ///
    /// Tiêu đề KHÔNG tô màu Primary - đó là tô màu trang trí và làm loãng tín hiệu accent.
    /// Lồng card tối đa 1 cấp; sâu hơn là cấu trúc sai chứ không phải cần thêm card.
    /// </summary>
    [ToolboxItem(true)]
    public class ACard : APanel
    {
        private string _title = string.Empty;
        private bool _dense;

        public ACard()
        {
            Radius = ThemeRadius.Md;
            Elevation = ThemeShadows.Elevation.Flat;
            Padding = new Padding(ThemeSpacing.Md);
        }

        [DefaultValue("")]
        public string Title
        {
            get => _title;
            set
            {
                value ??= string.Empty;
                if (_title == value) return;
                _title = value;
                PerformLayout();
                Invalidate();
            }
        }

        /// <summary>Card dày đặc dữ liệu: padding Sm thay vì Md (DESIGN.md §O).</summary>
        [DefaultValue(false)]
        public bool Dense
        {
            get => _dense;
            set
            {
                if (_dense == value) return;
                _dense = value;
                Padding = new Padding(value ? ThemeSpacing.Sm : ThemeSpacing.Md);
                PerformLayout();
                Invalidate();
            }
        }

        private int HeaderHeight =>
            string.IsNullOrEmpty(_title) ? 0 : ThemeTypography.H2.Height + S(ThemeSpacing.Sm) * 2;

        /// <summary>Đẩy vùng chứa control con xuống dưới tiêu đề.</summary>
        public override Rectangle DisplayRectangle
        {
            get
            {
                var r = base.DisplayRectangle;
                int h = HeaderHeight;
                return h == 0 ? r : new Rectangle(r.X, r.Y + h, r.Width, r.Height - h);
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            if (string.IsNullOrEmpty(_title)) return;

            var c = Theme;
            int padX = Padding.Left;
            int header = HeaderHeight;

            var titleRect = new Rectangle(padX, Padding.Top, Width - padX * 2, header - S(ThemeSpacing.Sm) * 2);
            TextRenderer.DrawText(e.Graphics, _title, ThemeTypography.H2, titleRect, c.Text,
                ControlStyler.TextLeft);

            int y = Padding.Top + header - S(ThemeSpacing.Sm);
            using (var pen = new Pen(c.Border, S(ThemeBorders.Hairline)))
                e.Graphics.DrawLine(pen, padX, y, Width - padX, y);
        }
    }
}

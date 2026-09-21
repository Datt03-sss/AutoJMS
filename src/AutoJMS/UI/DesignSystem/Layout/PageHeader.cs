using System;
using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;

namespace AutoJMS.UI.DesignSystem
{
    /// <summary>
    /// Tiêu đề trang + vùng hành động bên phải.
    /// Xem DesignReference/AutoJMS.DESIGN.md §D, §F.
    ///
    /// Tiêu đề KHÔNG tô Primary (DESIGN.md §Z). Thêm nút vào <see cref="Actions"/>;
    /// chúng xếp từ phải sang trái, nút chính ngoài cùng bên phải.
    /// </summary>
    [ToolboxItem(true)]
    public class PageHeader : APanel
    {
        private readonly FlowLayoutPanel _actions;
        private string _title = string.Empty;
        private string _subtitle = string.Empty;

        public PageHeader()
        {
            Radius = ThemeRadius.None;
            Elevation = ThemeShadows.Elevation.Background;
            Dock = DockStyle.Top;
            Height = 56;
            Padding = new Padding(ThemeSpacing.Lg, ThemeSpacing.Md, ThemeSpacing.Lg, ThemeSpacing.Md);

            _actions = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.RightToLeft,
                Dock = DockStyle.Right,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                WrapContents = false,
                BackColor = Color.Transparent,
                Margin = new Padding(0)
            };
            Controls.Add(_actions);
        }

        [DefaultValue("")]
        public string Title
        {
            get => _title;
            set { _title = value ?? string.Empty; Invalidate(); }
        }

        [DefaultValue("")]
        public string Subtitle
        {
            get => _subtitle;
            set { _subtitle = value ?? string.Empty; Invalidate(); }
        }

        /// <summary>Vùng chứa nút hành động. Xếp từ phải sang trái.</summary>
        [Browsable(false)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public FlowLayoutPanel Actions => _actions;

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);

            var c = Theme;
            var g = e.Graphics;
            int x = Padding.Left;
            int w = Math.Max(1, Width - Padding.Left - _actions.Width - S(ThemeSpacing.Md));

            bool hasSub = !string.IsNullOrEmpty(_subtitle);
            int titleH = ThemeTypography.Display.Height;
            int subH = hasSub ? ThemeTypography.Small.Height : 0;
            int y = Math.Max(Padding.Top, (Height - titleH - subH) / 2);

            TextRenderer.DrawText(g, _title, ThemeTypography.Display,
                new Rectangle(x, y, w, titleH), c.Text, ControlStyler.TextLeft);

            if (!hasSub) return;

            TextRenderer.DrawText(g, _subtitle, ThemeTypography.Small,
                new Rectangle(x, y + titleH, w, subH), c.TextSecondary, ControlStyler.TextLeft);
        }
    }
}

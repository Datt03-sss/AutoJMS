using System;
using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;

namespace AutoJMS.UI.DesignSystem
{
    /// <summary>
    /// Hộp lỗi tại chỗ. Xem DesignReference/AutoJMS.DESIGN.md §W.
    ///
    /// Lỗi hiện ĐÚNG CHỖ nó xảy ra, không phải trong hộp thoại toàn cục - trừ khi
    /// nó chặn cả app. Luôn có đường thoát (nút Thử lại).
    /// Không bao giờ đổ stack trace ra đây; ghi vào log, hiện câu tiếng Việt.
    /// </summary>
    [ToolboxItem(true)]
    public class ErrorState : APanel
    {
        private readonly AButton _retry;
        private string _title = "Đã xảy ra lỗi";
        private string _detail = string.Empty;

        private const int IconSymbol = ASymbols.Warning;

        public ErrorState()
        {
            Radius = ThemeRadius.Sm;
            Padding = new Padding(ThemeSpacing.Md);
            Height = 96;

            _retry = new AButton
            {
                Variant = AButtonVariant.Secondary,
                Text = "Thử lại",
                AutoSize = false
            };
            _retry.Click += (s, e) => RetryClick?.Invoke(this, EventArgs.Empty);
            Controls.Add(_retry);
        }

        public event EventHandler RetryClick;

        [DefaultValue("Đã xảy ra lỗi")]
        public string Title
        {
            get => _title;
            set { _title = value ?? string.Empty; Invalidate(); }
        }

        [DefaultValue("")]
        public string Detail
        {
            get => _detail;
            set { _detail = value ?? string.Empty; Invalidate(); }
        }

        [DefaultValue("Thử lại")]
        public string RetryText
        {
            get => _retry.Text;
            set
            {
                _retry.Text = value ?? string.Empty;
                _retry.Visible = !string.IsNullOrEmpty(value);
                PerformLayout();
            }
        }

        private int TextLeft => Padding.Left + S(ThemeMetrics.IconSizeDefault) + S(ThemeSpacing.Sm);

        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            if (_retry == null || !_retry.Visible) return;

            var size = _retry.GetPreferredSize(Size.Empty);
            _retry.Size = size;
            _retry.Location = new Point(TextLeft, Height - Padding.Bottom - size.Height);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var c = Theme;
            var g = e.Graphics;
            int radius = S(Radius);

            ControlStyler.Prepare(g, radius);
            using (var brush = new SolidBrush(Parent?.BackColor ?? c.Surface))
                g.FillRectangle(brush, ClientRectangle);

            ControlStyler.FillSurface(g, ClientRectangle, ThemeColors.Blend(c.Danger, c.SurfaceRaised, 8), radius);
            ControlStyler.DrawBorder(g, ClientRectangle, c.Danger, S(ThemeBorders.Hairline), radius);

            int icon = S(ThemeMetrics.IconSizeDefault);
            ASymbols.Draw(g, IconSymbol, icon, c.Danger,
                new Rectangle(Padding.Left, Padding.Top, icon, icon));

            int x = TextLeft;
            int w = Math.Max(1, Width - x - Padding.Right);
            int y = Padding.Top;

            int titleH = ThemeTypography.BodyStrong.Height;
            TextRenderer.DrawText(g, _title, ThemeTypography.BodyStrong,
                new Rectangle(x, y, w, titleH), c.Danger, ControlStyler.TextLeft);
            y += titleH + S(ThemeSpacing.Xs);

            if (string.IsNullOrEmpty(_detail)) return;

            TextRenderer.DrawText(g, _detail, ThemeTypography.Small,
                new Rectangle(x, y, w, ThemeTypography.Small.Height * 2), c.Text,
                TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.WordBreak |
                TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
        }
    }
}

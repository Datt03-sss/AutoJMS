using System;
using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;

namespace AutoJMS.UI.DesignSystem
{
    /// <summary>
    /// Vùng rỗng. Xem DesignReference/AutoJMS.DESIGN.md §U.
    ///
    /// LUÔN nói người dùng cần làm gì tiếp theo. "Không có dữ liệu" đứng một mình
    /// là ngõ cụt. Phân biệt rõ rỗng-vì-chưa-tìm với rỗng-vì-tìm-không-ra bằng
    /// hai cặp câu chữ khác nhau, do người gọi truyền vào.
    /// </summary>
    [ToolboxItem(true)]
    public class EmptyState : APanel
    {
        private readonly AButton _action;
        private string _title = "Chưa có dữ liệu";
        private string _description = string.Empty;
        private int _symbol = ASymbols.Inbox;

        public EmptyState()
        {
            Radius = ThemeRadius.None;
            Elevation = ThemeShadows.Elevation.Background;

            _action = new AButton
            {
                Variant = AButtonVariant.Secondary,
                Visible = false,
                AutoSize = false
            };
            _action.Click += (s, e) => ActionClick?.Invoke(this, EventArgs.Empty);
            Controls.Add(_action);
        }

        public event EventHandler ActionClick;

        [DefaultValue("Chưa có dữ liệu")]
        public string Title
        {
            get => _title;
            set { _title = value ?? string.Empty; Invalidate(); }
        }

        [DefaultValue("")]
        public string Description
        {
            get => _description;
            set { _description = value ?? string.Empty; Invalidate(); }
        }

        /// <summary>Mã FontAwesome. Một icon là đủ - không minh hoạ, không ảnh.</summary>
        [DefaultValue(61763)]
        public int Symbol
        {
            get => _symbol;
            set { _symbol = value; Invalidate(); }
        }

        [DefaultValue("")]
        public string ActionText
        {
            get => _action.Text;
            set
            {
                _action.Text = value ?? string.Empty;
                _action.Visible = !string.IsNullOrEmpty(value);
                PerformLayout();
                Invalidate();
            }
        }

        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            if (_action == null || !_action.Visible) return;

            var size = _action.GetPreferredSize(Size.Empty);
            _action.Size = size;
            _action.Location = new Point((Width - size.Width) / 2, BlockBottom() + S(ThemeSpacing.Lg));
        }

        private int BlockHeight()
        {
            int h = S(32) + S(ThemeSpacing.Md) + ThemeTypography.H2.Height;
            if (!string.IsNullOrEmpty(_description))
                h += S(ThemeSpacing.Xs) + ThemeTypography.Small.Height;
            return h;
        }

        private int BlockTop()
        {
            int total = BlockHeight();
            if (_action != null && _action.Visible)
                total += S(ThemeSpacing.Lg) + S(ThemeMetrics.ControlHeight);
            return Math.Max(S(ThemeSpacing.Xl), (Height - total) / 2);
        }

        private int BlockBottom() => BlockTop() + BlockHeight();

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);

            var c = Theme;
            var g = e.Graphics;
            int y = BlockTop();
            int icon = S(32);

            if (_symbol != 0)
            {
                ASymbols.Draw(g, _symbol, icon, c.TextMuted,
                    new Rectangle((Width - icon) / 2, y, icon, icon));
            }
            y += icon + S(ThemeSpacing.Md);

            int titleH = ThemeTypography.H2.Height;
            TextRenderer.DrawText(g, _title, ThemeTypography.H2,
                new Rectangle(0, y, Width, titleH), c.TextSecondary, ControlStyler.TextCenter);
            y += titleH;

            if (string.IsNullOrEmpty(_description)) return;

            y += S(ThemeSpacing.Xs);
            TextRenderer.DrawText(g, _description, ThemeTypography.Small,
                new Rectangle(0, y, Width, ThemeTypography.Small.Height), c.TextMuted,
                ControlStyler.TextCenter);
        }
    }
}

using System;
using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;
using Sunny.UI;

namespace AutoJMS.UI.DesignSystem
{
    public enum AButtonVariant
    {
        /// <summary>Hành động chính. TỐI ĐA MỘT trên mỗi vùng.</summary>
        Primary,
        /// <summary>Hành động thường.</summary>
        Secondary,
        /// <summary>Hành động phụ, nút icon trong toolbar.</summary>
        Ghost,
        /// <summary>Huỷ, xoá, dừng khẩn.</summary>
        Danger,
        /// <summary>CHỈ nút mang nghĩa nghiệp vụ (DESIGN.md §B).</summary>
        Success,
        /// <summary>CHỈ nút mang nghĩa nghiệp vụ (DESIGN.md §B).</summary>
        Warning
    }

    /// <summary>
    /// Nút tự vẽ. Xem DesignReference/AutoJMS.DESIGN.md §K.
    ///
    /// Không animation, không dịch control khi nhấn, không gradient, không đổ bóng.
    /// </summary>
    [ToolboxItem(true)]
    [DefaultEvent(nameof(Click))]
    public class AButton : AControl
    {
        private AButtonVariant _variant = AButtonVariant.Secondary;
        private bool _hover;
        private bool _pressed;
        private int _symbol;
        private int _symbolSize = ThemeMetrics.IconSizeDefault;
        private int _radius = ThemeRadius.Sm;

        public AButton()
        {
            SetStyle(ControlStyles.Selectable, true);
            TabStop = true;
            Font = ThemeTypography.Button;
            Size = new Size(96, ThemeMetrics.ControlHeight);
            Cursor = Cursors.Hand;
        }

        [DefaultValue(AButtonVariant.Secondary)]
        public AButtonVariant Variant
        {
            get => _variant;
            set { if (_variant == value) return; _variant = value; Invalidate(); }
        }

        /// <summary>Mã ký tự FontAwesome (cùng bộ số với UISymbolButton.Symbol). 0 = không icon.</summary>
        [DefaultValue(0)]
        public int Symbol
        {
            get => _symbol;
            set { if (_symbol == value) return; _symbol = value; Invalidate(); }
        }

        [DefaultValue(ThemeMetrics.IconSizeDefault)]
        public int SymbolSize
        {
            get => _symbolSize;
            set { if (_symbolSize == value) return; _symbolSize = value; Invalidate(); }
        }

        [DefaultValue(ThemeRadius.Sm)]
        public int Radius
        {
            get => _radius;
            set { if (_radius == value) return; _radius = value; Invalidate(); }
        }

        private struct Palette
        {
            public Color Back, Fore, Border;
        }

        private Palette Resolve()
        {
            var c = Theme;
            var p = new Palette();

            switch (_variant)
            {
                case AButtonVariant.Primary:
                    p.Back = c.Primary; p.Fore = c.OnPrimary; p.Border = Color.Empty;
                    if (!Enabled) { p.Back = c.PrimaryDisabled; break; }
                    if (_pressed) p.Back = c.PrimaryPressed;
                    else if (_hover) p.Back = c.PrimaryHover;
                    break;

                case AButtonVariant.Secondary:
                    p.Back = c.SurfaceRaised; p.Fore = c.Text; p.Border = c.BorderStrong;
                    if (!Enabled) { p.Back = c.SurfaceAlt; p.Fore = c.TextMuted; p.Border = c.Border; break; }
                    if (_pressed) p.Back = c.Border;
                    else if (_hover) p.Back = c.SurfaceAlt;
                    break;

                case AButtonVariant.Ghost:
                    p.Back = Color.Empty; p.Fore = c.TextSecondary; p.Border = Color.Empty;
                    if (!Enabled) { p.Fore = c.TextMuted; break; }
                    if (_pressed) p.Back = c.Border;
                    else if (_hover) { p.Back = c.SurfaceAlt; p.Fore = c.Text; }
                    break;

                default:
                    Color b = _variant == AButtonVariant.Danger ? c.Danger
                            : _variant == AButtonVariant.Success ? c.Success
                            : c.Warning;
                    p.Fore = c.OnPrimary; p.Border = Color.Empty;
                    p.Back = !Enabled ? ThemeColors.Blend(b, c.Surface, 35)
                           : _pressed ? ThemeColors.Blend(Color.Black, b, 12)
                           : _hover ? ThemeColors.Blend(Color.White, b, 12)
                           : b;
                    break;
            }

            return p;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            var c = Theme;
            var p = Resolve();
            int radius = ThemeRadius.Resolve(S(_radius), Height);

            ControlStyler.Prepare(g, radius);
            PaintParentBackground(e);

            var body = ClientRectangle;
            ControlStyler.FillSurface(g, body, p.Back, radius);
            ControlStyler.DrawBorder(g, body, p.Border, S(ThemeBorders.Control), radius);

            DrawContent(g, body, p.Fore);

            if (Focused && TabStop)
                ControlStyler.DrawFocusRing(g, body, c, radius);
        }

        private void DrawContent(Graphics g, Rectangle body, Color fore)
        {
            bool hasText = !string.IsNullOrEmpty(Text);
            int size = S(_symbolSize);

            if (_symbol == 0)
            {
                if (hasText)
                    TextRenderer.DrawText(g, Text, Font, body, fore, ControlStyler.TextCenter);
                return;
            }

            if (!hasText)
            {
                g.DrawFontImage(_symbol, size, fore, body);
                return;
            }

            // Icon + chữ: đo chữ rồi căn giữa cả cụm.
            int gap = S(ThemeSpacing.Xs);
            Size textSize = TextRenderer.MeasureText(g, Text, Font, body.Size, ControlStyler.TextLeft);
            int total = size + gap + textSize.Width;
            int x = body.X + Math.Max(0, (body.Width - total) / 2);

            g.DrawFontImage(_symbol, size, fore, new Rectangle(x, body.Y, size, body.Height));
            TextRenderer.DrawText(g, Text, Font,
                new Rectangle(x + size + gap, body.Y, body.Width - (x - body.X) - size - gap, body.Height),
                fore, ControlStyler.TextLeft);
        }

        /// <summary>Kích hoạt như vừa được bấm. Control không có sẵn như Button.</summary>
        public void PerformClick()
        {
            if (Enabled && Visible) OnClick(EventArgs.Empty);
        }

        public override Size GetPreferredSize(Size proposedSize)
        {
            int padX = S(ThemeSpacing.Md) * 2;
            int w = padX;

            if (!string.IsNullOrEmpty(Text))
                w += TextRenderer.MeasureText(Text, Font).Width;
            if (_symbol != 0)
                w += S(_symbolSize) + (string.IsNullOrEmpty(Text) ? 0 : S(ThemeSpacing.Xs));

            return new Size(
                Math.Max(w, S(ThemeMetrics.MinTouchTarget)),
                Math.Max(S(ThemeMetrics.ControlHeight), S(ThemeMetrics.MinTouchTarget)));
        }

        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; _pressed = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left) { _pressed = true; Focus(); Invalidate(); }
            base.OnMouseDown(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            if (_pressed) { _pressed = false; Invalidate(); }
            base.OnMouseUp(e);
        }

        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { _pressed = false; Invalidate(); base.OnLostFocus(e); }
        protected override void OnEnabledChanged(EventArgs e) { _hover = false; _pressed = false; Invalidate(); base.OnEnabledChanged(e); }
        protected override void OnTextChanged(EventArgs e) { Invalidate(); base.OnTextChanged(e); }

        // Control không tự biến Space/Enter thành Click như Button - phải tự nối.
        protected override bool IsInputKey(Keys keyData)
            => keyData == Keys.Space || keyData == Keys.Enter || base.IsInputKey(keyData);

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Space || e.KeyCode == Keys.Enter)
            {
                _pressed = true;
                Invalidate();
                e.Handled = true;
            }
            base.OnKeyDown(e);
        }

        protected override void OnKeyUp(KeyEventArgs e)
        {
            if (_pressed && (e.KeyCode == Keys.Space || e.KeyCode == Keys.Enter))
            {
                _pressed = false;
                Invalidate();
                e.Handled = true;
                OnClick(EventArgs.Empty);
            }
            base.OnKeyUp(e);
        }
    }
}

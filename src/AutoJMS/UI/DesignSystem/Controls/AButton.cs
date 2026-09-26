using System;
using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;

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
        private Image _image;

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

        /// <summary>Mã icon lấy từ <see cref="ASymbols"/>. 0 = không icon.</summary>
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

        /// <summary>
        /// Ảnh bitmap vẽ bên trái chữ — dành cho nút lấy icon từ .resx, thứ mà
        /// <see cref="Symbol"/> (icon dạng font) không biểu diễn được.
        /// Đặt cả hai thì Image thắng. Ảnh được co theo tỉ lệ cho vừa thân nút.
        /// </summary>
        [DefaultValue(null)]
        public Image Image
        {
            get => _image;
            set { if (_image == value) return; _image = value; Invalidate(); }
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

                // Nút viền: lúc nghỉ luôn có viền màu Primary; hover/bấm tô đầy như Primary.
                case AButtonVariant.Secondary:
                case AButtonVariant.Ghost:
                    bool ghost = _variant == AButtonVariant.Ghost;
                    p.Back = ghost ? Color.Empty : c.SurfaceRaised; p.Fore = c.Text; p.Border = c.Primary;
                    if (!Enabled) { p.Back = ghost ? Color.Empty : c.SurfaceAlt; p.Fore = c.TextMuted; p.Border = c.Border; break; }
                    if (_pressed) { p.Back = p.Border = c.PrimaryPressed; p.Fore = c.OnPrimary; }
                    else if (_hover) { p.Back = p.Border = c.Primary; p.Fore = c.OnPrimary; }
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

            if (Focused && TabStop && ShowFocusCues)
                ControlStyler.DrawFocusRing(g, body, c, radius);
        }

        private void DrawContent(Graphics g, Rectangle body, Color fore)
        {
            bool hasText = !string.IsNullOrEmpty(Text);
            Size glyph = GlyphSize(body.Height);

            if (glyph.IsEmpty)
            {
                if (hasText)
                    TextRenderer.DrawText(g, Text, Font, body, fore, ControlStyler.TextCenter);
                return;
            }

            // Icon + chữ: đo chữ rồi căn giữa cả cụm.
            int gap = hasText ? S(ThemeSpacing.Xs) : 0;
            int textWidth = hasText
                ? TextRenderer.MeasureText(g, Text, Font, body.Size, ControlStyler.TextLeft).Width
                : 0;
            int x = body.X + Math.Max(0, (body.Width - (glyph.Width + gap + textWidth)) / 2);
            var glyphRect = new Rectangle(x, body.Y + (body.Height - glyph.Height) / 2, glyph.Width, glyph.Height);

            if (_image != null) g.DrawImage(_image, glyphRect);
            else ASymbols.Draw(g, _symbol, S(_symbolSize), fore, glyphRect);

            if (hasText)
                TextRenderer.DrawText(g, Text, Font,
                    new Rectangle(glyphRect.Right + gap, body.Y, body.Right - glyphRect.Right - gap, body.Height),
                    fore, ControlStyler.TextLeft);
        }

        /// <summary>Kích thước phần icon. Rỗng = nút chỉ có chữ.</summary>
        private Size GlyphSize(int bodyHeight)
        {
            if (_image != null)
            {
                // Ảnh .resx có kích thước tuỳ ý; co theo tỉ lệ cho lọt thân nút, và không quá bậc
                // icon Nav — nút cao ControlHeightLarge mà để ảnh cao theo thân là icon đè cả chữ.
                int max = Math.Max(1, Math.Min(bodyHeight - S(ThemeSpacing.Xs) * 2, S(ThemeMetrics.IconSizeNav)));
                return _image.Height <= max
                    ? _image.Size
                    : new Size(Math.Max(1, _image.Width * max / _image.Height), max);
            }

            if (_symbol == 0) return Size.Empty;
            int size = S(_symbolSize);
            return new Size(size, size);
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

            Size glyph = GlyphSize(Height);
            if (!glyph.IsEmpty)
                w += glyph.Width + (string.IsNullOrEmpty(Text) ? 0 : S(ThemeSpacing.Xs));

            // MinimumSize là pixel thật (Designer đã nhân DPI). FlowLayoutPanel xếp theo kích thước
            // ưu tiên chứ không theo MinimumSize, thiếu hai Max này là nút lớn bị xếp như nút nhỏ.
            return new Size(
                Math.Max(Math.Max(w, S(ThemeMetrics.MinTouchTarget)), MinimumSize.Width),
                Math.Max(Math.Max(S(ThemeMetrics.ControlHeight), S(ThemeMetrics.MinTouchTarget)), MinimumSize.Height));
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

using System;
using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;

namespace AutoJMS.UI.DesignSystem
{
    /// <summary>
    /// Nền chung cho ACheckBox, ARadioButton, AToggleSwitch.
    ///
    /// Ba control này chỉ khác nhau ở hình vẽ và cách gom nhóm; phần trạng thái
    /// bật/tắt, hover, phím Space, đo chữ đều y hệt. Lớp này giữ phần giống,
    /// lớp con vẽ phần khác.
    /// </summary>
    [ToolboxItem(false)]
    public abstract class ACheckControl : AControl
    {
        private bool _checked;
        private bool _hover;

        protected ACheckControl()
        {
            SetStyle(ControlStyles.Selectable, true);
            TabStop = true;
            AutoSize = true;
            Cursor = Cursors.Hand;
            Height = ThemeMetrics.ControlHeight;
        }

        public event EventHandler CheckedChanged;

        [DefaultValue(false)]
        public bool Checked
        {
            get => _checked;
            set
            {
                if (_checked == value) return;
                _checked = value;
                if (value) OnCheckedTrue();
                Invalidate();
                CheckedChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        protected bool Hovered => _hover;

        /// <summary>Kích thước vùng vẽ hình, ở 96 DPI.</summary>
        protected virtual int GlyphWidth => 16;
        protected virtual int GlyphHeight => 16;

        /// <summary>Gọi khi control vừa được bật. ARadioButton dùng để tắt các nút cùng nhóm.</summary>
        protected virtual void OnCheckedTrue() { }

        protected abstract void DrawGlyph(Graphics g, Rectangle glyph, ThemeColors c);

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            var c = Theme;

            // PixelOffsetMode.Half như mọi control A* khác: DrawBorder/FillSurface tính toạ độ theo nó.
            ControlStyler.Prepare(g, 1);
            PaintParentBackground(e);

            int gw = S(GlyphWidth), gh = S(GlyphHeight);
            int inset = S(ThemeBorders.FocusInset);
            var glyph = new Rectangle(inset, (Height - gh) / 2, gw, gh);

            DrawGlyph(g, glyph, c);

            if (!string.IsNullOrEmpty(Text))
            {
                int textX = glyph.Right + S(ThemeSpacing.Sm);
                TextRenderer.DrawText(g, Text, Font,
                    new Rectangle(textX, 0, Width - textX - inset, Height),
                    Enabled ? c.Text : c.TextMuted,
                    ControlStyler.TextLeft);
            }

            if (Focused && TabStop && ShowFocusCues)
                ControlStyler.DrawFocusRing(g, ClientRectangle, c, S(ThemeRadius.Sm));
        }

        public override Size GetPreferredSize(Size proposedSize)
        {
            int w = S(ThemeBorders.FocusInset) * 2 + S(GlyphWidth);
            if (!string.IsNullOrEmpty(Text))
                w += S(ThemeSpacing.Sm) + TextRenderer.MeasureText(Text, Font).Width;

            return new Size(w, Math.Max(S(ThemeMetrics.ControlHeight), S(ThemeMetrics.MinTouchTarget)));
        }

        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left) Focus();
            base.OnMouseDown(e);
        }

        protected override void OnClick(EventArgs e)
        {
            Toggle();
            base.OnClick(e);
        }

        /// <summary>ARadioButton ghi đè: bấm lại nút đang chọn không tắt nó.</summary>
        protected virtual void Toggle() => Checked = !Checked;

        protected override bool IsInputKey(Keys keyData)
            => keyData == Keys.Space || base.IsInputKey(keyData);

        protected override void OnKeyUp(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Space) { Toggle(); e.Handled = true; }
            base.OnKeyUp(e);
        }

        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
        protected override void OnEnabledChanged(EventArgs e) { _hover = false; Invalidate(); base.OnEnabledChanged(e); }

        protected override void OnTextChanged(EventArgs e)
        {
            if (AutoSize) PerformLayout();
            Invalidate();
            base.OnTextChanged(e);
        }
    }
}

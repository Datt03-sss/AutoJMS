using System;
using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;

namespace AutoJMS.UI.DesignSystem
{
    /// <summary>
    /// Ô nhập. Xem DesignReference/AutoJMS.DESIGN.md §L.
    ///
    /// Vỏ tự vẽ, RUỘT là TextBox thật của WinForms.
    /// Cố ý không tự vẽ phần soạn thảo: con trỏ nháy, bôi chọn, clipboard, undo và
    /// nhất là IME tiếng Việt đều nằm trong TextBox. Viết lại phần đó là cách chắc chắn
    /// nhất để làm hỏng việc gõ tiếng Việt của người dùng.
    ///
    /// Placeholder dùng TextBox.PlaceholderText (có sẵn từ .NET Core 3.0) - nó KHÔNG
    /// nằm trong Text, nên không lặp lại lỗi so sánh watermark bằng chuỗi ở frmLogin.cs:69.
    /// </summary>
    [ToolboxItem(true)]
    [DefaultEvent(nameof(TextChanged))]
    public class ATextBox : AControl
    {
        private readonly TextBox _inner;
        private bool _hover;
        private bool _hasError;

        public ATextBox()
        {
            _inner = new TextBox
            {
                BorderStyle = BorderStyle.None,
                Font = ThemeTypography.Body,
                AutoSize = false
            };
            _inner.TextChanged += (s, e) => OnTextChanged(EventArgs.Empty);
            _inner.GotFocus += (s, e) => Invalidate();
            _inner.LostFocus += (s, e) => Invalidate();
            _inner.KeyDown += (s, e) => OnKeyDown(e);
            _inner.KeyPress += (s, e) => OnKeyPress(e);
            _inner.KeyUp += (s, e) => OnKeyUp(e);
            Controls.Add(_inner);

            Size = new Size(180, ThemeMetrics.ControlHeight);
            ApplyInnerColors();
        }

        /// <summary>TextBox bên trong - dùng khi cần API mà lớp vỏ chưa bọc.</summary>
        [Browsable(false)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public TextBox Inner => _inner;

        [Browsable(false)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public override string Text
        {
            get => _inner.Text;
            set => _inner.Text = value;
        }

        [DefaultValue("")]
        public string PlaceholderText
        {
            get => _inner.PlaceholderText;
            set => _inner.PlaceholderText = value ?? string.Empty;
        }

        [DefaultValue(false)]
        public bool ReadOnly
        {
            get => _inner.ReadOnly;
            set { _inner.ReadOnly = value; ApplyInnerColors(); Invalidate(); }
        }

        [DefaultValue(32767)]
        public int MaxLength
        {
            get => _inner.MaxLength;
            set => _inner.MaxLength = value;
        }

        [DefaultValue(false)]
        public bool UseSystemPasswordChar
        {
            get => _inner.UseSystemPasswordChar;
            set => _inner.UseSystemPasswordChar = value;
        }

        [DefaultValue(false)]
        public bool Multiline
        {
            get => _inner.Multiline;
            set { _inner.Multiline = value; PerformLayout(); }
        }

        /// <summary>
        /// Chỉ có tác dụng khi <see cref="Multiline"/> bật. Ô nhập mã vận đơn nhận
        /// hàng trăm mã dán một lần nên PHẢI có thanh cuộn dọc, nếu không người dùng
        /// mất hẳn phần mã nằm dưới đáy ô mà không có dấu hiệu gì.
        /// </summary>
        [DefaultValue(ScrollBars.None)]
        public ScrollBars ScrollBars
        {
            get => _inner.ScrollBars;
            set => _inner.ScrollBars = value;
        }

        [DefaultValue(HorizontalAlignment.Left)]
        public HorizontalAlignment TextAlign
        {
            get => _inner.TextAlign;
            set => _inner.TextAlign = value;
        }

        /// <summary>Viền Danger. Câu lỗi hiện DƯỚI ô, không dùng tooltip (DESIGN.md §L).</summary>
        [DefaultValue(false)]
        public bool HasError
        {
            get => _hasError;
            set { if (_hasError == value) return; _hasError = value; Invalidate(); }
        }

        public void SelectAll() => _inner.SelectAll();

        public override Font Font
        {
            get => base.Font;
            set
            {
                base.Font = value;
                if (_inner != null) { _inner.Font = base.Font; PerformLayout(); }
            }
        }

        protected override void OnThemeChanged()
        {
            ApplyInnerColors();
            Invalidate();
        }

        private void ApplyInnerColors()
        {
            var c = Theme;
            _inner.BackColor = !Enabled || ReadOnly ? c.SurfaceAlt : c.SurfaceRaised;
            _inner.ForeColor = Enabled ? c.Text : c.TextMuted;
        }

        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            if (_inner == null) return;

            int padX = S(ThemeSpacing.Sm);
            int inset = S(ThemeBorders.Control);

            if (_inner.Multiline)
            {
                int padY = S(ThemeSpacing.Xs);
                _inner.Bounds = new Rectangle(padX, padY + inset,
                    Math.Max(1, Width - padX * 2), Math.Max(1, Height - (padY + inset) * 2));
            }
            else
            {
                int h = _inner.PreferredHeight;
                _inner.Bounds = new Rectangle(padX, Math.Max(inset, (Height - h) / 2),
                    Math.Max(1, Width - padX * 2), h);
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var c = Theme;
            int radius = S(ThemeRadius.Sm);
            ControlStyler.Prepare(e.Graphics, radius);
            PaintParentBackground(e);   // góc ngoài khung bo; thiếu là lộ BackColor sáng trong Dark

            bool focused = _inner.Focused;

            Color back = !Enabled || ReadOnly ? c.SurfaceAlt : c.SurfaceRaised;
            Color border = !Enabled ? c.Border
                         : _hasError ? c.Danger
                         : focused ? c.Focus
                         : _hover ? c.Primary
                         : ReadOnly ? c.Border
                         : c.BorderStrong;

            ControlStyler.FillSurface(e.Graphics, ClientRectangle, back, radius);
            ControlStyler.DrawBorder(e.Graphics, ClientRectangle, border,
                focused && !_hasError ? S(ThemeBorders.Focus) : S(ThemeBorders.Control), radius);
        }

        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            _inner.Focus();
            base.OnMouseDown(e);
        }

        protected override void OnEnabledChanged(EventArgs e)
        {
            _inner.Enabled = Enabled;
            ApplyInnerColors();
            Invalidate();
            base.OnEnabledChanged(e);
        }

        protected override void OnGotFocus(EventArgs e)
        {
            base.OnGotFocus(e);
            _inner.Focus();
        }

        protected override void OnPaddingChanged(EventArgs e) { base.OnPaddingChanged(e); PerformLayout(); }
    }
}

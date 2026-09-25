using System;
using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;

namespace AutoJMS.UI.DesignSystem
{
    /// <summary>
    /// Ô tìm kiếm + nút. Xem DesignReference/AutoJMS.DESIGN.md §R.
    ///
    ///  - Ô nhập là control RỘNG NHẤT trong hàng: nó là hành động chính.
    ///  - Enter = Tìm, Esc = xoá ô. Bắt buộc ở mọi search panel.
    ///  - "Tìm" là nút Primary, "Xoá" là Ghost.
    ///  - Số kết quả luôn hiển thị, căn phải.
    /// </summary>
    [ToolboxItem(true)]
    public class SearchPanel : APanel
    {
        private readonly ATextBox _input;
        private readonly AButton _search;
        private readonly AButton _clear;
        private string _resultText = string.Empty;

        private const int SearchSymbol = ASymbols.Search;

        public SearchPanel()
        {
            Radius = ThemeRadius.None;
            Elevation = ThemeShadows.Elevation.Background;
            Padding = new Padding(0);
            Height = ThemeMetrics.ControlHeight;

            _input = new ATextBox { PlaceholderText = "Nhập mã vận đơn..." };
            _input.Inner.KeyDown += OnInputKeyDown;

            _search = new AButton { Variant = AButtonVariant.Primary, Text = "Tìm", Symbol = SearchSymbol };
            _search.Click += (s, e) => RaiseSearch();

            _clear = new AButton { Variant = AButtonVariant.Ghost, Text = "Xoá" };
            _clear.Click += (s, e) => Clear();

            Controls.Add(_input);
            Controls.Add(_search);
            Controls.Add(_clear);
        }

        /// <summary>Người dùng bấm Tìm hoặc nhấn Enter.</summary>
        public event EventHandler Search;

        /// <summary>Ô nhập vừa bị xoá trắng.</summary>
        public event EventHandler Cleared;

        [Browsable(false)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public string Query
        {
            get => _input.Text;
            set => _input.Text = value;
        }

        [DefaultValue("Nhập mã vận đơn...")]
        public string PlaceholderText
        {
            get => _input.PlaceholderText;
            set => _input.PlaceholderText = value;
        }

        [DefaultValue("Tìm")]
        public string SearchText
        {
            get => _search.Text;
            set { _search.Text = value; PerformLayout(); }
        }

        /// <summary>Ví dụ "12 kết quả". Luôn hiển thị, kể cả khi bằng 0.</summary>
        [DefaultValue("")]
        public string ResultText
        {
            get => _resultText;
            set { _resultText = value ?? string.Empty; PerformLayout(); Invalidate(); }
        }

        [Browsable(false)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public ATextBox Input => _input;

        public void Clear()
        {
            _input.Text = string.Empty;
            _input.Inner.Focus();
            Cleared?.Invoke(this, EventArgs.Empty);
        }

        public void FocusInput() => _input.Inner.Focus();

        private void RaiseSearch() => Search?.Invoke(this, EventArgs.Empty);

        private void OnInputKeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                RaiseSearch();
                e.Handled = true;
                e.SuppressKeyPress = true;   // chặn tiếng "ding" của WinForms
            }
            else if (e.KeyCode == Keys.Escape)
            {
                Clear();
                e.Handled = true;
                e.SuppressKeyPress = true;
            }
        }

        private int ResultWidth()
            => string.IsNullOrEmpty(_resultText)
                ? 0
                : TextRenderer.MeasureText(_resultText, ThemeTypography.Small).Width + S(ThemeSpacing.Md);

        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            if (_input == null) return;

            int gap = S(ThemeSpacing.Sm);
            int h = S(ThemeMetrics.ControlHeight);
            int y = (Height - h) / 2;

            var searchSize = _search.GetPreferredSize(Size.Empty);
            var clearSize = _clear.GetPreferredSize(Size.Empty);

            int right = Width - Padding.Right - ResultWidth();
            int inputW = Math.Max(S(80),
                right - Padding.Left - searchSize.Width - clearSize.Width - gap * 2);

            int x = Padding.Left;
            _input.Bounds = new Rectangle(x, y, inputW, h);
            x += inputW + gap;
            _search.Bounds = new Rectangle(x, y, searchSize.Width, h);
            x += searchSize.Width + gap;
            _clear.Bounds = new Rectangle(x, y, clearSize.Width, h);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            if (string.IsNullOrEmpty(_resultText)) return;

            int w = ResultWidth();
            TextRenderer.DrawText(e.Graphics, _resultText, ThemeTypography.Small,
                new Rectangle(Width - Padding.Right - w, 0, w, Height),
                Theme.TextSecondary, ControlStyler.TextRight);
        }
    }
}

using System;
using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;

namespace AutoJMS.UI.DesignSystem
{
    /// <summary>
    /// Hộp chọn. Xem DesignReference/AutoJMS.DESIGN.md §L.
    ///
    /// Trạng thái đóng tự vẽ hoàn toàn; danh sách thả xuống là ToolStripDropDown (BCL)
    /// bọc một ListBox vẽ tay.
    ///
    /// Vì sao không dùng thẳng ComboBox: dù đặt FlatStyle.Flat, ComboBox vẫn tự vẽ
    /// viền và mũi tên bằng màu hệ thống, không đổi được - ở theme Dark nó hiện ra
    /// như một mảng sáng. Vì sao không tự viết cả danh sách: ToolStripDropDown đã lo
    /// cửa sổ top-level, bắt chuột ngoài vùng và phím Esc; ListBox đã lo cuộn,
    /// điều hướng bàn phím và dò item theo toạ độ. Chỉ phần pixel là của mình.
    /// </summary>
    [ToolboxItem(true)]
    [DefaultEvent(nameof(SelectedIndexChanged))]
    public class AComboBox : AControl
    {
        private readonly ListBox _list;
        private readonly ToolStripDropDown _dropDown;
        private readonly ToolStripControlHost _host;
        private bool _hover;
        private bool _dropped;

        private const int MaxVisibleItems = 10;

        public AComboBox()
        {
            SetStyle(ControlStyles.Selectable, true);
            TabStop = true;
            Cursor = Cursors.Hand;
            Size = new Size(180, ThemeMetrics.ControlHeight);

            _list = new ListBox
            {
                BorderStyle = BorderStyle.None,
                DrawMode = DrawMode.OwnerDrawFixed,
                IntegralHeight = false,
                Font = ThemeTypography.Body
            };
            _list.DrawItem += OnDrawItem;
            _list.MouseUp += (s, e) => CommitAndClose();
            _list.KeyDown += OnListKeyDown;

            _host = new ToolStripControlHost(_list)
            {
                Margin = Padding.Empty,
                Padding = Padding.Empty,
                AutoSize = false
            };

            _dropDown = new ToolStripDropDown
            {
                AutoClose = true,
                DropShadowEnabled = false,   // DESIGN.md §I - không đổ bóng
                Padding = Padding.Empty,
                Margin = Padding.Empty
            };
            _dropDown.Items.Add(_host);
            _dropDown.Closed += (s, e) => { _dropped = false; Invalidate(); };
        }

        public event EventHandler SelectedIndexChanged;

        [Browsable(false)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Content)]
        public ListBox.ObjectCollection Items => _list.Items;

        [Browsable(false)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public int SelectedIndex
        {
            get => _list.SelectedIndex;
            set
            {
                if (value < -1 || value >= _list.Items.Count) return;
                if (_list.SelectedIndex == value) return;
                _list.SelectedIndex = value;
                Invalidate();
                SelectedIndexChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        [Browsable(false)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public object SelectedItem
        {
            get => _list.SelectedItem;
            set => SelectedIndex = _list.Items.IndexOf(value);
        }

        [Browsable(false)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public override string Text
        {
            get => SelectedItem?.ToString() ?? string.Empty;
            set => SelectedIndex = _list.Items.IndexOf(value);
        }

        [DefaultValue("")]
        public string PlaceholderText { get; set; } = string.Empty;

        protected override void OnThemeChanged()
        {
            _list.BackColor = Theme.SurfaceRaised;
            Invalidate();
        }

        // ---- vẽ trạng thái đóng ----

        protected override void OnPaint(PaintEventArgs e)
        {
            var c = Theme;
            var g = e.Graphics;
            int radius = S(ThemeRadius.Sm);

            ControlStyler.Prepare(g, radius);

            Color back = Enabled ? c.SurfaceRaised : c.SurfaceAlt;
            Color border = !Enabled ? c.Border
                         : _dropped || Focused ? c.Focus
                         : _hover ? c.Primary
                         : c.BorderStrong;

            ControlStyler.FillSurface(g, ClientRectangle, back, radius);
            ControlStyler.DrawBorder(g, ClientRectangle, border,
                (_dropped || Focused) ? S(ThemeBorders.Focus) : S(ThemeBorders.Control), radius);

            int padX = S(ThemeSpacing.Sm);
            int chevron = S(ThemeMetrics.IconSizeDense);

            bool empty = SelectedIndex < 0;
            string text = empty ? PlaceholderText : Text;
            Color fore = !Enabled ? c.TextMuted : empty ? c.TextMuted : c.Text;

            TextRenderer.DrawText(g, text, Font,
                new Rectangle(padX, 0, Math.Max(1, Width - padX * 2 - chevron), Height),
                fore, ControlStyler.TextLeft);

            DrawChevron(g, new Rectangle(Width - padX - chevron, 0, chevron, Height),
                Enabled ? c.TextSecondary : c.TextMuted);
        }

        private void DrawChevron(Graphics g, Rectangle r, Color color)
        {
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            int w = S(8), h = S(4);
            int x = r.X + (r.Width - w) / 2;
            int y = r.Y + (r.Height - h) / 2;

            using (var pen = new Pen(color, S(ThemeBorders.Focus)))
            {
                pen.StartCap = System.Drawing.Drawing2D.LineCap.Round;
                pen.EndCap = System.Drawing.Drawing2D.LineCap.Round;
                pen.LineJoin = System.Drawing.Drawing2D.LineJoin.Round;
                g.DrawLines(pen, new[]
                {
                    new Point(x, y),
                    new Point(x + w / 2, y + h),
                    new Point(x + w, y)
                });
            }
        }

        private void OnDrawItem(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0) return;

            var c = Theme;
            bool selected = (e.State & DrawItemState.Selected) == DrawItemState.Selected;

            using (var brush = new SolidBrush(selected ? c.PrimaryTint : c.SurfaceRaised))
                e.Graphics.FillRectangle(brush, e.Bounds);

            var text = _list.Items[e.Index]?.ToString() ?? string.Empty;
            var r = new Rectangle(e.Bounds.X + S(ThemeSpacing.Sm), e.Bounds.Y,
                e.Bounds.Width - S(ThemeSpacing.Sm) * 2, e.Bounds.Height);

            TextRenderer.DrawText(e.Graphics, text, _list.Font, r, c.Text, ControlStyler.TextLeft);
        }

        // ---- mở / đóng ----

        private void ToggleDropDown()
        {
            if (_dropped) { _dropDown.Close(); return; }
            if (_list.Items.Count == 0 || !Enabled) return;

            var c = Theme;
            _list.BackColor = c.SurfaceRaised;
            _list.ItemHeight = S(ThemeMetrics.ControlHeight) - S(ThemeSpacing.Xs);

            int visible = Math.Min(_list.Items.Count, MaxVisibleItems);
            var size = new Size(Width, _list.ItemHeight * visible + S(ThemeSpacing.Xs));

            _list.Size = size;
            _host.Size = size;
            _dropDown.Size = size;

            _dropped = true;
            Invalidate();
            _dropDown.Show(this, new Point(0, Height));
            _list.Focus();
        }

        private void CommitAndClose()
        {
            int index = _list.SelectedIndex;
            _dropDown.Close();
            Focus();

            if (index < 0) return;
            Invalidate();
            SelectedIndexChanged?.Invoke(this, EventArgs.Empty);
        }

        private void OnListKeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter) { CommitAndClose(); e.Handled = true; }
            else if (e.KeyCode == Keys.Escape) { _dropDown.Close(); Focus(); e.Handled = true; }
        }

        // ---- chuột / bàn phím trên trạng thái đóng ----

        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left) { Focus(); ToggleDropDown(); }
            base.OnMouseDown(e);
        }

        protected override bool IsInputKey(Keys keyData)
        {
            switch (keyData & Keys.KeyCode)
            {
                case Keys.Up:
                case Keys.Down:
                case Keys.Space:
                case Keys.F4:
                    return true;
                default:
                    return base.IsInputKey(keyData);
            }
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            // Alt+Down hoặc F4 mở danh sách - quy ước Windows.
            if (e.KeyCode == Keys.F4 || (e.Alt && e.KeyCode == Keys.Down) || e.KeyCode == Keys.Space)
            {
                ToggleDropDown();
                e.Handled = true;
            }
            else if (e.KeyCode == Keys.Down && SelectedIndex < _list.Items.Count - 1)
            {
                SelectedIndex++;
                e.Handled = true;
            }
            else if (e.KeyCode == Keys.Up && SelectedIndex > 0)
            {
                SelectedIndex--;
                e.Handled = true;
            }
            base.OnKeyDown(e);
        }

        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
        protected override void OnEnabledChanged(EventArgs e) { _hover = false; Invalidate(); base.OnEnabledChanged(e); }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _dropDown?.Dispose();
                _host?.Dispose();
                _list?.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}

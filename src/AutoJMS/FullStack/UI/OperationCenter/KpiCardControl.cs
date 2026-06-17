using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace AutoJMS.FullStack.UI.OperationCenter
{
    public sealed class KpiCardControl : UserControl
    {
        private readonly Panel _accent;
        private readonly Label _title;
        private readonly Label _value;
        private readonly Label _subtitle;
        private readonly Label _badge;
        private Color _accentColor = Color.FromArgb(37, 99, 235);
        private string _filterKey = string.Empty;

        public event EventHandler<OperationQueueSelectedEventArgs> CardClicked;

        public KpiCardControl()
        {
            DoubleBuffered = true;
            BackColor = Color.White;
            Cursor = Cursors.Hand;
            Margin = new Padding(0, 0, 8, 0);
            Padding = new Padding(12, 10, 12, 10);
            MinimumSize = new Size(120, 82);

            _accent = new Panel { Dock = DockStyle.Left, Width = 5, BackColor = _accentColor };

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 3,
                Padding = new Padding(10, 0, 0, 0),
                Margin = Padding.Empty,
                BackColor = Color.Transparent
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 58F));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 24F));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 22F));

            _title = new Label
            {
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                Font = new Font("Segoe UI Semibold", 9.5F, FontStyle.Bold),
                ForeColor = Color.FromArgb(75, 85, 99),
                AutoEllipsis = true
            };

            _badge = new Label
            {
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleCenter,
                Font = new Font("Segoe UI Semibold", 8F, FontStyle.Bold),
                ForeColor = Color.White,
                BackColor = _accentColor,
                AutoEllipsis = true
            };

            _value = new Label
            {
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                Font = new Font("Segoe UI Semibold", 24F, FontStyle.Bold),
                ForeColor = Color.FromArgb(17, 24, 39),
                AutoEllipsis = true
            };

            _subtitle = new Label
            {
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                Font = new Font("Segoe UI", 8.5F),
                ForeColor = Color.FromArgb(107, 114, 128),
                AutoEllipsis = true
            };

            layout.Controls.Add(_title, 0, 0);
            layout.Controls.Add(_badge, 1, 0);
            layout.Controls.Add(_value, 0, 1);
            layout.SetColumnSpan(_value, 2);
            layout.Controls.Add(_subtitle, 0, 2);
            layout.SetColumnSpan(_subtitle, 2);

            Controls.Add(layout);
            Controls.Add(_accent);
            WireClick(this);
        }

        public void SetMetric(string title, string value, string subtitle, string badge, Color accentColor, string filterKey)
        {
            _title.Text = title ?? string.Empty;
            _value.Text = value ?? "0";
            _subtitle.Text = subtitle ?? string.Empty;
            _badge.Text = badge ?? string.Empty;
            _accentColor = accentColor;
            _filterKey = filterKey ?? string.Empty;
            _accent.BackColor = _accentColor;
            _badge.BackColor = _accentColor;
        }

        private void WireClick(Control control)
        {
            control.Click += (s, e) =>
            {
                if (!string.IsNullOrWhiteSpace(_filterKey))
                    CardClicked?.Invoke(this, new OperationQueueSelectedEventArgs(_filterKey));
            };
            foreach (Control child in control.Controls)
                WireClick(child);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            using var pen = new Pen(Color.FromArgb(217, 226, 236));
            var rect = ClientRectangle;
            rect.Width -= 1;
            rect.Height -= 1;
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.DrawRectangle(pen, rect);
        }
    }
}

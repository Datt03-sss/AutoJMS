using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;

namespace AutoJMS.FullStack.UI.OperationCenter
{
    public sealed class QueueSidebarControl : UserControl
    {
        private readonly FlowLayoutPanel _list;
        private readonly Label _subtitle;
        private readonly Label _footer;

        public event EventHandler<OperationQueueSelectedEventArgs> QueueSelected;

        public QueueSidebarControl()
        {
            DoubleBuffered = true;
            BackColor = Color.White;
            Padding = new Padding(12);

            var header = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                Height = 58,
                ColumnCount = 1,
                RowCount = 2,
                Margin = Padding.Empty,
                Padding = Padding.Empty,
                BackColor = Color.White
            };
            header.RowStyles.Add(new RowStyle(SizeType.Absolute, 28F));
            header.RowStyles.Add(new RowStyle(SizeType.Absolute, 30F));

            var title = new Label
            {
                Dock = DockStyle.Fill,
                Text = "ACTION QUEUES",
                TextAlign = ContentAlignment.MiddleLeft,
                Font = new Font("Segoe UI Semibold", 10F, FontStyle.Bold),
                ForeColor = Color.FromArgb(17, 24, 39)
            };

            _subtitle = new Label
            {
                Dock = DockStyle.Fill,
                Text = "Luồng ưu tiên cho điều phối tồn",
                TextAlign = ContentAlignment.TopLeft,
                Font = new Font("Segoe UI", 8.5F),
                ForeColor = Color.FromArgb(107, 114, 128),
                AutoEllipsis = true
            };
            header.Controls.Add(title, 0, 0);
            header.Controls.Add(_subtitle, 0, 1);

            _list = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                AutoScroll = true,
                BackColor = Color.Transparent,
                Padding = new Padding(0, 6, 0, 4)
            };

            _footer = new Label
            {
                Dock = DockStyle.Bottom,
                Height = 28,
                TextAlign = ContentAlignment.MiddleLeft,
                Font = new Font("Segoe UI Semibold", 8.3F, FontStyle.Bold),
                ForeColor = Color.FromArgb(75, 85, 99),
                AutoEllipsis = true
            };

            Controls.Add(_list);
            Controls.Add(_footer);
            Controls.Add(header);
        }

        public void SetQueues(IEnumerable<OperationQueueItem> queues)
        {
            _list.SuspendLayout();
            try
            {
                _list.Controls.Clear();
                var items = queues?.ToList() ?? new List<OperationQueueItem>();
                foreach (var item in items)
                    _list.Controls.Add(CreateQueueButton(item));

                var active = items.FirstOrDefault(x => x.Active)?.Title ?? "Tất cả";
                var openQueues = items.Count(x => x.Key != "Tất cả tồn kho" && x.Count > 0);
                _footer.Text = $"Focus: {active} | queues mở: {openQueues:N0}";
            }
            finally
            {
                _list.ResumeLayout(true);
            }
        }

        private Control CreateQueueButton(OperationQueueItem item)
        {
            var panel = new Panel
            {
                Width = Math.Max(180, Width - 28),
                Height = 58,
                Margin = new Padding(0, 0, 0, 7),
                Padding = new Padding(0),
                BackColor = item.Active ? Color.FromArgb(17, 24, 39) : Color.FromArgb(249, 250, 251),
                Tag = item.Key,
                Cursor = Cursors.Hand
            };
            panel.Paint += (s, e) => PaintQueueBorder(e.Graphics, panel.ClientRectangle, item.Active);

            var colorBar = new Panel
            {
                Dock = DockStyle.Left,
                Width = item.Active ? 7 : 5,
                BackColor = item.AccentColor
            };

            var count = new Label
            {
                Dock = DockStyle.Right,
                Width = 58,
                Text = item.Count.ToString("N0"),
                TextAlign = ContentAlignment.MiddleCenter,
                Font = new Font("Segoe UI Semibold", 10.5F, FontStyle.Bold),
                ForeColor = item.Active ? Color.White : item.AccentColor,
                AutoEllipsis = true
            };

            var title = new Label
            {
                Dock = DockStyle.Top,
                Height = 28,
                Padding = new Padding(10, 5, 4, 0),
                Text = item.Title,
                TextAlign = ContentAlignment.BottomLeft,
                Font = new Font("Segoe UI Semibold", 9.2F, FontStyle.Bold),
                ForeColor = item.Active ? Color.White : Color.FromArgb(17, 24, 39),
                AutoEllipsis = true
            };

            var desc = new Label
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(10, 0, 4, 5),
                Text = item.Description,
                TextAlign = ContentAlignment.TopLeft,
                Font = new Font("Segoe UI", 8F),
                ForeColor = item.Active ? Color.FromArgb(229, 231, 235) : Color.FromArgb(107, 114, 128),
                AutoEllipsis = true
            };

            panel.Controls.Add(desc);
            panel.Controls.Add(title);
            panel.Controls.Add(count);
            panel.Controls.Add(colorBar);
            WireClick(panel, item.Key);
            return panel;
        }

        private static void PaintQueueBorder(Graphics graphics, Rectangle bounds, bool active)
        {
            var rect = bounds;
            rect.Width -= 1;
            rect.Height -= 1;
            using var pen = new Pen(active ? Color.FromArgb(17, 24, 39) : Color.FromArgb(217, 226, 236));
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.DrawRectangle(pen, rect);
        }

        private void WireClick(Control root, string key)
        {
            root.Click += (s, e) => QueueSelected?.Invoke(this, new OperationQueueSelectedEventArgs(key));
            foreach (Control child in root.Controls)
                WireClick(child, key);
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            foreach (Control child in _list.Controls)
                child.Width = Math.Max(180, Width - 28);
        }
    }
}

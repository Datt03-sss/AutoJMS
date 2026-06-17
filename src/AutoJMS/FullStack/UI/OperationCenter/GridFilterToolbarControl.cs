using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace AutoJMS.FullStack.UI.OperationCenter
{
    public sealed class GridFilterToolbarControl : UserControl
    {
        private readonly Label _title;
        private readonly FlowLayoutPanel _filters;
        private readonly Button _saveView;
        private readonly Button _applyView;
        private readonly Dictionary<string, Button> _buttons = new(StringComparer.OrdinalIgnoreCase);

        public event EventHandler<OperationGridFilterEventArgs> FilterRequested;
        public event EventHandler<OperationGridFilterEventArgs> PresetRequested;

        public GridFilterToolbarControl()
        {
            DoubleBuffered = true;
            Dock = DockStyle.Fill;
            Height = 64;
            BackColor = Color.White;
            Padding = new Padding(10, 6, 10, 6);

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 2,
                Margin = Padding.Empty,
                Padding = Padding.Empty,
                BackColor = Color.White
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 170F));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 22F));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

            _title = new Label
            {
                Dock = DockStyle.Fill,
                Text = "Grid filters - chỉ lọc danh sách, không thao tác đơn",
                TextAlign = ContentAlignment.MiddleLeft,
                Font = new Font("Segoe UI Semibold", 9F, FontStyle.Bold),
                ForeColor = Color.FromArgb(17, 24, 39),
                AutoEllipsis = true
            };

            _filters = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                Margin = Padding.Empty,
                Padding = new Padding(0, 2, 0, 0),
                BackColor = Color.White
            };

            AddFilter("Tất cả", "Tất cả tồn kho", Color.FromArgb(75, 85, 99), 56);
            AddFilter("Cần XL", "Cần xử lý ngay", Color.FromArgb(245, 158, 11), 62);
            AddFilter("SLA", "SLA quá hạn", Color.FromArgb(245, 158, 11), 48);
            AddFilter(">48h", "Tồn quá hạn (>48h)", Color.FromArgb(124, 58, 237), 52);
            AddFilter("KVD", "Giao thất bại", Color.FromArgb(37, 99, 235), 48);
            AddFilter("Checked", "Checked", Color.FromArgb(22, 163, 74), 68);
            AddFilter("Task", "Has task", Color.FromArgb(245, 158, 11), 54);
            AddFilter("Enriched", "Enriched", Color.FromArgb(37, 99, 235), 70);

            var presets = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.RightToLeft,
                WrapContents = false,
                Margin = Padding.Empty,
                Padding = Padding.Empty,
                BackColor = Color.White
            };
            _applyView = AddPresetButton(presets, "Apply", "APPLY_PRESET", Color.FromArgb(31, 41, 55));
            _saveView = AddPresetButton(presets, "Save view", "SAVE_PRESET", Color.FromArgb(107, 114, 128));

            layout.Controls.Add(_title, 0, 0);
            layout.Controls.Add(presets, 1, 0);
            layout.Controls.Add(_filters, 0, 1);
            layout.SetColumnSpan(_filters, 2);
            Controls.Add(layout);
        }

        public void SetActiveFilter(string filterKey, bool hasPreset)
        {
            foreach (var pair in _buttons)
            {
                bool active = string.Equals(pair.Key, filterKey, StringComparison.OrdinalIgnoreCase)
                    || (string.IsNullOrWhiteSpace(filterKey) && pair.Key == "Tất cả tồn kho");
                pair.Value.BackColor = active ? Color.FromArgb(17, 24, 39) : Color.White;
                pair.Value.ForeColor = active ? Color.White : (Color)pair.Value.Tag;
                pair.Value.FlatAppearance.BorderColor = active ? Color.FromArgb(17, 24, 39) : Color.FromArgb(217, 226, 236);
            }

            _applyView.Enabled = hasPreset;
        }

        private void AddFilter(string text, string filterKey, Color color, int width)
        {
            var button = CreateButton(text, filterKey, color, width);
            button.UseMnemonic = false;
            button.Click += (s, e) => FilterRequested?.Invoke(this, new OperationGridFilterEventArgs(filterKey));
            _buttons[filterKey] = button;
            _filters.Controls.Add(button);
        }

        private Button AddPresetButton(FlowLayoutPanel parent, string text, string action, Color color)
        {
            var button = CreateButton(text, action, color, 76);
            button.Click += (s, e) => PresetRequested?.Invoke(this, new OperationGridFilterEventArgs(action));
            parent.Controls.Add(button);
            return button;
        }

        private static Button CreateButton(string text, string key, Color color, int width)
        {
            var button = new Button
            {
                Text = text,
                Name = key,
                Tag = color,
                Width = width,
                Height = 28,
                Margin = new Padding(0, 0, 4, 0),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.White,
                ForeColor = color,
                Font = new Font("Segoe UI Semibold", 8.2F, FontStyle.Bold)
            };
            button.FlatAppearance.BorderColor = Color.FromArgb(217, 226, 236);
            button.FlatAppearance.BorderSize = 1;
            return button;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            using var pen = new Pen(Color.FromArgb(217, 226, 236));
            var rect = ClientRectangle;
            rect.Width -= 1;
            rect.Height -= 1;
            e.Graphics.DrawRectangle(pen, rect);
        }
    }
}

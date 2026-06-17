using System.Drawing;
using System.Windows.Forms;

namespace AutoJMS.FullStack.UI.OperationCenter
{
    public sealed class StatusFooterControl : UserControl
    {
        private readonly Label _left;
        private readonly Label _center;
        private readonly Label _right;

        public StatusFooterControl()
        {
            Height = 32;
            Dock = DockStyle.Fill;
            BackColor = Color.FromArgb(17, 24, 39);

            _left = new Label
            {
                Dock = DockStyle.Left,
                Width = 340,
                Padding = new Padding(12, 0, 0, 0),
                TextAlign = ContentAlignment.MiddleLeft,
                Font = new Font("Segoe UI Semibold", 8.8F, FontStyle.Bold),
                ForeColor = Color.FromArgb(230, 235, 240)
            };

            _center = new Label
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(8, 0, 8, 0),
                TextAlign = ContentAlignment.MiddleLeft,
                Font = new Font("Segoe UI", 8.5F),
                ForeColor = Color.FromArgb(209, 213, 219),
                Text = "SQLite local-first | Supabase giữ manifest/config/update"
            };

            _right = new Label
            {
                Dock = DockStyle.Right,
                Width = 560,
                Padding = new Padding(0, 0, 12, 0),
                TextAlign = ContentAlignment.MiddleRight,
                Font = new Font("Segoe UI Semibold", 8.8F, FontStyle.Bold),
                ForeColor = Color.White
            };

            Controls.Add(_center);
            Controls.Add(_left);
            Controls.Add(_right);
        }

        public void SetStatus(string left, string right)
        {
            _left.Text = left ?? string.Empty;
            _right.Text = right ?? string.Empty;
        }
    }
}

using Sunny.UI;
using System;
using System.Drawing;
using System.Windows.Forms;

namespace AutoJMS
{
    public sealed class TermsDialog : UIForm
    {
        public TermsDialog()
        {
            InitializeDialog();
        }

        private void InitializeDialog()
        {
            Text = "Điều khoản sử dụng & Chính sách bảo mật";
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            ClientSize = new Size(680, 700);
            BackColor = Color.FromArgb(246, 248, 250);
            Font = new Font("Segoe UI", 9F, FontStyle.Regular);

            // ── Header bar ────────────────────────────────────────
            var header = new UIPanel
            {
                Location = new Point(18, 18),
                Size = new Size(644, 64),
                FillColor = Color.White,
                RectColor = Color.FromArgb(222, 226, 230)
            };
            Controls.Add(header);

            var logo = new PictureBox
            {
                Location = new Point(16, 8),
                Size = new Size(48, 48),
                SizeMode = PictureBoxSizeMode.Zoom
            };
            try
            {
                logo.Image = Properties.Resources._64x64;
            }
            catch
            {
                logo.BackColor = Color.FromArgb(232, 245, 233);
            }
            header.Controls.Add(logo);

            header.Controls.Add(new UILabel
            {
                Text = "Điều khoản sử dụng & Chính sách bảo mật",
                Location = new Point(76, 8),
                Size = new Size(550, 28),
                Font = new Font("Segoe UI Semibold", 14F, FontStyle.Bold),
                ForeColor = Color.FromArgb(33, 37, 41),
                TextAlign = ContentAlignment.MiddleLeft
            });

            header.Controls.Add(new UILabel
            {
                Text = "AutoJMS — Cập nhật: 2026",
                Location = new Point(76, 38),
                Size = new Size(300, 18),
                Font = new Font("Segoe UI", 9F),
                ForeColor = Color.FromArgb(108, 117, 125),
                TextAlign = ContentAlignment.MiddleLeft
            });

            // ── Scrollable content panel ──────────────────────────
            var scrollPanel = new UIPanel
            {
                Location = new Point(18, 94),
                Size = new Size(644, 490),
                FillColor = Color.White,
                RectColor = Color.FromArgb(222, 226, 230)
            };
            Controls.Add(scrollPanel);

            // ── Rich text content ─────────────────────────────────
            var content = new UIRichTextBox
            {
                Location = new Point(20, 14),
                Size = new Size(604, 462),
                ReadOnly = true,
                BackColor = Color.White,
                Font = new Font("Segoe UI", 9.5F, FontStyle.Regular),
                ForeColor = Color.FromArgb(52, 58, 64),
                WordWrap = true
            };

            var terms = TermsContentProvider.GetTermsText();

            content.Text = terms;
            scrollPanel.Controls.Add(content);

            // ── Footer: Đóng button ──────────────────────────────
            var closeBtn = new UIButton
            {
                Location = new Point(264, 598),
                Size = new Size(152, 40),
                Text = "Đóng",
                FillColor = Color.FromArgb(108, 117, 125),
                FillHoverColor = Color.FromArgb(173, 181, 189),
                FillPressColor = Color.FromArgb(73, 80, 87),
                RectColor = Color.FromArgb(73, 80, 87),
                Font = new Font("Segoe UI Semibold", 10F, FontStyle.Bold),
                ForeColor = Color.White,
                Radius = 8
            };
            closeBtn.Click += (_, _) => Close();
            Controls.Add(closeBtn);

            CancelButton = closeBtn;

            // Close on ESC
            KeyPreview = true;
            KeyDown += (_, e) =>
            {
                if (e.KeyCode == Keys.Escape)
                    Close();
            };

            // Close on X button
            FormClosing += (_, e) =>
            {
                if (DialogResult == DialogResult.None)
                    DialogResult = DialogResult.Cancel;
            };
        }
    }
}

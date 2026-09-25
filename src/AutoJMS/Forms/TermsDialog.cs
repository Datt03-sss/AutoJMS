using AutoJMS.UI.DesignSystem;
using System;
using System.Drawing;
using System.Windows.Forms;

namespace AutoJMS
{
    public sealed class TermsDialog : Form
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
            BackColor = ThemeManager.Current.SurfaceAlt;
            Font = ThemeTypography.Body;

            // ── Header bar ────────────────────────────────────────
            // APanel mặc định đã là Elevation.Flat = nền SurfaceRaised + viền Hairline,
            // đúng cặp FillColor trắng / RectColor xám mà UIPanel đặt tay trước đây.
            var header = new APanel
            {
                Location = new Point(18, 18),
                Size = new Size(644, 64)
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

            header.Controls.Add(new Label
            {
                Text = "Điều khoản sử dụng & Chính sách bảo mật",
                Location = new Point(76, 8),
                Size = new Size(550, 28),
                Font = new Font(ThemeTypography.FamilySemibold, 14F, FontStyle.Bold),
                ForeColor = ThemeManager.Current.Text,
                TextAlign = ContentAlignment.MiddleLeft
            });

            header.Controls.Add(new Label
            {
                Text = "AutoJMS — Cập nhật: 2026",
                Location = new Point(76, 38),
                Size = new Size(300, 18),
                Font = ThemeTypography.Grid,
                ForeColor = ThemeManager.Current.TextSecondary,
                TextAlign = ContentAlignment.MiddleLeft
            });

            // ── Scrollable content panel ──────────────────────────
            var scrollPanel = new APanel
            {
                Location = new Point(18, 94),
                Size = new Size(644, 490)
            };
            Controls.Add(scrollPanel);

            // ── Rich text content ─────────────────────────────────
            var content = new RichTextBox
            {
                Location = new Point(20, 14),
                Size = new Size(604, 462),
                ReadOnly = true,
                BorderStyle = BorderStyle.None,
                BackColor = ThemeManager.Current.SurfaceRaised,
                Font = new Font(ThemeTypography.Family, 9.5F, FontStyle.Regular),
                ForeColor = ThemeManager.Current.Text,
                WordWrap = true
            };

            var terms = TermsContentProvider.GetTermsText();

            content.Text = terms;
            scrollPanel.Controls.Add(content);

            // ── Footer: Đóng button ──────────────────────────────
            var closeBtn = new AButton
            {
                Location = new Point(264, 598),
                Size = new Size(152, 40),
                Text = "Đóng",
                Variant = AButtonVariant.Secondary,
                Radius = ThemeRadius.Lg
            };
            closeBtn.Click += (_, _) => Close();
            Controls.Add(closeBtn);

            // CancelButton bỏ: AButton không phải IButtonControl, và nhánh KeyDown ngay
            // dưới đã làm đúng việc đó — Esc gọi Close().

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

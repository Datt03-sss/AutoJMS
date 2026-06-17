using Sunny.UI;
using System;
using System.Drawing;
using System.Windows.Forms;

namespace AutoJMS
{
    public enum UpdateChannelChoice
    {
        Cancel,
        Stable,
        Beta
    }

    public sealed class UpdateChannelDialog : UIForm
    {
        private readonly string _currentVersion;
        private readonly VersionChannel _stable;
        private readonly VersionChannel _beta;

        public UpdateChannelChoice Choice { get; private set; } = UpdateChannelChoice.Cancel;

        public UpdateChannelDialog(string currentVersion, VersionChannel stable, VersionChannel beta)
        {
            _currentVersion = string.IsNullOrWhiteSpace(currentVersion) ? "Không có thông tin" : currentVersion.Trim();
            _stable = stable;
            _beta = beta;

            InitializeDialog();
        }

        public static bool ConfirmDowngrade(
            IWin32Window owner,
            string currentVersion,
            string targetVersion,
            VersionChannel targetChannel)
        {
            using var dialog = new DowngradeConfirmDialog(currentVersion, targetVersion, targetChannel);
            return dialog.ShowDialog(owner) == DialogResult.OK;
        }

        private void InitializeDialog()
        {
            Text = "Chọn kênh cập nhật";
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            ClientSize = new Size(780, 520);
            BackColor = Color.FromArgb(246, 248, 250);
            Font = new Font("Segoe UI", 9F, FontStyle.Regular);

            var header = new UIPanel
            {
                Location = new Point(18, 18),
                Size = new Size(744, 80),
                FillColor = Color.White,
                RectColor = Color.FromArgb(222, 226, 230),
                BackColor = BackColor,
                Radius = 6
            };
            Controls.Add(header);

            var logo = new PictureBox
            {
                Location = new Point(18, 16),
                Size = new Size(50, 50),
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

            header.Controls.Add(CreateTextLabel(
                "Chọn kênh cập nhật",
                new Point(82, 14),
                new Size(420, 28),
                new Font("Segoe UI Semibold", 16F, FontStyle.Bold),
                Color.FromArgb(33, 37, 41)));

            header.Controls.Add(CreateTextLabel(
                $"Phiên bản hiện tại: {_currentVersion}",
                new Point(84, 48),
                new Size(610, 22),
                new Font("Segoe UI", 10F, FontStyle.Regular),
                Color.FromArgb(73, 80, 87)));

            // Determine states before building cards
            var betaEnabled = IsBetaSelectable(_currentVersion, _beta);
            var stableEnabled = IsStableSelectable(_currentVersion, _stable);

            // Stable card with embedded button
            var stableCard = CreateChannelCardWithButton(
                "Stable",
                "Bản ổn định, khuyến nghị",
                _stable,
                Color.FromArgb(232, 245, 233),
                Color.FromArgb(76, 175, 80),
                new Point(18, 112),
                stableEnabled ? "Cập nhật Stable" : "Đang là bản mới nhất",
                stableEnabled,
                isBeta: false);
            stableCard.Tag = "stable";
            Controls.Add(stableCard);

            // Beta card with embedded button
            var betaCard = CreateChannelCardWithButton(
                "Beta",
                "Bản thử nghiệm",
                _beta,
                Color.FromArgb(232, 244, 255),
                Color.FromArgb(30, 136, 229),
                new Point(396, 112),
                betaEnabled ? "Cập nhật Beta" : "Không có beta mới",
                betaEnabled,
                isBeta: true,
                warningText: betaEnabled ? "Beta có thể chưa ổn định. Chỉ dùng khi bạn cần test bản mới." : null);
            betaCard.Tag = "beta";
            Controls.Add(betaCard);

            // Close on ESC
            KeyPreview = true;
            KeyDown += (_, e) =>
            {
                if (e.KeyCode == Keys.Escape)
                    Close();
            };

            // Ensure Cancel on any form close that wasn't triggered by a card button
            FormClosing += (_, e) =>
            {
                if (Choice == UpdateChannelChoice.Cancel)
                    DialogResult = DialogResult.Cancel;
            };
        }

        private static bool IsStableSelectable(string currentVersion, VersionChannel stable)
        {
            if (stable == null || string.IsNullOrWhiteSpace(stable.Version))
                return false;

            if (!TryCompareVersions(stable.Version, currentVersion, out var compare))
                return true;

            return compare > 0;
        }

        private static bool IsBetaSelectable(string currentVersion, VersionChannel beta)
        {
            if (beta == null || string.IsNullOrWhiteSpace(beta.Version))
                return false;

            if (!TryCompareVersions(beta.Version, currentVersion, out var compare))
                return true;

            return compare > 0;
        }

        private UIPanel CreateChannelCardWithButton(
            string title,
            string description,
            VersionChannel channel,
            Color fill,
            Color accent,
            Point location,
            string buttonText,
            bool buttonEnabled,
            bool isBeta,
            string warningText = null)
        {
            var cardHeight = 382;
            var panel = new UIPanel
            {
                Location = location,
                Size = new Size(366, cardHeight),
                FillColor = Color.White,
                RectColor = Color.FromArgb(222, 226, 230),
                BackColor = BackColor,
                Radius = 6,
                Tag = isBeta ? "beta" : "stable"
            };

            // Badge header
            var badge = new UIPanel
            {
                Location = new Point(16, 16),
                Size = new Size(334, 46),
                FillColor = fill,
                RectColor = accent,
                BackColor = Color.White,
                Radius = 4
            };
            panel.Controls.Add(badge);

            badge.Controls.Add(CreateTextLabel(
                title,
                new Point(12, 5),
                new Size(120, 24),
                new Font("Segoe UI Semibold", 12F, FontStyle.Bold),
                accent));

            badge.Controls.Add(CreateTextLabel(
                description,
                new Point(12, 25),
                new Size(300, 18),
                new Font("Segoe UI", 8.5F, FontStyle.Regular),
                Color.FromArgb(73, 80, 87)));

            // Info labels
            panel.Controls.Add(CreateInfoBlock("VelopackVersion", Safe(channel?.Version), new Point(18, 82)));
            panel.Controls.Add(CreateInfoBlock("DisplayVersion", Safe(channel?.DisplayVersion ?? channel?.Version), new Point(18, 124)));
            panel.Controls.Add(CreateInfoBlock("InternalBuild", Safe(channel?.InternalBuild), new Point(18, 166)));

            // Release notes
            var notesTitle = CreateTextLabel(
                "Release notes",
                new Point(18, 210),
                new Size(120, 20),
                new Font("Segoe UI Semibold", 9F, FontStyle.Bold),
                Color.FromArgb(52, 58, 64));
            panel.Controls.Add(notesTitle);

            var bottomOfNotes = warningText == null ? 316 : 286;
            var notes = CreateTextLabel(
                NormalizeNotes(channel?.ReleaseNotes),
                new Point(18, 232),
                new Size(326, bottomOfNotes - 232),
                new Font("Segoe UI", 8.7F, FontStyle.Regular),
                Color.FromArgb(73, 80, 87),
                ContentAlignment.TopLeft);
            panel.Controls.Add(notes);

            // Warning text for beta
            if (!string.IsNullOrWhiteSpace(warningText))
            {
                panel.Controls.Add(CreateTextLabel(
                    warningText,
                    new Point(18, 294),
                    new Size(326, 32),
                    new Font("Segoe UI", 8.2F, FontStyle.Bold),
                    Color.FromArgb(183, 101, 16),
                    ContentAlignment.MiddleLeft));
            }

            // Button centered at bottom of card
            var buttonY = cardHeight - 16 - 36; // 310
            var buttonWidth = isBeta ? 146 : 148;
            var buttonX = (366 - buttonWidth) / 2; // 109 or 110

            var btnColor = isBeta
                ? (buttonEnabled ? Color.FromArgb(100, 181, 246) : Color.FromArgb(206, 212, 218))
                : (buttonEnabled ? Color.FromArgb(102, 187, 106) : Color.FromArgb(206, 212, 218));
            var btnRectColor = isBeta
                ? (buttonEnabled ? Color.FromArgb(25, 118, 210) : Color.FromArgb(173, 181, 189))
                : (buttonEnabled ? Color.FromArgb(56, 142, 60) : Color.FromArgb(173, 181, 189));

            var button = new UIButton
            {
                Text = buttonText,
                Location = new Point(buttonX, buttonY),
                Size = new Size(buttonWidth, 36),
                FillColor = btnColor,
                FillHoverColor = buttonEnabled ? ControlPaint.Light(btnColor) : btnColor,
                FillPressColor = buttonEnabled ? ControlPaint.Dark(btnColor) : btnColor,
                RectColor = btnRectColor,
                Font = new Font("Segoe UI Semibold", 9F, FontStyle.Bold),
                ForeColor = Color.White,
                Enabled = buttonEnabled
            };

            if (isBeta)
            {
                button.Click += (_, _) =>
                {
                    Choice = UpdateChannelChoice.Beta;
                    DialogResult = DialogResult.OK;
                    Close();
                };
            }
            else
            {
                button.Click += (_, _) =>
                {
                    Choice = UpdateChannelChoice.Stable;
                    DialogResult = DialogResult.OK;
                    Close();
                };
            }
            panel.Controls.Add(button);

            return panel;
        }

        private static Panel CreateInfoBlock(string label, string value, Point location)
        {
            var block = new Panel
            {
                Location = location,
                Size = new Size(326, 38),
                BackColor = Color.Transparent
            };

            block.Controls.Add(CreateTextLabel(
                label,
                new Point(0, 0),
                new Size(326, 16),
                new Font("Segoe UI Semibold", 8.2F, FontStyle.Bold),
                Color.FromArgb(52, 58, 64)));

            block.Controls.Add(CreateTextLabel(
                value,
                new Point(0, 17),
                new Size(326, 18),
                new Font("Segoe UI", 8.7F, FontStyle.Regular),
                Color.FromArgb(33, 37, 41)));

            return block;
        }

        private static Label CreateTextLabel(
            string text,
            Point location,
            Size size,
            Font font,
            Color foreColor,
            ContentAlignment textAlign = ContentAlignment.MiddleLeft)
        {
            return new Label
            {
                Text = text ?? string.Empty,
                Location = location,
                Size = size,
                Font = font,
                ForeColor = foreColor,
                TextAlign = textAlign,
                BackColor = Color.Transparent,
                AutoSize = false,
                UseMnemonic = false
            };
        }

        private static string Safe(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? "Không có thông tin" : value.Trim();
        }

        private static string NormalizeNotes(string notes)
        {
            if (string.IsNullOrWhiteSpace(notes)) return "Không có thông tin";
            var trimmed = notes.Trim();
            return trimmed.Length <= 240 ? trimmed : trimmed.Substring(0, 237) + "...";
        }

        internal static bool IsDowngrade(string currentVersion, string targetVersion)
        {
            return TryCompareVersions(targetVersion, currentVersion, out var compare) && compare < 0;
        }

        private static bool TryCompareVersions(string leftVersion, string rightVersion, out int compare)
        {
            compare = 0;
            if (!TryParseComparableVersion(leftVersion, out var left) ||
                !TryParseComparableVersion(rightVersion, out var right))
            {
                return false;
            }

            compare = CompareComparableVersion(left, right);
            return true;
        }

        private static bool TryParseComparableVersion(string value, out ComparableVersion version)
        {
            version = default;
            if (string.IsNullOrWhiteSpace(value)) return false;

            var clean = value.Trim().TrimStart('v', 'V');
            var plus = clean.IndexOf('+');
            if (plus >= 0) clean = clean.Substring(0, plus);
            clean = clean.Replace(" beta ", "-beta.", StringComparison.OrdinalIgnoreCase)
                         .Replace(" beta.", "-beta.", StringComparison.OrdinalIgnoreCase);

            var main = clean;
            var preLabel = "";
            var preNumber = 0;
            var dash = clean.IndexOf('-');
            if (dash >= 0)
            {
                main = clean.Substring(0, dash);
                var pre = clean.Substring(dash + 1);
                var preParts = pre.Split('.');
                preLabel = preParts.Length > 0 ? preParts[0] : "";
                if (preParts.Length > 1) int.TryParse(preParts[1], out preNumber);
            }

            var parts = main.Split('.');
            if (parts.Length != 3 && parts.Length != 4) return false;
            if (!int.TryParse(parts[0], out var major) ||
                !int.TryParse(parts[1], out var minor) ||
                !int.TryParse(parts[2], out var patch))
            {
                return false;
            }

            if (parts.Length == 4 && string.IsNullOrWhiteSpace(preLabel))
            {
                if (!int.TryParse(parts[3], out var revision)) return false;
                if (revision > 0)
                {
                    preLabel = "beta";
                    preNumber = revision;
                }
            }

            version = new ComparableVersion(major, minor, patch, preLabel, preNumber);
            return true;
        }

        private static int CompareComparableVersion(ComparableVersion left, ComparableVersion right)
        {
            var cmp = left.Major.CompareTo(right.Major);
            if (cmp != 0) return cmp;
            cmp = left.Minor.CompareTo(right.Minor);
            if (cmp != 0) return cmp;
            cmp = left.Patch.CompareTo(right.Patch);
            if (cmp != 0) return cmp;

            var leftPre = !string.IsNullOrWhiteSpace(left.PreLabel);
            var rightPre = !string.IsNullOrWhiteSpace(right.PreLabel);
            if (!leftPre && !rightPre) return 0;
            if (!leftPre) return 1;
            if (!rightPre) return -1;

            cmp = string.Compare(left.PreLabel, right.PreLabel, StringComparison.OrdinalIgnoreCase);
            if (cmp != 0) return cmp;
            return left.PreNumber.CompareTo(right.PreNumber);
        }

        private readonly struct ComparableVersion
        {
            public ComparableVersion(int major, int minor, int patch, string preLabel, int preNumber)
            {
                Major = major;
                Minor = minor;
                Patch = patch;
                PreLabel = preLabel ?? "";
                PreNumber = preNumber;
            }

            public int Major { get; }
            public int Minor { get; }
            public int Patch { get; }
            public string PreLabel { get; }
            public int PreNumber { get; }
        }

        private sealed class DowngradeConfirmDialog : UIForm
        {
            public DowngradeConfirmDialog(string currentVersion, string targetVersion, VersionChannel targetChannel)
            {
                Text = "Xác nhận downgrade";
                StartPosition = FormStartPosition.CenterParent;
                FormBorderStyle = FormBorderStyle.FixedDialog;
                MaximizeBox = false;
                MinimizeBox = false;
                ShowInTaskbar = false;
                ClientSize = new Size(520, 260);
                BackColor = Color.FromArgb(255, 248, 240);
                Font = new Font("Segoe UI", 9F, FontStyle.Regular);

                var title = new UILabel
                {
                    Text = "Kênh Stable thấp hơn phiên bản đang cài",
                    Location = new Point(24, 24),
                    Size = new Size(460, 30),
                    Font = new Font("Segoe UI Semibold", 13F, FontStyle.Bold),
                    ForeColor = Color.FromArgb(121, 85, 72),
                    TextAlign = ContentAlignment.MiddleLeft
                };
                Controls.Add(title);

                var targetDisplay = targetChannel?.DisplayVersion ?? targetVersion ?? "Không có thông tin";
                var message = new UILabel
                {
                    Text =
                        $"Đang cài: {Safe(currentVersion)}\n" +
                        $"Stable hiện có: {Safe(targetVersion)} ({Safe(targetDisplay)})\n\n" +
                        "Chỉ tiếp tục nếu bạn muốn chuyển khỏi beta hoặc quay về nhánh ổn định.",
                    Location = new Point(26, 68),
                    Size = new Size(456, 96),
                    Font = new Font("Segoe UI", 9.5F, FontStyle.Regular),
                    ForeColor = Color.FromArgb(73, 80, 87),
                    TextAlign = ContentAlignment.TopLeft
                };
                Controls.Add(message);

                var confirm = CreateButton(
                    "Cho phép downgrade",
                    new Point(238, 190),
                    Color.FromArgb(255, 183, 77),
                    Color.FromArgb(245, 124, 0));
                confirm.Size = new Size(136, 36);
                confirm.Click += (_, _) =>
                {
                    DialogResult = DialogResult.OK;
                    Close();
                };
                Controls.Add(confirm);

                var cancel = CreateButton(
                    "Hủy",
                    new Point(390, 190),
                    Color.FromArgb(248, 249, 250),
                    Color.FromArgb(173, 181, 189));
                cancel.ForeColor = Color.FromArgb(52, 58, 64);
                cancel.Click += (_, _) =>
                {
                    DialogResult = DialogResult.Cancel;
                    Close();
                };
                Controls.Add(cancel);

                CancelButton = cancel;
            }

            private static UIButton CreateButton(string text, Point location, Color fill, Color rect)
            {
                return new UIButton
                {
                    Text = text,
                    Location = location,
                    Size = new Size(118, 36),
                    FillColor = fill,
                    FillHoverColor = ControlPaint.Light(fill),
                    FillPressColor = ControlPaint.Dark(fill),
                    RectColor = rect,
                    Font = new Font("Segoe UI Semibold", 9F, FontStyle.Bold),
                    ForeColor = Color.White
                };
            }

            private static string Safe(string value)
            {
                return string.IsNullOrWhiteSpace(value) ? "Không có thông tin" : value.Trim();
            }
        }
    }
}

using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace AutoJMS.FullStack.UI.OperationCenter
{
    public sealed class WaybillDetailPanel : UserControl
    {
        private readonly Label _title;
        private readonly Label _stateBadge;
        private readonly Label _riskBadge;
        private readonly Label _slaBadge;
        private readonly Label _recommendedAction;
        private readonly Label _riskScore;
        private readonly Label _age;
        private readonly Label _employee;
        private readonly Label _site;
        private readonly Label _lastAction;
        private readonly Label _kvdReason;
        private readonly TextBox _whyAtRisk;
        private readonly TextBox _notes;
        private readonly TextBox _tasksTimeline;
        private readonly TextBox _noteInput;
        private string _waybillNo = string.Empty;

        public event EventHandler<OperationDetailActionEventArgs> ActionRequested;

        public WaybillDetailPanel()
        {
            DoubleBuffered = true;
            BackColor = Color.White;
            Padding = new Padding(12);

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 9,
                Margin = Padding.Empty,
                Padding = Padding.Empty,
                BackColor = Color.White
            };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 82F));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 50F));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 74F));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34F));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 34F));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28F));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 31F));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28F));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 35F));

            var header = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 2,
                Margin = Padding.Empty,
                Padding = Padding.Empty,
                BackColor = Color.White
            };
            header.RowStyles.Add(new RowStyle(SizeType.Absolute, 42F));
            header.RowStyles.Add(new RowStyle(SizeType.Absolute, 40F));

            _title = new Label
            {
                Dock = DockStyle.Fill,
                Text = "Chọn một vận đơn",
                TextAlign = ContentAlignment.MiddleLeft,
                Font = new Font("Segoe UI Semibold", 15F, FontStyle.Bold),
                ForeColor = Color.FromArgb(17, 24, 39),
                AutoEllipsis = true
            };

            var badgeBar = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                Margin = Padding.Empty,
                Padding = new Padding(0, 4, 0, 4),
                BackColor = Color.White
            };
            _stateBadge = CreateBadge(Color.FromArgb(37, 99, 235));
            _riskBadge = CreateBadge(Color.FromArgb(22, 163, 74));
            _slaBadge = CreateBadge(Color.FromArgb(107, 114, 128));
            badgeBar.Controls.Add(_stateBadge);
            badgeBar.Controls.Add(_riskBadge);
            badgeBar.Controls.Add(_slaBadge);
            header.Controls.Add(_title, 0, 0);
            header.Controls.Add(badgeBar, 0, 1);

            var actions = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 5,
                RowCount = 1,
                Margin = new Padding(0, 0, 0, 8),
                Padding = Padding.Empty,
                BackColor = Color.White
            };
            actions.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 88F));
            for (int i = 0; i < 4; i++)
                actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25F));
            actions.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            actions.Controls.Add(new Label
            {
                Dock = DockStyle.Fill,
                Text = "Đơn đang chọn",
                TextAlign = ContentAlignment.MiddleLeft,
                Font = new Font("Segoe UI Semibold", 8F, FontStyle.Bold),
                ForeColor = Color.FromArgb(75, 85, 99),
                AutoEllipsis = true
            }, 0, 0);
            actions.Controls.Add(CreateActionButton("Copy", "COPY", Color.FromArgb(31, 41, 55)), 1, 0);
            actions.Controls.Add(CreateActionButton("Checked", "MARK_CHECKED", Color.FromArgb(22, 163, 74)), 2, 0);
            actions.Controls.Add(CreateActionButton("Task", "CREATE_TASK", Color.FromArgb(245, 158, 11)), 3, 0);
            actions.Controls.Add(CreateActionButton("Export", "EXPORT_SELECTED", Color.FromArgb(37, 99, 235)), 4, 0);

            var recommend = CreateSectionShell("Recommended action", out _recommendedAction);
            _recommendedAction.Font = new Font("Segoe UI Semibold", 9.3F, FontStyle.Bold);
            _recommendedAction.ForeColor = Color.FromArgb(17, 24, 39);

            var facts = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 3,
                Margin = new Padding(0, 0, 0, 8),
                Padding = Padding.Empty,
                BackColor = Color.White
            };
            facts.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            facts.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            for (int i = 0; i < 3; i++)
                facts.RowStyles.Add(new RowStyle(SizeType.Percent, 33.3F));

            _riskScore = CreateFactLabel();
            _age = CreateFactLabel();
            _employee = CreateFactLabel();
            _site = CreateFactLabel();
            _lastAction = CreateFactLabel();
            _kvdReason = CreateFactLabel();
            facts.Controls.Add(_riskScore, 0, 0);
            facts.Controls.Add(_age, 1, 0);
            facts.Controls.Add(_employee, 0, 1);
            facts.Controls.Add(_site, 1, 1);
            facts.Controls.Add(_lastAction, 0, 2);
            facts.Controls.Add(_kvdReason, 1, 2);

            _whyAtRisk = CreateReadonlyBox();
            _notes = CreateReadonlyBox();
            _tasksTimeline = CreateReadonlyBox();

            var noteBar = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 1,
                Margin = new Padding(0, 8, 0, 0),
                Padding = Padding.Empty,
                BackColor = Color.White
            };
            noteBar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            noteBar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 94F));
            noteBar.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            _noteInput = new TextBox
            {
                Dock = DockStyle.Fill,
                Font = new Font("Segoe UI", 9F),
                BorderStyle = BorderStyle.FixedSingle,
                Margin = new Padding(0, 0, 6, 0)
            };
            noteBar.Controls.Add(_noteInput, 0, 0);
            noteBar.Controls.Add(CreateActionButton("Add note", "ADD_NOTE", Color.FromArgb(31, 41, 55)), 1, 0);

            layout.Controls.Add(header, 0, 0);
            layout.Controls.Add(actions, 0, 1);
            layout.Controls.Add(recommend, 0, 2);
            layout.Controls.Add(CreateSectionLabel("Why at risk"), 0, 3);
            layout.Controls.Add(_whyAtRisk, 0, 4);
            layout.Controls.Add(CreateSectionLabel("Notes"), 0, 5);
            layout.Controls.Add(_notes, 0, 6);
            layout.Controls.Add(CreateSectionLabel("Tasks / timeline"), 0, 7);
            layout.Controls.Add(WrapBottom(_tasksTimeline, noteBar), 0, 8);

            Controls.Add(layout);
        }

        public void SetDetail(OperationWaybillDetail detail)
        {
            if (detail == null)
                detail = new OperationWaybillDetail();

            _waybillNo = DisplayDash(detail.WaybillNo);
            bool empty = string.IsNullOrWhiteSpace(_waybillNo) || _waybillNo == "-";
            _title.Text = empty ? "Chọn một vận đơn" : _waybillNo;

            SetBadge(_stateBadge, detail.State, StateColor(detail.State));
            SetBadge(_riskBadge, $"{DisplayDash(detail.RiskLevel)} {detail.RiskScore}", RiskColor(detail.RiskLevel));
            SetBadge(_slaBadge, detail.SlaStatus, SlaColor(detail.SlaStatus));

            var primaryAction = detail.RecommendedActions?.Select(DisplayDash).FirstOrDefault(x => x != "-");
            _recommendedAction.Text = string.IsNullOrWhiteSpace(primaryAction)
                ? "Chưa có hành động khuyến nghị."
                : primaryAction;

            _riskScore.Text = $"Risk score: {detail.RiskScore:N0}";
            _age.Text = $"Age: {DisplayDash(detail.AgeText)}";
            _employee.Text = $"Employee: {DisplayDash(detail.Employee)}";
            _site.Text = $"Site: {DisplayDash(detail.Site)}";
            _lastAction.Text = $"Last: {DisplayDash(detail.LastAction)} | {DisplayDash(detail.LastActionTime)}";
            _kvdReason.Text = $"KVD: {DisplayDash(detail.KvdReason)}";

            var reasons = NormalizeLines(detail.RiskReasons);
            _whyAtRisk.Text = string.Join(Environment.NewLine, reasons);
            _notes.Text = string.Join(Environment.NewLine, NormalizeLines(detail.Notes));
            var tasks = (detail.Tasks ?? Array.Empty<string>())
                .Concat((detail.Timeline ?? Array.Empty<string>()).Select(x => $"Timeline: {x}"));
            _tasksTimeline.Text = string.Join(Environment.NewLine, NormalizeLines(tasks));
        }

        public void SetLoading(string waybillNo, string message)
        {
            _waybillNo = waybillNo ?? string.Empty;
            _title.Text = string.IsNullOrWhiteSpace(_waybillNo) ? "Đang tải chi tiết" : _waybillNo;
            SetBadge(_stateBadge, "LOADING", Color.FromArgb(37, 99, 235));
            SetBadge(_riskBadge, "-", Color.FromArgb(107, 114, 128));
            SetBadge(_slaBadge, "-", Color.FromArgb(107, 114, 128));
            _recommendedAction.Text = message ?? "Đang tải dữ liệu local...";
            _riskScore.Text = "Risk score: -";
            _age.Text = "Age: -";
            _employee.Text = "Employee: -";
            _site.Text = "Site: -";
            _lastAction.Text = "Last: -";
            _kvdReason.Text = "KVD: -";
            _whyAtRisk.Text = "Đang xử lý...";
            _notes.Text = "-";
            _tasksTimeline.Text = "-";
        }

        private Button CreateActionButton(string text, string action, Color color)
        {
            var button = new Button
            {
                Text = text,
                Tag = action,
                Dock = DockStyle.Fill,
                Height = 30,
                Margin = new Padding(0, 0, 6, 0),
                FlatStyle = FlatStyle.Flat,
                BackColor = color,
                ForeColor = Color.White,
                Font = new Font("Segoe UI Semibold", 8.2F, FontStyle.Bold)
            };
            button.FlatAppearance.BorderSize = 0;
            button.Click += (s, e) =>
            {
                var value = action == "ADD_NOTE" ? _noteInput.Text.Trim() : string.Empty;
                ActionRequested?.Invoke(this, new OperationDetailActionEventArgs(action, _waybillNo, value));
                if (action == "ADD_NOTE")
                    _noteInput.Clear();
            };
            return button;
        }

        private static Label CreateBadge(Color color)
        {
            return new Label
            {
                AutoSize = false,
                Width = 104,
                Height = 26,
                Margin = new Padding(0, 0, 6, 0),
                TextAlign = ContentAlignment.MiddleCenter,
                Font = new Font("Segoe UI Semibold", 8.2F, FontStyle.Bold),
                ForeColor = Color.White,
                BackColor = color,
                AutoEllipsis = true
            };
        }

        private static void SetBadge(Label label, string text, Color color)
        {
            label.Text = DisplayDash(text);
            label.BackColor = color;
        }

        private static string DisplayDash(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "-";

            value = value.Trim();
            return string.Equals(value, "empty", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(value, "--", StringComparison.OrdinalIgnoreCase)
                ? "-"
                : value;
        }

        private static string[] NormalizeLines(IEnumerable<string> values)
        {
            var normalized = (values ?? Array.Empty<string>())
                .Select(DisplayDash)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .ToArray();

            return normalized.Length == 0 ? new[] { "-" } : normalized;
        }

        private static Control CreateSectionShell(string title, out Label content)
        {
            var shell = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 2,
                Margin = new Padding(0, 0, 0, 8),
                Padding = new Padding(10, 7, 10, 8),
                BackColor = Color.FromArgb(255, 251, 235)
            };
            shell.RowStyles.Add(new RowStyle(SizeType.Absolute, 22F));
            shell.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            shell.Controls.Add(new Label
            {
                Dock = DockStyle.Fill,
                Text = title,
                TextAlign = ContentAlignment.MiddleLeft,
                Font = new Font("Segoe UI Semibold", 8.2F, FontStyle.Bold),
                ForeColor = Color.FromArgb(146, 64, 14)
            }, 0, 0);

            content = new Label
            {
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                AutoEllipsis = true
            };
            shell.Controls.Add(content, 0, 1);
            return shell;
        }

        private static Label CreateFactLabel()
        {
            return new Label
            {
                Dock = DockStyle.Fill,
                Margin = new Padding(0, 0, 8, 2),
                Padding = new Padding(8, 0, 8, 0),
                TextAlign = ContentAlignment.MiddleLeft,
                Font = new Font("Segoe UI Semibold", 8.2F, FontStyle.Bold),
                ForeColor = Color.FromArgb(55, 65, 81),
                BackColor = Color.FromArgb(249, 250, 251),
                AutoEllipsis = true
            };
        }

        private static Label CreateSectionLabel(string text)
        {
            return new Label
            {
                Dock = DockStyle.Fill,
                Text = text,
                TextAlign = ContentAlignment.MiddleLeft,
                Font = new Font("Segoe UI Semibold", 9F, FontStyle.Bold),
                ForeColor = Color.FromArgb(17, 24, 39)
            };
        }

        private static TextBox CreateReadonlyBox()
        {
            return new TextBox
            {
                Dock = DockStyle.Fill,
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                BorderStyle = BorderStyle.FixedSingle,
                Font = new Font("Segoe UI", 9F),
                BackColor = Color.FromArgb(249, 250, 251),
                ForeColor = Color.FromArgb(31, 41, 55)
            };
        }

        private static Control WrapBottom(Control content, Control noteBar)
        {
            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 2,
                Margin = Padding.Empty,
                Padding = Padding.Empty,
                BackColor = Color.White
            };
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38F));
            layout.Controls.Add(content, 0, 0);
            layout.Controls.Add(noteBar, 0, 1);
            return layout;
        }

        private static Color RiskColor(string level)
        {
            return (level ?? string.Empty).ToUpperInvariant() switch
            {
                "CRITICAL" => Color.FromArgb(220, 38, 38),
                "HIGH" => Color.FromArgb(245, 158, 11),
                "MEDIUM" => Color.FromArgb(37, 99, 235),
                _ => Color.FromArgb(22, 163, 74)
            };
        }

        private static Color StateColor(string state)
        {
            return (state ?? string.Empty).ToUpperInvariant() switch
            {
                "SLA_BREACHED" => Color.FromArgb(220, 38, 38),
                "SLA_URGENT" => Color.FromArgb(245, 158, 11),
                "LOST_RISK" => Color.FromArgb(124, 58, 237),
                "FAILED_DELIVERY" => Color.FromArgb(37, 99, 235),
                "RETURN_PENDING" => Color.FromArgb(124, 58, 237),
                "DELIVERED" => Color.FromArgb(22, 163, 74),
                _ => Color.FromArgb(31, 41, 55)
            };
        }

        private static Color SlaColor(string text)
        {
            var value = text ?? string.Empty;
            if (value.Contains("TRỄ", StringComparison.OrdinalIgnoreCase) || value.Contains("quá", StringComparison.OrdinalIgnoreCase))
                return Color.FromArgb(220, 38, 38);
            if (value.Contains("sắp", StringComparison.OrdinalIgnoreCase) || value.Contains("URGENT", StringComparison.OrdinalIgnoreCase))
                return Color.FromArgb(245, 158, 11);
            return Color.FromArgb(22, 163, 74);
        }
    }
}

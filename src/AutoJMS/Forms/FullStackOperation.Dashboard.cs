using AutoJMS.FullStack.UI.OperationCenter;
using Sunny.UI;
using System;
using System.Drawing;
using System.Windows.Forms;

namespace AutoJMS
{
    public partial class FullStackOperation
    {
        private void BuildDashboardPageCodeFirst()
        {
            tabDash.SuspendLayout();
            try
            {
                uiTableLayoutPanel4 = new UITableLayoutPanel
                {
                    Dock = DockStyle.Fill,
                    Margin = Padding.Empty,
                    Padding = Padding.Empty,
                    ColumnCount = 1,
                    RowCount = 3,
                    BackColor = WorkspaceBackColor
                };
                uiTableLayoutPanel4.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
                uiTableLayoutPanel4.RowStyles.Add(new RowStyle(SizeType.Absolute, 118F));
                uiTableLayoutPanel4.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
                uiTableLayoutPanel4.RowStyles.Add(new RowStyle(SizeType.Absolute, 30F));

                uiPanel10 = CreateOperationHeaderBar();
                uiTableLayoutPanel4.Controls.Add(uiPanel10, 0, 0);

                var body = new UITableLayoutPanel
                {
                    Dock = DockStyle.Fill,
                    Margin = Padding.Empty,
                    Padding = new Padding(10),
                    ColumnCount = 3,
                    RowCount = 1,
                    BackColor = WorkspaceBackColor
                };
                body.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 252F));
                body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
                body.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 386F));
                body.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

                _operationQueueSidebar = new QueueSidebarControl { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 10, 0) };
                _operationQueueSidebar.QueueSelected += OperationQueueSidebar_QueueSelected;

                var center = CreateOperationMainWorkArea();
                _operationDetailPanel = new WaybillDetailPanel { Dock = DockStyle.Fill, Margin = new Padding(10, 0, 0, 0) };
                _operationDetailPanel.ActionRequested += OperationDetailPanel_ActionRequested;

                body.Controls.Add(_operationQueueSidebar, 0, 0);
                body.Controls.Add(center, 1, 0);
                body.Controls.Add(_operationDetailPanel, 2, 0);
                uiTableLayoutPanel4.Controls.Add(body, 0, 1);

                _operationStatusFooter = new StatusFooterControl();
                uiTableLayoutPanel4.Controls.Add(_operationStatusFooter, 0, 2);

                tabDash.Controls.Add(uiTableLayoutPanel4);
            }
            finally
            {
                tabDash.ResumeLayout(false);
            }
        }

        private UIPanel CreateOperationHeaderBar()
        {
            var panel = new UIPanel
            {
                Dock = DockStyle.Fill,
                Margin = Padding.Empty,
                Padding = new Padding(12, 8, 12, 10),
                FillColor = HeaderDark,
                RectColor = HeaderDark,
                Text = null,
                TextAlignment = ContentAlignment.MiddleCenter,
                MinimumSize = new Size(1, 1)
            };

            var shell = new UITableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 2,
                Margin = Padding.Empty,
                Padding = Padding.Empty,
                BackColor = HeaderDark
            };
            shell.RowStyles.Add(new RowStyle(SizeType.Absolute, 42F));
            shell.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

            var identity = new UITableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 3,
                RowCount = 1,
                Margin = Padding.Empty,
                Padding = Padding.Empty,
                BackColor = HeaderDark
            };
            identity.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            identity.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 420F));
            identity.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 310F));
            identity.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

            var titleBlock = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 2,
                Margin = Padding.Empty,
                Padding = Padding.Empty,
                BackColor = HeaderDark
            };
            titleBlock.RowStyles.Add(new RowStyle(SizeType.Percent, 58F));
            titleBlock.RowStyles.Add(new RowStyle(SizeType.Percent, 42F));
            _operationHeaderTitle = new Label
            {
                Dock = DockStyle.Fill,
                Text = "Operation Control Center",
                TextAlign = ContentAlignment.BottomLeft,
                Font = new Font("Segoe UI Semibold", 15F, FontStyle.Bold),
                ForeColor = Color.White,
                AutoEllipsis = true
            };
            _operationHeaderStatus = new Label
            {
                Dock = DockStyle.Fill,
                Text = "Local-first SQLite | Waiting for auth/data",
                TextAlign = ContentAlignment.TopLeft,
                Font = new Font("Segoe UI", 9F),
                ForeColor = Color.FromArgb(209, 213, 219),
                AutoEllipsis = true
            };
            titleBlock.Controls.Add(_operationHeaderTitle, 0, 0);
            titleBlock.Controls.Add(_operationHeaderStatus, 0, 1);
            identity.Controls.Add(titleBlock, 0, 0);

            tabDash_lblLastUpdate = new UISymbolLabel
            {
                Dock = DockStyle.Fill,
                Text = "SQLite local chưa tải",
                TextAlign = ContentAlignment.MiddleRight,
                Font = new Font("Segoe UI Semibold", 9F, FontStyle.Bold),
                ForeColor = Color.White,
                Symbol = 61473,
                SymbolSize = 16,
                Margin = new Padding(8, 0, 8, 0),
                MinimumSize = new Size(1, 1)
            };
            identity.Controls.Add(tabDash_lblLastUpdate, 1, 0);
            identity.Controls.Add(CreateHeaderStatusPill("DB/Auth", "Local-first | Auth gated"), 2, 0);

            uiTableLayoutPanel18 = new UITableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 4,
                RowCount = 1,
                Margin = Padding.Empty,
                Padding = Padding.Empty,
                BackColor = HeaderDark
            };
            uiTableLayoutPanel18.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 330F));
            uiTableLayoutPanel18.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            uiTableLayoutPanel18.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 300F));
            uiTableLayoutPanel18.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 180F));
            uiTableLayoutPanel18.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

            var scopeGroup = CreateCommandGroup("Scope", out _);
            var scopeContent = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 1,
                Margin = Padding.Empty,
                Padding = Padding.Empty,
                BackColor = HeaderDark
            };
            scopeContent.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 42F));
            scopeContent.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 58F));
            scopeContent.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            uiTableLayoutPanel37 = CreateHeaderInputGroup("Nguồn", out uiSymbolLabel8);
            tabDash_dataSource = CreateHeaderComboBox("tabDash_dataSource");
            tabDash_dataSource.Items.AddRange(new object[] { "ALL", "LOCAL", "PHATLAI" });
            tabDash_dataSource.SelectedIndex = 1;
            uiTableLayoutPanel37.Controls.Add(tabDash_dataSource, 0, 1);

            uiTableLayoutPanel36 = CreateHeaderInputGroup("Queue", out uiSymbolLabel7);
            tabDash_statusSelect = CreateHeaderComboBox("tabDash_statusSelect");
            tabDash_statusSelect.Items.Add("Tất cả tồn kho");
            tabDash_statusSelect.SelectedIndex = 0;
            uiTableLayoutPanel36.Controls.Add(tabDash_statusSelect, 0, 1);
            scopeContent.Controls.Add(uiTableLayoutPanel37, 0, 0);
            scopeContent.Controls.Add(uiTableLayoutPanel36, 1, 0);
            scopeGroup.Controls.Add(scopeContent, 0, 1);
            uiTableLayoutPanel18.Controls.Add(scopeGroup, 0, 0);

            var searchPanel = CreateCommandGroup("Search / time / export", out _);
            searchPanel.Controls.Add(CreateOperationSearchRow(), 0, 1);
            uiTableLayoutPanel18.Controls.Add(searchPanel, 1, 0);

            uiTableLayoutPanel34 = CreateCommandGroup("Sync", out uiSymbolLabel1);
            var syncFlow = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 4,
                RowCount = 1,
                Margin = Padding.Empty,
                Padding = Padding.Empty,
                BackColor = HeaderDark
            };
            syncFlow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 100F));
            syncFlow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 104F));
            syncFlow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 80F));
            syncFlow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            syncFlow.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            tabDash_timeUpdateData = CreateHeaderComboBox("tabDash_timeUpdateData");
            tabDash_timeUpdateData.Items.AddRange(new object[] { "2 PHÚT", "5 PHÚT", "10 PHÚT", "30 PHÚT", "1 GIỜ" });
            tabDash_timeUpdateData.Text = "2 PHÚT";
            tabDash_timeUpdateData.Size = new Size(100, 32);
            tabDash_timeUpdateData.Margin = Padding.Empty;

            tabDash_updateData = CreateHeaderButton("Đồng bộ", 61473, AccentGreen);
            tabDash_updateData.Size = new Size(104, 32);
            tabDash_updateData.Margin = Padding.Empty;

            _operationRefreshLocalButton = CreateHeaderButton("Local", 61473, AccentBlue);
            _operationRefreshLocalButton.Size = new Size(82, 32);
            _operationRefreshLocalButton.Margin = Padding.Empty;
            _operationRefreshLocalButton.Click += async (s, e) => await LoadDataAndRefreshViewsAsync();
            syncFlow.Controls.Add(CreateToolbarInputHost(tabDash_timeUpdateData, new Padding(0, 0, 6, 0)), 0, 0);
            syncFlow.Controls.Add(CreateToolbarInputHost(tabDash_updateData, new Padding(0, 0, 6, 0)), 1, 0);
            syncFlow.Controls.Add(CreateToolbarInputHost(_operationRefreshLocalButton, Padding.Empty), 2, 0);
            uiTableLayoutPanel34.Controls.Add(syncFlow, 0, 1);
            uiTableLayoutPanel18.Controls.Add(uiTableLayoutPanel34, 2, 0);

            uiTableLayoutPanel35 = CreateCommandGroup("Data health", out _);
            uiTableLayoutPanel35.Controls.Add(CreateHeaderStatusPill("SQLite", "Ready after load"), 0, 1);
            uiTableLayoutPanel18.Controls.Add(uiTableLayoutPanel35, 3, 0);

            shell.Controls.Add(identity, 0, 0);
            shell.Controls.Add(uiTableLayoutPanel18, 0, 1);
            panel.Controls.Add(shell);
            return panel;
        }

        private Control CreateOperationMainWorkArea()
        {
            var center = new UITableLayoutPanel
            {
                Dock = DockStyle.Fill,
                Margin = Padding.Empty,
                Padding = Padding.Empty,
                ColumnCount = 1,
                RowCount = 5,
                BackColor = WorkspaceBackColor
            };
            center.RowStyles.Add(new RowStyle(SizeType.Absolute, 112F));
            center.RowStyles.Add(new RowStyle(SizeType.Absolute, 68F));
            center.RowStyles.Add(new RowStyle(SizeType.Absolute, 44F));
            center.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            center.RowStyles.Add(new RowStyle(SizeType.Absolute, 34F));

            uiTableLayoutPanel14 = new UITableLayoutPanel
            {
                Dock = DockStyle.Fill,
                Margin = Padding.Empty,
                Padding = Padding.Empty,
                ColumnCount = 1,
                RowCount = 1,
                BackColor = WorkspaceBackColor
            };
            uiTableLayoutPanel14.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            uiTableLayoutPanel14.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            _operationFocusStrip = CreatePriorityFocusStrip();
            uiTableLayoutPanel14.Controls.Add(_operationFocusStrip, 0, 0);

            _operationGridFilterToolbar = new GridFilterToolbarControl
            {
                Dock = DockStyle.Fill,
                Margin = new Padding(0, 6, 0, 0)
            };
            _operationGridFilterToolbar.FilterRequested += OperationGridFilterToolbar_FilterRequested;
            _operationGridFilterToolbar.PresetRequested += OperationGridFilterToolbar_PresetRequested;

            var gridToolbar = CreateGridToolbar();
            tabPage3 = new TabPage { Name = "tabPage3", Text = "Tồn kho" };
            tabPage4 = new TabPage { Name = "tabPage4", Text = "Thời hiệu cũ" };
            uiTabControl2 = new UITabControl { Dock = DockStyle.Fill, Visible = false };
            uiDataGridView2 = CreateGrid("uiDataGridView2");

            tabDash_dataGridView = CreateGrid("tabDash_dataGridView");
            tabDash_dataGridView.Dock = DockStyle.Fill;

            _operationGridHost = new Panel
            {
                Dock = DockStyle.Fill,
                Margin = Padding.Empty,
                Padding = Padding.Empty,
                BackColor = Color.White
            };
            _operationInventoryWorkspace = new Panel
            {
                Dock = DockStyle.Fill,
                Margin = Padding.Empty,
                Padding = Padding.Empty,
                BackColor = Color.White,
                Visible = true
            };
            _operationInventoryWorkspace.Controls.Add(tabDash_dataGridView);
            InitializeWaybillJourneyWorkspace();
            _operationGridHost.Controls.Add(_waybillJourneyWorkspace);
            _operationGridHost.Controls.Add(_operationInventoryWorkspace);

            _operationMiniMetricStrip = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                BackColor = Color.White,
                Padding = new Padding(8, 5, 8, 4),
                Margin = new Padding(0, 6, 0, 0)
            };

            center.Controls.Add(uiTableLayoutPanel14, 0, 0);
            center.Controls.Add(_operationGridFilterToolbar, 0, 1);
            center.Controls.Add(gridToolbar, 0, 2);
            center.Controls.Add(_operationGridHost, 0, 3);
            center.Controls.Add(_operationMiniMetricStrip, 0, 4);
            return center;
        }

        private TableLayoutPanel CreatePriorityFocusStrip()
        {
            var strip = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 4,
                RowCount = 1,
                Margin = Padding.Empty,
                Padding = Padding.Empty,
                BackColor = WorkspaceBackColor
            };
            for (int i = 0; i < 4; i++)
                strip.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25F));
            strip.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

            _kpiCriticalFocus = CreateFocusCard("Critical focus", "0", "Đơn cần xử lý ngay", "CRIT", AccentRed, "Cần xử lý ngay");
            _kpiSlaBreach = CreateFocusCard("SLA breach", "0", "Đơn đã quá SLA", "SLA", AccentRed, "SLA quá hạn");
            _kpiAgingRisk = CreateFocusCard("Aging risk", "0", "Tồn >48h / đứng lâu", "RISK", AccentPurple, "Tồn quá hạn (>48h)");
            _kpiDataHealth = CreateFocusCard("Data health", "0", "Đang lọc / tổng local", "LOCAL", AccentBlue, "Tất cả tồn kho");

            strip.Controls.Add(_kpiCriticalFocus, 0, 0);
            strip.Controls.Add(_kpiSlaBreach, 1, 0);
            strip.Controls.Add(_kpiAgingRisk, 2, 0);
            strip.Controls.Add(_kpiDataHealth, 3, 0);
            return strip;
        }

        private KpiCardControl CreateFocusCard(string title, string value, string subtitle, string badge, Color color, string filterKey)
        {
            var card = new KpiCardControl { Dock = DockStyle.Fill };
            card.SetMetric(title, value, subtitle, badge, color, filterKey);
            card.CardClicked += OperationKpiCard_Clicked;
            return card;
        }

        private UITableLayoutPanel CreateHeaderInputGroup(string title, out UISymbolLabel label)
        {
            var group = new UITableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 2,
                Margin = new Padding(4, 0, 4, 0),
                Padding = Padding.Empty,
                BackColor = HeaderDark
            };
            group.RowStyles.Add(new RowStyle(SizeType.Absolute, 18F));
            group.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            label = new UISymbolLabel
            {
                Dock = DockStyle.Fill,
                Text = title,
                TextAlign = ContentAlignment.MiddleLeft,
                Font = new Font("Segoe UI Semibold", 7.8F, FontStyle.Bold),
                ForeColor = Color.FromArgb(209, 213, 219),
                Symbol = 61555,
                SymbolSize = 12,
                MinimumSize = new Size(1, 1)
            };
            group.Controls.Add(label, 0, 0);
            return group;
        }

        private UITableLayoutPanel CreateCommandGroup(string title, out UISymbolLabel label)
        {
            var group = new UITableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 2,
                Margin = new Padding(0, 0, 8, 0),
                Padding = Padding.Empty,
                BackColor = HeaderDark
            };
            group.RowStyles.Add(new RowStyle(SizeType.Absolute, 18F));
            group.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            label = new UISymbolLabel
            {
                Dock = DockStyle.Fill,
                Text = title,
                TextAlign = ContentAlignment.MiddleLeft,
                Font = new Font("Segoe UI Semibold", 8F, FontStyle.Bold),
                ForeColor = Color.FromArgb(209, 213, 219),
                Symbol = 61555,
                SymbolSize = 12,
                MinimumSize = new Size(1, 1)
            };
            group.Controls.Add(label, 0, 0);
            return group;
        }

        private Control CreateHeaderStatusPill(string title, string value)
        {
            var panel = new Panel
            {
                Dock = DockStyle.Fill,
                Margin = new Padding(4, 4, 0, 4),
                Padding = new Padding(10, 3, 10, 3),
                BackColor = Color.FromArgb(31, 41, 55)
            };
            var label = new Label
            {
                Dock = DockStyle.Fill,
                Text = $"{title}: {value}",
                TextAlign = ContentAlignment.MiddleCenter,
                Font = new Font("Segoe UI Semibold", 8.5F, FontStyle.Bold),
                ForeColor = Color.FromArgb(229, 231, 235),
                AutoEllipsis = true
            };
            panel.Controls.Add(label);
            return panel;
        }

        private UIComboBox CreateHeaderComboBox(string name)
        {
            var combo = CreateComboBox(name);
            combo.Font = new Font("Segoe UI", 8.5F);
            combo.FillColor = Color.White;
            combo.RectColor = Color.FromArgb(156, 163, 175);
            combo.Size = new Size(120, 32);
            combo.Margin = new Padding(0, 0, 6, 0);
            return combo;
        }

        private UISymbolButton CreateHeaderButton(string text, int symbol, Color color)
        {
            return new UISymbolButton
            {
                Text = text,
                Font = UiBoldFont,
                Symbol = symbol,
                SymbolSize = 16,
                Radius = 6,
                FillColor = color,
                FillHoverColor = ControlPaint.Light(color),
                RectColor = color,
                MinimumSize = new Size(1, 1),
                Size = new Size(96, 32),
                Margin = new Padding(0, 0, 6, 0)
            };
        }

        private Control CreateOperationSearchRow()
        {
            var row = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 5,
                RowCount = 1,
                Margin = Padding.Empty,
                Padding = Padding.Empty,
                BackColor = HeaderDark
            };
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 114F));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 114F));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 44F));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 1F));
            row.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

            _dashSearchBox = new TextBox
            {
                AutoSize = false,
                BorderStyle = BorderStyle.FixedSingle,
                Font = new Font("Segoe UI", 10F),
                Margin = Padding.Empty,
                Size = new Size(220, 32),
                ForeColor = Color.FromArgb(35, 45, 55),
                PlaceholderText = "Tìm mã vận đơn, NV, BC..."
            };
            _dashSearchBox.TextChanged += (s, e) =>
            {
                if (_lastDashSourceData.Count == 0) return;
                RefreshFilteredGrid();
                UpdateFilterInfo();
            };

            _dashDateFrom = new DateTimePicker
            {
                Font = new Font("Segoe UI", 9F),
                Format = DateTimePickerFormat.Short,
                Margin = Padding.Empty,
                Size = new Size(116, 32),
                Checked = false,
                ShowCheckBox = true
            };
            _dashDateFrom.ValueChanged += (s, e) =>
            {
                if (_lastDashSourceData.Count == 0) return;
                RefreshFilteredGrid();
                UpdateFilterInfo();
            };

            _dashDateTo = new DateTimePicker
            {
                Font = new Font("Segoe UI", 9F),
                Format = DateTimePickerFormat.Short,
                Margin = Padding.Empty,
                Size = new Size(116, 32),
                Checked = false,
                ShowCheckBox = true
            };
            _dashDateTo.ValueChanged += (s, e) =>
            {
                if (_lastDashSourceData.Count == 0) return;
                RefreshFilteredGrid();
                UpdateFilterInfo();
            };

            _dashExportBtn = new UISymbolButton
            {
                Font = new Font("Segoe UI Semibold", 8.5F, FontStyle.Bold),
                Margin = Padding.Empty,
                Size = new Size(42, 32),
                Symbol = 61714,
                SymbolSize = 14,
                Radius = 6,
                FillColor = AccentGreen,
                FillHoverColor = Color.FromArgb(0, 180, 0)
            };
            _tooltip?.SetToolTip(_dashExportBtn, "Xuất CSV danh sách đang lọc");
            _dashExportBtn.Click += async (s, e) => await ExportOperationCurrentViewAsync(false);

            row.Controls.Add(CreateToolbarInputHost(_dashSearchBox, new Padding(0, 0, 6, 0)), 0, 0);
            row.Controls.Add(CreateToolbarInputHost(_dashDateFrom, new Padding(0, 0, 6, 0)), 1, 0);
            row.Controls.Add(CreateToolbarInputHost(_dashDateTo, new Padding(0, 0, 6, 0)), 2, 0);
            row.Controls.Add(CreateToolbarInputHost(_dashExportBtn, Padding.Empty), 3, 0);
            return row;
        }

        private static Control CreateToolbarInputHost(Control input, Padding margin)
        {
            var host = new Panel
            {
                Dock = DockStyle.Fill,
                Margin = margin,
                Padding = Padding.Empty,
                BackColor = HeaderDark
            };

            input.Margin = Padding.Empty;
            input.Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom;
            host.Controls.Add(input);

            void Align()
            {
                input.Width = host.ClientSize.Width;
                input.Left = 0;
                input.Top = Math.Max(0, host.ClientSize.Height - input.Height);
            }

            host.Resize += (s, e) => Align();
            input.SizeChanged += (s, e) => Align();
            Align();
            return host;
        }

        private Control CreateGridToolbar()
        {
            var host = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 1,
                Margin = new Padding(0, 8, 0, 0),
                Padding = new Padding(10, 6, 10, 6),
                BackColor = Color.White
            };
            host.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            host.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 416F));
            host.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

            _dashFilterInfo = new Label
            {
                Dock = DockStyle.Fill,
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                ForeColor = Color.FromArgb(70, 80, 90),
                TextAlign = ContentAlignment.MiddleLeft,
                AutoEllipsis = true,
                Text = "Inventory grid | Hiển thị 0 đơn"
            };
            host.Controls.Add(_dashFilterInfo, 0, 0);

            _dashQuickFilterPanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.RightToLeft,
                WrapContents = false,
                BackColor = Color.White,
                Margin = Padding.Empty,
                Padding = Padding.Empty
            };
            AddDashQuickFilterButton("Tất cả", "Tất cả tồn kho", Color.FromArgb(90, 105, 115));
            AddDashQuickFilterButton("KVD", "Giao thất bại", AccentBlue);
            AddDashQuickFilterButton(">48h", "Tồn quá hạn (>48h)", AccentPurple);
            AddDashQuickFilterButton("SLA", "SLA quá hạn", Color.FromArgb(245, 158, 11));
            AddDashQuickFilterButton("Cần xử lý", "Cần xử lý ngay", Color.FromArgb(245, 158, 11));
            host.Controls.Add(_dashQuickFilterPanel, 1, 0);
            return host;
        }
    }
}

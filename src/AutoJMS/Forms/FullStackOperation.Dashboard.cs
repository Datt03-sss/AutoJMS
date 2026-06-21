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
                    RowCount = 4,
                    BackColor = WorkspaceBackColor
                };
                uiTableLayoutPanel4.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
                uiTableLayoutPanel4.RowStyles.Add(new RowStyle(SizeType.Absolute, 56F));
                uiTableLayoutPanel4.RowStyles.Add(new RowStyle(SizeType.Absolute, 64F));
                uiTableLayoutPanel4.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                uiTableLayoutPanel4.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

                uiPanel10 = CreateTopBar();
                _filterBarPanel = CreateFilterBar();
                _queueNavPanel = CreateQueueNavigator();
                var body = CreateBodyPanel();

                uiTableLayoutPanel4.Controls.Add(uiPanel10, 0, 0);
                uiTableLayoutPanel4.Controls.Add(_filterBarPanel, 0, 1);
                uiTableLayoutPanel4.Controls.Add(_queueNavPanel, 0, 2);
                uiTableLayoutPanel4.Controls.Add(body, 0, 3);

                tabDash.Controls.Add(uiTableLayoutPanel4);
            }
            finally
            {
                tabDash.ResumeLayout(false);
            }
        }

        private UIPanel CreateTopBar()
        {
            var panel = new UIPanel
            {
                Dock = DockStyle.Fill,
                Margin = Padding.Empty,
                Padding = new Padding(18, 0, 18, 0),
                FillColor = HeaderDark,
                RectColor = HeaderDark,
                Text = null
            };

            var table = new UITableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 3,
                RowCount = 1,
                Margin = Padding.Empty,
                Padding = Padding.Empty,
                BackColor = Color.Transparent
            };
            table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));

            _operationHeaderTitle = new Label
            {
                Text = "AutoJMS",
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                Font = new Font("Segoe UI", 13F, FontStyle.Bold),
                ForeColor = Color.White,
                AutoSize = true,
                Margin = new Padding(0, 0, 16, 0)
            };

            _operationHeaderStatus = new Label
            {
                Text = "Điều phối Vận hành Bưu cục Realtime",
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                Font = new Font("Segoe UI", 10.5F, FontStyle.Regular),
                ForeColor = Color.FromArgb(230, 236, 245),
                AutoSize = true
            };

            table.Controls.Add(_operationHeaderTitle, 0, 0);
            table.Controls.Add(_operationHeaderStatus, 1, 0);
            panel.Controls.Add(table);
            return panel;
        }

        private UIPanel CreateFilterBar()
        {
            var panel = new UIPanel
            {
                Dock = DockStyle.Fill,
                Margin = Padding.Empty,
                Padding = new Padding(18, 0, 18, 0),
                FillColor = Color.White,
                RectColor = BorderColor,
                RectSides = ToolStripStatusLabelBorderSides.Bottom,
                Text = null
            };

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 7,
                RowCount = 1,
                Margin = Padding.Empty,
                Padding = Padding.Empty,
                BackColor = Color.Transparent
            };
            
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 140F)); // Nguồn
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 160F)); // TG Đồng bộ
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 160F)); // Phân loại (Queue)
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F)); // Search
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize)); // Sync BTN
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize)); // Export BTN
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize)); // Sync Status

            tabDash_dataSource = CreateHeaderComboBox("tabDash_dataSource");
            tabDash_dataSource.Items.AddRange(new object[] { "ALL", "LOCAL", "PHATLAI" });
            tabDash_dataSource.SelectedIndex = 1;

            tabDash_statusSelect = CreateHeaderComboBox("tabDash_statusSelect");
            tabDash_statusSelect.Items.Add("Tất cả tồn kho");
            tabDash_statusSelect.SelectedIndex = 0;

            tabDash_timeUpdateData = CreateHeaderComboBox("tabDash_timeUpdateData");
            tabDash_timeUpdateData.Items.AddRange(new object[] { "2 PHÚT", "5 PHÚT", "10 PHÚT", "30 PHÚT", "1 GIỜ" });
            tabDash_timeUpdateData.Text = "2 PHÚT";

            tabDash_updateData = CreateHeaderButton("Đồng bộ", 61473, AccentBlue);
            tabDash_updateData.Size = new Size(104, 34);

            _operationRefreshLocalButton = CreateHeaderButton("Local", 61473, AccentBlue);
            _operationRefreshLocalButton.Size = new Size(82, 34);
            _operationRefreshLocalButton.Visible = false; // Hide this since we use one sync button? No, let's keep it visible but maybe smaller.
            _operationRefreshLocalButton.Click += async (s, e) => await LoadDataAndRefreshViewsAsync();

            _dashExportBtn = CreateHeaderButton("Xuất dữ liệu", 61714, AccentSlate);
            _dashExportBtn.FillColor = Color.White;
            _dashExportBtn.ForeColor = TextPrimary;
            _dashExportBtn.RectColor = BorderColor;
            _dashExportBtn.Size = new Size(110, 34);
            _dashExportBtn.Click += async (s, e) => await ExportOperationCurrentViewAsync(false);

            tabDash_lblLastUpdate = new UISymbolLabel
            {
                Text = "Chưa tải",
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                ForeColor = TextPrimary,
                Symbol = 61473,
                SymbolSize = 14,
                AutoSize = true,
                Margin = new Padding(12, 0, 0, 0)
            };

            _dashSearchBox = new TextBox
            {
                Font = new Font("Segoe UI", 10F),
                Margin = new Padding(0, 20, 16, 0),
                Width = 260,
                ForeColor = TextPrimary,
                PlaceholderText = "Tìm mã vận đơn, NV, SĐT..."
            };
            _dashSearchBox.TextChanged += (s, e) => { if (_lastDashSourceData.Count == 0) return; RefreshFilteredGrid(); UpdateFilterInfo(); };

            _dashDateFrom = new DateTimePicker { Visible = false };
            _dashDateTo = new DateTimePicker { Visible = false };

            layout.Controls.Add(CreateFilterField("Bưu cục / Nguồn", tabDash_dataSource), 0, 0);
            layout.Controls.Add(CreateFilterField("Ngày / Tự tải lại", tabDash_timeUpdateData), 1, 0);
            layout.Controls.Add(CreateFilterField("Thời gian tồn kho", tabDash_statusSelect), 2, 0);
            layout.Controls.Add(_dashSearchBox, 3, 0);
            
            var syncFlow = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, Margin = new Padding(0, 16, 0, 0), AutoSize = true };
            syncFlow.Controls.Add(tabDash_updateData);
            syncFlow.Controls.Add(_operationRefreshLocalButton);
            layout.Controls.Add(syncFlow, 4, 0);

            var exportFlow = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, Margin = new Padding(0, 16, 0, 0), AutoSize = true };
            exportFlow.Controls.Add(_dashExportBtn);
            layout.Controls.Add(exportFlow, 5, 0);

            var statusFlow = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, Margin = new Padding(0, 24, 0, 0), AutoSize = true };
            statusFlow.Controls.Add(tabDash_lblLastUpdate);
            layout.Controls.Add(statusFlow, 6, 0);

            panel.Controls.Add(layout);
            return panel;
        }

        private Control CreateFilterField(string labelText, Control input)
        {
            var p = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Margin = new Padding(0, 8, 16, 8) };
            p.RowStyles.Add(new RowStyle(SizeType.Absolute, 18F));
            p.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            p.Controls.Add(new Label { Text = labelText, Font = new Font("Segoe UI", 8F), ForeColor = TextSecondary, Dock = DockStyle.Fill }, 0, 0);
            input.Dock = DockStyle.Fill;
            p.Controls.Add(input, 0, 1);
            return p;
        }

        private Panel CreateQueueNavigator()
        {
            var p = new Panel { Dock = DockStyle.Fill, BackColor = Color.White, Padding = new Padding(18, 12, 18, 12) };
            
            _operationFocusStrip = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 9,
                RowCount = 1,
                Margin = Padding.Empty,
                BackColor = Color.Transparent
            };
            for (int i = 0; i < 9; i++) _operationFocusStrip.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 11.11F));

            _kpiTotalInventory = CreateFocusCard("TỔNG TỒN", "0", "Tổng số đơn", "ALL", AccentBlue, "Tất cả tồn kho");
            _kpiInbound = CreateFocusCard("HÀNG ĐẾN", "0", "Đang trung chuyển", "IN", AccentBlue, "Hàng đến");
            _kpiDelivery = CreateFocusCard("PHÁT HÀNG", "0", "Đang đi giao", "DEL", AccentGreen, "Phát hàng");
            _kpiBacklog = CreateFocusCard("BACKLOG", "0", "Tồn đọng", "BL", AccentRed, "Cần xử lý ngay");
            _kpiReturn = CreateFocusCard("CHUYỂN HOÀN", "0", "Đang hoàn", "RET", Color.Orange, "Chuyển hoàn");
            _kpiInventoryCheck = CreateFocusCard("KIỂM KHO", "0", "Quá SLA", "SLA", AccentRed, "SLA quá hạn");
            _kpiCustomerService = CreateFocusCard("CSKH", "0", "Khiếu nại", "CS", AccentPurple, "CSKH");
            _kpiStationHalt = CreateFocusCard("DỪNG TRẠM", "0", "Hold", "HLT", Color.Gray, "Dừng trạm");
            _kpiStarred = CreateFocusCard("STAR", "0", "Đánh dấu", "FAV", Color.Gold, "Đánh dấu");

            _operationFocusStrip.Controls.Add(_kpiTotalInventory, 0, 0);
            _operationFocusStrip.Controls.Add(_kpiInbound, 1, 0);
            _operationFocusStrip.Controls.Add(_kpiDelivery, 2, 0);
            _operationFocusStrip.Controls.Add(_kpiBacklog, 3, 0);
            _operationFocusStrip.Controls.Add(_kpiReturn, 4, 0);
            _operationFocusStrip.Controls.Add(_kpiInventoryCheck, 5, 0);
            _operationFocusStrip.Controls.Add(_kpiCustomerService, 6, 0);
            _operationFocusStrip.Controls.Add(_kpiStationHalt, 7, 0);
            _operationFocusStrip.Controls.Add(_kpiStarred, 8, 0);

            p.Controls.Add(_operationFocusStrip);
            return p;
        }

        private Control CreateBodyPanel()
        {
            var body = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 3,
                RowCount = 1,
                Margin = Padding.Empty,
                Padding = Padding.Empty,
                BackColor = WorkspaceBackColor
            };
            body.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 256F));
            body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            body.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 336F));

            _leftContextPanel = CreateSmartContextPanel();
            var center = CreateOperationMainWorkArea();
            _rightIntelligencePanel = CreateRightOrderIntelligencePanel();

            body.Controls.Add(_leftContextPanel, 0, 0);
            body.Controls.Add(center, 1, 0);
            body.Controls.Add(_rightIntelligencePanel, 2, 0);
            return body;
        }

        private Label _lblRightWaybillNo;
        private Label _lblRightStatus;

        private Panel CreateRightOrderIntelligencePanel()
        {
            var p = new Panel { Dock = DockStyle.Fill, BackColor = Color.White, Padding = new Padding(20) };
            
            var tl = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4 };
            tl.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
            tl.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
            tl.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            tl.RowStyles.Add(new RowStyle(SizeType.Absolute, 100));

            var lblTitle = new Label { Text = "THÔNG TIN VẬN ĐƠN", Font = new Font("Segoe UI", 12F, FontStyle.Bold), ForeColor = TextPrimary, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };
            _lblRightWaybillNo = new Label { Text = "Chưa chọn", Font = new Font("Segoe UI", 10F, FontStyle.Regular), ForeColor = TextSecondary, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };
            _lblRightStatus = new Label { Text = "", Font = new Font("Segoe UI", 10F, FontStyle.Bold), ForeColor = AccentBlue, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };
            
            tl.Controls.Add(lblTitle, 0, 0);
            tl.Controls.Add(_lblRightWaybillNo, 0, 1);
            tl.Controls.Add(_lblRightStatus, 0, 2);

            p.Controls.Add(tl);
            return p;
        }

        private Panel CreateSmartContextPanel()
        {
            var p = new Panel { Dock = DockStyle.Fill, BackColor = Color.White };
            var lbl = new Label 
            { 
                Text = "DANH SÁCH ĐƠN\n\n(Smart Context)", 
                Dock = DockStyle.Fill, 
                TextAlign = ContentAlignment.MiddleCenter, 
                ForeColor = TextSecondary,
                Font = new Font("Segoe UI", 9F)
            };
            p.Controls.Add(lbl);
            return p;
        }

        private Control CreateOperationMainWorkArea()
        {
            var center = new UITableLayoutPanel
            {
                Dock = DockStyle.Fill,
                Margin = Padding.Empty,
                Padding = new Padding(20, 18, 20, 18),
                ColumnCount = 1,
                RowCount = 3,
                BackColor = WorkspaceBackColor
            };
            center.RowStyles.Add(new RowStyle(SizeType.Absolute, 44F)); // Toolbar
            center.RowStyles.Add(new RowStyle(SizeType.Percent, 100F)); // Grid
            center.RowStyles.Add(new RowStyle(SizeType.Absolute, 34F)); // Footer metrics

            var gridToolbar = CreateGridToolbar();

            tabPage3 = new TabPage { Name = "tabPage3", Text = "Tồn kho" };
            tabPage4 = new TabPage { Name = "tabPage4", Text = "Thời hiệu cũ" };
            uiTabControl2 = new UITabControl { Dock = DockStyle.Fill, Visible = false };
            uiDataGridView2 = CreateGrid("uiDataGridView2");

            tabDash_dataGridView = CreateGrid("tabDash_dataGridView");
            tabDash_dataGridView.Dock = DockStyle.Fill;
            tabDash_dataGridView.BorderStyle = BorderStyle.None;

            _operationGridHost = new Panel { Dock = DockStyle.Fill, BackColor = Color.White, BorderStyle = BorderStyle.FixedSingle };
            _operationInventoryWorkspace = new Panel { Dock = DockStyle.Fill, Visible = true };
            _operationInventoryWorkspace.Controls.Add(tabDash_dataGridView);
            
            InitializeWaybillJourneyWorkspace();
            _operationGridHost.Controls.Add(_waybillJourneyWorkspace);
            _operationGridHost.Controls.Add(_operationInventoryWorkspace);

            _operationMiniMetricStrip = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight,
                BackColor = Color.Transparent,
                Margin = new Padding(0, 6, 0, 0)
            };

            center.Controls.Add(gridToolbar, 0, 0);
            center.Controls.Add(_operationGridHost, 0, 1);
            center.Controls.Add(_operationMiniMetricStrip, 0, 2);

            _operationGridFilterToolbar = new GridFilterToolbarControl { Visible = false };
            _operationGridFilterToolbar.FilterRequested += OperationGridFilterToolbar_FilterRequested;
            _operationGridFilterToolbar.PresetRequested += OperationGridFilterToolbar_PresetRequested;

            return center;
        }

        private UIComboBox CreateHeaderComboBox(string name)
        {
            var combo = CreateComboBox(name);
            combo.Font = new Font("Segoe UI", 9.5F);
            combo.FillColor = Color.White;
            combo.RectColor = BorderColor;
            combo.Size = new Size(150, 32);
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
                Size = new Size(96, 32),
                Margin = new Padding(0, 0, 6, 0)
            };
        }

        private KpiCardControl CreateFocusCard(string title, string value, string subtitle, string badge, Color color, string filterKey)
        {
            var card = new KpiCardControl { Dock = DockStyle.Fill };
            card.SetMetric(title, value, subtitle, badge, color, filterKey);
            card.CardClicked += OperationKpiCard_Clicked;
            return card;
        }

        private Control CreateGridToolbar()
        {
            var host = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 1,
                Margin = Padding.Empty,
                BackColor = Color.Transparent
            };
            host.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            host.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            _dashFilterInfo = new Label
            {
                Dock = DockStyle.Fill,
                Font = new Font("Segoe UI", 9.5F, FontStyle.Bold),
                ForeColor = TextPrimary,
                TextAlign = ContentAlignment.MiddleLeft,
                Text = "Danh sách đơn | Tất cả tồn kho"
            };
            host.Controls.Add(_dashFilterInfo, 0, 0);

            _dashQuickFilterPanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.RightToLeft,
                AutoSize = true
            };
            AddDashQuickFilterButton("Tất cả", "Tất cả tồn kho", AccentSlate);
            AddDashQuickFilterButton("KVD", "Giao thất bại", AccentBlue);
            AddDashQuickFilterButton(">48h", "Tồn quá hạn (>48h)", AccentPurple);
            AddDashQuickFilterButton("SLA", "SLA quá hạn", AccentWarning);
            AddDashQuickFilterButton("Cần xử lý", "Cần xử lý ngay", AccentRed);
            host.Controls.Add(_dashQuickFilterPanel, 1, 0);
            return host;
        }
    }
}







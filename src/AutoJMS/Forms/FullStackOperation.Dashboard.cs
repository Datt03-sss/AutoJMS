using AutoJMS.FullStack.UI.OperationCenter;
using Sunny.UI;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using AutoJMS.Data;

namespace AutoJMS
{
    public partial class FullStackOperation
    {
        private Microsoft.Web.WebView2.WinForms.WebView2 _webView;
        private bool _webViewInitialized = false;
        private static readonly System.Collections.Generic.HashSet<string> StarredWaybills = new(StringComparer.OrdinalIgnoreCase);

        private void BuildDashboardPageCodeFirst()
        {
            tabDash.SuspendLayout();
            try
            {
                // Call creator methods so all old controls are instantiated in memory to prevent NullReferenceExceptions
                uiPanel10 = CreateTopBar();
                _filterBarPanel = CreateFilterBar();
                _queueNavPanel = CreateQueueNavigator();
                var body = CreateBodyPanel();

                _webView = new Microsoft.Web.WebView2.WinForms.WebView2
                {
                    Dock = DockStyle.Fill
                };
                tabDash.Controls.Add(_webView);

                LoadStarredWaybills();
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
            tabDash_timeUpdateData.Text = "30 PHÚT";

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

            _dashDateFrom = new DateTimePicker { Visible = false, ShowCheckBox = true, Checked = false, Format = DateTimePickerFormat.Custom, CustomFormat = "dd/MM/yyyy HH:mm" };
            _dashDateTo = new DateTimePicker { Visible = false, ShowCheckBox = true, Checked = false, Format = DateTimePickerFormat.Custom, CustomFormat = "dd/MM/yyyy HH:mm" };

            layout.Controls.Add(CreateFilterField("Bưu cục", tabDash_dataSource), 0, 0);
            layout.Controls.Add(CreateFilterField("Cập nhật sau", tabDash_timeUpdateData), 1, 0);
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

        private async System.Threading.Tasks.Task InitializeWebView2Async()
        {
            if (_webViewInitialized) return;
            try
            {
                var userDataFolder = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "AppData", "BrowserData");
                var env = await Microsoft.Web.WebView2.Core.CoreWebView2Environment.CreateAsync(null, userDataFolder);
                await _webView.EnsureCoreWebView2Async(env);

                _webView.CoreWebView2.SetVirtualHostNameToFolderMapping(
                    "autojms.local",
                    System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Web"),
                    Microsoft.Web.WebView2.Core.CoreWebView2HostResourceAccessKind.Allow);

                _webView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
                _webView.CoreWebView2.Settings.IsStatusBarEnabled = false;
                // Lock page zoom for the whole dashboard (Ctrl+scroll / pinch / Ctrl±) so image
                // zoom in the overlay never rescales tabDash itself.
                _webView.CoreWebView2.Settings.IsZoomControlEnabled = false;
                _webView.ZoomFactor = 1.0;

                _webView.WebMessageReceived += OnWebViewMessageReceived;
                _webView.CoreWebView2.Navigate("https://autojms.local/index.html");

                _webViewInitialized = true;
            }
            catch (Exception ex)
            {
                AppLogger.Error("InitializeWebView2Async failed", ex);
                MessageBox.Show("Lỗi khởi tạo WebView2: " + ex.Message, "AutoJMS Dashboard", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void OnWebViewMessageReceived(object sender, Microsoft.Web.WebView2.Core.CoreWebView2WebMessageReceivedEventArgs e)
        {
            try
            {
                var rawJson = e.WebMessageAsJson;
                using var doc = System.Text.Json.JsonDocument.Parse(rawJson);
                var root = doc.RootElement;
                if (!root.TryGetProperty("action", out var actionProp)) return;
                var action = actionProp.GetString();

                if (action == "READY")
                {
                    _ = LoadDataAndRefreshViewsAsync();
                }
                else if (action == "SYNC")
                {
                    tabDash_updateData_Click(null, null);
                }
                else if (action == "EXPORT")
                {
                    _ = ExportOperationCurrentViewAsync(false);
                }
                else if (action == "CHANGE_SOURCE")
                {
                    if (root.TryGetProperty("source", out var valProp))
                    {
                        var source = valProp.GetString();
                        tabDash_dataSource.SelectedItem = source;
                        tabDash_dataSource_SelectedIndexChanged(null, null);
                    }
                }
                else if (action == "CHANGE_SEARCH")
                {
                    if (root.TryGetProperty("text", out var valProp))
                    {
                        var text = valProp.GetString();
                        _dashSearchBox.Text = text;
                    }
                }
                else if (action == "CHANGE_TIME_INTERVAL")
                {
                    if (root.TryGetProperty("text", out var valProp))
                    {
                        var text = valProp.GetString();
                        tabDash_timeUpdateData.Text = text;
                        tabDash_timeUpdateData_SelectedIndexChanged(null, null);
                    }
                }
                else if (action == "CHANGE_STATUS_SELECT")
                {
                    if (root.TryGetProperty("text", out var valProp))
                    {
                        var text = valProp.GetString();
                        tabDash_statusSelect.Text = text;
                        tabDash_statusSelect_SelectedIndexChanged(null, null);
                    }
                }
                else if (action == "FETCH_JOURNEY")
                {
                    if (root.TryGetProperty("waybillNo", out var valProp))
                    {
                        var waybillNo = valProp.GetString();
                        ShowWaybillJourneyWorkspace(waybillNo);
                    }
                }
                else if (action == "SELECT_WAYBILL")
                {
                    if (root.TryGetProperty("waybillNo", out var valProp))
                    {
                        var waybillNo = valProp.GetString();
                        _ = FetchAndPostWaybillDetailAsync(waybillNo);
                        _ = FetchAndPostIssueHistoryAsync(waybillNo);
                    }
                }
                else if (action == "FETCH_RECEIVER_NETWORK")
                {
                    if (root.TryGetProperty("waybillNo", out var valProp))
                    {
                        _ = FetchAndPostReceiverNetworkAsync(valProp.GetString());
                    }
                }
                else if (action == "FETCH_NETWORK_INFO")
                {
                    if (root.TryGetProperty("code", out var valProp))
                    {
                        _ = FetchAndPostNetworkInfoAsync(valProp.GetString());
                    }
                }
                else if (action == "FETCH_NETWORK_SEARCH")
                {
                    if (root.TryGetProperty("query", out var valProp))
                    {
                        _ = FetchAndPostNetworkSearchAsync(valProp.GetString());
                    }
                }
                else if (action == "SUBMIT_ISSUE")
                {
                    string Sv(string k) => root.TryGetProperty(k, out var p) ? (p.GetString() ?? "") : "";
                    _ = SubmitIssueRegistrationAsync(Sv("waybillNo"), Sv("level1"), Sv("level2"), Sv("level2Code"), Sv("level2Name"), Sv("network"), Sv("description"));
                }
                else if (action == "TOGGLE_STAR")
                {
                    if (root.TryGetProperty("waybillNo", out var valProp))
                    {
                        var waybillNo = valProp.GetString();
                        ToggleStarWaybill(waybillNo);
                    }
                }
                else if (action == "SEARCH")
                {
                    if (root.TryGetProperty("text", out var valProp))
                    {
                        _dashSearchBox.Text = valProp.GetString() ?? string.Empty;
                    }
                }
                else if (action == "CHANGE_DATE_RANGE")
                {
                    string fromIso = root.TryGetProperty("from", out var fromProp) ? fromProp.GetString() : null;
                    string toIso = root.TryGetProperty("to", out var toProp) ? toProp.GetString() : null;
                    ApplyDashboardDateRange(fromIso, toIso);
                }
                else if (action == "REGISTER_ISSUE")
                {
                    if (root.TryGetProperty("waybillNo", out var valProp))
                    {
                        _ = RegisterIssueFromWebAsync(valProp.GetString());
                    }
                }
                else if (action == "SAVE_NOTE")
                {
                    string noteWaybill = root.TryGetProperty("waybillNo", out var wbProp) ? wbProp.GetString() : null;
                    string noteText = root.TryGetProperty("text", out var textProp) ? textProp.GetString() : null;
                    _ = SaveNoteFromWebAsync(noteWaybill, noteText);
                }
                else if (action == "REFRESH_JOURNEY")
                {
                    if (root.TryGetProperty("waybillNo", out var valProp))
                    {
                        ShowWaybillJourneyWorkspace(valProp.GetString());
                    }
                }
            }
            catch (Exception ex)
            {
                AppLogger.Error("OnWebViewMessageReceived failed", ex);
            }
        }

        private void ApplyDashboardDateRange(string fromIso, string toIso)
        {
            try
            {
                if (_dashDateFrom != null)
                {
                    _dashDateFrom.ShowCheckBox = true;
                    if (!string.IsNullOrWhiteSpace(fromIso) && DateTime.TryParse(fromIso, out var fromValue))
                    {
                        _dashDateFrom.Value = fromValue;
                        _dashDateFrom.Checked = true;
                    }
                    else
                    {
                        _dashDateFrom.Checked = false;
                    }
                }

                if (_dashDateTo != null)
                {
                    _dashDateTo.ShowCheckBox = true;
                    if (!string.IsNullOrWhiteSpace(toIso) && DateTime.TryParse(toIso, out var toValue))
                    {
                        _dashDateTo.Value = toValue;
                        _dashDateTo.Checked = true;
                    }
                    else
                    {
                        _dashDateTo.Checked = false;
                    }
                }

                if (_lastDashSourceData.Count > 0)
                {
                    RefreshFilteredGrid();
                    UpdateFilterInfo();
                }
            }
            catch (Exception ex)
            {
                AppLogger.Error("ApplyDashboardDateRange failed", ex);
            }
        }

        private async System.Threading.Tasks.Task RegisterIssueFromWebAsync(string waybillNo)
        {
            if (string.IsNullOrWhiteSpace(waybillNo)) return;
            try
            {
                await _fullStackWorkflowService.CreateTaskAsync(
                    waybillNo,
                    "REGISTER_ISSUE",
                    Math.Max(50, GetRiskScore(GetWaybillByNo(waybillNo))),
                    Environment.UserName,
                    "Đăng ký kiện vấn đề từ Dashboard",
                    _cts.Token);
                SetFullStackStatus($"Đã đăng ký kiện vấn đề {waybillNo}");
                await RefreshOperationMetadataAsync();
                RefreshFilteredGrid();
            }
            catch (Exception ex)
            {
                AppLogger.Error("RegisterIssueFromWebAsync failed", ex);
            }
        }

        private async System.Threading.Tasks.Task SaveNoteFromWebAsync(string waybillNo, string text)
        {
            if (string.IsNullOrWhiteSpace(waybillNo) || string.IsNullOrWhiteSpace(text)) return;
            try
            {
                await _fullStackWorkflowService.AddNoteAsync(waybillNo, text, Environment.UserName, _cts.Token);
                SetFullStackStatus($"Đã lưu ghi chú cho {waybillNo}");
                await RefreshOperationMetadataAsync();
                RefreshFilteredGrid();
            }
            catch (Exception ex)
            {
                AppLogger.Error("SaveNoteFromWebAsync failed", ex);
            }
        }

        private void LoadStarredWaybills()
        {
            try
            {
                var filePath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "AppData", "starred_waybills.txt");
                if (System.IO.File.Exists(filePath))
                {
                    var lines = System.IO.File.ReadAllLines(filePath);
                    lock (StarredWaybills)
                    {
                        StarredWaybills.Clear();
                        foreach (var line in lines)
                        {
                            if (!string.IsNullOrWhiteSpace(line))
                            {
                                StarredWaybills.Add(line.Trim());
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                AppLogger.Error("LoadStarredWaybills failed", ex);
            }
        }

        private void SaveStarredWaybills()
        {
            try
            {
                var dirPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "AppData");
                if (!System.IO.Directory.Exists(dirPath))
                {
                    System.IO.Directory.CreateDirectory(dirPath);
                }
                var filePath = System.IO.Path.Combine(dirPath, "starred_waybills.txt");
                System.Collections.Generic.List<string> list;
                lock (StarredWaybills)
                {
                    list = new System.Collections.Generic.List<string>(StarredWaybills);
                }
                System.IO.File.WriteAllLines(filePath, list);
            }
            catch (Exception ex)
            {
                AppLogger.Error("SaveStarredWaybills failed", ex);
            }
        }

        private void ToggleStarWaybill(string waybillNo)
        {
            if (string.IsNullOrWhiteSpace(waybillNo)) return;
            lock (StarredWaybills)
            {
                if (StarredWaybills.Contains(waybillNo))
                {
                    StarredWaybills.Remove(waybillNo);
                }
                else
                {
                    StarredWaybills.Add(waybillNo);
                }
            }
            SaveStarredWaybills();
            _ = LoadDataAndRefreshViewsAsync();
        }

        private static string NormalizeQueueText(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return string.Empty;

            var normalized = value
                .Replace('Đ', 'D')
                .Replace('đ', 'd')
                .Normalize(System.Text.NormalizationForm.FormD);
            var builder = new System.Text.StringBuilder(normalized.Length);
            foreach (var ch in normalized)
            {
                if (System.Globalization.CharUnicodeInfo.GetUnicodeCategory(ch) != System.Globalization.UnicodeCategory.NonSpacingMark)
                {
                    builder.Append(char.ToLowerInvariant(ch));
                }
            }

            return builder.ToString().Normalize(System.Text.NormalizationForm.FormC).Trim();
        }

        private static bool OperationHas(WaybillDbModel row, params string[] needles)
        {
            var operation = NormalizeQueueText(row?.ThaoTacCuoi ?? string.Empty);
            if (operation.Length == 0) return false;

            foreach (var needle in needles)
            {
                var normalizedNeedle = NormalizeQueueText(needle);
                if (normalizedNeedle.Length > 0 && operation.Contains(normalizedNeedle))
                    return true;
            }

            return false;
        }

        private static bool IsRedeliveryOperation(WaybillDbModel row) =>
            OperationHas(row, "giao lại", "giao lai", "phát lại", "phat lai", "redelivery");

        private static bool IsDeliveryScanOperation(WaybillDbModel row) =>
            !IsRedeliveryOperation(row) && OperationHas(row, "quét phát", "quet phat", "phát hàng", "phat hang");

        private static bool IsReturnRegisteredOperation(WaybillDbModel row) =>
            OperationHas(row, "đăng ký chuyển hoàn", "dang ky chuyen hoan", "chuyển hoàn", "chuyen hoan", "return");

        private static bool IsWarehouseHoldOperation(WaybillDbModel row) =>
            OperationHas(row, "lưu kho", "luu kho", "warehouse");

        private static bool IsAwaitingPickupOperation(WaybillDbModel row) =>
            OperationHas(row, "chờ lấy", "cho lay", "pickup");

        // Any null / empty / whitespace display value becomes "-" so the UI never shows blank cells.
        private static string Dash(string value) => string.IsNullOrWhiteSpace(value) ? "-" : value.Trim();

        // Like Dash, but also treats the "empty" placeholder (used for un-enriched rows) as blank.
        private static string DashE(string value) =>
            (string.IsNullOrWhiteSpace(value) || string.Equals(value.Trim(), "empty", StringComparison.OrdinalIgnoreCase))
                ? "-" : value.Trim();

        private object MapWaybillToDto(WaybillDbModel row)
        {
            var mains = new System.Collections.Generic.List<string>();
            var subs = new System.Collections.Generic.List<string>();
            var subKeys = new System.Collections.Generic.List<string>();

            void AddMain(string key)
            {
                if (!string.IsNullOrWhiteSpace(key) && !mains.Contains(key)) mains.Add(key);
            }

            void AddSub(string label)
            {
                if (!string.IsNullOrWhiteSpace(label) && !subs.Contains(label)) subs.Add(label);
            }

            void AddSubKey(string key)
            {
                if (!string.IsNullOrWhiteSpace(key) && !subKeys.Contains(key)) subKeys.Add(key);
            }

            AddMain("tong-ton");
            AddSub("Tất cả");
            AddSubKey("all");

            var isDeliveryScan = IsDeliveryScanOperation(row);
            var isRedelivery = IsRedeliveryOperation(row);
            var isReturnRegistered = IsReturnRegisteredOperation(row);

            if (OperationHas(row, "hàng đến", "hang den"))
            {
                AddMain("hang-den");
                // "Đã đến" and "Chưa quét đến" are sourced from the JMS arrival-monitor report,
                // not from local data (see FullStackOperation.ArrivalMonitor.cs).
            }

            if (isDeliveryScan || isRedelivery)
            {
                AddMain("phat-hang");
                AddSub("Cần phát");
                AddSubKey("need_delivery");
                if (isDeliveryScan)
                {
                    AddSub("Quét phát hàng");
                    AddSubKey("delivery_scan");
                }
                if (isRedelivery)
                {
                    AddSub("Giao lại hàng");
                    AddSubKey("redelivery");
                }

                if (IsNotDispatched(row))
                {
                    AddSub("Chưa phát");
                    AddSubKey("not_delivered");
                }
                else
                {
                    AddSub("Đã phát");
                    AddSubKey("delivered");
                }
            }

            if (IsNeedsAction(row))
            {
                AddMain("backlog");
                double days = GetWarehouseAgeDays(row.ThoiGianThaoTac);
                if (days >= 7.0)
                {
                    AddSub("7D+");
                    AddSub("7D");
                    AddSubKey("age_7d_plus");
                    AddSubKey("age_7d");
                }
                else if (days >= 6.0) { AddSub("6D"); AddSubKey("age_6d"); }
                else if (days >= 5.0) { AddSub("5D"); AddSubKey("age_5d"); }
                else if (days >= 4.0) { AddSub("4D"); AddSubKey("age_4d"); }
                else if (days >= 3.0) { AddSub("3D"); AddSubKey("age_3d"); }
                else { AddSub("<2D"); AddSubKey("age_lt_2d"); }
            }

            if (isReturnRegistered || IsPendingReturn(row))
            {
                AddMain("chuyen-hoan");
                if (isReturnRegistered)
                {
                    AddSub("Đăng ký chuyển hoàn");
                    AddSubKey("return_registered");
                }
                if (row.RebackStatus == "Đợi xét duyệt") { AddSub("Đợi xét duyệt"); AddSubKey("return_waiting_approval"); }
                else if (row.RebackStatus == "Đã phê duyệt") { AddSub("Đã phê duyệt"); AddSubKey("return_approved"); }
                else if (row.RebackStatus == "Phát lại") { AddSub("Phát lại"); AddSubKey("return_redelivery"); }
            }

            if (IsSlaBreached(row.ThoiGianNhanHang))
            {
                AddMain("kiem-kho");
                AddSub("Đơn cần kiểm");
                AddSub("Kiểm thiếu");
                AddSubKey("inventory_check");
                AddSubKey("inventory_missing");
            }

            if (HasTaskWaybill(row))
            {
                AddMain("cskh");
                AddSub("Ticket CSKH");
                AddSubKey("support_ticket");
                if (IsSlaBreached(row.ThoiGianNhanHang))
                {
                    AddSub("Khiếu nại SLA");
                    AddSubKey("sla_complaint");
                }
            }

            double ageDays = GetWarehouseAgeDays(row.ThoiGianThaoTac);
            if (ageDays >= 1.0)
            {
                AddMain("dung-tram");
                if (ageDays >= 7.0) { AddSub("Dừng >7D"); AddSubKey("halt_7d"); }
                else if (ageDays >= 3.0) { AddSub("Dừng >3D"); AddSubKey("halt_3d"); }
                else if (ageDays >= 2.0) { AddSub("Dừng >48h"); AddSubKey("halt_48h"); }
                else if (ageDays >= 1.0) { AddSub("Dừng >24h"); AddSubKey("halt_24h"); }
                else { AddSub("Dừng >12h"); AddSubKey("halt_12h"); }
            }

            bool isStarred = false;
            lock (StarredWaybills)
            {
                isStarred = StarredWaybills.Contains(row.WaybillNo);
            }

            if (isStarred)
            {
                AddMain("star");
                AddSub("Đơn đã ghim");
                AddSub("Đang theo dõi");
                AddSubKey("starred");
                AddSubKey("watching");
                if (IsNeedsAction(row)) { AddSub("Chưa xử lý"); AddSubKey("star_unresolved"); }
                else { AddSub("Đã xử lý"); AddSubKey("star_resolved"); }
            }

            if (IsWarehouseHoldOperation(row))
            {
                AddSub("Lưu kho");
                AddSubKey("warehouse_hold");
            }

            if (IsAwaitingPickupOperation(row))
            {
                AddSub("Chờ lấy hàng");
                AddSubKey("awaiting_pickup");
            }

            // "Tồn kho" = số ngày tồn (kể từ thao tác cuối cùng).
            bool hasOpTime = !string.IsNullOrWhiteSpace(row.ThoiGianThaoTac)
                && !string.Equals(row.ThoiGianThaoTac.Trim(), "empty", StringComparison.OrdinalIgnoreCase);
            double tonKhoDays = hasOpTime ? GetWarehouseAgeDays(row.ThoiGianThaoTac) : 0;
            int tonKhoWhole = (int)Math.Floor(tonKhoDays);
            string tonKhoText = !hasOpTime ? "-" : (tonKhoWhole <= 0 ? "< 1 ngày" : tonKhoWhole + " ngày");
            string tonKhoColor = !hasOpTime
                ? "#6b7588"
                : (tonKhoDays >= 7.0 ? "#d23a2e" : (tonKhoDays >= 3.0 ? "#e07b39" : "#3a4555"));

            return new
            {
                code = Dash(row.WaybillNo),
                curOp = DashE(row.TrangThaiHienTai),
                lastOp = DashE(row.ThaoTacCuoi),
                opTime = DashE(row.ThoiGianThaoTac),
                tonKho = tonKhoText,
                tonKhoColor = tonKhoColor,
                recipient = Dash(row.NhanVienNhanHang),
                phone = "-",
                addr = Dash(row.DiaChiNhanHang),
                source = Dash(row.BuuCucThaoTac),
                cod = string.IsNullOrWhiteSpace(row.CODThucTe) ? "0 đ" : row.CODThucTe.Trim(),
                sla = IsSlaBreached(row.ThoiGianNhanHang) ? "Quá hạn" : "Trong hạn",
                slaColor = IsSlaBreached(row.ThoiGianNhanHang) ? "#d23a2e" : "#1c8a3c",
                service = "Chuyển phát",
                weight = Dash(row.TrongLuong),
                pkgs = "1",
                orderNo = Dash(row.WaybillNo),
                sender = Dash(row.TenNguoiGui),
                staff = Dash(row.NguoiThaoTac),
                senderAddr = Dash(row.DiaChiLayHang),
                content = Dash(row.NoiDungHangHoa),
                payMethod = Dash(row.PTTT),
                ward = Dash(row.Phuong),
                lastScan = Dash(row.ThoiGianThaoTac),
                status = Dash(row.TrangThaiHienTai),
                op = row.ThaoTacCuoi ?? "",
                issueReason = Dash(row.NguyenNhanKienVanDe),
                mains = mains,
                mainKeys = mains,
                subs = subs,
                subKeys = subKeys,
                isStar = isStarred
            };
        }

        private void PostStateToWebView2()
        {
            if (_webView == null || !_webViewInitialized || _webView.CoreWebView2 == null) return;

            try
            {
                string siteId = "214A02";
                // "Last Update" = only the timestamp the data was last refreshed (never status text).
                string lastUpdateTime = _lastDataUpdate.HasValue
                    ? _lastDataUpdate.Value.ToString("HH:mm:ss dd/MM/yyyy")
                    : "Chưa cập nhật";

                string syncStatus = "DB: Synced";

                int tongTonCount = _cloudData.Count;
                // "Hàng đến" card mirrors the "Đã đến" (arrived today) count from the arrival monitor.
                int phatHangCount = _cloudData.Count(x => IsDeliveryScanOperation(x) || IsRedeliveryOperation(x));
                int backlogCount = _cloudData.Count(IsNeedsAction);
                int chuyenHoanCount = _cloudData.Count(x => x.ThaoTacCuoi == "Chuyển hoàn" || IsPendingReturn(x));
                int kiemKhoCount = _cloudData.Count(x => IsSlaBreached(x.ThoiGianNhanHang));
                int cskhCount = _cloudData.Count(HasTaskWaybill);
                int dungTramCount = _cloudData.Count(x => GetWarehouseAgeDays(x.ThoiGianThaoTac) >= 1.0);
                
                int starCount = 0;
                lock (StarredWaybills)
                {
                    starCount = _cloudData.Count(x => StarredWaybills.Contains(x.WaybillNo));
                }

                var mainsObj = new System.Collections.Generic.Dictionary<string, string>
                {
                    { "tong-ton", tongTonCount.ToString("N0") },
                    { "hang-den", _arrivalArrivedTotal.ToString("N0") },
                    { "phat-hang", phatHangCount.ToString("N0") },
                    { "backlog", backlogCount.ToString("N0") },
                    { "chuyen-hoan", chuyenHoanCount.ToString("N0") },
                    { "kiem-kho", kiemKhoCount.ToString("N0") },
                    { "cskh", cskhCount.ToString("N0") },
                    { "dung-tram", dungTramCount.ToString("N0") },
                    { "star", starCount.ToString("N0") }
                };

                var subsObj = new System.Collections.Generic.Dictionary<string, System.Collections.Generic.List<object>>
                {
                    { "tong-ton", new System.Collections.Generic.List<object> {
                        new { key = "all", label = "Tất cả", count = tongTonCount.ToString("N0") },
                        new { key = "delivery_scan", label = "Quét phát hàng", count = _cloudData.Count(IsDeliveryScanOperation).ToString("N0") },
                        new { key = "return_registered", label = "Đăng ký chuyển hoàn", count = _cloudData.Count(IsReturnRegisteredOperation).ToString("N0") },
                        new { key = "redelivery", label = "Giao lại hàng", count = _cloudData.Count(IsRedeliveryOperation).ToString("N0") },
                        new { key = "awaiting_pickup", label = "Chờ lấy hàng", count = _cloudData.Count(IsAwaitingPickupOperation).ToString("N0") },
                        new { key = "warehouse_hold", label = "Lưu kho", count = _cloudData.Count(IsWarehouseHoldOperation).ToString("N0") }
                    } },
                    { "hang-den", new System.Collections.Generic.List<object> {
                        new { key = "hang_den_all", label = "Đã đến", count = _arrivalArrivedTotal.ToString("N0") },
                        new { key = "not_scanned_in", label = "Chưa quét đến", count = _arrivalNotScannedTotal.ToString("N0") }
                    } },
                    { "phat-hang", new System.Collections.Generic.List<object> {
                        new { key = "need_delivery", label = "Cần phát", count = phatHangCount.ToString("N0") },
                        new { key = "delivered", label = "Đã phát", count = _cloudData.Count(x => (IsDeliveryScanOperation(x) || IsRedeliveryOperation(x)) && !IsNotDispatched(x)).ToString("N0") },
                        new { key = "not_delivered", label = "Chưa phát", count = _cloudData.Count(x => (IsDeliveryScanOperation(x) || IsRedeliveryOperation(x)) && IsNotDispatched(x)).ToString("N0") }
                    } },
                    { "backlog", new System.Collections.Generic.List<object> {
                        new { key = "age_7d_plus", label = "7D+", count = _cloudData.Count(x => IsNeedsAction(x) && GetWarehouseAgeDays(x.ThoiGianThaoTac) >= 7.0).ToString("N0") },
                        new { key = "age_7d", label = "7D", count = _cloudData.Count(x => IsNeedsAction(x) && GetWarehouseAgeDays(x.ThoiGianThaoTac) >= 7.0).ToString("N0") },
                        new { key = "age_6d", label = "6D", count = _cloudData.Count(x => IsNeedsAction(x) && GetWarehouseAgeDays(x.ThoiGianThaoTac) >= 6.0 && GetWarehouseAgeDays(x.ThoiGianThaoTac) < 7.0).ToString("N0") },
                        new { key = "age_5d", label = "5D", count = _cloudData.Count(x => IsNeedsAction(x) && GetWarehouseAgeDays(x.ThoiGianThaoTac) >= 5.0 && GetWarehouseAgeDays(x.ThoiGianThaoTac) < 6.0).ToString("N0") },
                        new { key = "age_4d", label = "4D", count = _cloudData.Count(x => IsNeedsAction(x) && GetWarehouseAgeDays(x.ThoiGianThaoTac) >= 4.0 && GetWarehouseAgeDays(x.ThoiGianThaoTac) < 5.0).ToString("N0") },
                        new { key = "age_3d", label = "3D", count = _cloudData.Count(x => IsNeedsAction(x) && GetWarehouseAgeDays(x.ThoiGianThaoTac) >= 3.0 && GetWarehouseAgeDays(x.ThoiGianThaoTac) < 4.0).ToString("N0") },
                        new { key = "age_lt_2d", label = "<2D", count = _cloudData.Count(x => IsNeedsAction(x) && GetWarehouseAgeDays(x.ThoiGianThaoTac) < 2.0).ToString("N0") }
                    } },
                    { "chuyen-hoan", new System.Collections.Generic.List<object> {
                        new { key = "return_waiting_approval", label = "Đợi xét duyệt", count = _cloudData.Count(x => (IsReturnRegisteredOperation(x) || IsPendingReturn(x)) && x.RebackStatus == "Đợi xét duyệt").ToString("N0") },
                        new { key = "return_approved", label = "Đã phê duyệt", count = _cloudData.Count(x => (IsReturnRegisteredOperation(x) || IsPendingReturn(x)) && x.RebackStatus == "Đã phê duyệt").ToString("N0") },
                        new { key = "return_redelivery", label = "Phát lại", count = _cloudData.Count(x => (IsReturnRegisteredOperation(x) || IsPendingReturn(x)) && x.RebackStatus == "Phát lại").ToString("N0") }
                    } },
                    { "kiem-kho", new System.Collections.Generic.List<object> {
                        new { key = "inventory_check", label = "Đơn cần kiểm", count = kiemKhoCount.ToString("N0") },
                        new { key = "inventory_missing", label = "Kiểm thiếu", count = _cloudData.Count(x => IsSlaBreached(x.ThoiGianNhanHang)).ToString("N0") }
                    } },
                    { "cskh", new System.Collections.Generic.List<object> {
                        new { key = "support_ticket", label = "Ticket CSKH", count = cskhCount.ToString("N0") },
                        new { key = "sla_complaint", label = "Khiếu nại SLA", count = _cloudData.Count(x => HasTaskWaybill(x) && IsSlaBreached(x.ThoiGianNhanHang)).ToString("N0") },
                        new { key = "responsibility_complaint", label = "Khiếu nại chịu trách nhiệm", count = "0" },
                        new { key = "cldv_appeal", label = "Kháng cáo CLDV", count = "0" },
                        new { key = "expected_penalty", label = "Tiền phạt dự kiến", count = "0" }
                    } },
                    { "dung-tram", new System.Collections.Generic.List<object> {
                        new { key = "halt_12h", label = "Dừng >12h", count = _cloudData.Count(x => GetWarehouseAgeDays(x.ThoiGianThaoTac) >= 0.5).ToString("N0") },
                        new { key = "halt_24h", label = "Dừng >24h", count = _cloudData.Count(x => GetWarehouseAgeDays(x.ThoiGianThaoTac) >= 1.0).ToString("N0") },
                        new { key = "halt_48h", label = "Dừng >48h", count = _cloudData.Count(x => GetWarehouseAgeDays(x.ThoiGianThaoTac) >= 2.0).ToString("N0") },
                        new { key = "halt_3d", label = "Dừng >3D", count = _cloudData.Count(x => GetWarehouseAgeDays(x.ThoiGianThaoTac) >= 3.0).ToString("N0") },
                        new { key = "halt_7d", label = "Dừng >7D", count = _cloudData.Count(x => GetWarehouseAgeDays(x.ThoiGianThaoTac) >= 7.0).ToString("N0") },
                        new { key = "no_new_scan", label = "Không có scan mới", count = "0" }
                    } },
                    { "star", new System.Collections.Generic.List<object> {
                        new { key = "starred", label = "Đơn đã ghim", count = starCount.ToString("N0") },
                        new { key = "watching", label = "Đang theo dõi", count = starCount.ToString("N0") },
                        new { key = "star_unresolved", label = "Chưa xử lý", count = _cloudData.Count(x => StarredWaybills.Contains(x.WaybillNo) && IsNeedsAction(x)).ToString("N0") },
                        new { key = "star_resolved", label = "Đã xử lý", count = _cloudData.Count(x => StarredWaybills.Contains(x.WaybillNo) && !IsNeedsAction(x)).ToString("N0") }
                    } }
                };

                var waybillsList = _cloudData.Select(MapWaybillToDto).ToList();
                // "Đã đến" is a count only (no list). "Chưa quét đến" shows its list.
                if (_arrivalNotScanned != null && _arrivalNotScanned.Count > 0)
                    waybillsList.AddRange(_arrivalNotScanned);
                AppLogger.Info($"[FullStackOperation] PostStateToWebView2 waybills mapped: source={_cloudData.Count}, mapped={waybillsList.Count}, arrivedTotal={_arrivalArrivedTotal}, notScanned={_arrivalNotScanned?.Count ?? 0}");

                var starredCodesObj = new System.Collections.Generic.Dictionary<string, bool>();
                lock (StarredWaybills)
                {
                    foreach (var code in StarredWaybills)
                    {
                        starredCodesObj[code] = true;
                    }
                }

                var payload = new
                {
                    type = "UPDATE_DATA",
                    siteId = siteId,
                    lastUpdateTime = lastUpdateTime,
                    syncStatus = syncStatus,
                    counts = new
                    {
                        mains = mainsObj,
                        subs = subsObj
                    },
                    waybills = waybillsList,
                    starredCodes = starredCodesObj,
                    selectedSource = tabDash_dataSource?.Text ?? "LOCAL",
                    selectedTimeInterval = tabDash_timeUpdateData?.Text ?? "2 PHÚT",
                    selectedStatusSelect = tabDash_statusSelect?.Text ?? "Tất cả tồn kho",
                    searchText = _dashSearchBox?.Text ?? "",
                    userName = JmsAuthStateService.CurrentUserName ?? ""
                };

                var options = new System.Text.Json.JsonSerializerOptions
                {
                    PropertyNamingPolicy = null
                };
                var json = System.Text.Json.JsonSerializer.Serialize(payload, options);
                _webView.CoreWebView2.PostWebMessageAsJson(json);
            }
            catch (Exception ex)
            {
                AppLogger.Error("PostStateToWebView2 failed", ex);
            }
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

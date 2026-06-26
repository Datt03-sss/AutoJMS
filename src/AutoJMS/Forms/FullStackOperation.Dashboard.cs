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

            _dashDateFrom = new DateTimePicker { Visible = false, ShowCheckBox = true, Checked = false };
            _dashDateTo = new DateTimePicker { Visible = false, ShowCheckBox = true, Checked = false };

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
                    }
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

        private object MapWaybillToDto(WaybillDbModel row)
        {
            var mains = new System.Collections.Generic.List<string> { "tong-ton" };
            var subs = new System.Collections.Generic.List<string> { "Tất cả" };

            if (row.ThaoTacCuoi == "Hàng đến")
            {
                mains.Add("hang-den");
                subs.Add("Tổng số đơn");
                if (IsNotDispatched(row))
                {
                    subs.Add("Chưa quét đến");
                }
            }

            if (row.ThaoTacCuoi == "Phát hàng")
            {
                mains.Add("phat-hang");
                subs.Add("Cần phát");
                if (IsNotDispatched(row))
                {
                    subs.Add("Chưa phát");
                }
            }

            if (IsNeedsAction(row))
            {
                mains.Add("backlog");
                double days = GetWarehouseAgeDays(row.ThoiGianThaoTac);
                if (days >= 7.0)
                {
                    subs.Add("7D+");
                    subs.Add("7D");
                }
                else if (days >= 6.0) subs.Add("6D");
                else if (days >= 5.0) subs.Add("5D");
                else if (days >= 4.0) subs.Add("4D");
                else if (days >= 3.0) subs.Add("3D");
                else subs.Add("<2D");
            }

            if (row.ThaoTacCuoi == "Chuyển hoàn" || IsPendingReturn(row))
            {
                mains.Add("chuyen-hoan");
                if (row.RebackStatus == "Đợi xét duyệt") subs.Add("Đợi xét duyệt");
                else if (row.RebackStatus == "Đã phê duyệt") subs.Add("Đã phê duyệt");
                else if (row.RebackStatus == "Phát lại") subs.Add("Phát lại");
            }

            if (IsSlaBreached(row.ThoiGianNhanHang))
            {
                mains.Add("kiem-kho");
                subs.Add("Đơn cần kiểm");
                subs.Add("Kiểm thiếu");
            }

            if (HasTaskWaybill(row))
            {
                mains.Add("cskh");
                subs.Add("Ticket CSKH");
                if (IsSlaBreached(row.ThoiGianNhanHang))
                {
                    subs.Add("Khiếu nại SLA");
                }
            }

            double ageDays = GetWarehouseAgeDays(row.ThoiGianThaoTac);
            if (ageDays >= 1.0)
            {
                mains.Add("dung-tram");
                if (ageDays >= 7.0) subs.Add("Dừng >7D");
                else if (ageDays >= 3.0) subs.Add("Dừng >3D");
                else if (ageDays >= 2.0) subs.Add("Dừng >48h");
                else if (ageDays >= 1.0) subs.Add("Dừng >24h");
                else subs.Add("Dừng >12h");
            }

            bool isStarred = false;
            lock (StarredWaybills)
            {
                isStarred = StarredWaybills.Contains(row.WaybillNo);
            }

            if (isStarred)
            {
                mains.Add("star");
                subs.Add("Đơn đã ghim");
                subs.Add("Đang theo dõi");
                if (IsNeedsAction(row)) subs.Add("Chưa xử lý");
                else subs.Add("Đã xử lý");
            }

            if (row.ThaoTacCuoi == "Phát hàng") subs.Add("Quét phát hàng");
            if (row.ThaoTacCuoi == "Chuyển hoàn") subs.Add("Đăng ký chuyển hoàn");
            if (row.ThaoTacCuoi?.ToLower().Contains("phát lại") == true || row.ThaoTacCuoi?.ToLower().Contains("giao lại") == true) subs.Add("Giao lại hàng");
            if (row.ThaoTacCuoi == "Chờ lấy") subs.Add("Chờ lấy hàng");
            if (row.ThaoTacCuoi == "Lưu kho") subs.Add("Lưu kho");

            return new
            {
                code = row.WaybillNo,
                recipient = row.NhanVienNhanHang ?? "Chưa rõ",
                phone = "-",
                addr = row.DiaChiNhanHang ?? "-",
                source = row.BuuCucThaoTac ?? "-",
                cod = row.CODThucTe ?? "0 đ",
                sla = IsSlaBreached(row.ThoiGianNhanHang) ? "Quá hạn" : "Trong hạn",
                slaColor = IsSlaBreached(row.ThoiGianNhanHang) ? "#d23a2e" : "#1c8a3c",
                service = "Chuyển phát",
                weight = row.TrongLuong ?? "-",
                pkgs = "1",
                orderNo = row.WaybillNo,
                sender = row.TenNguoiGui ?? "-",
                staff = row.NguoiThaoTac ?? "-",
                senderAddr = row.DiaChiLayHang ?? "-",
                content = row.NoiDungHangHoa ?? "-",
                payMethod = row.PTTT ?? "-",
                ward = row.Phuong ?? "-",
                lastScan = row.ThoiGianThaoTac ?? "-",
                status = row.TrangThaiHienTai ?? "-",
                op = row.ThaoTacCuoi ?? "",
                issueReason = row.NguyenNhanKienVanDe ?? "-",
                mains = mains,
                subs = subs,
                isStar = isStarred
            };
        }

        private void PostStateToWebView2()
        {
            if (_webView == null || !_webViewInitialized || _webView.CoreWebView2 == null) return;

            try
            {
                string siteId = "214A02";
                string lastUpdateTime = DateTime.Now.ToString("HH:mm:ss");
                if (tabDash_lblLastUpdate != null && !string.IsNullOrEmpty(tabDash_lblLastUpdate.Text))
                {
                    lastUpdateTime = tabDash_lblLastUpdate.Text.Replace("Local refresh: ", "").Replace("Chưa tải", "Chưa cập nhật");
                }

                string syncStatus = "DB: Synced";

                int tongTonCount = _cloudData.Count;
                int hangDenCount = _cloudData.Count(x => x.ThaoTacCuoi == "Hàng đến");
                int phatHangCount = _cloudData.Count(x => x.ThaoTacCuoi == "Phát hàng");
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
                    { "hang-den", hangDenCount.ToString("N0") },
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
                        new { label = "Tất cả", count = tongTonCount.ToString("N0") },
                        new { label = "Quét phát hàng", count = _cloudData.Count(x => x.ThaoTacCuoi == "Phát hàng").ToString("N0") },
                        new { label = "Đăng ký chuyển hoàn", count = _cloudData.Count(x => x.ThaoTacCuoi == "Chuyển hoàn").ToString("N0") },
                        new { label = "Giao lại hàng", count = _cloudData.Count(x => x.ThaoTacCuoi?.ToLower().Contains("phát lại") == true || x.ThaoTacCuoi?.ToLower().Contains("giao lại") == true).ToString("N0") },
                        new { label = "Chờ lấy hàng", count = _cloudData.Count(x => x.ThaoTacCuoi == "Chờ lấy").ToString("N0") },
                        new { label = "Lưu kho", count = _cloudData.Count(x => x.ThaoTacCuoi == "Lưu kho").ToString("N0") }
                    } },
                    { "hang-den", new System.Collections.Generic.List<object> {
                        new { label = "Tổng số đơn", count = hangDenCount.ToString("N0") },
                        new { label = "Chưa quét đến", count = _cloudData.Count(x => x.ThaoTacCuoi == "Hàng đến" && IsNotDispatched(x)).ToString("N0") }
                    } },
                    { "phat-hang", new System.Collections.Generic.List<object> {
                        new { label = "Cần phát", count = phatHangCount.ToString("N0") },
                        new { label = "Đã phát", count = _cloudData.Count(x => x.ThaoTacCuoi == "Phát hàng" && !IsNotDispatched(x)).ToString("N0") },
                        new { label = "Chưa phát", count = _cloudData.Count(x => x.ThaoTacCuoi == "Phát hàng" && IsNotDispatched(x)).ToString("N0") }
                    } },
                    { "backlog", new System.Collections.Generic.List<object> {
                        new { label = "7D+", count = _cloudData.Count(x => IsNeedsAction(x) && GetWarehouseAgeDays(x.ThoiGianThaoTac) >= 7.0).ToString("N0") },
                        new { label = "7D", count = _cloudData.Count(x => IsNeedsAction(x) && GetWarehouseAgeDays(x.ThoiGianThaoTac) >= 7.0).ToString("N0") },
                        new { label = "6D", count = _cloudData.Count(x => IsNeedsAction(x) && GetWarehouseAgeDays(x.ThoiGianThaoTac) >= 6.0 && GetWarehouseAgeDays(x.ThoiGianThaoTac) < 7.0).ToString("N0") },
                        new { label = "5D", count = _cloudData.Count(x => IsNeedsAction(x) && GetWarehouseAgeDays(x.ThoiGianThaoTac) >= 5.0 && GetWarehouseAgeDays(x.ThoiGianThaoTac) < 6.0).ToString("N0") },
                        new { label = "4D", count = _cloudData.Count(x => IsNeedsAction(x) && GetWarehouseAgeDays(x.ThoiGianThaoTac) >= 4.0 && GetWarehouseAgeDays(x.ThoiGianThaoTac) < 5.0).ToString("N0") },
                        new { label = "3D", count = _cloudData.Count(x => IsNeedsAction(x) && GetWarehouseAgeDays(x.ThoiGianThaoTac) >= 3.0 && GetWarehouseAgeDays(x.ThoiGianThaoTac) < 4.0).ToString("N0") },
                        new { label = "<2D", count = _cloudData.Count(x => IsNeedsAction(x) && GetWarehouseAgeDays(x.ThoiGianThaoTac) < 2.0).ToString("N0") }
                    } },
                    { "chuyen-hoan", new System.Collections.Generic.List<object> {
                        new { label = "Đợi xét duyệt", count = _cloudData.Count(x => (x.ThaoTacCuoi == "Chuyển hoàn" || IsPendingReturn(x)) && x.RebackStatus == "Đợi xét duyệt").ToString("N0") },
                        new { label = "Đã phê duyệt", count = _cloudData.Count(x => (x.ThaoTacCuoi == "Chuyển hoàn" || IsPendingReturn(x)) && x.RebackStatus == "Đã phê duyệt").ToString("N0") },
                        new { label = "Phát lại", count = _cloudData.Count(x => (x.ThaoTacCuoi == "Chuyển hoàn" || IsPendingReturn(x)) && x.RebackStatus == "Phát lại").ToString("N0") }
                    } },
                    { "kiem-kho", new System.Collections.Generic.List<object> {
                        new { label = "Đơn cần kiểm", count = kiemKhoCount.ToString("N0") },
                        new { label = "Kiểm thiếu", count = _cloudData.Count(x => IsSlaBreached(x.ThoiGianNhanHang)).ToString("N0") }
                    } },
                    { "cskh", new System.Collections.Generic.List<object> {
                        new { label = "Ticket CSKH", count = cskhCount.ToString("N0") },
                        new { label = "Khiếu nại SLA", count = _cloudData.Count(x => HasTaskWaybill(x) && IsSlaBreached(x.ThoiGianNhanHang)).ToString("N0") },
                        new { label = "Khiếu nại chịu trách nhiệm", count = "0" },
                        new { label = "Kháng cáo CLDV", count = "0" },
                        new { label = "Tiền phạt dự kiến", count = "0" }
                    } },
                    { "dung-tram", new System.Collections.Generic.List<object> {
                        new { label = "Dừng >12h", count = _cloudData.Count(x => GetWarehouseAgeDays(x.ThoiGianThaoTac) >= 0.5).ToString("N0") },
                        new { label = "Dừng >24h", count = _cloudData.Count(x => GetWarehouseAgeDays(x.ThoiGianThaoTac) >= 1.0).ToString("N0") },
                        new { label = "Dừng >48h", count = _cloudData.Count(x => GetWarehouseAgeDays(x.ThoiGianThaoTac) >= 2.0).ToString("N0") },
                        new { label = "Dừng >3D", count = _cloudData.Count(x => GetWarehouseAgeDays(x.ThoiGianThaoTac) >= 3.0).ToString("N0") },
                        new { label = "Dừng >7D", count = _cloudData.Count(x => GetWarehouseAgeDays(x.ThoiGianThaoTac) >= 7.0).ToString("N0") },
                        new { label = "Không có scan mới", count = "0" }
                    } },
                    { "star", new System.Collections.Generic.List<object> {
                        new { label = "Đơn đã ghim", count = starCount.ToString("N0") },
                        new { label = "Đang theo dõi", count = starCount.ToString("N0") },
                        new { label = "Chưa xử lý", count = _cloudData.Count(x => StarredWaybills.Contains(x.WaybillNo) && IsNeedsAction(x)).ToString("N0") },
                        new { label = "Đã xử lý", count = _cloudData.Count(x => StarredWaybills.Contains(x.WaybillNo) && !IsNeedsAction(x)).ToString("N0") }
                    } }
                };

                var waybillsList = _cloudData.Select(MapWaybillToDto).ToList();

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
                    searchText = _dashSearchBox?.Text ?? ""
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

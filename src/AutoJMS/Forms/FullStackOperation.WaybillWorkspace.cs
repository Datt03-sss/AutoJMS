using AutoJMS.FullStack.Models;
using Sunny.UI;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace AutoJMS
{
    public partial class FullStackOperation
    {
        private static readonly Color JmsJourneyText = Color.FromArgb(75, 85, 99);
        private static readonly Color JmsJourneyDarkText = Color.FromArgb(55, 65, 81);
        private static readonly Color JmsJourneyOrange = Color.FromArgb(245, 158, 11);
        private static readonly Color JmsJourneyBlue = Color.FromArgb(37, 99, 235);
        private static readonly Color JmsJourneyGreen = Color.FromArgb(22, 163, 74);
        private static readonly Color JmsJourneyPurple = Color.FromArgb(124, 58, 237);

        private void InitializeWaybillJourneyWorkspace()
        {
            _waybillJourneyWorkspace = new Panel
            {
                Dock = DockStyle.Fill,
                Margin = Padding.Empty,
                Padding = Padding.Empty,
                BackColor = Color.White,
                Visible = false
            };

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 2,
                Margin = Padding.Empty,
                Padding = Padding.Empty,
                BackColor = Color.White
            };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 46F));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

            layout.Controls.Add(CreateJourneyHeader(), 0, 0);
            layout.Controls.Add(CreateJourneyGridShell(), 0, 1);
            _waybillJourneyWorkspace.Controls.Add(layout);
        }

        private Control CreateJourneyHeader()
        {
            var header = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 7,
                RowCount = 1,
                Padding = new Padding(14, 7, 14, 5),
                Margin = Padding.Empty,
                BackColor = Color.White
            };
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 132F));
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 172F));
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 136F));
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 86F));
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 92F));
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 82F));
            header.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

            _waybillJourneyBackButton = new Button
            {
                Dock = DockStyle.Fill,
                Text = "← Danh sách đơn",
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.White,
                ForeColor = Color.FromArgb(37, 99, 235),
                Font = new Font("Segoe UI", 8.5F, FontStyle.Bold),
                Margin = new Padding(0, 0, 8, 0)
            };
            _waybillJourneyBackButton.FlatAppearance.BorderSize = 1;
            _waybillJourneyBackButton.FlatAppearance.BorderColor = Color.FromArgb(209, 213, 219);
            _waybillJourneyBackButton.Click += (s, e) => HideWaybillJourneyWorkspace();

            _waybillJourneyTitle = new Label
            {
                Dock = DockStyle.Fill,
                Text = "Hành trình vận chuyển",
                TextAlign = ContentAlignment.MiddleLeft,
                Font = new Font("Segoe UI Semibold", 10F, FontStyle.Bold),
                ForeColor = Color.FromArgb(17, 24, 39),
                AutoEllipsis = true
            };

            _waybillJourneyWaybillLabel = new Label
            {
                Dock = DockStyle.Fill,
                Text = "--",
                TextAlign = ContentAlignment.MiddleLeft,
                Font = new Font("Segoe UI", 8.5F, FontStyle.Bold),
                ForeColor = Color.FromArgb(55, 65, 81),
                AutoEllipsis = true
            };

            _waybillJourneyStatusLabel = new Label
            {
                Dock = DockStyle.Fill,
                Text = "",
                TextAlign = ContentAlignment.MiddleLeft,
                Font = new Font("Segoe UI", 8F),
                ForeColor = Color.FromArgb(107, 114, 128),
                AutoEllipsis = true
            };

            _waybillJourneyCacheButton = CreateJourneyHeaderButton("Xem cache");
            _waybillJourneyCacheButton.Visible = false;
            _waybillJourneyCacheButton.Click += WaybillJourneyCacheButton_Click;

            _waybillJourneyRawJsonButton = CreateJourneyHeaderButton("Copy raw");
            _waybillJourneyRawJsonButton.Visible = false;
            _waybillJourneyRawJsonButton.Click += WaybillJourneyRawJsonButton_Click;

            header.Controls.Add(_waybillJourneyBackButton, 0, 0);
            header.Controls.Add(_waybillJourneyTitle, 1, 0);
            header.Controls.Add(_waybillJourneyWaybillLabel, 2, 0);
            header.Controls.Add(_waybillJourneyStatusLabel, 3, 0);
            header.Controls.Add(_waybillJourneyCacheButton, 4, 0);
            header.Controls.Add(_waybillJourneyRawJsonButton, 5, 0);
            header.Controls.Add(CreateJourneySortIndicator(), 6, 0);
            return header;
        }

        private static Button CreateJourneyHeaderButton(string text)
        {
            var button = new Button
            {
                Dock = DockStyle.Fill,
                Text = text,
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.White,
                ForeColor = Color.FromArgb(55, 65, 81),
                Font = new Font("Segoe UI", 8F, FontStyle.Regular),
                Margin = new Padding(4, 0, 4, 0)
            };
            button.FlatAppearance.BorderSize = 1;
            button.FlatAppearance.BorderColor = Color.FromArgb(209, 213, 219);
            return button;
        }

        private static Control CreateJourneySortIndicator()
        {
            var sort = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 1,
                Margin = Padding.Empty,
                Padding = Padding.Empty,
                BackColor = Color.White
            };
            sort.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            sort.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 10F));
            sort.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

            sort.Controls.Add(new Label
            {
                Dock = DockStyle.Fill,
                Text = "Giảm dần",
                TextAlign = ContentAlignment.MiddleRight,
                Font = new Font("Segoe UI", 8F),
                ForeColor = Color.FromArgb(75, 85, 99)
            }, 0, 0);

            sort.Controls.Add(new Label
            {
                Dock = DockStyle.Fill,
                Text = "▲",
                TextAlign = ContentAlignment.MiddleLeft,
                Font = new Font("Segoe UI", 6.5F, FontStyle.Bold),
                ForeColor = Color.FromArgb(255, 59, 48)
            }, 1, 0);

            return sort;
        }

        private Control CreateJourneyGridShell()
        {
            var shell = new Panel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(14, 0, 14, 8),
                Margin = Padding.Empty,
                BackColor = Color.White
            };

            _waybillJourneyGrid = new UIDataGridView
            {
                Dock = DockStyle.Fill,
                AutoGenerateColumns = false,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                AllowUserToResizeRows = false,
                ReadOnly = true,
                MultiSelect = false,
                RowHeadersVisible = false,
                BackgroundColor = Color.White,
                BorderStyle = BorderStyle.None,
                GridColor = Color.FromArgb(218, 221, 226),
                Font = new Font("Segoe UI", 7.5F, FontStyle.Regular),
                ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing,
                ColumnHeadersHeight = 34,
                RowTemplate = { Height = 27 },
                SelectionMode = DataGridViewSelectionMode.CellSelect,
                EnableHeadersVisualStyles = false,
                AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.None,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.DisplayedCells,
                ScrollBars = ScrollBars.Both,
                CellBorderStyle = DataGridViewCellBorderStyle.Single,
                ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.Single
            };
            _waybillJourneyGrid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(242, 242, 242);
            _waybillJourneyGrid.ColumnHeadersDefaultCellStyle.ForeColor = Color.FromArgb(55, 65, 81);
            _waybillJourneyGrid.ColumnHeadersDefaultCellStyle.Font = new Font("Segoe UI", 7.5F, FontStyle.Bold);
            _waybillJourneyGrid.ColumnHeadersDefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleLeft;
            _waybillJourneyGrid.ColumnHeadersDefaultCellStyle.Padding = new Padding(5, 0, 3, 0);
            _waybillJourneyGrid.DefaultCellStyle.Font = new Font("Segoe UI", 7.5F, FontStyle.Regular);
            _waybillJourneyGrid.DefaultCellStyle.BackColor = Color.White;
            _waybillJourneyGrid.DefaultCellStyle.ForeColor = JmsJourneyText;
            _waybillJourneyGrid.DefaultCellStyle.Padding = new Padding(5, 0, 3, 0);
            _waybillJourneyGrid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(225, 239, 255);
            _waybillJourneyGrid.DefaultCellStyle.SelectionForeColor = Color.FromArgb(17, 24, 39);
            _waybillJourneyGrid.AlternatingRowsDefaultCellStyle.BackColor = Color.White;
            _waybillJourneyGrid.DataBindingComplete += WaybillJourneyGrid_DataBindingComplete;
            _waybillJourneyGrid.CellToolTipTextNeeded += WaybillJourneyGrid_CellToolTipTextNeeded;
            _waybillJourneyGrid.CellPainting += WaybillJourneyGrid_CellPainting;
            _waybillJourneyGrid.CellClick += WaybillJourneyGrid_CellClick;
            _waybillJourneyGrid.DataError += FullStackGrid_DataError;

            ConfigureJourneyGridColumns();
            shell.Controls.Add(_waybillJourneyGrid);
            return shell;
        }

        private void ConfigureJourneyGridColumns()
        {
            _waybillJourneyGrid.Columns.Clear();
            _waybillJourneyGrid.Columns.Add(CreateJourneyColumn(nameof(JourneyEventViewModel.Stt), "STT", 52, DataGridViewContentAlignment.MiddleCenter));
            _waybillJourneyGrid.Columns.Add(CreateJourneyDateColumn());
            _waybillJourneyGrid.Columns.Add(CreateJourneyUploadedDateColumn());
            _waybillJourneyGrid.Columns.Add(CreateJourneyColumn(nameof(JourneyEventViewModel.ActionType), "Loại thao tác", 150, DataGridViewContentAlignment.MiddleLeft));
            _waybillJourneyGrid.Columns.Add(CreateJourneyColumn(nameof(JourneyEventViewModel.Description), "Mô tả lịch sử hành trình", 575, DataGridViewContentAlignment.MiddleLeft, true));
            _waybillJourneyGrid.Columns.Add(CreateJourneyColumn(nameof(JourneyEventViewModel.ScanSource), "Nguồn", 125, DataGridViewContentAlignment.MiddleLeft));
            _waybillJourneyGrid.Columns.Add(CreateJourneyColumn(nameof(JourneyEventViewModel.Weight), "Trọng lượng", 92, DataGridViewContentAlignment.MiddleLeft));
            _waybillJourneyGrid.Columns.Add(CreateJourneyColumn(nameof(JourneyEventViewModel.AttachmentText), "Tệp đính kèm", 105, DataGridViewContentAlignment.MiddleLeft));
        }

        private static DataGridViewTextBoxColumn CreateJourneyDateColumn()
        {
            return new DataGridViewTextBoxColumn
            {
                DataPropertyName = nameof(JourneyEventViewModel.EventTimeText),
                HeaderText = "Thời gian thao tác",
                Name = nameof(JourneyEventViewModel.EventTimeText),
                Width = 150,
                MinimumWidth = 120,
                DefaultCellStyle = new DataGridViewCellStyle
                {
                    Alignment = DataGridViewContentAlignment.MiddleLeft,
                    WrapMode = DataGridViewTriState.False,
                    Padding = new Padding(5, 0, 3, 0)
                }
            };
        }

        private static DataGridViewTextBoxColumn CreateJourneyUploadedDateColumn()
        {
            return new DataGridViewTextBoxColumn
            {
                DataPropertyName = nameof(JourneyEventViewModel.UploadTimeText),
                HeaderText = "Thời gian tải lên",
                Name = nameof(JourneyEventViewModel.UploadTimeText),
                Width = 150,
                MinimumWidth = 120,
                DefaultCellStyle = new DataGridViewCellStyle
                {
                    Alignment = DataGridViewContentAlignment.MiddleLeft,
                    WrapMode = DataGridViewTriState.False,
                    Padding = new Padding(5, 0, 3, 0)
                }
            };
        }

        private static DataGridViewTextBoxColumn CreateJourneyColumn(
            string propertyName,
            string headerText,
            int width,
            DataGridViewContentAlignment alignment,
            bool wrap = false)
        {
            return new DataGridViewTextBoxColumn
            {
                DataPropertyName = propertyName,
                HeaderText = headerText,
                Name = propertyName,
                Width = width,
                MinimumWidth = Math.Min(width, 90),
                DefaultCellStyle = new DataGridViewCellStyle
                {
                    Alignment = alignment,
                    WrapMode = DataGridViewTriState.False,
                    Padding = new Padding(5, 0, 3, 0)
                }
            };
        }

        private void ShowWaybillJourneyWorkspace(string waybillNo)
        {
            _ = OpenJourneyAsync(waybillNo);
        }

        private async Task OpenJourneyAsync(string waybillNo)
        {
            if (string.IsNullOrWhiteSpace(waybillNo)) return;

            CancelCurrentJourneyLoad();
            var journeyWaybill = waybillNo.Trim().ToUpperInvariant();
            _activeJourneyWaybillNo = journeyWaybill;
            var requestCts = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token);
            _journeyLoadCts = requestCts;

            SetJourneyWorkspaceVisible(true);
            ClearJourneyGrid();
            SetJourneyLoading(journeyWaybill);

            try
            {
                var token = await ResolveJourneyAuthTokenAsync(requestCts.Token).ConfigureAwait(true);
                if (string.IsNullOrWhiteSpace(token))
                {
                    var hasCache = await _fullStackJourneyService
                        .HasLocalJourneyAsync(journeyWaybill, requestCts.Token)
                        .ConfigureAwait(true);

                    if (!IsActiveJourneyRequest(journeyWaybill, requestCts))
                        return;

                    ShowJourneyError(
                        journeyWaybill,
                        "Chưa có authToken, không gọi JMS. Không hiển thị dữ liệu cũ để tránh sai lệch.",
                        hasCache,
                        hasCache);
                    return;
                }

                var refresh = await _fullStackJourneyService
                    .FetchFreshJourneyAsync(journeyWaybill, token, requestCts.Token)
                    .ConfigureAwait(true);

                if (!IsActiveJourneyRequest(journeyWaybill, requestCts))
                    return;

                if (refresh.Success && refresh.ViewModel?.Rows?.Count > 0)
                {
                    BindJourneyGrid(
                        refresh.ViewModel,
                        BuildJourneyLoadedStatus(refresh.ViewModel.Rows.Count, refresh.FetchedAt ?? DateTime.Now, "Fresh"));
                    SetJourneyAuxButtons(false, !string.IsNullOrWhiteSpace(refresh.RawJson));
                    QueueJourneySnapshotSave(journeyWaybill, refresh);
                }
                else if (refresh.Success)
                {
                    ClearJourneyGrid();
                    SetJourneyStatus("Response có data nhưng parser không lấy được details[].");
                    SetJourneyAuxButtons(false, !string.IsNullOrWhiteSpace(refresh.RawJson));
                    QueueJourneySnapshotSave(journeyWaybill, refresh);
                }
                else
                {
                    ClearJourneyGrid();
                    var hasCache = await _fullStackJourneyService
                        .HasLocalJourneyAsync(journeyWaybill, requestCts.Token)
                        .ConfigureAwait(true);

                    if (!IsActiveJourneyRequest(journeyWaybill, requestCts))
                        return;

                    var message = refresh.ResponseMismatch
                        ? "Response không khớp mã vận đơn đang chọn."
                        : refresh.AuthExpired
                            ? "AuthToken không hợp lệ hoặc đã hết hạn."
                            : "Không lấy được hành trình vận chuyển từ JMS. Không hiển thị dữ liệu cũ để tránh sai lệch.";
                    ShowJourneyError(journeyWaybill, message, hasCache, true);
                }
            }
            catch (OperationCanceledException)
            {
                // User selected another waybill; stale responses are intentionally ignored.
            }
            catch (Exception ex)
            {
                AppLogger.Warning($"Open journey workspace failed for {journeyWaybill}: {ex.Message}");
                if (IsActiveJourneyRequest(journeyWaybill, requestCts))
                {
                    var hasCache = false;
                    try
                    {
                        hasCache = await _fullStackJourneyService
                            .HasLocalJourneyAsync(journeyWaybill, _cts.Token)
                            .ConfigureAwait(true);
                    }
                    catch
                    {
                    }

                    ShowJourneyError(
                        journeyWaybill,
                        "Không lấy được hành trình vận chuyển. Không hiển thị dữ liệu cũ để tránh sai lệch.",
                        hasCache,
                        hasCache);
                }
            }
            finally
            {
                if (ReferenceEquals(_journeyLoadCts, requestCts))
                    _journeyLoadCts = null;
                requestCts.Dispose();
            }
        }

        private void HideWaybillJourneyWorkspace()
        {
            CancelCurrentJourneyLoad();
            SetJourneyWorkspaceVisible(false);
            _activeJourneyWaybillNo = string.Empty;
            SetFullStackStatus("Đã quay lại Inventory Grid");
        }

        private void CancelCurrentJourneyLoad()
        {
            var cts = _journeyLoadCts;
            if (cts == null || cts.IsCancellationRequested) return;
            try
            {
                cts.Cancel();
            }
            catch (ObjectDisposedException)
            {
            }
        }

        private bool IsActiveJourneyRequest(string waybillNo, CancellationTokenSource requestCts)
        {
            return !_isClosing
                && !IsDisposed
                && ReferenceEquals(_journeyLoadCts, requestCts)
                && !requestCts.IsCancellationRequested
                && string.Equals(_activeJourneyWaybillNo, waybillNo, StringComparison.OrdinalIgnoreCase);
        }

        private void QueueJourneySnapshotSave(string waybillNo, WaybillJourneyRefreshResult refresh)
        {
            var rows = refresh?.ViewModel?.Rows?.Select(CloneJourneyRow).ToList() ?? new List<WaybillJourneyRow>();
            var rawJson = refresh?.RawJson ?? string.Empty;
            var parsedCount = rows.Count;

            AppLogger.Info(
                "[FullStackJourney] queue-save " +
                $"waybill={waybillNo}; parsedRows={parsedCount}; rawLength={rawJson.Length}");

            _ = Task.Run(async () =>
            {
                try
                {
                    await _fullStackJourneyService
                        .SaveJourneyAsync(waybillNo, rows, rawJson, CancellationToken.None)
                        .ConfigureAwait(false);

                    AppLogger.Info($"[FullStackJourney] details.db save completed waybill={waybillNo}; rows={parsedCount}");
                }
                catch (Exception ex)
                {
                    AppLogger.Warning($"[FullStackJourney] details.db save failed waybill={waybillNo}: {ex.Message}");
                }
            });
        }

        private static async Task<string> ResolveJourneyAuthTokenAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var memoryToken = JmsAuthStateService.CurrentToken;
            if (JmsAuthTokenService.IsValidJmsToken(memoryToken))
                return memoryToken;

            var webViewToken = string.Empty;
            if (JmsAuthTokenService.WebViewTokenReader != null)
            {
                try
                {
                    webViewToken = await JmsAuthTokenService.WebViewTokenReader().ConfigureAwait(true);
                }
                catch (Exception ex)
                {
                    AppLogger.Warning($"[FullStackJourney] WebView authToken read failed: {ex.Message}");
                }
            }

            cancellationToken.ThrowIfCancellationRequested();
            if (JmsAuthTokenService.IsValidJmsToken(webViewToken))
            {
                JmsAuthStateService.SetToken(webViewToken);
                AuthStateService.Instance.SetToken(webViewToken);
                AppLogger.Info("[FullStackJourney] authToken source=webview, valid=32hex");
                return webViewToken;
            }

            var configToken = string.Empty;
            try
            {
                configToken = JmsAuthTokenService.ConfigTokenProvider?.Invoke() ?? string.Empty;
            }
            catch (Exception ex)
            {
                AppLogger.Warning($"[FullStackJourney] config authToken read failed: {ex.Message}");
            }

            if (JmsAuthTokenService.IsValidJmsToken(configToken))
            {
                JmsAuthStateService.SetToken(configToken);
                AuthStateService.Instance.SetToken(configToken);
                AppLogger.Info("[FullStackJourney] authToken source=local-config, valid=32hex");
                return configToken;
            }

            var authStateToken = AuthStateService.Instance.AuthToken;
            if (JmsAuthTokenService.IsValidJmsToken(authStateToken))
            {
                JmsAuthStateService.SetToken(authStateToken);
                AppLogger.Info("[FullStackJourney] authToken source=auth-state, valid=32hex");
                return authStateToken;
            }

            if (AuthStateService.Instance.IsAuthenticated)
                AppLogger.Warning("[FullStackJourney] rejected non-JMS token from AuthStateService; expected 32-hex JMS authToken.");

            return string.Empty;
        }

        private void SetJourneyWorkspaceVisible(bool visible)
        {
            if (_operationInventoryWorkspace != null)
                _operationInventoryWorkspace.Visible = !visible;
            if (_waybillJourneyWorkspace != null)
            {
                _waybillJourneyWorkspace.Visible = visible;
                if (visible)
                    _waybillJourneyWorkspace.BringToFront();
            }

            if (_operationGridHost != null && !visible)
                _operationInventoryWorkspace?.BringToFront();
        }

        private void SetJourneyLoading(string waybillNo)
        {
            _waybillJourneyWaybillLabel.Text = waybillNo;
            SetJourneyAuxButtons(false, false);
            SetJourneyStatus("Đang tải hành trình vận chuyển từ Tracking API...");
        }

        private void ShowJourneyError(string waybillNo, string message, bool hasCache, bool showRaw)
        {
            _waybillJourneyWaybillLabel.Text = waybillNo;
            ClearJourneyGrid();
            SetJourneyStatus(message + (hasCache ? " Có dữ liệu cache cũ." : string.Empty));
            SetJourneyAuxButtons(hasCache, showRaw);
        }

        private void BindJourneyGrid(WaybillJourneyViewModel vm, string statusText = null)
        {
            vm ??= new WaybillJourneyViewModel();
            _waybillJourneyWaybillLabel.Text = string.IsNullOrWhiteSpace(vm.WaybillNo)
                ? "--"
                : vm.WaybillNo;

            var rows = vm.Rows?
                .OrderByDescending(x => x.ActionTime ?? DateTime.MinValue)
                .Select(CloneJourneyRow)
                .ToList() ?? new List<WaybillJourneyRow>();

            for (int i = 0; i < rows.Count; i++)
                rows[i].Stt = i + 1;

            var eventRows = rows
                .Select((row, index) => ToJourneyEventViewModel(row, index))
                .ToList();
            var gridRowsAfterBind = BindJourneyRows(eventRows);

            if (_webView != null && _webViewInitialized && _webView.CoreWebView2 != null)
            {
                try
                {
                    var payload = new
                    {
                        type = "JOURNEY_DATA",
                        journeyRows = eventRows
                    };
                    var json = System.Text.Json.JsonSerializer.Serialize(payload);
                    _webView.CoreWebView2.PostWebMessageAsJson(json);
                }
                catch (Exception ex)
                {
                    AppLogger.Error("Post JOURNEY_DATA failed", ex);
                }
            }

            LogJourneyBind(vm.WaybillNo, rows, eventRows.Count, gridRowsAfterBind);
            SetJourneyStatus(statusText ?? (rows.Count == 0
                ? "Chưa có dữ liệu hành trình cho mã này."
                : BuildJourneyLoadedStatus(rows.Count, vm.FetchedAt ?? DateTime.Now, vm.Source)));
        }

        private static WaybillJourneyRow CloneJourneyRow(WaybillJourneyRow row)
        {
            return new WaybillJourneyRow
            {
                Stt = row.Stt,
                ActionTime = row.ActionTime,
                UploadedAt = row.UploadedAt,
                ActionType = row.ActionType,
                Description = row.Description,
                SiteInfo = row.SiteInfo,
                ContainerCode = row.ContainerCode,
                ScanSource = row.ScanSource,
                Weight = row.Weight,
                ConvertedWeight = row.ConvertedWeight,
                AttachmentText = row.AttachmentText,
                Severity = row.Severity,
                SiteCode = row.SiteCode,
                SiteName = row.SiteName,
                OperatorCode = row.OperatorCode,
                OperatorName = row.OperatorName,
                PackageNumber = row.PackageNumber,
                TaskCode = row.TaskCode,
                ImgType = row.ImgType,
                RawJson = row.RawJson
            };
        }

        private static JourneyEventViewModel ToJourneyEventViewModel(WaybillJourneyRow row, int index)
        {
            return new JourneyEventViewModel
            {
                Stt = index + 1,
                EventTime = row?.ActionTime,
                UploadTime = row?.UploadedAt,
                ActionType = CleanJourneyText(row?.ActionType),
                Description = CleanJourneyText(row?.Description),
                ScanSite = CleanJourneyText(row?.SiteInfo),
                BagNo = CleanJourneyText(row?.ContainerCode),
                ScanSource = CleanJourneyText(row?.ScanSource),
                Weight = CleanJourneyText(row?.Weight),
                VolumeWeight = CleanJourneyText(row?.ConvertedWeight),
                AttachmentText = CleanJourneyText(row?.AttachmentText),
                ImgType = row?.ImgType,
                RawEventJson = row?.RawJson ?? string.Empty
            };
        }

        private static string CleanJourneyText(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "-";

            value = value.Trim();
            return string.Equals(value, "--", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(value, "empty", StringComparison.OrdinalIgnoreCase)
                ? "-"
                : value;
        }

        private void ClearJourneyGrid()
        {
            BindJourneyRows(new List<JourneyEventViewModel>());
        }

        private int BindJourneyRows(List<JourneyEventViewModel> rows)
        {
            _waybillJourneyGrid.SuspendLayout();
            try
            {
                _waybillJourneyGrid.DataSource = null;
                _waybillJourneyGrid.DataSource = rows;
                _waybillJourneyGrid.Refresh();
                return _waybillJourneyGrid.Rows.Count;
            }
            finally
            {
                _waybillJourneyGrid.ResumeLayout();
            }
        }

        private void SetJourneyStatus(string text)
        {
            if (_waybillJourneyStatusLabel != null)
                _waybillJourneyStatusLabel.Text = text ?? string.Empty;
            SetFullStackStatus(text ?? string.Empty);
        }

        private void SetJourneyAuxButtons(bool showCache, bool showRaw)
        {
            if (_waybillJourneyCacheButton != null)
                _waybillJourneyCacheButton.Visible = showCache;
            if (_waybillJourneyRawJsonButton != null)
                _waybillJourneyRawJsonButton.Visible = showRaw;
        }

        private static string BuildJourneyLoadedStatus(int count, DateTime fetchedAt, string source)
        {
            var sourceText = string.IsNullOrWhiteSpace(source) ? "Fresh" : source;
            return $"{sourceText} | {count:N0} sự kiện | Cập nhật lúc {fetchedAt:HH:mm:ss}";
        }

        private async void WaybillJourneyCacheButton_Click(object sender, EventArgs e)
        {
            var waybillNo = _activeJourneyWaybillNo;
            if (string.IsNullOrWhiteSpace(waybillNo))
                return;

            SetJourneyStatus("Đang mở dữ liệu cache...");
            try
            {
                var local = await _fullStackJourneyService
                    .GetLocalJourneyAsync(waybillNo, _cts.Token)
                    .ConfigureAwait(true);

                if (!string.Equals(_activeJourneyWaybillNo, waybillNo, StringComparison.OrdinalIgnoreCase))
                    return;

                local.Source = "Cache";
                BindJourneyGrid(local, BuildJourneyLoadedStatus(local.Rows.Count, local.FetchedAt ?? DateTime.Now, "Cache"));
                SetJourneyAuxButtons(false, true);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                AppLogger.Warning($"Open journey cache failed for {waybillNo}: {ex.Message}");
                SetJourneyStatus("Không mở được dữ liệu cache.");
            }
        }

        private async void WaybillJourneyRawJsonButton_Click(object sender, EventArgs e)
        {
            var waybillNo = _activeJourneyWaybillNo;
            if (string.IsNullOrWhiteSpace(waybillNo))
                return;

            try
            {
                var cache = await _fullStackJourneyService
                    .GetLatestRawJsonAsync(waybillNo, _cts.Token)
                    .ConfigureAwait(true);

                if (!cache.HasValue)
                {
                    SetJourneyStatus("Chưa có raw JSON trong details.db.");
                    return;
                }

                Clipboard.SetText(cache.RawJson);
                SetJourneyStatus($"Đã copy raw JSON cache {waybillNo}.");
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                AppLogger.Warning($"Copy journey raw JSON failed for {waybillNo}: {ex.Message}");
                SetJourneyStatus("Không copy được raw JSON.");
            }
        }

        private void WaybillJourneyGrid_DataBindingComplete(object sender, DataGridViewBindingCompleteEventArgs e)
        {
            foreach (DataGridViewRow row in _waybillJourneyGrid.Rows)
            {
                if (row.DataBoundItem is not JourneyEventViewModel item) continue;
                row.Height = 27;
                row.DefaultCellStyle.BackColor = Color.White;
                row.DefaultCellStyle.ForeColor = JmsJourneyText;

                if (_waybillJourneyGrid.Columns.Contains(nameof(JourneyEventViewModel.Description)))
                    row.Cells[nameof(JourneyEventViewModel.Description)].Style.ForeColor = JmsJourneyDarkText;

                if (_waybillJourneyGrid.Columns.Contains(nameof(JourneyEventViewModel.ActionType)))
                    row.Cells[nameof(JourneyEventViewModel.ActionType)].Style.ForeColor = ResolveJourneyActionTypeColor(item.ActionType);

                if (_waybillJourneyGrid.Columns.Contains(nameof(JourneyEventViewModel.AttachmentText)))
                {
                    row.Cells[nameof(JourneyEventViewModel.AttachmentText)].Style.ForeColor =
                        string.Equals(item.AttachmentText, "Xem", StringComparison.OrdinalIgnoreCase)
                            ? Color.FromArgb(220, 38, 38)
                            : Color.FromArgb(107, 114, 128);
                }
            }
        }

        private void WaybillJourneyGrid_CellToolTipTextNeeded(object sender, DataGridViewCellToolTipTextNeededEventArgs e)
        {
            if (e.RowIndex < 0 || e.ColumnIndex < 0) return;
            if (_waybillJourneyGrid.Rows[e.RowIndex].DataBoundItem is not JourneyEventViewModel item) return;
            var columnName = _waybillJourneyGrid.Columns[e.ColumnIndex].Name;
            if (columnName == nameof(JourneyEventViewModel.Description))
                e.ToolTipText = item.Description ?? string.Empty;
            else if (columnName == nameof(JourneyEventViewModel.AttachmentText))
                e.ToolTipText = string.Equals(item.AttachmentText, "Xem", StringComparison.OrdinalIgnoreCase)
                    ? "Chưa cấu hình API xem ảnh. Hãy capture request ảnh bằng DevTools."
                    : string.Empty;
            else if (columnName == nameof(JourneyEventViewModel.ScanSite))
                e.ToolTipText = item.ScanSite ?? string.Empty;
            else if (columnName == nameof(JourneyEventViewModel.ActionType))
                e.ToolTipText = item.ActionType ?? string.Empty;
        }

        private void WaybillJourneyGrid_CellPainting(object sender, DataGridViewCellPaintingEventArgs e)
        {
            if (e.RowIndex < 0 || e.ColumnIndex < 0) return;
            if (_waybillJourneyGrid.Columns[e.ColumnIndex].Name != nameof(JourneyEventViewModel.Description)) return;
            if (_waybillJourneyGrid.Rows[e.RowIndex].DataBoundItem is not JourneyEventViewModel item) return;

            e.Paint(e.CellBounds, DataGridViewPaintParts.Background | DataGridViewPaintParts.Border);

            var textBounds = new Rectangle(
                e.CellBounds.Left + 5,
                e.CellBounds.Top + 1,
                Math.Max(4, e.CellBounds.Width - 9),
                Math.Max(4, e.CellBounds.Height - 2));

            var segments = BuildJourneyDescriptionSegments(
                item.Description,
                appendAttachment: false);

            DrawJourneyRichText(e.Graphics, segments, textBounds, e.CellStyle.Font ?? _waybillJourneyGrid.Font);
            e.Handled = true;
        }

        private async void WaybillJourneyGrid_CellClick(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0 || e.ColumnIndex < 0) return;
            var columnName = _waybillJourneyGrid.Columns[e.ColumnIndex].Name;
            if (!string.Equals(columnName, nameof(JourneyEventViewModel.AttachmentText), StringComparison.OrdinalIgnoreCase))
                return;

            if (_waybillJourneyGrid.Rows[e.RowIndex].DataBoundItem is not JourneyEventViewModel item)
                return;
            if (!string.Equals(item.AttachmentText, "Xem", StringComparison.OrdinalIgnoreCase))
                return;

            try
            {
                await _journeyAttachmentService
                    .OpenAttachmentAsync(_activeJourneyWaybillNo, item, _cts.Token)
                    .ConfigureAwait(true);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                SetJourneyStatus(ex.Message);
            }
        }

        private static Color ResolveJourneyActionTypeColor(string action)
        {
            if (string.IsNullOrWhiteSpace(action))
                return JmsJourneyDarkText;

            return action.IndexOf("vấn đề", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   action.IndexOf("bất thường", StringComparison.OrdinalIgnoreCase) >= 0
                ? Color.FromArgb(239, 68, 68)
                : JmsJourneyDarkText;
        }

        private void LogJourneyBind(
            string waybillNo,
            IReadOnlyList<WaybillJourneyRow> rows,
            int bindRows,
            int gridRowsAfterBind)
        {
            var first = rows != null && rows.Count > 0 ? rows[0] : null;
            var last = rows != null && rows.Count > 0 ? rows[^1] : null;
            AppLogger.Info(
                "[FullStackJourney] bind " +
                $"waybill={waybillNo}; currentJourneyWaybillAtBind={_activeJourneyWaybillNo}; " +
                $"detailsCount={(rows?.Count ?? 0)}; bindRows={bindRows}; gridRowsAfterBind={gridRowsAfterBind}; " +
                $"first={FormatJourneyBindEvent(first)}; last={FormatJourneyBindEvent(last)}");
        }

        private static string FormatJourneyBindEvent(WaybillJourneyRow row)
        {
            if (row == null)
                return "--";
            return $"{row.ActionTime:yyyy-MM-dd HH:mm:ss} {TruncateJourneyLog(row.Description, 180)}";
        }

        private static string TruncateJourneyLog(string value, int maxLength)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;
            value = value.Replace('\r', ' ').Replace('\n', ' ').Trim();
            return value.Length <= maxLength ? value : value.Substring(0, maxLength) + "...";
        }

        private static List<JourneyTextRun> BuildJourneyDescriptionSegments(string rawText, bool appendAttachment)
        {
            var runs = new List<JourneyTextRun>();
            var text = string.IsNullOrWhiteSpace(rawText) ? "--" : rawText.Trim();

            var i = 0;
            while (i < text.Length)
            {
                if (StartsWith(text, i, "Xem hình ảnh"))
                {
                    runs.Add(new JourneyTextRun("Xem hình ảnh", Color.FromArgb(239, 68, 68)));
                    i += "Xem hình ảnh".Length;
                    continue;
                }

                var current = text[i];
                if (current == '【')
                {
                    var end = text.IndexOf('】', i + 1);
                    if (end > i)
                    {
                        runs.Add(new JourneyTextRun(text.Substring(i, end - i + 1), Color.FromArgb(239, 68, 68)));
                        i = end + 1;
                        continue;
                    }
                }

                if (current == '[')
                {
                    var end = text.IndexOf(']', i + 1);
                    if (end > i)
                    {
                        var segment = text.Substring(i, end - i + 1);
                        runs.Add(new JourneyTextRun(segment, ResolveBracketRunColor(segment)));
                        i = end + 1;
                        continue;
                    }
                }

                var next = FindNextJourneyToken(text, i + 1);
                runs.Add(new JourneyTextRun(text.Substring(i, next - i), JmsJourneyDarkText));
                i = next;
            }

            if (appendAttachment && text.IndexOf("Xem hình ảnh", StringComparison.OrdinalIgnoreCase) < 0)
                runs.Add(new JourneyTextRun("  Xem hình ảnh", Color.FromArgb(239, 68, 68)));

            return runs;
        }

        private static bool StartsWith(string text, int index, string value)
        {
            return index >= 0
                && index + value.Length <= text.Length
                && string.Compare(text, index, value, 0, value.Length, StringComparison.OrdinalIgnoreCase) == 0;
        }

        private static int FindNextJourneyToken(string text, int start)
        {
            for (var i = Math.Max(0, start); i < text.Length; i++)
            {
                if (text[i] == '【' || text[i] == '[' || StartsWith(text, i, "Xem hình ảnh"))
                    return i;
            }

            return text.Length;
        }

        private static Color ResolveBracketRunColor(string segment)
        {
            var value = segment.Trim('[', ']', ' ', '\t');
            if (value.Length == 0)
                return JmsJourneyDarkText;

            if (value.Any(char.IsDigit) ||
                value.IndexOf("Chưa", StringComparison.OrdinalIgnoreCase) >= 0 ||
                value.IndexOf("không", StringComparison.OrdinalIgnoreCase) >= 0 ||
                value.IndexOf("Đồng ý", StringComparison.OrdinalIgnoreCase) >= 0 ||
                value.IndexOf("Từ chối", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return Color.FromArgb(37, 99, 235);
            }

            return Color.FromArgb(239, 68, 68);
        }

        private static void DrawJourneyRichText(
            Graphics graphics,
            IReadOnlyList<JourneyTextRun> runs,
            Rectangle bounds,
            Font font)
        {
            var x = bounds.Left;
            var y = bounds.Top;
            var maxRight = bounds.Right;
            var lineHeight = Math.Max(10, TextRenderer.MeasureText(graphics, "A", font, bounds.Size, TextFormatFlags.NoPadding).Height);
            var flags = TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis;

            foreach (var run in runs)
            {
                foreach (var token in SplitJourneyTextRun(run.Text))
                {
                    if (string.IsNullOrEmpty(token))
                        continue;

                    var tokenSize = TextRenderer.MeasureText(graphics, token, font, new Size(int.MaxValue, lineHeight), flags);
                    var isWhitespace = string.IsNullOrWhiteSpace(token);
                    if (!isWhitespace && x > bounds.Left && x + tokenSize.Width > maxRight)
                        return;

                    if (!isWhitespace)
                    {
                        TextRenderer.DrawText(
                            graphics,
                            token,
                            font,
                            new Point(x, y),
                            run.Color,
                            flags);
                    }

                    x += tokenSize.Width;
                }
            }
        }

        private static IEnumerable<string> SplitJourneyTextRun(string text)
        {
            if (string.IsNullOrEmpty(text))
                yield break;

            var start = 0;
            var inWhitespace = char.IsWhiteSpace(text[0]);
            for (var i = 1; i < text.Length; i++)
            {
                var whitespace = char.IsWhiteSpace(text[i]);
                if (whitespace == inWhitespace)
                    continue;

                yield return text.Substring(start, i - start);
                start = i;
                inWhitespace = whitespace;
            }

            yield return text.Substring(start);
        }

        private readonly struct JourneyTextRun
        {
            public JourneyTextRun(string text, Color color)
            {
                Text = text ?? string.Empty;
                Color = color;
            }

            public string Text { get; }
            public Color Color { get; }
        }

        private static Color ResolveJourneyActionColor(string action, string severity)
        {
            var text = $"{action} {severity}";
            if (text.IndexOf("Quét kiện vấn đề", StringComparison.OrdinalIgnoreCase) >= 0 ||
                text.IndexOf("Exception", StringComparison.OrdinalIgnoreCase) >= 0)
                return JmsJourneyOrange;
            if (text.IndexOf("Đang chuyển hoàn", StringComparison.OrdinalIgnoreCase) >= 0 ||
                text.IndexOf("Đăng ký chuyển hoàn", StringComparison.OrdinalIgnoreCase) >= 0 ||
                text.IndexOf("Return", StringComparison.OrdinalIgnoreCase) >= 0)
                return JmsJourneyGreen;
            if (text.IndexOf("Delivery", StringComparison.OrdinalIgnoreCase) >= 0)
                return JmsJourneyBlue;
            if (text.IndexOf("Inventory", StringComparison.OrdinalIgnoreCase) >= 0)
                return JmsJourneyPurple;
            return JmsJourneyDarkText;
        }
    }
}

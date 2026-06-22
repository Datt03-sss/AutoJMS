import os

filepath = r"d:\v1.2605.2(new-test)\src\AutoJMS\Forms\FullStackOperation.cs"

with open(filepath, "rb") as f:
    raw_content = f.read()

# Decode content to string
content = raw_content.decode("utf-8")

# Normalize any double \r\r\n or \r\n to standard \n without double spacing
content_norm = content.replace("\r\r\n", "\n").replace("\r\n", "\n").replace("\r", "\n")

# 1. Replace FullStackOperation_Load body
target_load = """            SetupGrids();
            InitializeEnhancedUI();
            SetupDashToolbar();
            tabDash_dataSource.SelectedIndex = 1;
            tabDash_timeUpdateData.Text = "2 PHÚT";"""

replacement_load = """            _selectedSource = "LOCAL";
            _selectedTimeInterval = "2 PHÚT";
            _selectedStatusSelect = "Tất cả tồn kho";
            _searchText = string.Empty;"""

# 2. Remove ThoiHieu cached data call in Load
target_load_cached = """            // If data arrived before the UI was ready, apply it now.
            if (_pendingThoiHieuRows != null)
            {
                AppLogger.Info($"Applying cached ThoiHieu data after UI ready (count={_pendingThoiHieuRows.Count}).");
                SetThoiHieuData(_pendingThoiHieuRows);
                _pendingThoiHieuRows = null;
            }"""

# 3. Clean up FormClosing timers/services
target_closing = """            _isClosing = true;
            _autoRefreshTimer?.Stop();
            _autoRefreshTimer?.Dispose();
            _thoiHieuTimer?.Stop();
            _thoiHieuTimer?.Dispose();
            _alertCheckTimer?.Stop();
            _alertCheckTimer?.Dispose();
            CancelCurrentJourneyLoad();
            _cts.Cancel();
            if (_authTokenHandler != null)
            {
                AuthStateService.Instance.TokenAcquired -= _authTokenHandler;
                _authTokenHandler = null;
            }
            if (_zaloChatService != null)
            {
                _zaloChatService.StopAutoReminder();
            }"""

replacement_closing = """            _isClosing = true;
            _autoRefreshTimer?.Stop();
            _autoRefreshTimer?.Dispose();
            CancelCurrentJourneyLoad();
            _cts.Cancel();
            if (_authTokenHandler != null)
            {
                AuthStateService.Instance.TokenAcquired -= _authTokenHandler;
                _authTokenHandler = null;
            }"""

# 4. Refactor SyncDataAsync
target_sync = """        private async void tabDash_updateData_Click(object sender, EventArgs e)
        {
            if (_isSyncRunning) return;
            tabDash_updateData.Enabled = false;
            tabDash_updateData.Text = "Đang đồng bộ...";
            _isSyncRunning = true;
            try
            {
                var ct = _cts.Token;
                if (!JmsAuthStateService.HasToken && !AuthStateService.Instance.IsAuthenticated)
                {
                    SetFullStackStatus("Đang chờ đăng nhập / authToken");
                    MessageBox.Show("Đang chờ đăng nhập JMS / authToken. Vui lòng đăng nhập JMS trước khi đồng bộ tồn kho.", "FullStack local-first", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                SetFullStackStatus("Đang đồng bộ tồn kho 30 ngày...");
                var result = await _fullStackDashboardService.SyncInventoryAndRefreshTrackingAsync(ct);
                AppLogger.Info($"[FullStackOperation] manual sync finished runId={result.RunId}, fetched={result.TotalFetched}, new={result.NewWaybills}, left={result.LeftInventory}");
                SetFullStackStatus($"Sync xong: {result.TotalFetched:N0} đơn, mới {result.NewWaybills:N0}, rời tồn {result.LeftInventory:N0}");
                await LoadDataAndRefreshViewsAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Lỗi làm mới dữ liệu: " + ex.Message, "Lỗi", MessageBoxButtons.OK, MessageBoxIcon.Error);
                SetFullStackStatus("Sync failed");
            }
            finally
            {
                tabDash_updateData.Enabled = true;
                tabDash_updateData.Text = "Đồng bộ";
                _isSyncRunning = false;
            }
        }"""

replacement_sync = """        private async Task SyncDataAsync()
        {
            if (_isSyncRunning) return;
            _isSyncRunning = true;
            try
            {
                var ct = _cts.Token;
                if (!JmsAuthStateService.HasToken && !AuthStateService.Instance.IsAuthenticated)
                {
                    SetFullStackStatus("Đang chờ đăng nhập / authToken");
                    MessageBox.Show("Đang chờ đăng nhập JMS / authToken. Vui lòng đăng nhập JMS trước khi đồng bộ tồn kho.", "FullStack local-first", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                SetFullStackStatus("Đang đồng bộ tồn kho 30 ngày...");
                var result = await _fullStackDashboardService.SyncInventoryAndRefreshTrackingAsync(ct);
                AppLogger.Info($"[FullStackOperation] manual sync finished runId={result.RunId}, fetched={result.TotalFetched}, new={result.NewWaybills}, left={result.LeftInventory}");
                SetFullStackStatus($"Sync xong: {result.TotalFetched:N0} đơn, mới {result.NewWaybills:N0}, rời tồn {result.LeftInventory:N0}");
                await LoadDataAndRefreshViewsAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Lỗi làm mới dữ liệu: " + ex.Message, "Lỗi", MessageBoxButtons.OK, MessageBoxIcon.Error);
                SetFullStackStatus("Sync failed");
            }
            finally
            {
                _isSyncRunning = false;
            }
        }"""

# 5. Simplify RefreshFilteredGrid
target_refresh_filtered = """        private void RefreshFilteredGrid()
        {
            var filtered = ApplyDashFilter(_lastDashSourceData);
            UpdateDashSummaryLabels(_lastDashSourceData.Count, filtered.Count);

            // Load Thoi Hieu delivery tracking data
            RefreshThoiHieuKpiSheet();
        }"""

replacement_refresh_filtered = """        private void RefreshFilteredGrid()
        {
            var filtered = ApplyDashFilter(_lastDashSourceData);
        }"""

# 6. Simplify UpdateDashGridDataSource
target_update_datasource = """        private void UpdateDashGridDataSource(List<WaybillDbModel> data)
        {
            _lastFilteredDashRows = data?.ToList() ?? new List<WaybillDbModel>();
            PostStateToWebView2();

            // Compute hash for partial refresh
            string newHash = string.Join("|", _lastFilteredDashRows.Select(x => $"{x.WaybillNo}:{x.ThaoTacCuoi}:{x.ThoiGianThaoTac}"));
            if (newHash == _lastDataHash && tabDash_dataGridView.RowCount > 1)
            {
                UpdateSelectedOperationDetailFromGrid();
                return; // No changes, skip refresh
            }

            _lastDataHash = newHash;

            void Apply()
            {
                tabDash_dataGridView.DataSource = null;
                tabDash_dataGridView.DataSource = _lastFilteredDashRows;
                tabDash_dataGridView.Refresh();
                tabDash_dataGridView.ClearSelection();
                if (tabDash_dataGridView.Rows.Count > 0 && tabDash_dataGridView.Columns.Count > 0)
                {
                    tabDash_dataGridView.Rows[0].Selected = true;
                    tabDash_dataGridView.CurrentCell = tabDash_dataGridView.Rows[0].Cells[Math.Min(1, tabDash_dataGridView.Columns.Count - 1)];
                }
                UpdateSelectedOperationDetailFromGrid();
                UpdateOperationFilterToolbar();
            }

            if (tabDash_dataGridView.InvokeRequired)
                tabDash_dataGridView.Invoke(new Action(Apply));
            else
                Apply();
        }"""

replacement_update_datasource = """        private void UpdateDashGridDataSource(List<WaybillDbModel> data)
        {
            _lastFilteredDashRows = data?.ToList() ?? new List<WaybillDbModel>();
            PostStateToWebView2();
        }"""

# 7. Update GetSelectedDashStatus
target_selected_status = """        private string GetSelectedDashStatus()
        {
            return tabDash_statusSelect?.SelectedItem?.ToString()?.Trim()
                ?? tabDash_statusSelect?.Text?.Trim()
                ?? string.Empty;
        }"""

replacement_selected_status = """        private string GetSelectedDashStatus()
        {
            return _selectedStatusSelect ?? string.Empty;
        }"""

# 8. Simplify RefreshDashViewAsync to use internal state and no controls
target_refresh_dash_view = """        private async Task RefreshDashViewAsync(CancellationToken ct)
        {
            string dataSourceOption = GetControlTextSafe(tabDash_dataSource);
            List<WaybillDbModel> dashSourceData;

            if (dataSourceOption == "PHATLAI")
            {
                var phatLaiWaybills = await ZaloChatService.GetWaybillsFromPhatLaiAsync();
                var dbMap = _cloudData
                    .Where(x => !string.IsNullOrWhiteSpace(x.WaybillNo))
                    .GroupBy(x => x.WaybillNo, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

                dashSourceData = phatLaiWaybills
                    .Select(wb => dbMap.TryGetValue(wb, out var existing) ? existing : new WaybillDbModel { WaybillNo = wb })
                    .ToList();
            }
            else
            {
                dashSourceData = _cloudData.ToList();
            }

            _lastDashSourceData = dashSourceData;

            RefreshDashStatusCache(dashSourceData);
            PopulateDashStatusSelects();
            EnsureDashStatusSelectionValid();

            // Render Grids
            RefreshFilteredGrid();
        }"""

replacement_refresh_dash_view = """        private async Task RefreshDashViewAsync(CancellationToken ct)
        {
            string dataSourceOption = _selectedSource;
            List<WaybillDbModel> dashSourceData;

            if (dataSourceOption == "PHATLAI")
            {
                var phatLaiWaybills = await ZaloChatService.GetWaybillsFromPhatLaiAsync();
                var dbMap = _cloudData
                    .Where(x => !string.IsNullOrWhiteSpace(x.WaybillNo))
                    .GroupBy(x => x.WaybillNo, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

                dashSourceData = phatLaiWaybills
                    .Select(wb => dbMap.TryGetValue(wb, out var existing) ? existing : new WaybillDbModel { WaybillNo = wb })
                    .ToList();
            }
            else
            {
                dashSourceData = _cloudData.ToList();
            }

            _lastDashSourceData = dashSourceData;

            // Render Grids
            RefreshFilteredGrid();
        }"""

# 9. Update ApplyDashFilter search logic
target_filter_search = """            // Apply search text filter
            string search = _dashSearchBox?.Text?.Trim()?.ToLower() ?? "";"""

replacement_filter_search = """            // Apply search text filter
            string search = _searchText?.Trim()?.ToLower() ?? "";"""

# 10. Remove date filters from ApplyDashFilter as controls are deleted
target_filter_date = """            // Apply date range filter
            try
            {
                if (_dashDateFrom != null && _dashDateFrom.Checked)
                {
                    DateTime from = _dashDateFrom.Value.Date;
                    filtered = filtered.Where(x => DateTime.TryParse(x.ThoiGianThaoTac, out DateTime t) && t >= from).ToList();
                }
                if (_dashDateTo != null && _dashDateTo.Checked)
                {
                    DateTime to = _dashDateTo.Value.Date.AddDays(1);
                    filtered = filtered.Where(x => DateTime.TryParse(x.ThoiGianThaoTac, out DateTime t) && t <= to).ToList();
                }
            }
            catch { }"""

replacement_filter_date = ""

def patch(target, replacement, desc):
    global content_norm
    target_norm = target.replace("\r\n", "\n").replace("\r", "\n")
    replacement_norm = replacement.replace("\r\n", "\n").replace("\r", "\n")
    if target_norm not in content_norm:
        print(f"FAILED to locate: {desc}")
        return False
    content_norm = content_norm.replace(target_norm, replacement_norm)
    print(f"SUCCESS: {desc}")
    return True

ok = True
ok &= patch(target_load, replacement_load, "FullStackOperation_Load body")
ok &= patch(target_load_cached, "", "ThoiHieu cached data call in Load")
ok &= patch(target_closing, replacement_closing, "FormClosing cleanup")
ok &= patch(target_sync, replacement_sync, "SyncDataAsync refactoring")
ok &= patch(target_refresh_filtered, replacement_refresh_filtered, "RefreshFilteredGrid simplification")
ok &= patch(target_update_datasource, replacement_update_datasource, "UpdateDashGridDataSource simplification")
ok &= patch(target_selected_status, replacement_selected_status, "GetSelectedDashStatus updates")
ok &= patch(target_refresh_dash_view, replacement_refresh_dash_view, "RefreshDashViewAsync updates")
ok &= patch(target_filter_search, replacement_filter_search, "ApplyDashFilter search updates")
ok &= patch(target_filter_date, replacement_filter_date, "ApplyDashFilter date updates")

if ok:
    # Write back the file with standard CRLF (\r\n) line endings
    content_to_write = content_norm.replace("\n", "\r\n")
    with open(filepath, "wb") as f:
        f.write(content_to_write.encode("utf-8"))
    print("ALL PATCHES APPLIED SUCCESSFULLY!")
else:
    print("SOME PATCHES FAILED. NOT WRITING TO FILE.")

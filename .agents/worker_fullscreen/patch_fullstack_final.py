filepath = r"d:\v1.2605.2(new-test)\src\AutoJMS\Forms\FullStackOperation.cs"

with open(filepath, "r", encoding="utf-8") as f:
    content = f.read()

content_norm = content.replace("\r\n", "\n").replace("\r", "\n")

# 1. Empty ClearDashGridSelection
target_clear = """        public void ClearDashGridSelection()
        {
            if (tabDash_dataGridView == null) return;
            if (tabDash_dataGridView.InvokeRequired)
            {
                tabDash_dataGridView.Invoke(new Action(() => 
                {
                    tabDash_dataGridView.ClearSelection();
                    tabDash_dataGridView.CurrentCell = null;
                }));
            }
            else
            {
                tabDash_dataGridView.ClearSelection();
                tabDash_dataGridView.CurrentCell = null;
            }
        }"""

replacement_clear = """        public void ClearDashGridSelection()
        {
        }"""

# 2. Remove EnsureDashStatusSelectionValid
target_ensure = """        private void EnsureDashStatusSelectionValid()
        {
            if (tabDash_statusSelect == null) return;
            var current = GetSelectedDashStatus();
            if (string.IsNullOrWhiteSpace(current) || current == "Tất cả") return;

            // Check custom filters too
            if (IsCustomFilter(current)) return;

            if (_dashStatusCache == null || !_dashStatusCache.Any(s => string.Equals(s, current, StringComparison.OrdinalIgnoreCase)))
            {
                tabDash_statusSelect.SelectedIndex = 0;
            }
        }"""

# 3. Remove UpdateDashSummaryLabels and replace UpdateDashGridDataSource block with Export methods
target_update_datasource = """        private void UpdateDashSummaryLabels(int sourceCount, int filteredCount)
        {
            if (this.InvokeRequired)
            {
                this.Invoke(new Action(() => UpdateDashSummaryLabels(sourceCount, filteredCount)));
                return;
            }

            if (tabDash_lblLastUpdate != null)
                tabDash_lblLastUpdate.Text = $"Local refresh: {DateTime.Now:HH:mm:ss}";

            if (_dashFilterInfo != null)
                _dashFilterInfo.Text = $"Inventory grid | Hiển thị {filteredCount:N0} / {sourceCount:N0} đơn";

            UpdatePriorityFocusCards(sourceCount, filteredCount);
            UpdateDashQueueInsight();
            UpdateOperationCenterChrome(sourceCount, filteredCount);
            UpdateOperationFilterToolbar();
        }

        private void UpdateDashGridDataSource(List<WaybillDbModel> data)
        {
            _lastFilteredDashRows = data?.ToList() ?? new List<WaybillDbModel>();
            PostStateToWebView2();
        }"""

replacement_update_datasource = """        private void UpdateDashGridDataSource(List<WaybillDbModel> data)
        {
            _lastFilteredDashRows = data?.ToList() ?? new List<WaybillDbModel>();
            PostStateToWebView2();
        }

        private async Task ExportOperationCurrentViewAsync(bool selectedOnly)
        {
            var rows = _lastFilteredDashRows.ToList();

            if (rows.Count == 0)
            {
                MessageBox.Show("Không có dữ liệu để xuất.", "Operation Center", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            string path = await _fullStackExportService.ExportWaybillsCsvAsync(rows, "operation-center", _cts.Token);
            SetFullStackStatus($"Đã xuất CSV: {Path.GetFileName(path)}");
            TryOpenPath(path);
        }

        private static void TryOpenPath(string path)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true });
            }
            catch
            {
                // Export completed; opening the file is only a convenience.
            }
        }"""

def patch(target, replacement, desc):
    global content_norm
    target_norm = target.replace("\r\n", "\n").replace("\r", "\n")
    replacement_norm = replacement.replace("\r\n", "\n").replace("\r", "\n")
    if target_norm not in content_norm:
        print(f"FAILED: {desc}")
        return False
    content_norm = content_norm.replace(target_norm, replacement_norm)
    print(f"SUCCESS: {desc}")
    return True

ok = True
ok &= patch(target_clear, replacement_clear, "Empty ClearDashGridSelection")
ok &= patch(target_ensure, "", "Remove EnsureDashStatusSelectionValid")
ok &= patch(target_update_datasource, replacement_update_datasource, "UpdateDashGridDataSource and Export methods")

if ok:
    content_to_write = content_norm.replace("\n", "\r\n")
    with open(filepath, "wb") as f:
        f.write(content_to_write.encode("utf-8"))
    print("ALL FINAL PATCHES APPLIED!")
else:
    print("SOME FINAL PATCHES FAILED.")

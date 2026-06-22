filepath = r"d:\v1.2605.2(new-test)\src\AutoJMS\Forms\FullStackOperation.cs"

with open(filepath, "r", encoding="utf-8") as f:
    content = f.read()

content_norm = content.replace("\r\n", "\n").replace("\r", "\n")

# 1. Remove SetupGrids and ApplyStandardGridSettings
target_grids = """        private void SetupGrids()
        {
            ApplyStandardGridSettings(tabDash_dataGridView);
            ApplyStandardGridSettings(uiDataGridView2);
            ApplyStandardGridSettings(tabChat_dataGrid);

            // tabDash_dataGridView (tabPage3 - "Chuyển hoàn" / Dashboard)
            tabDash_dataGridView.AutoGenerateColumns = false;
            tabDash_dataGridView.Columns.Clear();
            tabDash_dataGridView.Columns.AddRange(new DataGridViewColumn[]
            {
                new DataGridViewTextBoxColumn { Name = "STT", HeaderText = "STT", Width = 45, ReadOnly = true, SortMode = DataGridViewColumnSortMode.NotSortable, Frozen = true },
                new DataGridViewTextBoxColumn { Name = "Mã vận đơn", HeaderText = "Mã vận đơn", DataPropertyName = "WaybillNo", Width = 140, SortMode = DataGridViewColumnSortMode.Programmatic, Frozen = true },
                new DataGridViewTextBoxColumn { Name = "Nhân viên xử lý cuối", HeaderText = "Nhân viên", DataPropertyName = "NguoiThaoTac", Width = 130, SortMode = DataGridViewColumnSortMode.Programmatic },
                new DataGridViewTextBoxColumn { Name = "Trạng thái hiện tại", HeaderText = "Trạng thái", DataPropertyName = "TrangThaiHienTai", Width = 140, SortMode = DataGridViewColumnSortMode.Programmatic },
                new DataGridViewTextBoxColumn { Name = "Loại quét kiện cuối", HeaderText = "Thao tác cuối", DataPropertyName = "ThaoTacCuoi", Width = 160, SortMode = DataGridViewColumnSortMode.Programmatic },
                new DataGridViewTextBoxColumn { Name = "Thời gian thao tác", HeaderText = "Thời gian", DataPropertyName = "ThoiGianThaoTac", Width = 150, SortMode = DataGridViewColumnSortMode.Programmatic },
                new DataGridViewTextBoxColumn { Name = "Nhân viên kiện vấn đề", HeaderText = "NV kiện vấn đề", DataPropertyName = "NhanVienKienVanDe", Width = 120, SortMode = DataGridViewColumnSortMode.Programmatic },
                new DataGridViewTextBoxColumn { Name = "Nguyên nhân kiện vấn đề", HeaderText = "Nguyên nhân KVD", DataPropertyName = "NguyenNhanKienVanDe", Width = 140, SortMode = DataGridViewColumnSortMode.Programmatic },
                new DataGridViewTextBoxColumn { Name = "Số lần nhắc", HeaderText = "Nhắc", DataPropertyName = "PrintCount", Width = 60, SortMode = DataGridViewColumnSortMode.Programmatic },
                new DataGridViewTextBoxColumn { Name = "Cập nhật lúc", HeaderText = "Cập nhật", DataPropertyName = "LastTrackedAt", Width = 140, SortMode = DataGridViewColumnSortMode.Programmatic }
            });
            tabDash_dataGridView.ColumnHeaderMouseClick += tabDash_dataGrid_ColumnHeaderMouseClick;

            // uiDataGridView2 (tabPage4 - "Thời hiệu")
            uiDataGridView2.AutoGenerateColumns = false;
            uiDataGridView2.Columns.Clear();
            uiDataGridView2.Columns.AddRange(new DataGridViewColumn[]
            {
                new DataGridViewTextBoxColumn { Name = "Mã vận đơn", HeaderText = "Mã vận đơn", DataPropertyName = "WaybillNo", SortMode = DataGridViewColumnSortMode.Programmatic },
                new DataGridViewTextBoxColumn { Name = "Thời gian nhận hàng", HeaderText = "Thời gian nhận hàng", DataPropertyName = "ThoiGianNhanHang", SortMode = DataGridViewColumnSortMode.Programmatic },
                new DataGridViewTextBoxColumn { Name = "Thời gian đến bưu cục", HeaderText = "Thời gian đến bưu cục", DataPropertyName = "ThoiGianThaoTac", SortMode = DataGridViewColumnSortMode.Programmatic },
                new DataGridViewTextBoxColumn { Name = "Thời gian tồn kho", HeaderText = "Thời gian tồn kho", DataPropertyName = "TonKhoDuration", SortMode = DataGridViewColumnSortMode.Programmatic },
                new DataGridViewTextBoxColumn { Name = "Hạn SLA còn lại", HeaderText = "Hạn SLA còn lại", DataPropertyName = "SlaRemaining", SortMode = DataGridViewColumnSortMode.Programmatic },
                new DataGridViewTextBoxColumn { Name = "Cảnh báo", HeaderText = "Cảnh báo", DataPropertyName = "LevelCanhBao", SortMode = DataGridViewColumnSortMode.Programmatic },
                new DataGridViewTextBoxColumn { Name = "Người thao tác cuối", HeaderText = "Người thao tác cuối", DataPropertyName = "NguoiThaoTac", SortMode = DataGridViewColumnSortMode.Programmatic },
                new DataGridViewTextBoxColumn { Name = "Trạng thái cuối", HeaderText = "Trạng thái cuối", DataPropertyName = "ThaoTacCuoi", SortMode = DataGridViewColumnSortMode.Programmatic }
            });
            uiDataGridView2.ColumnHeaderMouseClick += uiDataGridView2_ColumnHeaderMouseClick;

            // tabChat_dataGrid
            tabChat_dataGrid.AutoGenerateColumns = false;
            tabChat_dataGrid.Columns.Clear();
            tabChat_dataGrid.Columns.AddRange(new DataGridViewColumn[]
            {
                new DataGridViewTextBoxColumn { Name = "maDon", HeaderText = "Mã vận đơn", DataPropertyName = "WaybillNo", SortMode = DataGridViewColumnSortMode.Programmatic },
                new DataGridViewTextBoxColumn { Name = "nhanVien", HeaderText = "Tên nhân viên", DataPropertyName = "NguoiThaoTac", SortMode = DataGridViewColumnSortMode.Programmatic },
                new DataGridViewTextBoxColumn { Name = "trangThai", HeaderText = "Trạng thái hiện tại", DataPropertyName = "ThaoTacCuoi", SortMode = DataGridViewColumnSortMode.Programmatic },
                new DataGridViewTextBoxColumn { Name = "soLanNhac", HeaderText = "Số lần nhắc", DataPropertyName = "PrintCount", SortMode = DataGridViewColumnSortMode.Programmatic }
            });
            tabChat_dataGrid.ColumnHeaderMouseClick += tabChat_dataGrid_ColumnHeaderMouseClick;
        }

        private void ApplyStandardGridSettings(DataGridView grid)
        {
            if (grid == null) return;
            grid.ReadOnly = true;
            grid.ClipboardCopyMode = DataGridViewClipboardCopyMode.EnableWithoutHeaderText;
            grid.SelectionMode = DataGridViewSelectionMode.CellSelect;
            grid.AllowUserToResizeColumns = true;
            grid.AllowUserToResizeRows = false;
            grid.AllowUserToAddRows = false;
            grid.AllowUserToDeleteRows = false;
            grid.MultiSelect = true;
            grid.RowHeadersVisible = false;
            grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.DisplayedCells;
            grid.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.None;
            grid.RowTemplate.Height = 27;
            grid.ColumnHeadersHeight = 34;
            grid.ColumnHeadersDefaultCellStyle.BackColor = HeaderDark;
            grid.ColumnHeadersDefaultCellStyle.ForeColor = Color.White;
            grid.ColumnHeadersDefaultCellStyle.Font = new Font("Segoe UI Semibold", 7.5F, FontStyle.Bold);
            grid.ColumnHeadersDefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleLeft;
            grid.DefaultCellStyle.Font = new Font("Segoe UI", 7.5F);
            grid.DefaultCellStyle.ForeColor = TextPrimary;
            grid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(37, 99, 235);
            grid.DefaultCellStyle.SelectionForeColor = Color.White;
            grid.EnableHeadersVisualStyles = false;
            grid.DataError -= FullStackGrid_DataError;
            grid.DataError += FullStackGrid_DataError;
            if (grid is Sunny.UI.UIDataGridView uiGrid)
            {
                uiGrid.StripeOddColor = Color.White;
                uiGrid.StripeEvenColor = Color.FromArgb(249, 250, 251);
                uiGrid.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(249, 250, 251);
            }
        }"""

# 2. Refactor SetFullStackStatus
target_status = """        private void SetFullStackStatus(string text)
        {
            if (tabDash_lblLastUpdate == null) return;

            void Apply()
            {
                if (tabDash_lblLastUpdate == null || tabDash_lblLastUpdate.IsDisposed) return;
                tabDash_lblLastUpdate.Text = text;
            }

            if (tabDash_lblLastUpdate.InvokeRequired)
                tabDash_lblLastUpdate.BeginInvoke(new Action(Apply));
            else
                Apply();
        }"""

replacement_status = """        private void SetFullStackStatus(string text)
        {
            _fullStackStatus = text;
        }"""

# 3. Remove RefreshChatViewAsync call
target_load_refresh = """                await RefreshDashViewAsync(_cts.Token);
                await RefreshChatViewAsync(_cts.Token);"""

replacement_load_refresh = """                await RefreshDashViewAsync(_cts.Token);"""

# 4. Remove selected index changed events
target_events = """        private async void tabDash_dataSource_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (_isRefreshingStatusCombos) return;
            await RefreshDashViewAsync(_cts.Token);
        }

        private void tabDash_statusSelect_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (_isRefreshingStatusCombos) return;
            _dashQuickFilter = GetSelectedDashStatus();
            UpdateDashQuickFilterButtons();
            if (_lastDashSourceData.Count == 0) return;
            RefreshFilteredGrid();
        }

        private void tabDash_timeUpdateData_SelectedIndexChanged(object sender, EventArgs e)
        {
            string text = tabDash_timeUpdateData.Text?.Replace(" PHÚT", "")?.Replace(" GIỜ", "")?.Trim() ?? "2";
            int minutes = 2;
            if (tabDash_timeUpdateData.Text.Contains("GIỜ"))
            {
                if (int.TryParse(text, out int h)) minutes = h * 60;
            }
            else
            {
                int.TryParse(text, out minutes);
            }
            if (_autoRefreshTimer != null)
                _autoRefreshTimer.Interval = Math.Max(1, minutes) * 60 * 1000;
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
ok &= patch(target_grids, "", "Remove SetupGrids and ApplyStandardGridSettings")
ok &= patch(target_status, replacement_status, "Refactor SetFullStackStatus")
ok &= patch(target_load_refresh, replacement_load_refresh, "Remove RefreshChatViewAsync call")
ok &= patch(target_events, "", "Remove select index changed events")

if ok:
    content_to_write = content_norm.replace("\n", "\r\n")
    with open(filepath, "wb") as f:
        f.write(content_to_write.encode("utf-8"))
    print("ALL PATCHES APPLIED!")
else:
    print("SOME PATCHES FAILED.")

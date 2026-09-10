using ClosedXML.Excel;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using AutoJMS.Data;

namespace AutoJMS
{
    // Offline seed for tabDash: loads the inventory waybills straight out of an exported workbook
    // (docs/manual/tonkho30ngay.xlsx — ~2.400 mã of site 214A03) so the grid is populated without
    // waiting on JMS. The rows land in RAM only; a later "Đồng bộ" enriches them and writes to
    // SQLite as usual. Nothing here touches Web/index.html — it reuses the existing stream path,
    // so the WebView2 payload format is unchanged.
    public partial class FullStackOperation
    {
        private const string ExcelSeedFileName = "tonkho30ngay.xlsx";

        // Hosted on the "Đồng bộ" button's right-click menu rather than a new toolbar control:
        // the dash header is built in FullStackOperation.Dashboard.cs and adding a button there
        // would reflow the layout for a rarely-used offline action.
        private void AttachExcelSeedMenu()
        {
            var menu = new ContextMenuStrip();
            var item = new ToolStripMenuItem("Nạp nhanh từ Excel tồn kho (offline)");
            item.Click += async (s, e) => await SeedDashFromExcelAsync();
            menu.Items.Add(item);
            tabDash_updateData.ContextMenuStrip = menu;
            _tooltip?.SetToolTip(tabDash_updateData, "Đồng bộ tồn kho 30 ngày từ JMS.\nChuột phải: nạp nhanh từ file Excel tồn kho (offline).");
        }

        private async Task SeedDashFromExcelAsync()
        {
            if (_isSyncRunning)
            {
                SetFullStackStatus("Đang đồng bộ — thử lại sau khi xong");
                return;
            }

            string path = ResolveExcelSeedPath();
            if (string.IsNullOrEmpty(path)) return;

            try
            {
                SetFullStackStatus("Đang đọc file tồn kho...");
                // Parsing a 2.400-row workbook blocks for a beat; keep it off the UI thread.
                List<string> codes = await Task.Run(() => ReadWaybillsFromWorkbook(path));
                if (codes.Count == 0)
                {
                    SetFullStackStatus("File tồn kho không có mã vận đơn nào");
                    MessageBox.Show("Không tìm thấy mã vận đơn nào trong file.\n" + path,
                        "Nạp tồn kho", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                // Back on the UI thread (WinForms sync context) — safe to read _cloudData here.
                var known = new HashSet<string>(
                    _cloudData.Where(x => !string.IsNullOrWhiteSpace(x?.WaybillNo)).Select(x => x.WaybillNo.Trim()),
                    StringComparer.OrdinalIgnoreCase);

                // Only codes the dashboard has never seen become placeholders. An enriched row must
                // never be overwritten by a bare code — the drain replaces by waybill.
                var seeded = codes
                    .Where(c => !known.Contains(c))
                    .Select(c => new WaybillDbModel { WaybillNo = c })
                    .ToList();

                if (seeded.Count == 0)
                {
                    SetFullStackStatus($"Đã có đủ {codes.Count:N0} mã trong dashboard — không cần nạp thêm");
                    return;
                }

                _pendingStreamRows.Enqueue(seeded);
                ScheduleStreamRefresh();
                AppLogger.Info($"[FullStackOperation] excel seed: file={codes.Count} new={seeded.Count} from {Path.GetFileName(path)}");
                SetFullStackStatus($"Đã nạp {seeded.Count:N0} mã tồn kho từ Excel ({codes.Count:N0} mã trong file) — bấm Đồng bộ để lấy trạng thái");
            }
            catch (Exception ex)
            {
                AppLogger.Warning("[FullStackOperation] excel seed failed: " + ex.Message);
                SetFullStackStatus("Không đọc được file tồn kho");
                MessageBox.Show("Không đọc được file tồn kho:\n" + ex.Message,
                    "Nạp tồn kho", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        // Uses the checked-out docs/manual copy when running from a dev tree, otherwise asks.
        // Returns "" when the operator cancels the dialog.
        private static string ResolveExcelSeedPath()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            for (int i = 0; i < 6 && dir != null; i++, dir = dir.Parent)
            {
                string candidate = Path.Combine(dir.FullName, "docs", "manual", ExcelSeedFileName);
                if (File.Exists(candidate)) return candidate;
            }

            using var dlg = new OpenFileDialog
            {
                Title = "Chọn file tồn kho (Excel)",
                Filter = "Excel (*.xlsx)|*.xlsx|Tất cả (*.*)|*.*",
                FileName = ExcelSeedFileName
            };
            return dlg.ShowDialog() == DialogResult.OK ? dlg.FileName : string.Empty;
        }

        // Reads the waybill column of the first worksheet. The export names it "Mã vận đơn", but
        // the header is matched loosely and falls back to column 1 so a re-exported file with a
        // different label still loads. Duplicates are collapsed; order is preserved.
        private static List<string> ReadWaybillsFromWorkbook(string path)
        {
            var codes = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            using var workbook = new XLWorkbook(path);
            var sheet = workbook.Worksheets.FirstOrDefault();
            if (sheet == null) return codes;

            var used = sheet.RangeUsed();
            if (used == null) return codes;

            int firstRow = used.FirstRow().RowNumber();
            int lastRow = used.LastRow().RowNumber();
            int firstCol = used.FirstColumn().ColumnNumber();
            int lastCol = used.LastColumn().ColumnNumber();

            int column = firstCol;
            bool skipHeader = false;
            for (int c = firstCol; c <= lastCol; c++)
            {
                string header = sheet.Cell(firstRow, c).GetString()?.Trim() ?? string.Empty;
                if (header.IndexOf("vận đơn", StringComparison.OrdinalIgnoreCase) >= 0
                    || header.IndexOf("van don", StringComparison.OrdinalIgnoreCase) >= 0
                    || header.IndexOf("waybill", StringComparison.OrdinalIgnoreCase) >= 0
                    || header.IndexOf("billcode", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    column = c;
                    skipHeader = true;
                    break;
                }
            }

            for (int r = skipHeader ? firstRow + 1 : firstRow; r <= lastRow; r++)
            {
                string value = sheet.Cell(r, column).GetString()?.Trim();
                if (string.IsNullOrEmpty(value)) continue;
                // The waybill column is numeric in some exports, so ClosedXML hands back "8.62313E+11"
                // unless the cell is text; guard against that rather than seeding a garbage code.
                if (value.IndexOf('E') >= 0 || value.IndexOf('e') >= 0)
                {
                    var cell = sheet.Cell(r, column);
                    if (cell.DataType == XLDataType.Number) value = ((long)cell.GetDouble()).ToString();
                }
                if (seen.Add(value)) codes.Add(value);
            }

            return codes;
        }
    }
}

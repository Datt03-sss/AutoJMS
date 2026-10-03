using System.Diagnostics;
using System.Drawing.Imaging;
using System.IO;
using AutoJMS.UI.DesignSystem;
using unvell.ReoGrid;
using unvell.ReoGrid.IO;

namespace AutoJMS.FullStack.UI.ThoiHieu
{
    /// <summary>
    /// Tab "Thời hiệu": bản sao sheet "Tổng" của file Excel thật, nạp từ file mẫu nhúng trong assembly.
    /// Thanh công cụ: Nhập file (dữ liệu thật từ file JMS), Xuất ảnh, Mở thư mục; bảng chỉ đọc. Cả tab cố ý không theo theme: luôn sáng kiểu Excel.
    /// </summary>
    internal sealed class ThoiHieuKpiView : UserControl
    {
        private const string TemplateResource = "ThoiHieuKpi.template.xlsx";
        private const string SummarySheet = "Tổng";
        private const string DataSheet = "data";

        private readonly ReoGridControl _grid;
        private readonly Button _openFolderButton;
        private ThoiHieuKpiImport _data;   // null = đang hiện số mẫu của file mẫu

        private static string ExportDirectory => Path.Combine(AppPaths.UserDataDir, "FullStack", "Exports", "ThoiHieu");

        public ThoiHieuKpiView()
        {
            var grid = _grid = new ReoGridControl { Dock = DockStyle.Fill };

            var importButton = CreateButton("Nhập file");
            importButton.Click += async (s, e) => await ImportFileAsync(importButton);
            var exportButton = CreateButton("Xuất ảnh");
            exportButton.Click += (s, e) => ExportImage();
            _openFolderButton = CreateButton("Mở thư mục");
            _openFolderButton.Enabled = false;   // bật sau lần xuất ảnh đầu tiên
            _openFolderButton.Click += (s, e) => OpenExportFolder();

            var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, BackColor = Color.Transparent };
            buttons.Controls.Add(importButton);
            buttons.Controls.Add(exportButton);
            buttons.Controls.Add(_openFolderButton);

            // Chiều cao = nút S(32) + padding trên S(6) + padding dưới S(6): tránh 1 px bị cắt khi DpiHelper.Scale làm tròn ra xa 0
            var toolbar = new Panel
            {
                Dock = DockStyle.Top,
                Height = S(32) + 2 * S(6),
                Padding = new Padding(S(8), S(6), S(8), S(6)),
                BackColor = Color.FromArgb(245, 245, 245)
            };
            toolbar.Controls.Add(buttons);

            // Dock xếp control thêm SAU trước: lưới (Fill) thêm trước thì thanh công cụ (Top) giữ được đỉnh.
            Controls.Add(grid);
            Controls.Add(toolbar);

            try
            {
                grid.CurrentWorksheet = LoadTemplate(grid);
                grid.SheetTabNewButtonVisible = false;   // nút thêm sheet: bảng chỉ đọc
                grid.SheetTabWidth = S(200);             // mặc định quá hẹp, tab "data" bị cắt còn "da"
            }
            catch (Exception ex)
            {
                AppLogger.Error("ThoiHieuKpiView: nạp file mẫu thất bại", ex);
                importButton.Enabled = false;
                exportButton.Enabled = false;
            }
        }

        /// <summary>
        /// Nạp file mẫu vào <paramref name="workbook"/>, đổ <paramref name="data"/> (nếu có), khoá sửa mọi sheet và vẽ
        /// phần định dạng ReoGrid không tự nạp. Trả về sheet "Tổng". Màn hình, ảnh xuất và test đều đi qua đúng hàm này.
        /// </summary>
        internal static Worksheet LoadTemplate(IWorkbook workbook, ThoiHieuKpiImport data = null)
        {
            using (var stream = typeof(ThoiHieuKpiView).Assembly.GetManifestResourceStream(TemplateResource)
                ?? throw new InvalidOperationException($"Thiếu resource {TemplateResource}"))
            {
                workbook.Load(stream, FileFormat.Excel2007);
            }

            data?.Fill(workbook.Worksheets[SummarySheet], workbook.Worksheets[DataSheet]);
            foreach (var sheet in workbook.Worksheets)
                sheet.SetSettings(WorksheetSettings.Edit_Readonly, true);

            var summary = workbook.Worksheets[SummarySheet];
            summary.Recalculate();   // ReoGrid không tính shared formula lúc Load: thiếu dòng này I5, J5... ra 0
            ThoiHieuKpiConditionalFormat.Apply(summary);
            return summary;
        }

        /// <summary>
        /// Vẽ A1 tới hết phần chú thích dưới dòng Tổng ra PNG, không có khung Excel. Control không có Form cha nên
        /// không cướp focus và Windows không kẹp kích thước theo màn hình. Trả về đường dẫn file.
        /// </summary>
        internal static string ExportPng(string directory, ThoiHieuKpiImport data)
        {
            using var grid = new ReoGridControl();
            var sheet = LoadTemplate(grid, data);
            grid.CurrentWorksheet = sheet;
            sheet.SetSettings(WorksheetSettings.View_ShowHeaders | WorksheetSettings.View_ShowGridLine, false);
            sheet.ScaleFactor = 1f;
            sheet.SelectionStyle = WorksheetSelectionStyle.None;   // không thì khung chọn ô A1 lọt vào ảnh
            grid.SetSettings(WorkbookSettings.View_ShowSheetTabControl | WorkbookSettings.View_ShowScrolls, false);

            int total = ThoiHieuKpiConditionalFormat.FindTotalRow(sheet);
            var bounds = sheet.GetRangePhysicsBounds(new RangePosition(0, 0, total + 4, 30));   // A1:AD(Tổng+3)
            grid.Size = new Size((int)Math.Ceiling(bounds.Right) + 1, (int)Math.Ceiling(bounds.Bottom) + 1);
            _ = grid.Handle;

            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, $"thoi-hieu-{DateTime.Now:yyyyMMdd-HHmmss-fff}.png");
            using var bitmap = new Bitmap(grid.Width, grid.Height);
            grid.DrawToBitmap(bitmap, new Rectangle(0, 0, bitmap.Width, bitmap.Height));
            bitmap.Save(path, ImageFormat.Png);
            return path;
        }

        private int S(int value) => DpiHelper.Scale(this, value);

        // Button gốc thay AButton: AButton luôn vẽ theo theme hiện hành, tab này thì không theo theme.
        // FullStackOperation không qua AppTheme.Apply nên không ai tô lại nút này.
        private Button CreateButton(string text) => new Button
        {
            Text = text,
            Font = ThemeTypography.Button,
            UseVisualStyleBackColor = true,
            Size = new Size(S(112), S(32)),
            Margin = new Padding(0, 0, S(6), 0)
        };

        private async Task ImportFileAsync(Button importButton)
        {
            using var dialog = new OpenFileDialog
            {
                Title = "Chọn file ký nhận thực tế (Excel)",
                Filter = "Excel (*.xlsx)|*.xlsx"
            };
            if (dialog.ShowDialog(this) != DialogResult.OK) return;

            importButton.Enabled = false;
            try
            {
                // Đọc vài nghìn dòng bằng ClosedXML mất một nhịp: làm ngoài luồng UI. Nạp lại file mẫu mỗi lần nhập
                // nên nhập lần hai không cộng dồn lên lần một.
                var data = await Task.Run(() => ThoiHieuKpiImport.Read(dialog.FileName));
                _grid.CurrentWorksheet = LoadTemplate(_grid, data);
                _data = data;
                AppLogger.Info($"Đã nhập thời hiệu: {data.Employees.Count} nhân viên, {data.Rows.Count} dòng từ {Path.GetFileName(dialog.FileName)}");
                AToast.Show(this, $"Đã nhập {data.Employees.Count} nhân viên, {data.Employees.Sum(e => e.Orders):N0} đơn.");
            }
            catch (Exception ex)
            {
                AppLogger.Error("ThoiHieuKpiView.ImportFile failed", ex);
                MessageBox.Show(this, $"Lỗi nhập file thời hiệu: {ex.Message}", "Lỗi", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                importButton.Enabled = true;
            }
        }

        private void ExportImage()
        {
            try
            {
                string path = ExportPng(ExportDirectory, _data);
                _openFolderButton.Enabled = true;
                AppLogger.Info($"Đã xuất ảnh thời hiệu: {path}");
                AToast.Show(this, "Đã xuất ảnh thời hiệu.");
            }
            catch (Exception ex)
            {
                AppLogger.Error("ThoiHieuKpiView.ExportImage failed", ex);
                MessageBox.Show(this, $"Lỗi xuất ảnh thời hiệu: {ex.Message}", "Lỗi", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void OpenExportFolder()
        {
            try
            {
                Process.Start(new ProcessStartInfo { FileName = ExportDirectory, UseShellExecute = true });
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Không thể mở thư mục xuất ảnh: " + ex.Message, "Lỗi", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }
}

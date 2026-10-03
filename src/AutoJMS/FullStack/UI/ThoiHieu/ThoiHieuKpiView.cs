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
    /// Thanh công cụ chỉ có Xuất ảnh và Mở thư mục; bảng chỉ đọc.
    /// </summary>
    internal sealed class ThoiHieuKpiView : UserControl
    {
        private const string TemplateResource = "ThoiHieuKpi.template.xlsx";
        private const string SummarySheet = "Tổng";

        private readonly Panel _toolbar;
        private readonly AButton _openFolderButton;
        private readonly ThemeHook _themeHook;

        private static string ExportDirectory => Path.Combine(AppPaths.UserDataDir, "FullStack", "Exports", "ThoiHieu");

        public ThoiHieuKpiView()
        {
            var grid = new ReoGridControl { Dock = DockStyle.Fill };

            var exportButton = CreateButton("Xuất ảnh", ASymbols.Download);
            exportButton.Click += (s, e) => ExportImage();
            _openFolderButton = CreateButton("Mở thư mục", ASymbols.Export);
            _openFolderButton.Enabled = false;   // bật sau lần xuất ảnh đầu tiên
            _openFolderButton.Click += (s, e) => OpenExportFolder();

            var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, BackColor = Color.Transparent };
            buttons.Controls.Add(exportButton);
            buttons.Controls.Add(_openFolderButton);

            // Chiều cao = nút S(32) + padding trên S(6) + padding dưới S(6): tránh 1 px bị cắt khi DpiHelper.Scale làm tròn ra xa 0
            _toolbar = new Panel { Dock = DockStyle.Top, Height = S(32) + 2 * S(6), Padding = new Padding(S(8), S(6), S(8), S(6)) };
            _toolbar.Controls.Add(buttons);

            // Dock xếp control thêm SAU trước: lưới (Fill) thêm trước thì thanh công cụ (Top) giữ được đỉnh.
            Controls.Add(grid);
            Controls.Add(_toolbar);

            try
            {
                grid.CurrentWorksheet = LoadTemplate(grid);
                grid.SheetTabNewButtonVisible = false;   // nút thêm sheet: bảng chỉ đọc
                grid.SheetTabWidth = S(200);             // mặc định quá hẹp, tab "data" bị cắt còn "da"
            }
            catch (Exception ex)
            {
                AppLogger.Error("ThoiHieuKpiView: nạp file mẫu thất bại", ex);
                exportButton.Enabled = false;
            }

            ApplyTheme();
            _themeHook = new ThemeHook(this, ApplyTheme);
        }

        /// <summary>
        /// Nạp file mẫu vào <paramref name="workbook"/>, khoá sửa mọi sheet và vẽ phần định dạng ReoGrid không tự
        /// nạp. Trả về sheet "Tổng". Màn hình, ảnh xuất và test đều đi qua đúng hàm này.
        /// </summary>
        internal static Worksheet LoadTemplate(IWorkbook workbook)
        {
            using (var stream = typeof(ThoiHieuKpiView).Assembly.GetManifestResourceStream(TemplateResource)
                ?? throw new InvalidOperationException($"Thiếu resource {TemplateResource}"))
            {
                workbook.Load(stream, FileFormat.Excel2007);
            }

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
        internal static string ExportPng(string directory)
        {
            using var grid = new ReoGridControl();
            var sheet = LoadTemplate(grid);
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

        private AButton CreateButton(string text, int symbol) => new AButton
        {
            Text = text,
            Symbol = symbol,
            SymbolSize = 16,
            Radius = 6,
            Variant = AButtonVariant.Secondary,
            Size = new Size(S(112), S(32)),
            Margin = new Padding(0, 0, S(6), 0)
        };

        // Bảng giữ kiểu Excel nền trắng ở mọi theme; chỉ thanh công cụ theo theme (nút AButton tự theo).
        private void ApplyTheme()
        {
            _toolbar.BackColor = ThemeManager.IsDark ? ThemeManager.Current.SurfaceAlt : Color.FromArgb(245, 245, 245);
        }

        private void ExportImage()
        {
            try
            {
                string path = ExportPng(ExportDirectory);
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

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            ApplyTheme();   // view nằm trên tab chưa mở nên chưa có handle, ThemeHook bỏ qua tín hiệu đổi theme lúc đó — bắt kịp một lần
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                _themeHook?.Dispose();   // ThemeChanged là event tĩnh: không gỡ thì view bị giữ sống tới hết tiến trình
            base.Dispose(disposing);
        }
    }
}

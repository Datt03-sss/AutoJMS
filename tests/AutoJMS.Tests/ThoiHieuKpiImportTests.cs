using System.Globalization;
using AutoJMS.FullStack.UI.ThoiHieu;
using ClosedXML.Excel;
using unvell.ReoGrid;
using unvell.ReoGrid.Formula;
using Xunit;

namespace AutoJMS.Tests;

// File nhập là bản xuất "Ký nhận thực tế" của JMS: sheet "sheet0", mỗi dòng một vận đơn. Mọi tên ở đây là tên giả.
public sealed class ThoiHieuKpiImportTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"thoihieu-import-{Guid.NewGuid():N}.xlsx");

    public void Dispose() => File.Delete(_path);

    // Cột xếp lộn xộn và có cột thừa như file JMS thật; "Thời gian ký nhận quy hoạch" không được nhầm với "Thời gian ký nhận".
    private string WriteImport(params object[][] rows)
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("sheet0");
        string[] headers = { "Mã vận đơn", "Mã bưu cục phát", "Thời gian ký nhận quy hoạch", "Nhân viên phát kiện", "Thời gian ký nhận" };
        for (int c = 0; c < headers.Length; c++) sheet.Cell(1, c + 1).Value = headers[c];
        for (int r = 0; r < rows.Length; r++)
        {
            sheet.Cell(r + 2, 1).Value = (string)rows[r][0];
            sheet.Cell(r + 2, 2).Value = "999X01";
            sheet.Cell(r + 2, 3).Value = new DateTime(2026, 10, 2, 12, 0, 0);
            sheet.Cell(r + 2, 4).Value = (string)rows[r][1];
            sheet.Cell(r + 2, 5).Value = rows[r][2] switch
            {
                DateTime t => t,
                double d => d,
                string s => s,
                _ => Blank.Value
            };
        }
        workbook.SaveAs(_path);
        return _path;
    }

    private static Worksheet LoadSummary(ThoiHieuKpiImport data, out IWorkbook workbook)
    {
        var culture = CultureInfo.CurrentCulture;
        var uiCulture = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = new CultureInfo("vi-VN");
            workbook = ReoGridControl.CreateMemoryWorkbook();
            return ThoiHieuKpiView.LoadTemplate(workbook, data);
        }
        finally
        {
            CultureInfo.CurrentCulture = culture;
            CultureInfo.CurrentUICulture = uiCulture;
        }
    }

    private static ThoiHieuKpiImport FakeImport(int employees)
    {
        var list = Enumerable.Range(1, employees)
            .Select(i => new ThoiHieuKpiImport.Employee($"NV giả {i:D2}", 10 + i, Enumerable.Range(0, 17).Select(h => h < 16 ? i % 3 : 0).ToArray()))
            .ToList();
        var rows = list.Select(e => (e.Name, (DateTime?)new DateTime(2026, 10, 2, 9, 30, 0))).ToList();
        return new ThoiHieuKpiImport("999X01", list, rows);
    }

    private static double Value(Worksheet sheet, int row, int col)
        => ThoiHieuKpiConditionalFormat.Num(sheet.GetCellData(row, col)) ?? 0;

    [Fact]
    public void Read_GroupsLikeThePivot_AndBucketsSignTimeByHour()
    {
        var data = ThoiHieuKpiImport.Read(WriteImport(
            new object[] { "W1", "Nguyễn Văn B", new DateTime(2026, 10, 2, 7, 30, 0) },      // ≤ 8h
            new object[] { "W2", "Nguyễn Văn B", "2026-10-02 09:15:00" },                      // chuỗi → 9h
            new object[] { "W3", "Nguyễn Văn B", "" },                                         // chưa ký: có đơn, không vào cột giờ
            new object[] { "W4", "Anh Thị A", new DateTime(2026, 10, 2, 23, 59, 0).ToOADate() },// số OADate → 23H
            new object[] { "W5", "", new DateTime(2026, 10, 2, 10, 0, 0) },                    // không tên: pivot "(blank)", bỏ
            new object[] { "W6", "Thanh Văn D", "" },
            new object[] { "W7", "Tùng Văn C", "" }));

        Assert.Equal("999X01", data.SiteCode);
        // Như Excel: "Th" là một chữ đứng sau mọi "T" khác, nên "Tùng" trước "Thanh".
        Assert.Equal(new[] { "Anh Thị A", "Nguyễn Văn B", "Tùng Văn C", "Thanh Văn D" }, data.Employees.Select(e => e.Name).ToArray());

        var a = data.Employees[0];
        Assert.Equal(1, a.Orders);
        Assert.Equal(1, a.Hours[15]);                   // Z = 23H
        Assert.Equal(1, a.Hours.Sum());

        var b = data.Employees[1];
        Assert.Equal(3, b.Orders);
        Assert.Equal(1, b.Hours[0]);                    // K = 8h
        Assert.Equal(1, b.Hours[1]);                    // L = 9h
        Assert.Equal(2, b.Hours.Sum());
        Assert.All(data.Employees, e => Assert.Equal(0, e.Hours[16]));   // AA = 24H luôn 0, như COUNTIFS gốc

        Assert.Equal(7, data.Rows.Count);               // sheet data nhận đủ mọi dòng, kể cả dòng không tên
        Assert.Null(data.Rows[2].SignedAt);
    }

    [Fact]
    public void Read_WithoutRequiredHeaders_Throws()
    {
        using (var workbook = new XLWorkbook())
        {
            workbook.Worksheets.Add("sheet0").Cell(1, 1).Value = "Mã vận đơn";
            workbook.SaveAs(_path);
        }
        Assert.Throws<InvalidDataException>(() => ThoiHieuKpiImport.Read(_path));
    }

    [Fact]
    public void Fill_MoreEmployeesThanTemplate_InsertsStyledRows()
    {
        var data = FakeImport(40);
        var sheet = LoadSummary(data, out var workbook);
        int total = ThoiHieuKpiConditionalFormat.FindTotalRow(sheet);

        Assert.Equal(2 + 40, total);
        Assert.Equal("NV giả 40", sheet.Cells[total - 1, 4].Data);
        Assert.Equal(data.Employees.Sum(e => e.Orders), Value(sheet, total, 5));                // F Tổng
        Assert.Equal(data.Employees.Sum(e => e.Hours.Sum()), Value(sheet, total, 8));           // I Tổng
        Assert.Equal(Value(sheet, total, 5), Value(sheet, 0, 19));                              // T1 "Cần phát ra"
        Assert.Equal(new RangePosition(2, 1, 40, 1), sheet.GetRangeIfMergedCell(new CellPosition(2, 1)));   // B3:B42

        // Hàng chèn giữa bảng mang đủ định dạng và viền của hàng mẫu.
        Assert.Equal(unvell.ReoGrid.DataFormat.CellDataFormatFlag.Percent, sheet.Cells[35, 9].DataFormat);
        Assert.Equal(BorderLineStyle.Solid, sheet.GetRangeBorders(new RangePosition(35, 5, 1, 1)).Bottom.Style);

        Assert.Equal("Tỷ lệ <80%", sheet.Cells[total + 3, 4].Data);                             // chú thích dời xuống theo
        Assert.Equal("NV giả 01", workbook.Worksheets["data"].Cells[2, 2].Data);
        AssertFormulasNormal(sheet);
    }

    [Fact]
    public void Fill_FewerEmployeesThanTemplate_DeletesRows()
    {
        var data = FakeImport(1);
        var sheet = LoadSummary(data, out _);
        int total = ThoiHieuKpiConditionalFormat.FindTotalRow(sheet);

        Assert.Equal(3, total);
        Assert.Equal("NV giả 01", sheet.Cells[2, 4].Data);
        Assert.Equal(11, Value(sheet, total, 5));
        Assert.Equal(Value(sheet, 2, 8) / 11, Value(sheet, 2, 9), 9);                          // J3 = I3/F3
        Assert.Equal("BẢNG KÝ NHẬN THỜI HIỆU THEO MỐC THỜI GIAN 999X01", sheet.Cells[0, 0].Data);
        AssertFormulasNormal(sheet);
    }

    // Lưới đang hiện (có handle) nhập lần hai: vị trí chữ đã cache phải khớp ô sau khi chèn hàng, không thì chú thích dưới
    // Tổng vẽ lệch xuống dưới. ReoGrid không lộ TextBounds/Bounds ra public nên đọc bằng reflection.
    [Theory]
    [InlineData(41)]
    [InlineData(1)]
    public void Fill_OnLiveGrid_KeepsTextInsideMovedCells(int employees)
    {
        using var grid = new ReoGridControl { Size = new System.Drawing.Size(1400, 1300) };
        _ = grid.Handle;
        grid.CurrentWorksheet = ThoiHieuKpiView.LoadTemplate(grid);
        var sheet = grid.CurrentWorksheet = ThoiHieuKpiView.LoadTemplate(grid, FakeImport(employees));
        int total = ThoiHieuKpiConditionalFormat.FindTotalRow(sheet);

        const System.Reflection.BindingFlags any = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic;
        var textBounds = typeof(Cell).GetProperty("TextBounds", any)!;
        var bounds = typeof(Cell).GetProperty("Bounds", any)!;
        // "Giám sát" (B gộp), "Quy ước màu tỷ lệ", dòng chú thích đầu và cuối
        foreach (var cell in new[] { sheet.Cells[2, 1], sheet.Cells[total + 1, 1], sheet.Cells[total + 1, 4], sheet.Cells[total + 3, 4] })
        {
            var text = (unvell.ReoGrid.Graphics.Rectangle)textBounds.GetValue(cell)!;
            var box = (unvell.ReoGrid.Graphics.Rectangle)bounds.GetValue(cell)!;
            Assert.InRange((text.Y + text.Height / 2) / sheet.ScaleFactor, box.Y, box.Bottom);   // tâm chữ nằm trong ô
        }
    }

    private static void AssertFormulasNormal(Worksheet sheet)
    {
        var bad = new List<string>();
        sheet.IterateCells(sheet.UsedRange, (row, col, cell) =>
        {
            if (cell.HasFormula && cell.FormulaStatus != FormulaStatus.Normal) bad.Add($"{cell.Address}={cell.FormulaStatus}");
            return true;
        });
        Assert.Empty(bad);
    }
}

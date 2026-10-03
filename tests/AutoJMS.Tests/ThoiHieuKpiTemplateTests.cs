using System.Globalization;
using AutoJMS.FullStack.UI.ThoiHieu;
using unvell.ReoGrid;
using unvell.ReoGrid.Formula;
using Xunit;

namespace AutoJMS.Tests;

// Nạp file mẫu nhúng thật qua đúng hàm màn hình dùng (LoadTemplate), dưới culture vi-VN như máy Owner.
// ReoGrid 3.3.1 tách số trong công thức theo CurrentCulture: ai thêm công thức có số thập phân vào file
// mẫu thì Load ném lỗi trên máy vi-VN. Thiếu Recalculate thì các ô shared formula (I5, J5...) ra 0 mà
// không báo gì. Cả hai chỉ lộ ra khi nạp file thật, nên test nạp file thật.
public sealed class ThoiHieuKpiTemplateTests
{
    private const int TotalRow = 30;   // hàng 31 của Excel

    private static Worksheet LoadSummary(out IWorkbook workbook)
    {
        var culture = CultureInfo.CurrentCulture;
        var uiCulture = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = new CultureInfo("vi-VN");
            workbook = ReoGridControl.CreateMemoryWorkbook();
            return ThoiHieuKpiView.LoadTemplate(workbook);
        }
        finally
        {
            CultureInfo.CurrentCulture = culture;
            CultureInfo.CurrentUICulture = uiCulture;
        }
    }

    private static double Value(Worksheet sheet, int row, int col)
        => ThoiHieuKpiConditionalFormat.Num(sheet.GetCellData(row, col)) ?? 0;

    [Fact]
    public void Template_HasTwoReadonlySheets()
    {
        LoadSummary(out var workbook);
        Assert.Equal(new[] { "Tổng", "data" }, workbook.Worksheets.Select(s => s.Name).ToArray());
        Assert.All(workbook.Worksheets, s => Assert.True(s.HasSettings(WorksheetSettings.Edit_Readonly), s.Name));
    }

    [Fact]
    public void Template_AllFormulasEvaluate()
    {
        var sheet = LoadSummary(out _);
        var bad = new List<string>();
        int formulas = 0;
        sheet.IterateCells(sheet.UsedRange, (row, col, cell) =>
        {
            if (cell.HasFormula)
            {
                formulas++;
                if (cell.FormulaStatus != FormulaStatus.Normal) bad.Add($"{cell.Address}={cell.FormulaStatus}");
            }
            return true;
        });
        Assert.True(formulas > 100, $"chỉ có {formulas} công thức — file mẫu mất công thức?");
        Assert.Empty(bad);
    }

    // Chưa nhập file thì không hiện số liệu ảo: mọi ô số là 0, tên/giám sát/mã bưu cục để trống. J5 nằm trong vùng
    // shared formula: thiếu Recalculate thì ra NaN thay vì 0.
    [Fact]
    public void Template_BeforeImport_ShowsZerosOnly()
    {
        var sheet = LoadSummary(out _);
        Assert.Equal("BẢNG KÝ NHẬN THỜI HIỆU THEO MỐC THỜI GIAN", sheet.Cells[0, 0].Data);
        foreach (int col in new[] { 11, 13, 15, 17, 19, 21, 24 })                   // L1 N1 P1 R1 T1 V1 Y1
            Assert.Equal(0, Value(sheet, 0, col));
        for (int row = 2; row <= TotalRow; row++)
        {
            foreach (int col in new[] { 1, 2, 4 })                                  // B giám sát, C bưu cục, E tên
                Assert.True(string.IsNullOrEmpty(sheet.Cells[row, col].DisplayText), sheet.Cells[row, col].Address);
            for (int col = 5; col <= 28; col++)                                     // F..AC
                Assert.True(Value(sheet, row, col) == 0, $"{sheet.Cells[row, col].Address}={sheet.Cells[row, col].Data}");
        }
    }

    [Fact]
    public void Template_ConditionalFormat_IsApplied()
    {
        var sheet = LoadSummary(out _);
        Assert.Equal(TotalRow, ThoiHieuKpiConditionalFormat.FindTotalRow(sheet));

        // K3:AA30 toàn 0: Excel tô cả vùng màu max của thang (đã đối chiếu DisplayFormat trên Excel thật).
        for (int row = 2; row < TotalRow; row++)
            for (int col = 10; col <= 26; col++)
            {
                var back = sheet.GetCell(row, col).Style.BackColor;
                Assert.Equal((0x5A, 0x8A, 0xC6), (back.R, back.G, back.B));
            }

        // Dòng Tổng nằm ngoài vùng thang màu: giữ nền đỏ FF0000 của file gốc.
        var total = sheet.GetCell(TotalRow, 10).Style.BackColor;
        Assert.Equal((0xFF, 0x00, 0x00), (total.R, total.G, total.B));
    }
}

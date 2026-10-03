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

    // I5 nằm trong vùng shared formula: chỉ đúng khi LoadTemplate có gọi Recalculate.
    [Fact]
    public void Template_SharedFormulas_AreCalculated()
    {
        var sheet = LoadSummary(out _);
        double sum = Enumerable.Range(10, 17).Sum(col => Value(sheet, 4, col));   // K5:AA5
        Assert.True(sum > 0);
        Assert.Equal(sum, Value(sheet, 4, 8));                                     // I5
        Assert.Equal(Value(sheet, 2, 5) * 0.911, Value(sheet, 2, 6), 9);           // G3 = F3 × 91,1%
    }

    [Fact]
    public void Template_ConditionalFormat_IsApplied()
    {
        var sheet = LoadSummary(out _);
        Assert.Equal(TotalRow, ThoiHieuKpiConditionalFormat.FindTotalRow(sheet));

        // Ô nhỏ nhất của K3:AA30 mang màu min của thang.
        var min = (Row: 0, Col: 0, Value: double.MaxValue);
        for (int row = 2; row < TotalRow; row++)
            for (int col = 10; col <= 26; col++)
                if (Value(sheet, row, col) < min.Value) min = (row, col, Value(sheet, row, col));
        var back = sheet.GetCell(min.Row, min.Col).Style.BackColor;
        Assert.Equal((0xF8, 0x69, 0x6B), (back.R, back.G, back.B));

        // Dòng Tổng nằm ngoài vùng thang màu: giữ nền đỏ FF0000 của file gốc.
        var total = sheet.GetCell(TotalRow, 10).Style.BackColor;
        Assert.Equal((0xFF, 0x00, 0x00), (total.R, total.G, total.B));
    }
}

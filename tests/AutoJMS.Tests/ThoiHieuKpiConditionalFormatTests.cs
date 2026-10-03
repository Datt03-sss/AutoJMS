using AutoJMS.FullStack.UI.ThoiHieu;
using Xunit;

namespace AutoJMS.Tests;

// ReoGrid không nạp định dạng có điều kiện từ xlsx nên thang màu K3:AA và data bar J3:J do code tự tính.
// Mọi bộ số dưới đây đã đo trên chính Excel (COM) — chỗ dễ sai là các ca biên: mọi giá trị bằng nhau,
// giá trị trùng mốc giữa, kênh màu làm tròn hay cắt.
public sealed class ThoiHieuKpiConditionalFormatTests
{
    private static string[] Scale(params double?[] values)
        => ThoiHieuKpiConditionalFormat.ColorScale3(values)
            .Select(c => c.IsEmpty ? "-" : $"{c.R:X2}{c.G:X2}{c.B:X2}")
            .ToArray();

    [Fact]
    public void ColorScale_MinAndMax_GetEndColors()
        => Assert.Equal(new[] { "F8696B", "FCFCFF", "5A8AC6" }, Scale(0, 5, 10));

    // Excel xét max trước: cả vùng bằng nhau thì mọi ô ra màu max, không phải màu giữa.
    [Fact]
    public void ColorScale_AllEqual_GetsMaxColor()
        => Assert.Equal(new[] { "5A8AC6", "5A8AC6", "5A8AC6" }, Scale(3, 3, 3));

    // Mốc giữa trùng min (0,0,0,5 → giữa = 0) và trùng max (0,5,5,5 → giữa = 5).
    [Fact]
    public void ColorScale_MidEqualsMinOrMax_NoDivideByZero()
    {
        Assert.Equal(new[] { "F8696B", "F8696B", "F8696B", "5A8AC6" }, Scale(0, 0, 0, 5));
        Assert.Equal(new[] { "F8696B", "5A8AC6", "5A8AC6", "5A8AC6" }, Scale(0, 5, 5, 5));
    }

    // Mốc giữa là PERCENTILE.INC 50 (0,2,10,20 → 6), kênh màu cắt phần lẻ chứ không làm tròn.
    [Fact]
    public void ColorScale_Interpolates_LikeExcel()
    {
        var four = Scale(0, 2, 10, 20);
        Assert.Equal("F99A9C", four[1]);
        Assert.Equal("CEDCEF", four[2]);

        var five = Scale(1, 4, 7, 30, 31);
        Assert.Equal("FAB2B5", five[1]);
        Assert.Equal("FCFCFF", five[2]);
        Assert.Equal("618FC9", five[3]);
    }

    [Fact]
    public void ColorScale_NullCell_IsNotPainted()
        => Assert.Equal("-", Scale(null, 1, 2)[0]);

    [Fact]
    public void DataBarFraction_MatchesExcel()
    {
        Assert.Equal(0, ThoiHieuKpiConditionalFormat.DataBarFraction(2, 2, 10));
        Assert.Equal(1, ThoiHieuKpiConditionalFormat.DataBarFraction(10, 2, 10));
        Assert.Equal(0.5, ThoiHieuKpiConditionalFormat.DataBarFraction(6, 2, 10));
        Assert.Equal(0.5, ThoiHieuKpiConditionalFormat.DataBarFraction(4, 4, 4));   // min == max: nửa ô
        Assert.Equal(1, ThoiHieuKpiConditionalFormat.DataBarFraction(99, 2, 10));   // ngoài khoảng: kẹp
        Assert.Equal(0, ThoiHieuKpiConditionalFormat.DataBarFraction(-5, 2, 10));
    }

    // ReoGrid trả hằng trong ô là decimal, kết quả công thức là double.
    [Fact]
    public void Num_AcceptsDecimalAndDouble_RejectsText()
    {
        Assert.Equal(5, ThoiHieuKpiConditionalFormat.Num(5m));
        Assert.Equal(2.5, ThoiHieuKpiConditionalFormat.Num(2.5d));
        Assert.Null(ThoiHieuKpiConditionalFormat.Num("x"));
        Assert.Null(ThoiHieuKpiConditionalFormat.Num(null));
    }
}

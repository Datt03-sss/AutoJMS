using System;
using System.Collections.Generic;
using System.IO;
using PdfSharp.Pdf;
using Xunit;

namespace AutoJMS.Tests;

/// <summary>
/// Ghim phần quyết định của lượt "gửi gộp, thiếu thì bù": số trang thật của PDF nhận về là
/// thứ duy nhất nói được JMS đã dựng đủ nhãn hay chưa.
/// </summary>
public sealed class ReprintPdfBatchTests
{
    private static byte[] MakePdf(int pages)
    {
        using var doc = new PdfDocument();
        for (int i = 0; i < pages; i++) doc.AddPage();

        using var buffer = new MemoryStream();
        doc.Save(buffer, closeStream: false);
        return buffer.ToArray();
    }

    [Fact]
    public void DemTrangDungVaKhongNemKhiBytesHong()
    {
        Assert.Equal(3, ReprintPdfBatch.CountPages(MakePdf(3)));
        Assert.Equal(0, ReprintPdfBatch.CountPages(null));
        Assert.Equal(0, ReprintPdfBatch.CountPages(Array.Empty<byte>()));
        Assert.Equal(0, ReprintPdfBatch.CountPages(new byte[] { 1, 2, 3, 4 }));
    }

    /// <summary>
    /// Lượt bù gọi từng mã một rồi nối lại, nên tổng số trang phải bằng số mã — thiếu một
    /// trang là thiếu hẳn một nhãn mà người dùng đã chọn in.
    /// </summary>
    [Fact]
    public void GhepGiuDuSoTrangCuaTungPhan()
    {
        var merged = ReprintPdfBatch.Merge(new List<byte[]>
        {
            MakePdf(1), MakePdf(1), MakePdf(1)
        });

        Assert.Equal(3, ReprintPdfBatch.CountPages(merged));
    }

    [Fact]
    public void BoQuaPhanRongVaTraThangKhiChiConMotPhan()
    {
        var only = MakePdf(2);

        Assert.Same(only, ReprintPdfBatch.Merge(new List<byte[]> { null, only, Array.Empty<byte>() }));
        Assert.Empty(ReprintPdfBatch.Merge(new List<byte[]>()));
        Assert.Empty(ReprintPdfBatch.Merge(new List<byte[]> { null }));
    }

    /// <summary>
    /// successNumber/failNumber chỉ đi vào log để đối chiếu với số trang thật. JMS trả lỗi
    /// nghiệp vụ trong thân HTTP 200 với data:null, nên hàm này không được ném.
    /// </summary>
    [Fact]
    public void DocSuccessFailChoLogVaChiuDuocThanRong()
    {
        Assert.Equal(" success=1 fail=2", ReprintPdfBatch.DescribeCounts(
            """{"code":1,"data":{"successNumber":1,"failNumber":2,"pdfUrl":"x"}}"""));

        Assert.Equal("", ReprintPdfBatch.DescribeCounts("""{"code":0,"data":null}"""));
        Assert.Equal("", ReprintPdfBatch.DescribeCounts("""{"data":{"pdfUrl":"x"}}"""));
        Assert.Equal("", ReprintPdfBatch.DescribeCounts("khong phai json"));
        Assert.Equal("", ReprintPdfBatch.DescribeCounts(""));
    }
}

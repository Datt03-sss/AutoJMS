#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

namespace AutoJMS;

/// <summary>
/// Đếm nhãn và ghép nhiều PDF của <c>batchPrintPDF</c> thành một tệp.
/// <para>
/// JMS nhận cả danh sách mã trong một lượt nhưng có khi chỉ dựng được vài nhãn; nó báo
/// <c>successNumber</c>/<c>failNumber</c> mà KHÔNG nói thiếu mã nào. Số trang của PDF nhận về
/// mới là sự thật, nên tab "In lại đơn" đối chiếu bằng <see cref="CountPages"/> rồi mới quyết
/// định có phải gọi bù từng mã hay không.
/// </para>
/// </summary>
internal static class ReprintPdfBatch
{
    /// <summary>Số trang thật của một PDF. Rỗng hoặc hỏng thì trả 0, không ném.</summary>
    internal static int CountPages(byte[]? pdf)
    {
        if (pdf == null || pdf.Length == 0) return 0;
        try
        {
            using var stream = new MemoryStream(pdf, writable: false);
            // Import, không phải InformationOnly: PDFsharp 6 đánh dấu InformationOnly là
            // obsolete vì nó chưa được cài đặt.
            using var doc = PdfReader.Open(stream, PdfDocumentOpenMode.Import);
            return doc.PageCount;
        }
        catch (Exception ex)
        {
            AppLogger.Warning($"In lại đơn: không đếm được số trang PDF: {ex.Message}");
            return 0;
        }
    }

    /// <summary>
    /// Nối các PDF theo đúng thứ tự truyền vào. Phần rỗng bị bỏ qua; đúng một phần thì trả
    /// thẳng phần đó để khỏi ghi lại tệp mà không đổi gì.
    /// </summary>
    internal static byte[] Merge(IReadOnlyList<byte[]> parts)
    {
        if (parts == null || parts.Count == 0) return Array.Empty<byte>();

        var usable = new List<byte[]>(parts.Count);
        foreach (var part in parts)
            if (part != null && part.Length > 0) usable.Add(part);

        if (usable.Count == 0) return Array.Empty<byte>();
        if (usable.Count == 1) return usable[0];

        using var output = new PdfDocument();
        foreach (var part in usable)
        {
            using var stream = new MemoryStream(part, writable: false);
            using var input = PdfReader.Open(stream, PdfDocumentOpenMode.Import);
            for (int i = 0; i < input.PageCount; i++)
                output.AddPage(input.Pages[i]);
        }

        using var buffer = new MemoryStream();
        output.Save(buffer, closeStream: false);
        return buffer.ToArray();
    }

    /// <summary>
    /// <c>successNumber</c>/<c>failNumber</c> trong thân batchPrintPDF, chỉ để ghi log đối chiếu
    /// với số trang thật. Thiếu trường thì trả chuỗi rỗng.
    /// </summary>
    internal static string DescribeCounts(string rawJson)
    {
        if (string.IsNullOrWhiteSpace(rawJson)) return "";
        try
        {
            using var doc = JsonDocument.Parse(rawJson);
            if (!doc.RootElement.TryGetProperty("data", out var data)
                || data.ValueKind != JsonValueKind.Object) return "";

            string success = ReadNumber(data, "successNumber");
            string fail = ReadNumber(data, "failNumber");
            if (success.Length == 0 && fail.Length == 0) return "";
            return $" success={success} fail={fail}";
        }
        catch (Exception)
        {
            return "";
        }
    }

    private static string ReadNumber(JsonElement data, string name)
    {
        if (!data.TryGetProperty(name, out var element)) return "";
        return element.ValueKind switch
        {
            JsonValueKind.Number => element.GetRawText(),
            JsonValueKind.String => element.GetString() ?? "",
            _ => ""
        };
    }
}

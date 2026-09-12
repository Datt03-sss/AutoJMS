#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using PdfSharp.Drawing;
using PdfSharp.Drawing.Layout;
using PdfSharp.Fonts;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

namespace AutoJMS;

/// <summary>
/// What the user wants stamped over the reprinted label. Each region is opt-in:
/// a region that is not enabled is left exactly as the JMS gateway produced it.
/// </summary>
public sealed class ReprintOverlayContent
{
    public bool EditReceiver { get; set; }
    public bool EditRoute { get; set; }
    public bool EditNotes { get; set; }
    public bool EditPrintCount { get; set; }

    public string ReceiverName { get; set; } = "";

    /// <summary>
    /// Số điện thoại in kèm tên, đúng dạng Owner đang thấy trên app: bản che
    /// (<c>******1886</c>) khi chưa bấm nút con mắt, bản đầy đủ khi đã bấm.
    /// </summary>
    public string ReceiverPhone { get; set; } = "";
    public string ReceiverAddress { get; set; } = "";

    public string Route1 { get; set; } = "";
    public string Route2 { get; set; } = "";
    public string Route3 { get; set; } = "";

    public string Note { get; set; } = "";
    public string CodAmount { get; set; } = "";
    public string Deadline { get; set; } = "";
    public string WaybillNo { get; set; } = "";

    // ── Vùng 4: dòng "{mã bưu cục} in lần {n}: {giờ} {ngày}" ──
    public string PrintCountNetworkCode { get; set; } = "";
    public string PrintCountTimes { get; set; } = "";
    public string PrintCountTimestamp { get; set; } = "";

    public bool HasAnyEdit => EditReceiver || EditRoute || EditNotes || EditPrintCount;
}

/// <summary>
/// Feeds PDFsharp the Windows system fonts. The stock resolver has no font source on a
/// self-contained win-x64 publish, and the label text is Vietnamese, so we hand it a TTF
/// with full diacritic coverage (Arial first, then the usual UI fallbacks).
/// </summary>
internal sealed class WindowsFontResolver : IFontResolver
{
    internal const string RegularFace = "autojms-reprint#regular";
    internal const string BoldFace = "autojms-reprint#bold";

    private static readonly string[] RegularCandidates =
        { "arial.ttf", "segoeui.ttf", "tahoma.ttf", "calibri.ttf", "times.ttf" };

    private static readonly string[] BoldCandidates =
        { "arialbd.ttf", "segoeuib.ttf", "tahomabd.ttf", "calibrib.ttf", "timesbd.ttf" };

    private static readonly Dictionary<string, byte[]> Cache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly object Gate = new();

    public FontResolverInfo? ResolveTypeface(string familyName, bool isBold, bool isItalic)
        => new FontResolverInfo(isBold ? BoldFace : RegularFace);

    public byte[]? GetFont(string faceName)
    {
        lock (Gate)
        {
            if (Cache.TryGetValue(faceName, out var cached)) return cached;

            var candidates = string.Equals(faceName, BoldFace, StringComparison.OrdinalIgnoreCase)
                ? BoldCandidates
                : RegularCandidates;

            var bytes = LoadFirstAvailable(candidates);
            if (bytes != null) Cache[faceName] = bytes;
            return bytes;
        }
    }

    /// <summary>True when at least a regular face can be loaded from this machine.</summary>
    internal static bool HasUsableFonts()
    {
        lock (Gate)
        {
            return LoadFirstAvailable(RegularCandidates) != null;
        }
    }

    private static byte[]? LoadFirstAvailable(string[] fileNames)
    {
        string fontsDir;
        try
        {
            fontsDir = Environment.GetFolderPath(Environment.SpecialFolder.Fonts);
        }
        catch
        {
            fontsDir = @"C:\Windows\Fonts";
        }

        foreach (var name in fileNames)
        {
            try
            {
                var path = Path.Combine(fontsDir, name);
                if (File.Exists(path)) return File.ReadAllBytes(path);
            }
            catch (Exception ex)
            {
                AppLogger.Warning($"In lại đơn: không đọc được font {name}: {ex.Message}");
            }
        }
        return null;
    }
}

/// <summary>
/// Stamps the four editable regions onto a JMS label PDF while keeping the file vector.
/// Everything is drawn with PDFsharp path/text operators appended to the existing content
/// stream, so the 1D barcode and the QR code are never rasterized.
/// </summary>
public static class PdfReprintModifier
{
    private static readonly object FontGate = new();
    private static bool _fontResolverReady;

    /// <summary>False when this machine has no usable TTF — callers then print the original label.</summary>
    public static bool CanModify()
    {
        try
        {
            return WindowsFontResolver.HasUsableFonts();
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Applies the overlay and returns the new PDF bytes. Returns <paramref name="originalPdf"/>
    /// untouched when nothing is enabled. Throws when the PDF cannot be parsed or written.
    /// </summary>
    public static byte[] Apply(byte[] originalPdf, ReprintOverlayContent content, ReprintLayoutOptions? layout = null)
    {
        if (originalPdf == null || originalPdf.Length == 0)
            throw new ArgumentException("PDF gốc rỗng.", nameof(originalPdf));
        if (content == null || !content.HasAnyEdit)
            return originalPdf;

        layout ??= ReprintLayoutOptions.Load();
        EnsureFontResolver();

        using var input = new MemoryStream(originalPdf, writable: false);
        using var doc = PdfReader.Open(input, PdfDocumentOpenMode.Modify);

        for (int i = 0; i < doc.PageCount; i++)
        {
            var page = doc.Pages[i];
            if (page.Rotate % 360 != 0)
                AppLogger.Warning($"In lại đơn: trang {i + 1} xoay {page.Rotate} độ — toạ độ đè có thể lệch.");

            // Lưới kẻ phải đọc TRƯỚC khi mở XGraphics: FromPdfPage(Append) nối thêm một
            // content stream rỗng vào trang, và ContentReader gộp stream đó lại thì ném
            // NullReferenceException — đọc sau sẽ luôn hỏng và âm thầm rơi về toạ độ tỷ lệ.
            var grid = layout.SnapToGrid
                ? PdfLabelGrid.Detect(page, page.Width.Point, page.Height.Point)
                : PdfLabelGrid.Empty;

            using var gfx = XGraphics.FromPdfPage(page, XGraphicsPdfPageOptions.Append);
            DrawOverlay(gfx, grid, content, layout);
        }

        using var output = new MemoryStream();
        doc.Save(output, closeStream: false);
        return output.ToArray();
    }

    /// <summary>
    /// Non-throwing wrapper: on any failure the original bytes come back and
    /// <paramref name="error"/> explains why, so printing degrades to the untouched label.
    /// </summary>
    public static byte[] TryApply(byte[] originalPdf, ReprintOverlayContent content, out string error)
    {
        error = "";
        try
        {
            return Apply(originalPdf, content);
        }
        catch (Exception ex)
        {
            error = ex.Message;
            AppLogger.Error("In lại đơn: đè nội dung lên PDF thất bại, dùng bản gốc.", ex);
            return originalPdf;
        }
    }

    // ── drawing ──────────────────────────────────────────────

    private static void DrawOverlay(XGraphics gfx, PdfLabelGrid grid, ReprintOverlayContent content, ReprintLayoutOptions layout)
    {
        var pageSize = gfx.PageSize;
        double w = pageSize.Width, h = pageSize.Height;
        if (w <= 0 || h <= 0) return;

        var pen = new XPen(XColors.Black, layout.LineWidth);
        var page = new XSize(w, h);

        // Tỷ lệ trong layout chỉ là điểm khởi đầu; mép thật lấy từ chính đường kẻ của nhãn.
        if (!grid.IsUsable) grid = PdfLabelGrid.Empty;
        double tolerance = layout.SnapTolerance;

        if (content.EditReceiver)
            DrawReceiver(gfx, pen, Snap(ToRect(layout.Receiver, w, h), grid, tolerance), page, content, layout);

        if (content.EditRoute)
            DrawRoute(gfx, pen, Snap(ToRect(layout.Route, w, h), grid, tolerance), page, grid, content, layout);

        if (content.EditNotes)
            DrawNotes(gfx, pen, Snap(ToRect(layout.Notes, w, h), grid, tolerance), page, grid, content, layout);

        if (content.EditPrintCount)
            DrawPrintCount(gfx, pen, Snap(ToRect(layout.PrintCount, w, h), grid, tolerance), page, content, layout);
    }

    /// <summary>
    /// Kéo cả 4 mép vùng đè về đường kẻ thật gần nhất. Vùng 1 và Vùng 2 có cùng toạ độ X ở
    /// vạch chung (<see cref="ReprintLayoutOptions.Normalize"/> ép vậy) nên chúng luôn ghim
    /// vào cùng một đường — vạch dọc vẫn liền một nét sau khi ghim.
    /// </summary>
    private static XRect Snap(XRect rect, PdfLabelGrid grid, double tolerance)
    {
        if (grid.Vertical.Count == 0 && grid.Horizontal.Count == 0) return rect;

        double left = grid.SnapX(rect.X, tolerance);
        double right = grid.SnapX(rect.Right, tolerance);
        double top = grid.SnapY(rect.Y, tolerance);
        double bottom = grid.SnapY(rect.Bottom, tolerance);

        // Hai mép ghim trúng cùng một vạch thì vùng bị bẹp — thà giữ nguyên tỷ lệ.
        if (right - left <= 1 || bottom - top <= 1) return rect;
        return new XRect(left, top, right - left, bottom - top);
    }

    /// <summary>
    /// Khung "Người nhận": che sạch chữ cũ + chữ mờ "COD", kẻ lại đủ 4 cạnh của ô bảng
    /// (trên giáp Người gửi, dưới giáp Nội dung hàng, phải là vạch chung với Mã tuyến,
    /// trái là viền nhãn) rồi in lại nhãn / tên / địa chỉ.
    /// </summary>
    private static void DrawReceiver(XGraphics gfx, XPen pen, XRect rect, XSize page, ReprintOverlayContent c, ReprintLayoutOptions layout)
    {
        gfx.DrawRectangle(XBrushes.White, rect);
        var frame = layout.DrawReceiverBorder ? DrawFrame(gfx, pen, rect, layout.LineWidth, page) : rect;

        var inner = Pad(frame, layout.Padding);
        if (inner.Width <= 1 || inner.Height <= 1) return;

        // Dòng 1: nhãn in đậm.
        var labelFont = Font(layout, layout.ReceiverLabelFontSize, true);
        double y = inner.Y;
        double labelHeight = LineHeight(gfx, labelFont);
        gfx.DrawString("Người nhận :", labelFont, XBrushes.Black,
            new XRect(inner.X, y, inner.Width, labelHeight), XStringFormats.TopLeft);
        y += labelHeight;

        // Dòng 2: tên người nhận + SĐT, ghép đúng dạng nhãn gốc in ("Tên ,******1886")
        // để miếng vá không lộ ra khác kiểu so với những đơn không sửa.
        var name = JoinNameAndPhone(Clean(c.ReceiverName), Clean(c.ReceiverPhone));
        if (name.Length > 0 && y < inner.Bottom)
        {
            var nameFont = FitFont(gfx, name, layout, layout.ReceiverLabelFontSize, true, inner.Width, 5.0);
            double nameHeight = LineHeight(gfx, nameFont);
            gfx.DrawString(name, nameFont, XBrushes.Black,
                new XRect(inner.X, y, inner.Width, nameHeight), XStringFormats.TopLeft);
            y += nameHeight;
        }

        // Các dòng còn lại: địa chỉ, ngắt dòng theo bề ngang ô.
        var address = Clean(c.ReceiverAddress);
        double addressTop = y + 1.0;
        if (address.Length == 0 || addressTop >= inner.Bottom) return;

        var bodyFont = Font(layout, layout.ReceiverBodyFontSize, false);
        var formatter = new XTextFormatter(gfx) { Alignment = XParagraphAlignment.Left };
        formatter.DrawString(address, bodyFont, XBrushes.Black,
            new XRect(inner.X, addressTop, inner.Width, inner.Bottom - addressTop), XStringFormats.TopLeft);
    }

    private static void DrawRoute(XGraphics gfx, XPen pen, XRect rect, XSize page, PdfLabelGrid grid, ReprintOverlayContent c, ReprintLayoutOptions layout)
    {
        gfx.DrawRectangle(XBrushes.White, rect);
        // Mép trái đã được ReprintLayoutOptions.Normalize ghim trùng mép phải của Người nhận.
        var frame = layout.DrawRouteBorder ? DrawFrame(gfx, pen, rect, layout.LineWidth, page) : rect;

        // Vạch chia ô: lấy đúng vạch của nhãn nếu đọc được, không thì mới quy ra từ tỷ lệ.
        var dividers = grid.HorizontalInside(rect.Y, rect.Bottom, rect.X, rect.Right).ToList();
        if (dividers.Count == 0)
        {
            dividers = (layout.RouteDividers ?? Array.Empty<double>())
                .Where(d => d > 0 && d < 1)
                .Distinct()
                .OrderBy(d => d)
                .Select(d => rect.Y + d * rect.Height)
                .ToList();
        }

        foreach (var y in dividers)
            gfx.DrawLine(pen, frame.X, y, frame.Right, y);

        // N vạch chia tạo N+1 ô, đánh số từ 0 từ trên xuống.
        var bounds = new List<double> { rect.Y };
        bounds.AddRange(dividers);
        bounds.Add(rect.Bottom);

        var codes = new[] { c.Route1, c.Route2, c.Route3 };
        var cellIndexes = layout.RouteCellIndexes ?? Array.Empty<int>();

        for (int i = 0; i < codes.Length && i < cellIndexes.Length; i++)
        {
            var code = Clean(codes[i]);
            if (code.Length == 0) continue;

            int cell = cellIndexes[i];
            if (cell < 0 || cell >= bounds.Count - 1) continue;

            var cellRect = new XRect(rect.X, bounds[cell], rect.Width, bounds[cell + 1] - bounds[cell]);

            var padded = Pad(cellRect, layout.Padding);
            if (padded.Width <= 1 || padded.Height <= 1) continue;

            var font = FitFont(gfx, code, layout, layout.RouteFontSize, true, padded.Width, 5.0);
            gfx.DrawString(code, font, XBrushes.Black, padded, XStringFormats.Center);
        }
    }

    private static void DrawNotes(XGraphics gfx, XPen pen, XRect rect, XSize page, PdfLabelGrid grid, ReprintOverlayContent c, ReprintLayoutOptions layout)
    {
        gfx.DrawRectangle(XBrushes.White, rect);
        var frame = layout.DrawNotesBorder ? DrawFrame(gfx, pen, rect, layout.LineWidth, page) : rect;

        var columns = grid.VerticalInside(rect.X, rect.Right, rect.Y, rect.Bottom);
        double splitX = columns.Count > 0 ? columns[0] : rect.X + layout.NotesColumnSplit * rect.Width;
        gfx.DrawLine(pen, splitX, frame.Y, splitX, frame.Bottom);

        var rightRows = grid.HorizontalInside(rect.Y, rect.Bottom, splitX, rect.Right);
        double splitY = rightRows.Count > 0 ? rightRows[0] : rect.Y + layout.NotesRightRowSplit * rect.Height;
        gfx.DrawLine(pen, splitX, splitY, frame.Right, splitY);

        // Cột trái của nhãn gốc còn một vạch ngăn ô mã vận đơn ở đáy; miếng vá xoá mất nó,
        // nên kẻ lại đúng chỗ đọc được. Không đọc được thì thôi, giữ nguyên như trước.
        var leftRows = grid.HorizontalInside(rect.Y, rect.Bottom, rect.X, splitX);
        foreach (var y in leftRows)
            gfx.DrawLine(pen, frame.X, y, splitX, y);

        // Left column: "Ghi chú:" + content, waybill pinned to the bottom.
        var left = Pad(new XRect(rect.X, rect.Y, splitX - rect.X, rect.Height), layout.Padding);
        if (left.Width > 1 && left.Height > 1)
        {
            var labelFont = Font(layout, layout.NotesLabelFontSize, true);
            double labelHeight = LineHeight(gfx, labelFont);
            gfx.DrawString("Ghi chú:", labelFont, XBrushes.Black,
                new XRect(left.X, left.Y, left.Width, labelHeight), XStringFormats.TopLeft);

            // Ô mã vận đơn ở đáy cột trái: có vạch thật thì đặt gọn trong ô đó, không thì
            // chừa đúng một dòng ở đáy như trước.
            var waybill = Clean(c.WaybillNo);
            double waybillTop = left.Bottom;
            if (waybill.Length > 0)
            {
                var waybillFont = FitFont(gfx, waybill, layout, layout.WaybillFontSize, true, left.Width, 5.0);
                double waybillHeight = LineHeight(gfx, waybillFont);
                waybillTop = leftRows.Count > 0 ? leftRows[^1] : left.Bottom - waybillHeight;

                gfx.DrawString(waybill, waybillFont, XBrushes.Black,
                    new XRect(left.X, waybillTop, left.Width, Math.Max(waybillHeight, rect.Bottom - waybillTop)),
                    XStringFormats.CenterLeft);
            }

            double noteTop = left.Y + labelHeight + 1.0;
            double noteBottom = waybillTop - 1.0;
            var note = Clean(c.Note);
            if (note.Length > 0 && noteBottom > noteTop)
            {
                var bodyFont = Font(layout, layout.NotesBodyFontSize, false);
                var formatter = new XTextFormatter(gfx) { Alignment = XParagraphAlignment.Left };
                formatter.DrawString(note, bodyFont, XBrushes.Black,
                    new XRect(left.X, noteTop, left.Width, noteBottom - noteTop), XStringFormats.TopLeft);
            }
        }

        // Right column: COD on top, delivery deadline underneath.
        DrawLabeledValue(gfx, Pad(new XRect(splitX, rect.Y, rect.Right - splitX, splitY - rect.Y), layout.Padding),
            "Tiền thu người nhận:", c.CodAmount, layout);

        DrawLabeledValue(gfx, Pad(new XRect(splitX, splitY, rect.Right - splitX, rect.Bottom - splitY), layout.Padding),
            "Giao trước:", c.Deadline, layout);
    }

    /// <summary>
    /// Khung "đếm lần in" — ô nằm ngay dưới "Trọng lượng tính" ở cột phải, nhãn gốc in
    /// hai dòng: <c>214A03 in lần 11:</c> rồi <c>22:17 12-09-2026</c>. Ô này cao khoảng 21pt
    /// nên chỉ chừa 1pt trên/dưới, còn bề ngang vẫn theo Padding chung.
    /// </summary>
    private static void DrawPrintCount(XGraphics gfx, XPen pen, XRect rect, XSize page, ReprintOverlayContent c, ReprintLayoutOptions layout)
    {
        gfx.DrawRectangle(XBrushes.White, rect);
        var frame = layout.DrawPrintCountBorder ? DrawFrame(gfx, pen, rect, layout.LineWidth, page) : rect;

        var inner = new XRect(frame.X + layout.Padding, frame.Y + 1.0,
            Math.Max(0, frame.Width - 2 * layout.Padding),
            Math.Max(0, frame.Height - 2.0));
        if (inner.Width <= 1 || inner.Height <= 1) return;

        double y = inner.Y;
        foreach (var line in new[] { BuildPrintCountHeader(c), Clean(c.PrintCountTimestamp) })
        {
            if (line.Length == 0 || y >= inner.Bottom) continue;

            var font = FitFont(gfx, line, layout, layout.PrintCountFontSize, false, inner.Width, 4.0);
            double height = LineHeight(gfx, font);
            gfx.DrawString(line, font, XBrushes.Black,
                new XRect(inner.X, y, inner.Width, height), XStringFormats.TopLeft);
            y += height;
        }
    }

    /// <summary>Dòng "{mã bưu cục} in lần {n}:", bỏ gọn phần nào Owner để trống.</summary>
    private static string BuildPrintCountHeader(ReprintOverlayContent c)
    {
        var code = Clean(c.PrintCountNetworkCode);
        var times = Clean(c.PrintCountTimes);

        if (code.Length > 0 && times.Length > 0) return $"{code} in lần {times}:";
        if (code.Length > 0) return $"{code} in lần:";
        if (times.Length > 0) return $"in lần {times}:";
        return "";
    }

    /// <summary>Ghép tên + SĐT theo đúng dấu phân cách nhãn JMS dùng (" ,").</summary>
    private static string JoinNameAndPhone(string name, string phone)
    {
        if (name.Length == 0) return phone;
        if (phone.Length == 0) return name;
        return $"{name} ,{phone}";
    }

    private static void DrawLabeledValue(XGraphics gfx, XRect area, string label, string? value, ReprintLayoutOptions layout)
    {
        if (area.Width <= 1 || area.Height <= 1) return;

        var labelFont = FitFont(gfx, label, layout, layout.NotesLabelFontSize, false, area.Width, 4.5);
        double labelHeight = LineHeight(gfx, labelFont);
        gfx.DrawString(label, labelFont, XBrushes.Black,
            new XRect(area.X, area.Y, area.Width, labelHeight), XStringFormats.TopLeft);

        var text = Clean(value);
        if (text.Length == 0) return;

        double valueTop = area.Y + labelHeight + 1.0;
        if (valueTop >= area.Bottom) return;

        var valueFont = FitFont(gfx, text, layout, layout.NotesBodyFontSize, true, area.Width, 4.5);
        gfx.DrawString(text, valueFont, XBrushes.Black,
            new XRect(area.X, valueTop, area.Width, area.Bottom - valueTop), XStringFormats.TopLeft);
    }

    // ── helpers ──────────────────────────────────────────────

    private static void EnsureFontResolver()
    {
        if (_fontResolverReady) return;
        lock (FontGate)
        {
            if (_fontResolverReady) return;

            if (GlobalFontSettings.FontResolver == null)
                GlobalFontSettings.FontResolver = new WindowsFontResolver();

            // Vietnamese diacritics do not survive WinAnsi.
            GlobalFontSettings.DefaultFontEncoding = PdfFontEncoding.Unicode;
            _fontResolverReady = true;
        }
    }

    private static XRect ToRect(ReprintRegionBox box, double pageWidth, double pageHeight) =>
        new(box.X0 * pageWidth,
            box.Y0 * pageHeight,
            (box.X1 - box.X0) * pageWidth,
            (box.Y1 - box.Y0) * pageHeight);

    private static XRect Pad(XRect rect, double padding)
    {
        double pad = Math.Min(padding, Math.Min(rect.Width, rect.Height) / 4.0);
        if (pad <= 0) return rect;
        return new XRect(rect.X + pad, rect.Y + pad,
            Math.Max(0, rect.Width - 2 * pad), Math.Max(0, rect.Height - 2 * pad));
    }

    /// <summary>
    /// Kẻ lại 4 cạnh của vùng vừa che trắng. Nét được vẽ *giữa* mép mask (không thụt vào)
    /// để đè đúng chỗ đường kẻ bảng gốc bị miếng vá ăn mất — đó là thứ làm vết đè biến mất.
    /// Cạnh nào chạm mép trang thì kéo vào nửa nét để không bị xén mất một nửa.
    /// Trả về khung đã kẹp, dùng chung cho các vạch chia bên trong.
    /// </summary>
    private static XRect DrawFrame(XGraphics gfx, XPen pen, XRect rect, double lineWidth, XSize page)
    {
        // Khung ngoài của nhãn JMS nằm ở x=0.5 và x=210.5 trên trang rộng 210pt — tức mép phải
        // vốn đã nhô ra ngoài page box. Kẹp nét vào hẳn trong trang sẽ kéo cạnh phải lùi ~1pt
        // và hiện vạch đôi, nên cho phép nhô ra tối đa một bề rộng nét: phần thừa bị page box
        // cắt đúng như nhãn gốc, mà rect rác thì vẫn không thể chạy đi đâu xa.
        double slack = Math.Max(lineWidth, 0.5);
        double left = ClampTo(rect.X, -slack, page.Width + slack);
        double right = ClampTo(rect.Right, -slack, page.Width + slack);
        double top = ClampTo(rect.Y, -slack, page.Height + slack);
        double bottom = ClampTo(rect.Bottom, -slack, page.Height + slack);

        var framed = new XRect(left, top, Math.Max(0, right - left), Math.Max(0, bottom - top));
        if (framed.Width <= 0 || framed.Height <= 0) return rect;

        gfx.DrawRectangle(pen, framed);
        return framed;
    }

    private static double ClampTo(double value, double min, double max) =>
        max <= min ? value : Math.Min(Math.Max(value, min), max);

    private static XFont Font(ReprintLayoutOptions layout, double size, bool bold) =>
        new(layout.FontFamily, size, bold ? XFontStyleEx.Bold : XFontStyleEx.Regular);

    private static XFont FitFont(XGraphics gfx, string text, ReprintLayoutOptions layout,
        double size, bool bold, double maxWidth, double minSize)
    {
        var font = Font(layout, size, bold);
        if (string.IsNullOrEmpty(text) || maxWidth <= 0) return font;

        while (size > minSize && gfx.MeasureString(text, font).Width > maxWidth)
        {
            size -= 0.5;
            font = Font(layout, size, bold);
        }
        return font;
    }

    private static double LineHeight(XGraphics gfx, XFont font) => gfx.MeasureString("Ag", font).Height;

    private static string Clean(string? value) => (value ?? "").Trim();
}

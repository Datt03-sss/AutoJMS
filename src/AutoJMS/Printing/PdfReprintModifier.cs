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

    public string ReceiverName { get; set; } = "";
    public string ReceiverAddress { get; set; } = "";

    public string Route1 { get; set; } = "";
    public string Route2 { get; set; } = "";
    public string Route3 { get; set; } = "";

    public string Note { get; set; } = "";
    public string CodAmount { get; set; } = "";
    public string Deadline { get; set; } = "";
    public string WaybillNo { get; set; } = "";

    public bool HasAnyEdit => EditReceiver || EditRoute || EditNotes;
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
/// Stamps the three editable regions onto a JMS label PDF while keeping the file vector.
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

            using var gfx = XGraphics.FromPdfPage(page, XGraphicsPdfPageOptions.Append);
            DrawOverlay(gfx, content, layout);
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

    private static void DrawOverlay(XGraphics gfx, ReprintOverlayContent content, ReprintLayoutOptions layout)
    {
        var pageSize = gfx.PageSize;
        double w = pageSize.Width, h = pageSize.Height;
        if (w <= 0 || h <= 0) return;

        var pen = new XPen(XColors.Black, layout.LineWidth);

        if (content.EditReceiver)
            DrawReceiver(gfx, pen, ToRect(layout.Receiver, w, h), content, layout);

        if (content.EditRoute)
            DrawRoute(gfx, pen, ToRect(layout.Route, w, h), content, layout);

        if (content.EditNotes)
            DrawNotes(gfx, pen, ToRect(layout.Notes, w, h), content, layout);
    }

    private static void DrawReceiver(XGraphics gfx, XPen pen, XRect rect, ReprintOverlayContent c, ReprintLayoutOptions layout)
    {
        gfx.DrawRectangle(XBrushes.White, rect);
        if (layout.DrawReceiverBorder) DrawBorder(gfx, pen, rect, layout.LineWidth);

        var inner = Pad(rect, layout.Padding);
        if (inner.Width <= 1 || inner.Height <= 1) return;

        var header = "Người nhận :";
        var name = Clean(c.ReceiverName);
        if (name.Length > 0) header = header + " " + name;

        var headerFont = FitFont(gfx, header, layout, layout.ReceiverLabelFontSize, true, inner.Width, 5.0);
        double headerHeight = LineHeight(gfx, headerFont);
        gfx.DrawString(header, headerFont, XBrushes.Black,
            new XRect(inner.X, inner.Y, inner.Width, headerHeight), XStringFormats.TopLeft);

        double addressTop = inner.Y + headerHeight + 1.0;
        var address = Clean(c.ReceiverAddress);
        if (address.Length == 0 || addressTop >= inner.Bottom) return;

        var bodyFont = Font(layout, layout.ReceiverBodyFontSize, false);
        var formatter = new XTextFormatter(gfx) { Alignment = XParagraphAlignment.Left };
        formatter.DrawString(address, bodyFont, XBrushes.Black,
            new XRect(inner.X, addressTop, inner.Width, inner.Bottom - addressTop), XStringFormats.TopLeft);
    }

    private static void DrawRoute(XGraphics gfx, XPen pen, XRect rect, ReprintOverlayContent c, ReprintLayoutOptions layout)
    {
        gfx.DrawRectangle(XBrushes.White, rect);
        if (layout.DrawRouteBorder) DrawBorder(gfx, pen, rect, layout.LineWidth);

        var dividers = (layout.RouteDividers ?? Array.Empty<double>())
            .Where(d => d > 0 && d < 1)
            .Distinct()
            .OrderBy(d => d)
            .ToList();

        foreach (var d in dividers)
        {
            double y = rect.Y + d * rect.Height;
            gfx.DrawLine(pen, rect.X, y, rect.Right, y);
        }

        // N dividers produce N+1 cells, numbered top-down from 0.
        var bounds = new List<double> { 0.0 };
        bounds.AddRange(dividers);
        bounds.Add(1.0);

        var codes = new[] { c.Route1, c.Route2, c.Route3 };
        var cellIndexes = layout.RouteCellIndexes ?? Array.Empty<int>();

        for (int i = 0; i < codes.Length && i < cellIndexes.Length; i++)
        {
            var code = Clean(codes[i]);
            if (code.Length == 0) continue;

            int cell = cellIndexes[i];
            if (cell < 0 || cell >= bounds.Count - 1) continue;

            var cellRect = new XRect(
                rect.X,
                rect.Y + bounds[cell] * rect.Height,
                rect.Width,
                (bounds[cell + 1] - bounds[cell]) * rect.Height);

            var padded = Pad(cellRect, layout.Padding);
            if (padded.Width <= 1 || padded.Height <= 1) continue;

            var font = FitFont(gfx, code, layout, layout.RouteFontSize, true, padded.Width, 5.0);
            gfx.DrawString(code, font, XBrushes.Black, padded, XStringFormats.Center);
        }
    }

    private static void DrawNotes(XGraphics gfx, XPen pen, XRect rect, ReprintOverlayContent c, ReprintLayoutOptions layout)
    {
        gfx.DrawRectangle(XBrushes.White, rect);
        if (layout.DrawNotesBorder) DrawBorder(gfx, pen, rect, layout.LineWidth);

        double splitX = rect.X + layout.NotesColumnSplit * rect.Width;
        gfx.DrawLine(pen, splitX, rect.Y, splitX, rect.Bottom);

        double splitY = rect.Y + layout.NotesRightRowSplit * rect.Height;
        gfx.DrawLine(pen, splitX, splitY, rect.Right, splitY);

        // Left column: "Ghi chú:" + content, waybill pinned to the bottom.
        var left = Pad(new XRect(rect.X, rect.Y, splitX - rect.X, rect.Height), layout.Padding);
        if (left.Width > 1 && left.Height > 1)
        {
            var labelFont = Font(layout, layout.NotesLabelFontSize, true);
            double labelHeight = LineHeight(gfx, labelFont);
            gfx.DrawString("Ghi chú:", labelFont, XBrushes.Black,
                new XRect(left.X, left.Y, left.Width, labelHeight), XStringFormats.TopLeft);

            var waybill = Clean(c.WaybillNo);
            double waybillHeight = 0;
            if (waybill.Length > 0)
            {
                var waybillFont = FitFont(gfx, waybill, layout, layout.WaybillFontSize, true, left.Width, 5.0);
                waybillHeight = LineHeight(gfx, waybillFont);
                gfx.DrawString(waybill, waybillFont, XBrushes.Black,
                    new XRect(left.X, left.Bottom - waybillHeight, left.Width, waybillHeight), XStringFormats.TopLeft);
            }

            double noteTop = left.Y + labelHeight + 1.0;
            double noteBottom = left.Bottom - waybillHeight;
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

    private static void DrawBorder(XGraphics gfx, XPen pen, XRect rect, double lineWidth)
    {
        // Inset by half a stroke so a border sitting on the page edge is not clipped in half.
        double half = lineWidth / 2.0;
        double width = rect.Width - lineWidth;
        double height = rect.Height - lineWidth;
        if (width <= 0 || height <= 0) return;

        gfx.DrawRectangle(pen, new XRect(rect.X + half, rect.Y + half, width, height));
    }

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

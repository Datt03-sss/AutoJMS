#nullable enable
using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AutoJMS;

/// <summary>
/// One rectangle on the label, expressed as fractions of the page width/height so the
/// same numbers work for any label size the JMS gateway returns.
/// Origin is the top-left corner of the page (PDFsharp's default XGraphics origin).
/// </summary>
public sealed class ReprintRegionBox
{
    public double X0 { get; set; }
    public double Y0 { get; set; }
    public double X1 { get; set; }
    public double Y1 { get; set; }

    public ReprintRegionBox() { }

    public ReprintRegionBox(double x0, double y0, double x1, double y1)
    {
        X0 = x0; Y0 = y0; X1 = x1; Y1 = y1;
    }

    [JsonIgnore]
    public bool IsUsable => X1 > X0 && Y1 > Y0
                            && X0 >= 0 && Y0 >= 0
                            && X1 <= 1.0001 && Y1 <= 1.0001;
}

/// <summary>
/// Geometry + typography for the "In lại đơn" overlay.
///
/// The defaults were derived from a 701x756 px sample label, so they are an estimate.
/// Rather than force a rebuild for every nudge, an optional JSON file overrides them:
///
///   {InstallRoot}\AppData\modules\reprint-layout.json   (preferred — survives updates)
///   {InstallDir}\modules\reprint-layout.json            (fallback — shipped default)
///
/// A template with the built-in values is written to the first path on first use.
/// </summary>
public sealed class ReprintLayoutOptions
{
    public const string FileName = "reprint-layout.json";

    // ── Region 1: Người nhận & Địa chỉ (khung đỏ) ──
    public ReprintRegionBox Receiver { get; set; } = new(0.00, 0.340, 0.68, 0.648);

    // ── Region 2: Mã tuyến, 3 ô (khung xanh dương) ──
    public ReprintRegionBox Route { get; set; } = new(0.68, 0.163, 1.00, 0.606);

    // ── Region 3: Ghi chú & COD (khung xanh lá) ──
    public ReprintRegionBox Notes { get; set; } = new(0.00, 0.774, 0.68, 1.00);

    /// <summary>Horizontal dividers inside the route region, as a fraction of its height.</summary>
    public double[] RouteDividers { get; set; } = { 0.25, 0.50, 0.78 };

    /// <summary>
    /// Which of the cells produced by <see cref="RouteDividers"/> receive the 3 route codes.
    /// N dividers make N+1 cells, numbered top-down from 0.
    /// </summary>
    public int[] RouteCellIndexes { get; set; } = { 0, 1, 2 };

    /// <summary>Redraw the region outline after the whiteout (the mask erases the label's own box).</summary>
    public bool DrawReceiverBorder { get; set; } = false;
    public bool DrawRouteBorder { get; set; } = true;
    public bool DrawNotesBorder { get; set; } = true;

    /// <summary>Vertical divider inside the notes region, as a fraction of its width.</summary>
    public double NotesColumnSplit { get; set; } = 0.45;

    /// <summary>Horizontal divider in the notes region's right column, as a fraction of its height.</summary>
    public double NotesRightRowSplit { get; set; } = 0.50;

    /// <summary>Divider stroke width in points.</summary>
    public double LineWidth { get; set; } = 0.9;

    /// <summary>Inner padding (points) kept clear of every region edge when drawing text.</summary>
    public double Padding { get; set; } = 3.0;

    public string FontFamily { get; set; } = "Arial";
    public double ReceiverLabelFontSize { get; set; } = 9.0;
    public double ReceiverBodyFontSize { get; set; } = 8.5;
    public double RouteFontSize { get; set; } = 15.0;
    public double NotesLabelFontSize { get; set; } = 7.5;
    public double NotesBodyFontSize { get; set; } = 8.0;
    public double WaybillFontSize { get; set; } = 8.5;

    // ── loading ──────────────────────────────────────────────

    private static readonly object Gate = new();
    private static ReprintLayoutOptions? _cached;

    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true
    };

    /// <summary>Preferred override path — writable and preserved across Velopack updates.</summary>
    public static string UserOverridePath =>
        Path.Combine(AppPaths.ModulesCacheDir, "modules", FileName);

    /// <summary>Shipped fallback, read only when the user override is absent.</summary>
    public static string InstallOverridePath =>
        Path.Combine(AppPaths.InstallDir, "modules", FileName);

    /// <summary>Loads (and caches) the layout, applying a JSON override when one exists.</summary>
    public static ReprintLayoutOptions Load()
    {
        lock (Gate)
        {
            if (_cached != null) return _cached;

            var options = ReadOverride(UserOverridePath) ?? ReadOverride(InstallOverridePath);
            if (options == null)
            {
                options = new ReprintLayoutOptions();
                WriteTemplateIfMissing(options);
            }

            options.Normalize();
            _cached = options;
            return options;
        }
    }

    /// <summary>Drops the cache so the next <see cref="Load"/> re-reads the JSON from disk.</summary>
    public static void Invalidate()
    {
        lock (Gate) _cached = null;
    }

    private static ReprintLayoutOptions? ReadOverride(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;
            var json = File.ReadAllText(path);
            if (string.IsNullOrWhiteSpace(json)) return null;

            var parsed = JsonSerializer.Deserialize<ReprintLayoutOptions>(json, ReadOptions);
            if (parsed == null) return null;

            AppLogger.Info($"Reprint layout: dùng override {path}");
            return parsed;
        }
        catch (Exception ex)
        {
            AppLogger.Warning($"Reprint layout: không đọc được {path}: {ex.Message}");
            return null;
        }
    }

    private static void WriteTemplateIfMissing(ReprintLayoutOptions defaults)
    {
        try
        {
            var path = UserOverridePath;
            if (File.Exists(path)) return;

            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            File.WriteAllText(path, JsonSerializer.Serialize(defaults, WriteOptions));
            AppLogger.Info($"Reprint layout: đã tạo mẫu chỉnh toạ độ tại {path}");
        }
        catch (Exception ex)
        {
            AppLogger.Warning($"Reprint layout: không ghi được file mẫu: {ex.Message}");
        }
    }

    /// <summary>Replaces anything nonsensical in an override file with the built-in default.</summary>
    private void Normalize()
    {
        var fallback = new ReprintLayoutOptions();

        if (Receiver == null || !Receiver.IsUsable) Receiver = fallback.Receiver;
        if (Route == null || !Route.IsUsable) Route = fallback.Route;
        if (Notes == null || !Notes.IsUsable) Notes = fallback.Notes;

        if (RouteDividers == null || RouteDividers.Length == 0)
            RouteDividers = fallback.RouteDividers;

        if (RouteCellIndexes == null || RouteCellIndexes.Length == 0)
            RouteCellIndexes = fallback.RouteCellIndexes;

        NotesColumnSplit = Clamp01(NotesColumnSplit, fallback.NotesColumnSplit);
        NotesRightRowSplit = Clamp01(NotesRightRowSplit, fallback.NotesRightRowSplit);

        if (LineWidth <= 0 || LineWidth > 10) LineWidth = fallback.LineWidth;
        if (Padding < 0 || Padding > 40) Padding = fallback.Padding;
        if (string.IsNullOrWhiteSpace(FontFamily)) FontFamily = fallback.FontFamily;

        ReceiverLabelFontSize = ClampFont(ReceiverLabelFontSize, fallback.ReceiverLabelFontSize);
        ReceiverBodyFontSize = ClampFont(ReceiverBodyFontSize, fallback.ReceiverBodyFontSize);
        RouteFontSize = ClampFont(RouteFontSize, fallback.RouteFontSize);
        NotesLabelFontSize = ClampFont(NotesLabelFontSize, fallback.NotesLabelFontSize);
        NotesBodyFontSize = ClampFont(NotesBodyFontSize, fallback.NotesBodyFontSize);
        WaybillFontSize = ClampFont(WaybillFontSize, fallback.WaybillFontSize);
    }

    private static double Clamp01(double value, double fallback) =>
        value > 0 && value < 1 ? value : fallback;

    private static double ClampFont(double value, double fallback) =>
        value >= 3 && value <= 72 ? value : fallback;
}

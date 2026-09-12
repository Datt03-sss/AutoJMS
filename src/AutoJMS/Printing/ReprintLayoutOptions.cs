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

    /// <summary>
    /// Bumped whenever the built-in geometry changes. A template on disk carrying an older
    /// version is archived and regenerated, otherwise stale coordinates would shadow the fix.
    /// </summary>
    public const int CurrentVersion = 4;

    public int Version { get; set; } = CurrentVersion;

    /// <summary>
    /// Ghim mép vùng đè vào đúng đường kẻ thật đọc được từ PDF (<see cref="PdfLabelGrid"/>).
    /// Tỷ lệ bên dưới dù đo kỹ tới đâu vẫn lệch vài point trên khổ nhãn khác; bật cái này thì
    /// miếng vá luôn trùng khít khung bảng. Tắt đi là quay về dùng nguyên tỷ lệ.
    /// </summary>
    public bool SnapToGrid { get; set; } = true;

    /// <summary>
    /// Bán kính tìm đường kẻ để ghim, tính bằng point. Ghim luôn chọn đường kẻ GẦN NHẤT, nên
    /// bán kính rộng chỉ cứu được nhiều tỷ lệ lệch hơn chứ không kéo mép sang nhầm đường kẻ
    /// khác. 10pt đủ phủ cả trường hợp mép vùng đè lệch hẳn một dòng so với nhãn thật.
    /// </summary>
    public double SnapTolerance { get; set; } = 10.0;

    /// <summary>
    /// The single vertical rule that separates the left content column from the narrow right
    /// column, as a fraction of page width. <see cref="Receiver"/>.X1, <see cref="Route"/>.X0
    /// and <see cref="Notes"/>.X1 are all snapped onto it in <see cref="Normalize"/>, so the
    /// three masks share one continuous line with no gap and no double stroke.
    ///
    /// Đo từ nhãn thật 210x227pt: vạch nằm ở x = 146.5pt.
    /// </summary>
    public double SplitColumnX { get; set; } = 0.6976;

    // ── Region 1: Người nhận & Địa chỉ (khung đỏ) ──
    // Từ đường kẻ dưới "Người gửi" (y=79.5pt) xuống đường kẻ dưới khối người nhận (y=138.5pt).
    // KHÔNG lấn xuống 147.5pt — giữa hai vạch đó là dòng phường/xã của nhãn gốc.
    public ReprintRegionBox Receiver { get; set; } = new(0.00, 0.3502, 0.6976, 0.6101);

    // ── Region 2: Mã tuyến, 4 ô (khung xanh dương) ──
    // Từ đường kẻ dưới barcode 1D (y=42.5pt) xuống đường kẻ trên ô "Trọng lượng tính" (y=135.5pt).
    public ReprintRegionBox Route { get; set; } = new(0.6976, 0.1872, 1.00, 0.5969);

    // ── Region 3: Ghi chú & COD (khung xanh lá) ──
    // Từ đường kẻ dưới "Nội dung hàng hoá" (y=177.5pt) xuống đáy nhãn.
    public ReprintRegionBox Notes { get; set; } = new(0.00, 0.7819, 0.6976, 1.00);

    // ── Region 4: dòng đếm lần in ("214A03 in lần 11: 22:17 12-09-2026") ──
    // Cột phải, nằm giữa ô "Trọng lượng tính" (đáy y=135.5pt) và ô "Nội dung hàng hoá"
    // (đỉnh y=177.5pt). Dải 42pt đó chứa đúng hai ô hai dòng nên vạch ngăn ở y=156.5pt.
    public ReprintRegionBox PrintCount { get; set; } = new(0.6976, 0.6894, 1.00, 0.7819);

    /// <summary>
    /// Horizontal dividers inside the route region, as a fraction of its height.
    /// Đo từ nhãn thật: y = 65.5 / 89.5 / 112.5pt trong dải 42.5–135.5pt.
    /// </summary>
    public double[] RouteDividers { get; set; } = { 0.2472, 0.5055, 0.7527 };

    /// <summary>
    /// Which of the cells produced by <see cref="RouteDividers"/> receive the 3 route codes.
    /// N dividers make N+1 cells, numbered top-down from 0.
    /// </summary>
    public int[] RouteCellIndexes { get; set; } = { 0, 1, 2 };

    /// <summary>
    /// Redraw the region outline after the whiteout. The mask eats the label's own table rules,
    /// so without this the patch is obvious; the frame is stroked centred on the mask edge to
    /// land exactly where the original rule was.
    /// </summary>
    public bool DrawReceiverBorder { get; set; } = true;
    public bool DrawRouteBorder { get; set; } = true;
    public bool DrawNotesBorder { get; set; } = true;
    public bool DrawPrintCountBorder { get; set; } = true;

    /// <summary>Vertical divider inside the notes region, as a fraction of its width (x = 65.5pt).</summary>
    public double NotesColumnSplit { get; set; } = 0.4471;

    /// <summary>Horizontal divider in the notes region's right column, as a fraction of its height (y = 199.5pt).</summary>
    public double NotesRightRowSplit { get; set; } = 0.4490;

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

    /// <summary>Ô đếm lần in chỉ cao ~21pt mà phải chứa hai dòng, nên chữ nhỏ hơn hẳn.</summary>
    public double PrintCountFontSize { get; set; } = 7.0;

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

            ArchiveStaleTemplate(UserOverridePath);

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

    /// <summary>
    /// A template generated by an older build carries the old coordinates and would silently
    /// shadow every default below. Park it next to the original and let the loader regenerate.
    /// </summary>
    private static void ArchiveStaleTemplate(string path)
    {
        try
        {
            if (!File.Exists(path)) return;

            int version;
            using (var doc = JsonDocument.Parse(File.ReadAllText(path)))
            {
                version = doc.RootElement.TryGetProperty("Version", out var v) && v.TryGetInt32(out int parsed)
                    ? parsed
                    : 1;
            }
            if (version >= CurrentVersion) return;

            var backup = $"{path}.v{version}.bak";
            File.Copy(path, backup, overwrite: true);
            File.Delete(path);
            AppLogger.Info($"Reprint layout: template v{version} đã cũ, lưu tại {backup} và tạo lại theo mặc định mới.");
        }
        catch (Exception ex)
        {
            AppLogger.Warning($"Reprint layout: không thay được template cũ {path}: {ex.Message}");
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
        if (PrintCount == null || !PrintCount.IsUsable) PrintCount = fallback.PrintCount;

        // One vertical rule for all four masks: no hairline gap, no double-stroked border.
        SplitColumnX = Clamp01(SplitColumnX, fallback.SplitColumnX);
        if (SplitColumnX > Receiver.X0 && SplitColumnX < Route.X1)
        {
            Receiver.X1 = SplitColumnX;
            Route.X0 = SplitColumnX;
            PrintCount.X0 = SplitColumnX;
            if (SplitColumnX > Notes.X0) Notes.X1 = SplitColumnX;
        }
        else
        {
            SplitColumnX = fallback.SplitColumnX;
            Receiver.X1 = SplitColumnX;
            Route.X0 = SplitColumnX;
            PrintCount.X0 = SplitColumnX;
            Notes.X1 = SplitColumnX;
        }

        if (RouteDividers == null || RouteDividers.Length == 0)
            RouteDividers = fallback.RouteDividers;

        if (RouteCellIndexes == null || RouteCellIndexes.Length == 0)
            RouteCellIndexes = fallback.RouteCellIndexes;

        NotesColumnSplit = Clamp01(NotesColumnSplit, fallback.NotesColumnSplit);
        NotesRightRowSplit = Clamp01(NotesRightRowSplit, fallback.NotesRightRowSplit);

        if (LineWidth <= 0 || LineWidth > 10) LineWidth = fallback.LineWidth;
        if (Padding < 0 || Padding > 40) Padding = fallback.Padding;
        if (SnapTolerance < 0 || SnapTolerance > 30) SnapTolerance = fallback.SnapTolerance;
        if (string.IsNullOrWhiteSpace(FontFamily)) FontFamily = fallback.FontFamily;

        ReceiverLabelFontSize = ClampFont(ReceiverLabelFontSize, fallback.ReceiverLabelFontSize);
        ReceiverBodyFontSize = ClampFont(ReceiverBodyFontSize, fallback.ReceiverBodyFontSize);
        RouteFontSize = ClampFont(RouteFontSize, fallback.RouteFontSize);
        NotesLabelFontSize = ClampFont(NotesLabelFontSize, fallback.NotesLabelFontSize);
        NotesBodyFontSize = ClampFont(NotesBodyFontSize, fallback.NotesBodyFontSize);
        WaybillFontSize = ClampFont(WaybillFontSize, fallback.WaybillFontSize);
        PrintCountFontSize = ClampFont(PrintCountFontSize, fallback.PrintCountFontSize);
    }

    private static double Clamp01(double value, double fallback) =>
        value > 0 && value < 1 ? value : fallback;

    private static double ClampFont(double value, double fallback) =>
        value >= 3 && value <= 72 ? value : fallback;
}

#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using PdfSharp.Pdf;
using PdfSharp.Pdf.Content;
using PdfSharp.Pdf.Content.Objects;

namespace AutoJMS;

/// <summary>
/// Một đường kẻ của bảng trên nhãn, đã gộp các đoạn thẳng hàng lại thành một.
/// </summary>
public sealed class PdfGridRule
{
    /// <summary>X (kẻ dọc) hoặc Y tính từ mép trên (kẻ ngang), đơn vị point.</summary>
    public double Position { get; set; }

    /// <summary>Đầu và cuối của đường kẻ theo trục còn lại.</summary>
    public double Start { get; set; }
    public double End { get; set; }

    /// <summary>Tổng chiều dài thực sự được vẽ — dùng để loại các đoạn vụn.</summary>
    public double Length { get; set; }

    /// <summary>Đường kẻ này có chạy hết đoạn [<paramref name="from"/>, <paramref name="to"/>] không.</summary>
    public bool Covers(double from, double to, double slack = 2.0) =>
        Start <= from + slack && End >= to - slack;
}

/// <summary>
/// Lưới kẻ bảng của nhãn, đọc thẳng từ content stream của PDF.
///
/// Toạ độ vùng đè trong <see cref="ReprintLayoutOptions"/> là tỷ lệ ước lượng, nên miếng vá
/// trắng gần như luôn lệch vài point so với đường kẻ thật: lệch ít thì hiện vạch đôi, lệch
/// nhiều thì nuốt luôn một dòng của nhãn gốc. Đọc được vị trí đường kẻ thật thì chỉ việc
/// ghim mép vùng đè vào đúng đó — hết lệch, không phải chỉnh tay theo từng khổ nhãn.
///
/// Trục Y đã đổi sang gốc mép trên cho khớp với <c>XGraphics</c>.
/// </summary>
public sealed class PdfLabelGrid
{
    public static readonly PdfLabelGrid Empty = new(new List<PdfGridRule>(), new List<PdfGridRule>());

    /// <summary>Hai đoạn cách nhau dưới ngần này point được coi là cùng một đường kẻ.</summary>
    private const double ClusterEpsilon = 0.8;

    /// <summary>Đoạn phải dài tối thiểu bằng ngần này so với cạnh trang mới được tính là đường kẻ bảng.</summary>
    private const double MinRuleRatio = 0.15;

    private PdfLabelGrid(IReadOnlyList<PdfGridRule> vertical, IReadOnlyList<PdfGridRule> horizontal)
    {
        Vertical = vertical;
        Horizontal = horizontal;
    }

    /// <summary>Các đường kẻ dọc, sắp theo X tăng dần.</summary>
    public IReadOnlyList<PdfGridRule> Vertical { get; }

    /// <summary>Các đường kẻ ngang, sắp theo Y (gốc mép trên) tăng dần.</summary>
    public IReadOnlyList<PdfGridRule> Horizontal { get; }

    /// <summary>Đủ đường kẻ để tin được — nhãn JMS luôn có khung ngoài + vạch chia.</summary>
    public bool IsUsable => Vertical.Count >= 2 && Horizontal.Count >= 3;

    /// <summary>
    /// Quét content stream lấy mọi đoạn thẳng nằm ngang/dọc. Không bao giờ ném:
    /// đọc hỏng thì trả <see cref="Empty"/> và bên gọi dùng lại toạ độ tỷ lệ như cũ.
    /// </summary>
    public static PdfLabelGrid Detect(PdfPage page, double pageWidth, double pageHeight)
    {
        if (page == null || pageWidth <= 0 || pageHeight <= 0) return Empty;

        // Trang xoay thì trục X/Y của content không còn khớp với trục của XGraphics.
        if (page.Rotate % 360 != 0) return Empty;

        try
        {
            var vertical = new List<Segment>();
            var horizontal = new List<Segment>();
            Walk(ContentReader.ReadContent(page), Matrix.Identity, new Stack<Matrix>(),
                new PathState(), vertical, horizontal, pageHeight);

            var grid = new PdfLabelGrid(
                Cluster(vertical, pageHeight * MinRuleRatio),
                Cluster(horizontal, pageWidth * MinRuleRatio));

            AppLogger.Info($"In lại đơn: đọc được {grid.Vertical.Count} vạch dọc / {grid.Horizontal.Count} vạch ngang từ nhãn gốc.");
            return grid;
        }
        catch (Exception ex)
        {
            AppLogger.Warning($"In lại đơn: không đọc được lưới kẻ của nhãn ({ex.Message}) — dùng toạ độ tỷ lệ.");
            return Empty;
        }
    }

    /// <summary>Kéo <paramref name="x"/> về đường kẻ dọc gần nhất trong bán kính <paramref name="tolerance"/>.</summary>
    public double SnapX(double x, double tolerance) => Snap(Vertical, x, tolerance);

    /// <summary>Kéo <paramref name="y"/> về đường kẻ ngang gần nhất trong bán kính <paramref name="tolerance"/>.</summary>
    public double SnapY(double y, double tolerance) => Snap(Horizontal, y, tolerance);

    /// <summary>Các vạch ngang nằm hẳn bên trong dải Y và chạy suốt bề ngang [x0, x1].</summary>
    public IReadOnlyList<double> HorizontalInside(double top, double bottom, double x0, double x1) =>
        Inside(Horizontal, top, bottom, x0, x1);

    /// <summary>Các vạch dọc nằm hẳn bên trong dải X và chạy suốt bề cao [y0, y1].</summary>
    public IReadOnlyList<double> VerticalInside(double left, double right, double y0, double y1) =>
        Inside(Vertical, left, right, y0, y1);

    private static IReadOnlyList<double> Inside(
        IReadOnlyList<PdfGridRule> rules, double from, double to, double spanFrom, double spanTo)
    {
        const double margin = 2.0;
        return rules
            .Where(r => r.Position > from + margin && r.Position < to - margin && r.Covers(spanFrom, spanTo))
            .Select(r => r.Position)
            .ToList();
    }

    private static double Snap(IReadOnlyList<PdfGridRule> rules, double value, double tolerance)
    {
        if (rules.Count == 0 || tolerance <= 0) return value;

        double best = value;
        double bestDistance = tolerance;
        double bestLength = 0;

        foreach (var rule in rules)
        {
            double distance = Math.Abs(rule.Position - value);
            if (distance > tolerance) continue;

            // Cùng khoảng cách thì ưu tiên đường kẻ dài hơn — nó chắc chắn là kẻ bảng.
            if (distance < bestDistance - 0.01 || (distance <= bestDistance + 0.01 && rule.Length > bestLength))
            {
                best = rule.Position;
                bestDistance = distance;
                bestLength = rule.Length;
            }
        }

        return best;
    }

    // ── quét content stream ──────────────────────────────────

    private readonly struct Segment
    {
        public Segment(double position, double start, double end)
        {
            Position = position;
            Start = Math.Min(start, end);
            End = Math.Max(start, end);
        }

        public double Position { get; }
        public double Start { get; }
        public double End { get; }
        public double Length => End - Start;
    }

    private sealed class PathState
    {
        public double CurrentX, CurrentY, StartX, StartY;
        public bool HasCurrent;
    }

    private readonly struct Matrix
    {
        public static readonly Matrix Identity = new(1, 0, 0, 1, 0, 0);

        private readonly double _a, _b, _c, _d, _e, _f;

        public Matrix(double a, double b, double c, double d, double e, double f)
        {
            _a = a; _b = b; _c = c; _d = d; _e = e; _f = f;
        }

        /// <summary>this × other (this áp trước, rồi tới other).</summary>
        public Matrix Concat(Matrix o) => new(
            _a * o._a + _b * o._c,
            _a * o._b + _b * o._d,
            _c * o._a + _d * o._c,
            _c * o._b + _d * o._d,
            _e * o._a + _f * o._c + o._e,
            _e * o._b + _f * o._d + o._f);

        public void Apply(double x, double y, out double outX, out double outY)
        {
            outX = _a * x + _c * y + _e;
            outY = _b * x + _d * y + _f;
        }
    }

    private static void Walk(CSequence sequence, Matrix ctm, Stack<Matrix> stack, PathState path,
        List<Segment> vertical, List<Segment> horizontal, double pageHeight)
    {
        foreach (var obj in sequence)
        {
            if (obj is CSequence nested)
            {
                Walk(nested, ctm, stack, path, vertical, horizontal, pageHeight);
                continue;
            }

            if (obj is not COperator op) continue;

            var operands = op.Operands;
            switch (op.OpCode.OpCodeName)
            {
                case OpCodeName.q:
                    stack.Push(ctm);
                    break;

                case OpCodeName.Q:
                    if (stack.Count > 0) ctm = stack.Pop();
                    break;

                case OpCodeName.cm:
                    if (TryNumbers(operands, 6, out var m))
                        ctm = new Matrix(m[0], m[1], m[2], m[3], m[4], m[5]).Concat(ctm);
                    break;

                case OpCodeName.re:
                    if (TryNumbers(operands, 4, out var r))
                        AddRectangle(ctm, r[0], r[1], r[2], r[3], vertical, horizontal, pageHeight);
                    break;

                case OpCodeName.m:
                    if (TryNumbers(operands, 2, out var mv))
                    {
                        ctm.Apply(mv[0], mv[1], out double mx, out double my);
                        path.CurrentX = mx; path.CurrentY = my;
                        path.StartX = mx; path.StartY = my;
                        path.HasCurrent = true;
                    }
                    break;

                case OpCodeName.l:
                    if (TryNumbers(operands, 2, out var lv) && path.HasCurrent)
                    {
                        ctm.Apply(lv[0], lv[1], out double lx, out double ly);
                        AddSegment(path.CurrentX, path.CurrentY, lx, ly, vertical, horizontal, pageHeight);
                        path.CurrentX = lx; path.CurrentY = ly;
                    }
                    break;

                case OpCodeName.h:
                    if (path.HasCurrent)
                    {
                        AddSegment(path.CurrentX, path.CurrentY, path.StartX, path.StartY, vertical, horizontal, pageHeight);
                        path.CurrentX = path.StartX; path.CurrentY = path.StartY;
                    }
                    break;
            }
        }
    }

    private static void AddRectangle(Matrix ctm, double x, double y, double w, double h,
        List<Segment> vertical, List<Segment> horizontal, double pageHeight)
    {
        ctm.Apply(x, y, out double x0, out double y0);
        ctm.Apply(x + w, y, out double x1, out double y1);
        ctm.Apply(x + w, y + h, out double x2, out double y2);
        ctm.Apply(x, y + h, out double x3, out double y3);

        AddSegment(x0, y0, x1, y1, vertical, horizontal, pageHeight);
        AddSegment(x1, y1, x2, y2, vertical, horizontal, pageHeight);
        AddSegment(x2, y2, x3, y3, vertical, horizontal, pageHeight);
        AddSegment(x3, y3, x0, y0, vertical, horizontal, pageHeight);
    }

    private static void AddSegment(double x0, double y0, double x1, double y1,
        List<Segment> vertical, List<Segment> horizontal, double pageHeight)
    {
        const double straight = 0.35;   // lệch dưới ngần này vẫn coi là thẳng
        const double minLength = 2.0;   // ngắn hơn thì chắc chắn không phải kẻ bảng

        if (Math.Abs(x0 - x1) < straight && Math.Abs(y0 - y1) >= minLength)
        {
            vertical.Add(new Segment(x0, pageHeight - y0, pageHeight - y1));
        }
        else if (Math.Abs(y0 - y1) < straight && Math.Abs(x0 - x1) >= minLength)
        {
            horizontal.Add(new Segment(pageHeight - y0, x0, x1));
        }
    }

    /// <summary>Gộp các đoạn cùng toạ độ thành một đường kẻ, rồi bỏ những đường quá ngắn.</summary>
    private static List<PdfGridRule> Cluster(List<Segment> segments, double minLength)
    {
        var rules = new List<PdfGridRule>();

        foreach (var segment in segments.OrderBy(s => s.Position))
        {
            var last = rules.Count > 0 ? rules[^1] : null;
            if (last != null && segment.Position - last.Position <= ClusterEpsilon)
            {
                last.Start = Math.Min(last.Start, segment.Start);
                last.End = Math.Max(last.End, segment.End);
                last.Length += segment.Length;
                continue;
            }

            rules.Add(new PdfGridRule
            {
                Position = segment.Position,
                Start = segment.Start,
                End = segment.End,
                Length = segment.Length
            });
        }

        return rules.Where(r => r.Length >= minLength).ToList();
    }

    private static bool TryNumbers(CSequence operands, int count, out double[] values)
    {
        values = Array.Empty<double>();
        if (operands == null || operands.Count < count) return false;

        var result = new double[count];
        int offset = operands.Count - count;
        for (int i = 0; i < count; i++)
        {
            if (!TryNumber(operands[offset + i], out result[i])) return false;
        }

        values = result;
        return true;
    }

    private static bool TryNumber(CObject obj, out double value)
    {
        switch (obj)
        {
            case CInteger integer:
                value = integer.Value;
                return true;
            case CReal real:
                value = real.Value;
                return true;
            default:
                value = 0;
                return false;
        }
    }
}

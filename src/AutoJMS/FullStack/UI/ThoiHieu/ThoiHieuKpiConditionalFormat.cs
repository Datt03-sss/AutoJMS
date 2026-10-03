using unvell.ReoGrid;
using unvell.ReoGrid.CellTypes;
using unvell.ReoGrid.Rendering;
using RGRect = unvell.ReoGrid.Graphics.Rectangle;
using SolidColor = unvell.ReoGrid.Graphics.SolidColor;

namespace AutoJMS.FullStack.UI.ThoiHieu
{
    /// <summary>
    /// Phần định dạng của sheet "Tổng" mà ReoGrid không nạp từ xlsx: thang 3 màu K3:AA, data bar J3:J
    /// và định dạng kế toán (ReoGrid nạp thành số thường). Chỉ đọc giá trị đang có trong ô, nên đổ dữ
    /// liệu thật xong gọi lại <see cref="Apply"/> là đủ. Mọi quy tắc biên đã đối chiếu với Excel bằng COM.
    /// </summary>
    internal static class ThoiHieuKpiConditionalFormat
    {
        // 0-based: hàng 3 của Excel = 2; cột I = 8, J = 9, K = 10, AA = 26, AC = 28.
        private const int FirstBodyRow = 2;
        private const int ColI = 8, ColJ = 9, ColK = 10, ColAA = 26, ColAC = 28;

        private static readonly Color ScaleMin = Color.FromArgb(0xF8, 0x69, 0x6B);
        private static readonly Color ScaleMid = Color.FromArgb(0xFC, 0xFC, 0xFF);
        private static readonly Color ScaleMax = Color.FromArgb(0x5A, 0x8A, 0xC6);
        private static readonly SolidColor BarStart = new SolidColor(0x63, 0xC3, 0x84);
        private static readonly SolidColor BarEnd = new SolidColor(0xF3, 0xFA, 0xF5);

        /// <summary>Hằng trong ô là decimal, kết quả công thức là double; mọi thứ khác không phải số.</summary>
        public static double? Num(object value) => value switch
        {
            double d => d,
            decimal m => (double)m,
            int i => i,
            long l => l,
            float f => f,
            _ => null
        };

        /// <summary>
        /// Thang 3 màu như Excel: mốc giữa là PERCENTILE.INC 50; xét max trước rồi tới min (nên mọi giá trị
        /// bằng nhau ra màu max); kênh màu cắt phần lẻ. Ô null ra <see cref="Color.Empty"/> (không tô).
        /// </summary>
        public static Color[] ColorScale3(IReadOnlyList<double?> values)
        {
            var result = new Color[values.Count];
            var sorted = values.Where(v => v.HasValue).Select(v => v.Value).OrderBy(v => v).ToArray();
            if (sorted.Length == 0) return result;

            double min = sorted[0], max = sorted[^1];
            double pos = (sorted.Length - 1) * 0.5;
            int lo = (int)pos;
            double mid = sorted[lo] + (pos - lo) * (sorted[Math.Min(lo + 1, sorted.Length - 1)] - sorted[lo]);

            for (int i = 0; i < values.Count; i++)
            {
                if (values[i] is not double v) continue;
                result[i] = v >= max ? ScaleMax
                    : v <= min ? ScaleMin
                    : v <= mid ? Lerp(ScaleMin, ScaleMid, (v - min) / (mid - min))
                    : Lerp(ScaleMid, ScaleMax, (v - mid) / (max - mid));
            }
            return result;
        }

        /// <summary>Độ dài thanh so với độ dài tối đa, như Excel: kẹp [0, 1]; min == max thì nửa ô.</summary>
        public static double DataBarFraction(double value, double min, double max)
            => max == min ? 0.5 : Math.Clamp((value - min) / (max - min), 0, 1);

        /// <summary>Hàng (0-based) có chữ "Tổng" ở cột A; -1 nếu không có.</summary>
        public static int FindTotalRow(Worksheet sheet)
        {
            for (int r = FirstBodyRow; r <= sheet.UsedRange.EndRow; r++)
                if ((sheet.Cells[r, 0].DisplayText ?? "").Trim() == "Tổng") return r;
            return -1;
        }

        public static void Apply(Worksheet sheet)
        {
            int total = FindTotalRow(sheet);
            if (total < 0) return;

            // Thang màu K3:AA(Tổng-1): một luật cho cả vùng nên min/giữa/max tính trên mọi ô cùng lúc.
            // Vùng này KHÔNG gồm dòng Tổng.
            var cells = new List<(int Row, int Col)>();
            var values = new List<double?>();
            for (int r = FirstBodyRow; r < total; r++)
                for (int c = ColK; c <= ColAA; c++)
                {
                    cells.Add((r, c));
                    values.Add(Num(sheet.GetCellData(r, c)));
                }
            var colors = ColorScale3(values);
            for (int i = 0; i < cells.Count; i++)
            {
                if (colors[i].IsEmpty) continue;
                sheet.SetRangeStyles(new RangePosition(cells[i].Row, cells[i].Col, 1, 1), new WorksheetRangeStyle
                {
                    Flag = PlainStyleFlag.BackColor,
                    BackColor = new SolidColor(colors[i].R, colors[i].G, colors[i].B)
                });
            }

            // Data bar J3:J(Tổng): vùng này GỒM cả dòng Tổng, như file gốc.
            var bars = new List<(int Row, double Value)>();
            for (int r = FirstBodyRow; r <= total; r++)
                if (Num(sheet.GetCellData(r, ColJ)) is double v) bars.Add((r, v));
            if (bars.Count > 0)
            {
                double min = bars.Min(b => b.Value), max = bars.Max(b => b.Value);
                foreach (var (row, value) in bars)
                    sheet.SetCellBody(row, ColJ, new DataBarBody(DataBarFraction(value, min, max)));
            }

            // Định dạng kế toán của I và K..AC, từ hàng 3 tới dòng Tổng.
            for (int r = FirstBodyRow; r <= total; r++)
            {
                sheet.SetCellBody(r, ColI, new AccountingBody());
                for (int c = ColK; c <= ColAC; c++)
                    sheet.SetCellBody(r, c, new AccountingBody());
            }
        }

        private static Color Lerp(Color a, Color b, double t) => Color.FromArgb(
            a.R + (int)((b.R - a.R) * t),
            a.G + (int)((b.G - a.G) * t),
            a.B + (int)((b.B - a.B) * t));

        // Hai body dưới đây tô nền bằng FillRectangle chứ KHÔNG dùng dc.DrawCellBackground(): ở zoom khác
        // 100% hàm đó nhân tỉ lệ lần hai. Bounds chưa nhân tỉ lệ; Graphics đã mang sẵn phép nhân.

        /// <summary>Kế toán kiểu Excel: số dồn phải cách mép 4 px, số 0 ra "-", dấu trừ của số âm dồn trái.</summary>
        private sealed class AccountingBody : CellBody
        {
            public override void OnPaint(CellDrawingContext dc)
            {
                var style = Cell.Style;
                var b = Bounds;
                var g = dc.Graphics;
                g.FillRectangle(b, style.BackColor);
                if (Num(Cell.Data) is not double v)
                {
                    dc.DrawCellText();
                    return;
                }

                var right = new RGRect(b.X, b.Y, b.Width - 4, b.Height);
                if (v == 0)
                {
                    g.DrawText("-", style.FontName, style.FontSize, style.TextColor, right, ReoGridHorAlign.Right, ReoGridVerAlign.Bottom);
                    return;
                }
                if (v < 0)
                    g.DrawText("-", style.FontName, style.FontSize, style.TextColor, new RGRect(b.X + 3, b.Y, b.Width - 3, b.Height), ReoGridHorAlign.Left, ReoGridVerAlign.Bottom);
                // "#,##0" theo CurrentCulture: máy vi-VN ra "6.105", đúng như Excel trên cùng máy.
                g.DrawText(Math.Abs(v).ToString("#,##0"), style.FontName, style.FontSize, style.TextColor, right, ReoGridHorAlign.Right, ReoGridVerAlign.Bottom);
            }
        }

        /// <summary>Data bar kiểu Excel: lùi 1 px trái, 2 px trên/dưới, dài tối đa (rộng ô - 3), gradient không viền.</summary>
        private sealed class DataBarBody : CellBody
        {
            private readonly double _fraction;

            public DataBarBody(double fraction) => _fraction = fraction;

            public override void OnPaint(CellDrawingContext dc)
            {
                var b = Bounds;
                dc.Graphics.FillRectangle(b, Cell.Style.BackColor);
                float width = (float)((b.Width - 3) * _fraction);
                if (width >= 1)
                    dc.Graphics.FillRectangleLinear(BarStart, BarEnd, 0f, new RGRect(b.X + 1, b.Y + 2, width, b.Height - 4));
                dc.DrawCellText();
            }
        }
    }
}

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;

namespace AutoJMS.FullStack.UI.ThoiHieu
{
    public sealed class ThoiHieuKpiGridRenderer : IDisposable
    {
        private const int TitleHeight = 108;
        private const int HeaderHeight = 118;
        private const int RowHeight = 24;
        private const int MinFooterHeight = 24;

        private readonly List<SheetColumn> _columns = CreateColumns();
        private readonly Font _titleFont = new("Segoe UI", 16F, FontStyle.Regular);
        private readonly Font _headerFont = new("Tahoma", 9F, FontStyle.Bold);
        private readonly Font _bodyFont = new("Tahoma", 9F, FontStyle.Regular);
        private readonly Font _bodyBoldFont = new("Tahoma", 9F, FontStyle.Bold);
        private readonly Font _smallBoldFont = new("Tahoma", 8F, FontStyle.Bold);
        private readonly Font _filterFont = new("Segoe UI", 6F, FontStyle.Regular);
        private readonly StringFormat _centerFormat = CreateFormat(StringAlignment.Center, StringAlignment.Center);
        private readonly StringFormat _leftFormat = CreateFormat(StringAlignment.Near, StringAlignment.Center);
        private readonly StringFormat _rightFormat = CreateFormat(StringAlignment.Far, StringAlignment.Center);

        public Size Measure(ThoiHieuKpiSheetData data)
        {
            int rowCount = Math.Max(0, data?.Rows?.Count ?? 0);
            return new Size(CalculateFullSheetWidth(), CalculateFullSheetHeight(rowCount));
        }

        public int CalculateFullSheetWidth() => _columns.Sum(c => c.Width);

        public int CalculateFullSheetHeight(int rowCount)
        {
            return TitleHeight + HeaderHeight + (Math.Max(0, rowCount) + 1) * RowHeight + MinFooterHeight;
        }

        public Bitmap RenderFullBitmap(ThoiHieuKpiSheetData data, float scale = 1.0f)
        {
            data ??= new ThoiHieuKpiSheetData();
            data.Rows ??= new List<ThoiHieuEmployeeRow>();
            data.Summary ??= new ThoiHieuKpiSummary();

            scale = Math.Clamp(scale, 0.1f, 4.0f);
            Size fullSize = Measure(data);
            int bitmapWidth = Math.Max(1, (int)Math.Ceiling(fullSize.Width * scale));
            int bitmapHeight = Math.Max(1, (int)Math.Ceiling(fullSize.Height * scale));
            var bitmap = new Bitmap(bitmapWidth, bitmapHeight);

            using var graphics = Graphics.FromImage(bitmap);
            Draw(graphics, new Rectangle(Point.Empty, fullSize), Point.Empty, data, scale);
            return bitmap;
        }

        public void Draw(Graphics graphics, Rectangle viewport, Point scrollOffset, ThoiHieuKpiSheetData data, float scale = 1.0f)
        {
            if (graphics == null) return;

            data ??= new ThoiHieuKpiSheetData();
            data.Rows ??= new List<ThoiHieuEmployeeRow>();
            data.Summary ??= new ThoiHieuKpiSummary();
            scale = Math.Clamp(scale, 0.1f, 4.0f);

            graphics.SmoothingMode = SmoothingMode.None;
            graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            graphics.Clear(Color.White);
            graphics.ScaleTransform(scale, scale, MatrixOrder.Append);
            graphics.TranslateTransform(-scrollOffset.X, -scrollOffset.Y, MatrixOrder.Append);

            int totalWidth = CalculateFullSheetWidth();
            DrawTitleAndSummary(graphics, totalWidth, data);
            DrawColumnHeaders(graphics);
            DrawRows(graphics, data);

            using var outerPen = new Pen(ThoiHieuKpiColorPalette.Border, 1F);
            graphics.DrawRectangle(outerPen, new Rectangle(0, 0, totalWidth - 1, Measure(data).Height - MinFooterHeight - 1));

            graphics.ResetTransform();
        }

        private void DrawTitleAndSummary(Graphics graphics, int totalWidth, ThoiHieuKpiSheetData data)
        {
            int leftTitleWidth = GetColumnRight(9);
            var titleRect = new Rectangle(0, 0, leftTitleWidth, TitleHeight);
            FillRect(graphics, titleRect, ThoiHieuKpiColorPalette.TitleGreen);

            using (var brush = new SolidBrush(Color.White))
            {
                graphics.DrawString(
                    $"BẢNG KÝ NHẬN THỜI HIỆU THEO MỐC THỜI GIAN {data.SiteCode}",
                    _titleFont,
                    brush,
                    titleRect,
                    _centerFormat);
            }

            int x = leftTitleWidth;
            DrawSummaryTextCell(graphics, new Rectangle(x, 0, 64, TitleHeight), "Hàng\nmới:", false);
            x += 64;
            DrawSummaryValueCell(graphics, new Rectangle(x, 0, 50, TitleHeight), data.Summary.NewOrders, true);
            x += 50;
            DrawSummaryTextCell(graphics, new Rectangle(x, 0, 66, TitleHeight), "Số\nTTT\nC\nquét\ngửi\nđến", false);
            x += 66;
            DrawSummaryValueCell(graphics, new Rectangle(x, 0, 50, TitleHeight), data.Summary.ScanTttcCount, true);
            x += 50;
            DrawSummaryTextCell(graphics, new Rectangle(x, 0, 66, TitleHeight), "Số\nđơn\nchưa\nđến", false);
            x += 66;
            DrawSummaryValueCell(graphics, new Rectangle(x, 0, 68, TitleHeight), data.Summary.NotArrivedCount, true);
            x += 68;
            DrawSummaryTextCell(graphics, new Rectangle(x, 0, 70, TitleHeight), "Số\nđơn\nchuyể\nn tiếp", false);
            x += 70;
            DrawSummaryValueCell(graphics, new Rectangle(x, 0, 55, TitleHeight), data.Summary.TransferCount, true);
            x += 55;
            DrawSummaryTextCell(graphics, new Rectangle(x, 0, 52, TitleHeight), "Cần\nphát\nra:", false);
            x += 52;
            DrawSummaryValueCell(graphics, new Rectangle(x, 0, 66, TitleHeight), data.Summary.NeedDeliveryCount, false);
            x += 66;
            DrawSummaryTextCell(graphics, new Rectangle(x, 0, 66, TitleHeight), "Cần kí\nđạt\nKPI\n91%", false);
            x += 66;
            DrawSummaryValueCell(graphics, new Rectangle(x, 0, 64, TitleHeight), data.Summary.NeedKpi91Count, false);
            x += 64;
            DrawSummaryTextCell(graphics, new Rectangle(x, 0, 58, TitleHeight), "Cần\nkí\nđạt\nKPI\n90%", false);
            x += 58;
            DrawSummaryValueCell(graphics, new Rectangle(x, 0, Math.Max(86, totalWidth - x), TitleHeight), data.Summary.NeedKpi90Count, false);
        }

        private void DrawColumnHeaders(Graphics graphics)
        {
            int y = TitleHeight;
            for (int i = 0; i < _columns.Count; i++)
            {
                var col = _columns[i];
                var rect = new Rectangle(GetColumnLeft(i), y, col.Width, HeaderHeight);
                var back = GetHeaderBackColor(col);
                FillRect(graphics, rect, back);
                DrawBorder(graphics, rect, ThoiHieuKpiColorPalette.Border);

                using var brush = new SolidBrush(col.IsKpiTarget ? ThoiHieuKpiColorPalette.RedText : ThoiHieuKpiColorPalette.Text);
                using var font = new Font(_headerFont, col.IsKpiTarget ? FontStyle.Bold : FontStyle.Bold);
                graphics.DrawString(col.HeaderText, font, brush, Inflate(rect, -4, -6), _centerFormat);

                if (!col.IsHour)
                {
                    DrawFilterGlyph(graphics, rect);
                }
            }
        }

        private void DrawRows(Graphics graphics, ThoiHieuKpiSheetData data)
        {
            int y = TitleHeight + HeaderHeight;
            var rows = data.Rows ?? new List<ThoiHieuEmployeeRow>();
            for (int r = 0; r < rows.Count; r++)
            {
                DrawBodyRow(graphics, rows[r], y, r, rows);
                y += RowHeight;
            }

            DrawTotalRow(graphics, rows, y);
        }

        private void DrawBodyRow(Graphics graphics, ThoiHieuEmployeeRow row, int y, int rowIndex, IReadOnlyList<ThoiHieuEmployeeRow> rows)
        {
            for (int c = 0; c < _columns.Count; c++)
            {
                var col = _columns[c];
                var rect = new Rectangle(GetColumnLeft(c), y, col.Width, RowHeight);

                if (col.Key == "Supervisor")
                {
                    DrawMergedSupervisor(graphics, rect, row, rowIndex, rows);
                    continue;
                }

                Color back = GetBodyBackColor(col, row);
                FillRect(graphics, rect, back);
                DrawBorder(graphics, rect, col.IsLeftBlock ? ThoiHieuKpiColorPalette.Border : ThoiHieuKpiColorPalette.ThinBorder);

                string text = GetCellText(col, row);
                using var brush = new SolidBrush(ThoiHieuKpiColorPalette.Text);
                var format = col.Align switch
                {
                    ColumnAlign.Left => _leftFormat,
                    ColumnAlign.Right => _rightFormat,
                    _ => _centerFormat
                };
                graphics.DrawString(text, _bodyFont, brush, Inflate(rect, -3, 0), format);
            }
        }

        private void DrawMergedSupervisor(Graphics graphics, Rectangle rect, ThoiHieuEmployeeRow row, int rowIndex, IReadOnlyList<ThoiHieuEmployeeRow> rows)
        {
            bool first = rowIndex == 0 || !string.Equals(rows[rowIndex - 1].SupervisorName, row.SupervisorName, StringComparison.OrdinalIgnoreCase);
            if (!first) return;

            int span = 1;
            for (int i = rowIndex + 1; i < rows.Count; i++)
            {
                if (!string.Equals(rows[i].SupervisorName, row.SupervisorName, StringComparison.OrdinalIgnoreCase)) break;
                span++;
            }

            var merged = new Rectangle(rect.X, rect.Y, rect.Width, RowHeight * span);
            FillRect(graphics, merged, Color.White);
            DrawBorder(graphics, merged, ThoiHieuKpiColorPalette.Border);
            using var brush = new SolidBrush(ThoiHieuKpiColorPalette.Text);
            graphics.DrawString(row.SupervisorName, _bodyFont, brush, Inflate(merged, -3, 0), _centerFormat);
        }

        private void DrawTotalRow(Graphics graphics, IReadOnlyList<ThoiHieuEmployeeRow> rows, int y)
        {
            var totals = BuildTotals(rows);
            for (int c = 0; c < _columns.Count; c++)
            {
                var col = _columns[c];
                if (c > 0 && c <= 4) continue;

                var rect = new Rectangle(GetColumnLeft(c), y, col.Width, RowHeight);
                if (c == 0)
                {
                    rect.Width = GetColumnRight(4);
                }

                FillRect(graphics, rect, ThoiHieuKpiColorPalette.TotalRed);
                DrawBorder(graphics, rect, ThoiHieuKpiColorPalette.Border);

                string text = c == 0 ? "Tổng" : GetTotalText(col, totals);
                using var brush = new SolidBrush(col.IsKpiTarget || col.Key == "SignedRate" ? Color.Yellow : Color.White);
                var format = c == 0 ? _centerFormat : col.Align == ColumnAlign.Left ? _leftFormat : _centerFormat;
                graphics.DrawString(text, _bodyBoldFont, brush, Inflate(rect, -3, 0), format);
            }
        }

        private static TotalValues BuildTotals(IReadOnlyList<ThoiHieuEmployeeRow> rows)
        {
            var total = new TotalValues();
            foreach (var row in rows)
            {
                total.DeliveryCount += row.DeliveryCount;
                total.Required91 += row.Required91;
                total.Required90 += row.Required90;
                total.SignedCount += row.SignedCount;
                for (int hour = 8; hour <= 24; hour++)
                {
                    if (row.HourlySigned != null && row.HourlySigned.TryGetValue(hour, out int? value) && value.HasValue)
                    {
                        total.Hourly[hour] = total.Hourly.TryGetValue(hour, out int current)
                            ? current + value.Value
                            : value.Value;
                    }
                }
            }

            total.SignedRate = total.DeliveryCount > 0 ? total.SignedCount / (decimal)total.DeliveryCount : 0m;
            return total;
        }

        private static string GetCellText(SheetColumn col, ThoiHieuEmployeeRow row)
        {
            return col.Key switch
            {
                "Stt" => row.Stt.ToString(CultureInfo.InvariantCulture),
                "Supervisor" => row.SupervisorName,
                "SiteCode" => row.SiteCode,
                "EmployeeCode" => row.EmployeeCode,
                "EmployeeName" => row.EmployeeName,
                "DeliveryCount" => row.DeliveryCount.ToString("N0"),
                "Required91" => row.Required91.ToString("N0"),
                "Required90" => row.Required90.ToString("N0"),
                "SignedCount" => row.SignedCount.ToString("N0"),
                "SignedRate" => $"{row.SignedRate:P1}",
                _ when col.Hour.HasValue => GetHourText(row, col.Hour.Value),
                _ => ""
            };
        }

        private static string GetHourText(ThoiHieuEmployeeRow row, int hour)
        {
            if (row.HourlySigned == null || !row.HourlySigned.TryGetValue(hour, out int? value) || !value.HasValue || value.Value <= 0)
            {
                return "-";
            }

            return value.Value.ToString("N0");
        }

        private static string GetTotalText(SheetColumn col, TotalValues total)
        {
            return col.Key switch
            {
                "DeliveryCount" => total.DeliveryCount.ToString("N0"),
                "Required91" => total.Required91.ToString("N0"),
                "Required90" => total.Required90.ToString("N0"),
                "SignedCount" => total.SignedCount.ToString("N0"),
                "SignedRate" => $"{total.SignedRate:P1}",
                _ when col.Hour.HasValue => total.Hourly.TryGetValue(col.Hour.Value, out int value) && value > 0 ? value.ToString("N0") : "-",
                _ => ""
            };
        }

        private static Color GetHeaderBackColor(SheetColumn col)
        {
            if (col.IsKpiTarget) return ThoiHieuKpiColorPalette.Yellow;
            if (col.Key == "SignedCount" || col.Key == "SignedRate" || col.IsHour) return ThoiHieuKpiColorPalette.HeaderGreen;
            return ThoiHieuKpiColorPalette.White;
        }

        private static Color GetBodyBackColor(SheetColumn col, ThoiHieuEmployeeRow row)
        {
            if (col.IsKpiTarget) return Color.FromArgb(255, 255, 210);
            if (col.Key == "SignedRate")
            {
                if (row.SignedRate >= 0.9m) return ThoiHieuKpiColorPalette.RateGreen;
                if (row.SignedRate >= 0.8m) return ThoiHieuKpiColorPalette.RateLight;
                return Color.FromArgb(255, 199, 206);
            }

            if (col.Hour.HasValue)
            {
                int hour = col.Hour.Value;
                if (row.HourlySigned == null || !row.HourlySigned.TryGetValue(hour, out int? value) || !value.HasValue || value.Value <= 0)
                {
                    return ThoiHieuKpiColorPalette.DangerRed;
                }

                if (value.Value >= 18) return Color.FromArgb(142, 180, 227);
                if (value.Value >= 10) return ThoiHieuKpiColorPalette.LightBlue;
                return Color.FromArgb(235, 242, 252);
            }

            return ThoiHieuKpiColorPalette.White;
        }

        private void DrawSummaryTextCell(Graphics graphics, Rectangle rect, string text, bool yellow)
        {
            FillRect(graphics, rect, yellow ? ThoiHieuKpiColorPalette.Yellow : ThoiHieuKpiColorPalette.LightGray);
            DrawBorder(graphics, rect, ThoiHieuKpiColorPalette.Border);
            using var brush = new SolidBrush(ThoiHieuKpiColorPalette.Text);
            graphics.DrawString(text, _headerFont, brush, Inflate(rect, -3, -3), _centerFormat);
        }

        private void DrawSummaryValueCell(Graphics graphics, Rectangle rect, int value, bool yellow)
        {
            FillRect(graphics, rect, yellow ? ThoiHieuKpiColorPalette.Yellow : ThoiHieuKpiColorPalette.White);
            DrawBorder(graphics, rect, ThoiHieuKpiColorPalette.Border);
            using var brush = new SolidBrush(ThoiHieuKpiColorPalette.RedText);
            graphics.DrawString(value.ToString("N0"), _bodyBoldFont, brush, Inflate(rect, -2, 0), _centerFormat);
        }

        private void DrawFilterGlyph(Graphics graphics, Rectangle headerRect)
        {
            var glyphRect = new Rectangle(headerRect.Right - 22, headerRect.Bottom - 22, 18, 18);
            FillRect(graphics, glyphRect, Color.FromArgb(242, 242, 242));
            DrawBorder(graphics, glyphRect, Color.FromArgb(190, 190, 190));

            using var brush = new SolidBrush(Color.FromArgb(90, 90, 90));
            Point[] points =
            {
                new(glyphRect.Left + 5, glyphRect.Top + 7),
                new(glyphRect.Right - 5, glyphRect.Top + 7),
                new(glyphRect.Left + glyphRect.Width / 2, glyphRect.Bottom - 5)
            };
            graphics.FillPolygon(brush, points);
        }

        private static void FillRect(Graphics graphics, Rectangle rect, Color color)
        {
            using var brush = new SolidBrush(color);
            graphics.FillRectangle(brush, rect);
        }

        private static void DrawBorder(Graphics graphics, Rectangle rect, Color color)
        {
            using var pen = new Pen(color, 1F);
            graphics.DrawRectangle(pen, rect.X, rect.Y, rect.Width - 1, rect.Height - 1);
        }

        private int GetColumnLeft(int index)
        {
            int x = 0;
            for (int i = 0; i < index; i++) x += _columns[i].Width;
            return x;
        }

        private int GetColumnRight(int index) => GetColumnLeft(index) + _columns[index].Width;

        private static Rectangle Inflate(Rectangle rect, int x, int y)
        {
            rect.Inflate(x, y);
            return rect;
        }

        private static StringFormat CreateFormat(StringAlignment alignment, StringAlignment lineAlignment)
        {
            return new StringFormat
            {
                Alignment = alignment,
                LineAlignment = lineAlignment,
                Trimming = StringTrimming.EllipsisCharacter,
                FormatFlags = 0
            };
        }

        private static List<SheetColumn> CreateColumns()
        {
            var cols = new List<SheetColumn>
            {
                new("Stt", "STT", 45, ColumnAlign.Right, IsLeftBlock: true),
                new("Supervisor", "Người phụ\ntrách ( SPV)", 110, ColumnAlign.Center, IsLeftBlock: true),
                new("SiteCode", "Quét mã\nở Bưu cục", 85, ColumnAlign.Left, IsLeftBlock: true),
                new("EmployeeCode", "Mã nhân viên\nphát", 110, ColumnAlign.Center, IsLeftBlock: true),
                new("EmployeeName", "Nhân viên phát", 260, ColumnAlign.Left, IsLeftBlock: true),
                new("DeliveryCount", "Đơn phát", 80, ColumnAlign.Right),
                new("Required91", "Số đơn cần\nkí nhận thời\nhiệu 91%", 95, ColumnAlign.Right, IsKpiTarget: true),
                new("Required90", "Số đơn cần\nkí Basline\n90%", 95, ColumnAlign.Right, IsKpiTarget: true),
                new("SignedCount", "Số vận\nđơn ký\nnhận", 80, ColumnAlign.Right),
                new("SignedRate", "Tỉ lệ ký\nnhận", 90, ColumnAlign.Right)
            };

            for (int hour = 8; hour <= 24; hour++)
            {
                string label = hour <= 21 ? $"{hour}h" : $"{hour}H";
                cols.Add(new SheetColumn($"H{hour}", label, 55, ColumnAlign.Center, Hour: hour, IsHour: true));
            }

            return cols;
        }

        public void Dispose()
        {
            _titleFont.Dispose();
            _headerFont.Dispose();
            _bodyFont.Dispose();
            _bodyBoldFont.Dispose();
            _smallBoldFont.Dispose();
            _filterFont.Dispose();
            _centerFormat.Dispose();
            _leftFormat.Dispose();
            _rightFormat.Dispose();
        }

        private sealed record SheetColumn(
            string Key,
            string HeaderText,
            int Width,
            ColumnAlign Align,
            bool IsLeftBlock = false,
            bool IsKpiTarget = false,
            int? Hour = null,
            bool IsHour = false);

        private enum ColumnAlign
        {
            Left,
            Center,
            Right
        }

        private sealed class TotalValues
        {
            public int DeliveryCount { get; set; }
            public int Required91 { get; set; }
            public int Required90 { get; set; }
            public int SignedCount { get; set; }
            public decimal SignedRate { get; set; }
            public Dictionary<int, int> Hourly { get; } = new();
        }
    }
}

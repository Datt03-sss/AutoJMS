using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using ClosedXML.Excel;
using unvell.ReoGrid;
using unvell.ReoGrid.DataFormat;

namespace AutoJMS.FullStack.UI.ThoiHieu
{
    /// <summary>
    /// Dữ liệu thật cho tab Thời hiệu, đọc từ file "Ký nhận thực tế" của JMS. Làm thay Owner đúng các bước tay:
    /// pivot "Nhân viên phát kiện" × đếm "Mã vận đơn" chép vào sheet Tổng (bỏ dòng "(blank)"), chép
    /// "Nhân viên phát kiện" + "Thời gian ký nhận" sang sheet data. Cột giờ K..AA tính sẵn ở đây, khớp
    /// COUNTIFS(data!C:C, E, data!A:A, ...) của file gốc với data!A = HOUR(D).
    /// </summary>
    internal sealed class ThoiHieuKpiImport
    {
        private const string WaybillHeader = "Mã vận đơn";
        private const string NameHeader = "Nhân viên phát kiện";
        private const string SignedHeader = "Thời gian ký nhận";
        private const string SiteHeader = "Mã bưu cục phát";
        private const string TitlePrefix = "BẢNG KÝ NHẬN THỜI HIỆU THEO MỐC THỜI GIAN ";

        // 0-based trên sheet Tổng: hàng 3 Excel = 2; file mẫu có 28 hàng nhân viên (3..30). Hàng 4 là hàng giữa,
        // đủ viền 4 cạnh, làm mẫu định dạng cho hàng chèn thêm. Cột K..AA = 17 mốc giờ "8h".."24H".
        private const int FirstBodyRow = 2, TemplateBodyRows = 28, StyleRow = 3;
        private const int ColK = 10, HourColumns = 17;
        private const PartialGridCopyFlag StyleOnly = PartialGridCopyFlag.CellFormat | PartialGridCopyFlag.CellStyle | PartialGridCopyFlag.BorderAll;

        internal sealed record Employee(string Name, int Orders, int[] Hours);

        public string SiteCode { get; }
        public IReadOnlyList<Employee> Employees { get; }
        public IReadOnlyList<(string Name, DateTime? SignedAt)> Rows { get; }

        internal ThoiHieuKpiImport(string siteCode, IReadOnlyList<Employee> employees, IReadOnlyList<(string Name, DateTime? SignedAt)> rows)
        {
            SiteCode = siteCode;
            Employees = employees;
            Rows = rows;
        }

        /// <summary>Đọc sheet đầu tiên có đủ 3 cột cần dùng (file JMS: "sheet0"). Ném <see cref="InvalidDataException"/> nếu không có.</summary>
        public static ThoiHieuKpiImport Read(string path)
        {
            // FileShare.ReadWrite: Owner thường đang mở chính file này trong Excel.
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var workbook = new XLWorkbook(stream);
            foreach (var sheet in workbook.Worksheets)
            {
                var header = sheet.FirstRowUsed();
                if (header == null) continue;
                int Column(string name) => header.CellsUsed().FirstOrDefault(c => c.GetString().Trim() == name)?.Address.ColumnNumber ?? 0;
                int waybill = Column(WaybillHeader), employee = Column(NameHeader), signed = Column(SignedHeader), site = Column(SiteHeader);
                if (waybill == 0 || employee == 0 || signed == 0) continue;

                // Cùng một tên có dòng gõ dựng sẵn, có dòng gõ tổ hợp (file JMS thật có): nhìn y hệt nhưng khác chuỗi.
                // Pivot tách làm hai người còn COUNTIFS gộp, nên file Excel gốc thiếu đơn ở cột F. Chuẩn hoá NFC rồi
                // gộp không phân biệt hoa thường như COUNTIFS.
                var rows = sheet.RowsUsed(r => r.RowNumber() > header.RowNumber())
                    .Select(r => (Waybill: r.Cell(waybill).GetString().Trim(),
                                  Name: r.Cell(employee).GetString().Trim().Normalize(),
                                  SignedAt: ReadTime(r.Cell(signed)),
                                  Site: site == 0 ? "" : r.Cell(site).GetString().Trim()))
                    .ToList();

                // Như pivot: đếm mã vận đơn khác rỗng theo tên, xếp tên như Excel; dòng không tên là "(blank)", bỏ.
                var employees = rows.Where(r => r.Name.Length > 0)
                    .GroupBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
                    .OrderBy(g => g.Key, ExcelNameOrder)
                    .Select(g =>
                    {
                        var hours = new int[HourColumns];
                        foreach (var r in g)
                            if (r.SignedAt is DateTime t) hours[Math.Max(t.Hour - 8, 0)]++;   // ≤ 8h vào K, 9h..23h vào L..Z; AA "24H" luôn 0
                        return new Employee(g.Key, g.Count(r => r.Waybill.Length > 0), hours);
                    })
                    .ToList();
                if (employees.Count == 0)
                    throw new InvalidDataException($"Sheet \"{sheet.Name}\" không có dòng nào có \"{NameHeader}\".");

                string siteCode = rows.Select(r => r.Site).FirstOrDefault(s => s.Length > 0) ?? "";
                return new ThoiHieuKpiImport(siteCode, employees, rows.Select(r => (r.Name, r.SignedAt)).ToList());
            }
            throw new InvalidDataException($"Không tìm thấy sheet có đủ cột \"{WaybillHeader}\", \"{NameHeader}\", \"{SignedHeader}\".");
        }

        // Excel xếp theo NLS của Windows: vi-VN coi "Th", "Tr"... là một chữ nên "Tùng" đứng trước "Thanh". .NET 8 so bằng
        // ICU thì ngược lại, lệch thứ tự với pivot của Owner. CompareStringEx trả 1/2/3 cho nhỏ hơn/bằng/lớn hơn.
        private static readonly Comparer<string> ExcelNameOrder = Comparer<string>.Create(
            (a, b) => CompareStringEx("vi-VN", NormIgnoreCase, a, a.Length, b, b.Length, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero) - 2);

        private const uint NormIgnoreCase = 1;

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        private static extern int CompareStringEx(string locale, uint flags, string a, int aLength, string b, int bLength,
            IntPtr version, IntPtr reserved, IntPtr param);

        // JMS ghi giờ ký là số ngày Excel; ô chưa ký là chuỗi rỗng. Chuỗi "yyyy-MM-dd HH:mm:ss" cũng nhận.
        private static DateTime? ReadTime(IXLCell cell) => cell.DataType switch
        {
            XLDataType.DateTime => cell.GetDateTime(),
            XLDataType.Number => DateTime.FromOADate(cell.GetDouble()),
            XLDataType.Text when DateTime.TryParse(cell.GetString().Trim(), CultureInfo.InvariantCulture, DateTimeStyles.None, out var t) => t,
            _ => null
        };

        /// <summary>
        /// Đổ dữ liệu vào file mẫu vừa nạp, TRƯỚC khi khoá chỉ đọc (CopyRange tôn trọng chỉ đọc). ReoGrid chèn/xoá
        /// hàng không sửa tham chiếu trong công thức, nên mọi công thức hàng nhân viên, dòng Tổng và T1 viết lại hết.
        /// </summary>
        public void Fill(Worksheet summary, Worksheet data)
        {
            int n = Employees.Count;
            if (n > TemplateBodyRows)
            {
                // Chèn trên hàng nhân viên cuối để merge B3:B30 giãn theo. Hàng chèn không có định dạng %/kế toán và
                // viền trong: chép từ hàng mẫu, chừa cột B đang merge.
                int at = FirstBodyRow + TemplateBodyRows - 1;
                summary.InsertRows(at, n - TemplateBodyRows);
                for (int r = at; r < at + n - TemplateBodyRows; r++)
                {
                    summary.CopyRange(new RangePosition(StyleRow, 0, 1, 1), new RangePosition(r, 0, 1, 1), StyleOnly);
                    summary.CopyRange(new RangePosition(StyleRow, 2, 1, 28), new RangePosition(r, 2, 1, 28), StyleOnly);
                }
            }
            else if (n < TemplateBodyRows)
            {
                summary.DeleteRows(FirstBodyRow + 1, TemplateBodyRows - n);   // giữ hàng 3 (đầu merge B) và hàng cuối (viền dưới)
            }

            // File mẫu zoom 70%: ReoGrid chèn/xoá hàng cộng số px chưa nhân zoom vào vị trí chữ đã đo của ô bị dời, không
            // canh lại chữ ô gộp B, và đổi zoom chỉ đo lại ô trong khung nhìn. Trên lưới đang hiện "Tổng" và chú thích dưới
            // Tổng trôi xuống/mất. Ghi lại giá trị (qua null: ghi trùng giá trị bị bỏ qua) đánh dấu ô cần đo lại lúc vẽ.
            // Ô công thức không ghi lại được: hàng nhân viên và dòng Tổng viết lại công thức ngay dưới đây.
            summary.IterateCells(new RangePosition(FirstBodyRow, 0, summary.RowCount - FirstBodyRow, summary.ColumnCount), (row, col, cell) =>
            {
                if (cell.Data != null && !cell.HasFormula)
                {
                    var value = cell.Data;
                    cell.Data = null;
                    cell.Data = value;
                }
                return true;
            });

            for (int i = 0; i < n; i++)
            {
                var e = Employees[i];
                int r = FirstBodyRow + i, x = r + 1;
                summary[r, 0] = i + 1;
                if (SiteCode.Length > 0) summary[r, 2] = SiteCode;
                summary[r, 4] = e.Name;
                summary[r, 5] = e.Orders;
                summary.Cells[r, 6].Formula = $"F{x}*911/1000";   // 91,1%: số thập phân trong công thức vỡ dưới culture vi-VN
                summary.Cells[r, 7].Formula = $"F{x}*90%";
                summary.Cells[r, 8].Formula = $"SUM(K{x}:AA{x})";
                summary.Cells[r, 9].Formula = $"IF(F{x}=0, 0, I{x}/F{x})";   // không ra #DIV/0!; ReoGrid đọc "0,0" thành số nên phải có dấu cách
                for (int h = 0; h < HourColumns; h++) summary[r, ColK + h] = e.Hours[h];
                summary.Cells[r, 27].Formula = $"G{x}-I{x}";
                summary.Cells[r, 28].Formula = $"H{x}-I{x}";
            }

            int total = FirstBodyRow + n, xt = total + 1;
            foreach (int c in new[] { 5, 6, 7 }.Concat(Enumerable.Range(ColK, HourColumns)))
                summary.Cells[total, c].Formula = $"SUM({new RangePosition(FirstBodyRow, c, n, 1).ToAddress()})";
            summary.Cells[total, 8].Formula = $"SUM(K{xt}:AA{xt})";
            summary.Cells[total, 9].Formula = $"IF(F{xt}=0, 0, I{xt}/F{xt})";
            summary.Cells[total, 27].Formula = $"G{xt}-I{xt}";
            summary.Cells[total, 28].Formula = $"H{xt}-I{xt}";
            summary.Cells[0, 19].Formula = $"F{xt}";   // T1 "Cần phát ra"
            if (SiteCode.Length > 0) summary[0, 0] = TitlePrefix + SiteCode;

            // Sheet data: A = giờ ký (HOUR(D) của file gốc), C = tên, D = thời gian ký; B "Mã Shipper" để trống như file gốc.
            var values = new object[Rows.Count, 4];
            for (int i = 0; i < Rows.Count; i++)
            {
                values[i, 0] = Rows[i].SignedAt?.Hour;
                values[i, 2] = Rows[i].Name;
                values[i, 3] = Rows[i].SignedAt;
            }
            data.RowCount = Math.Max(data.RowCount, FirstBodyRow + Rows.Count + 1);
            data.SetRangeData(new RangePosition(FirstBodyRow, 0, Rows.Count, 4), values);
            data.SetRangeDataFormat(new RangePosition(FirstBodyRow, 3, Rows.Count, 1), CellDataFormatFlag.DateTime,
                new DateTimeDataFormatter.DateTimeFormatArgs { Format = "yyyy-MM-dd HH:mm:ss", CultureName = "vi-VN" });
        }
    }
}

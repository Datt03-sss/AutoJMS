using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using AutoJMS.Diagnostics;

namespace AutoJMS
{
    /// <summary>
    /// Sổ đếm lượt in của tab IN ĐƠN &gt; In Reverse, giữ trong TSV
    /// <c>AppData/logs/tab-print-reverse-reprints.tsv</c>.
    ///
    /// <para>JMS chỉ đếm lượt in thật (<c>printMode=2</c>) và chặn ở lượt thứ tư bằng
    /// code 121003005. Owner chốt cho in thêm hai lượt nữa bằng đúng đường bản xem trước
    /// (<c>printMode=1</c>) — cùng một PDF, nhưng JMS không tính lượt. Nghĩa là lượt thứ 4 và
    /// thứ 5 không tồn tại ở phía JMS: giữ trong RAM thôi thì tắt app một cái là đếm lại từ
    /// đầu, nên phải nằm dưới đĩa.</para>
    ///
    /// <para>Ghi số TUYỆT ĐỐI chứ không cộng dồn: cột "Số bản in" của lưới đã là tổng mà app
    /// biết (số JMS trả về lúc tra, cộng những lượt app tự in thêm), nên chép thẳng số đó
    /// xuống sổ là hai bên không bao giờ lệch nhau. Cộng dồn thì một lượt ghi hụt là sai
    /// vĩnh viễn.</para>
    ///
    /// <para>Không bao giờ ném: hỏng sổ thì tệ nhất là in dư một bản, còn chặn người dùng in
    /// vì lỗi đọc file thì hỏng cả ca làm việc.</para>
    /// </summary>
    public static class ReversePrintLedger
    {
        /// <summary>Trần tuyệt đối Owner chốt: ba lượt của JMS cộng hai lượt đi đường xem trước.</summary>
        public const int MaxPrints = 5;

        /// <summary>Số lượt JMS tự đếm. Chạm mốc này thì lượt sau phải đi đường xem trước.</summary>
        public const int JmsPrintLimit = 3;

        private const string FileName = "tab-print-reverse-reprints.tsv";
        private const string HeaderLine = "waybillNo\tprintCount\tlastPrintedAt";
        private const string TimeFormat = "yyyy-MM-dd HH:mm:ss";

        /// <summary>
        /// Dòng cũ hơn mốc này bị bỏ ở lượt ghi kế tiếp — nếu không thì file chỉ có lớn thêm.
        /// Rộng hơn hẳn <c>ReversePrintRetentionDays</c> của thư mục PDF: mất bản in thì in
        /// lại được, còn mất dòng sổ là mở lại hai lượt in mà JMS không hề hay biết.
        /// </summary>
        private const int RetentionDays = 180;

        private static readonly string[] HeaderComment =
        {
            "# Sổ lượt in của tab IN ĐƠN > In Reverse — app tự ghi, đừng sửa tay.",
            "# printCount là TỔNG số bản đã in của vận đơn đó (kể cả ba lượt JMS tự đếm).",
            $"# Chạm {MaxPrints} là app chặn không cho in thêm.",
        };

        private static readonly object Gate = new();
        private static Dictionary<string, (int Count, DateTime At)> _rows;

        private static string FilePath => Path.Combine(AppPaths.LogsDir, FileName);

        /// <summary>Số bản đã in mà sổ ghi nhận cho vận đơn này; 0 nếu chưa có dòng nào.</summary>
        public static int CountOf(string waybillNo)
        {
            if (string.IsNullOrWhiteSpace(waybillNo)) return 0;
            lock (Gate)
            {
                return Load().TryGetValue(waybillNo.Trim(), out var row) ? row.Count : 0;
            }
        }

        /// <summary>
        /// Chốt số bản in của một lượt vừa in xong rồi ghi thẳng xuống đĩa. Chỉ ghi đè khi số
        /// mới LỚN HƠN: sổ đi một chiều, một lượt tra trả về số JMS cũ hơn không được phép kéo
        /// lùi những lượt in mà app đã cho đi.
        /// </summary>
        public static void Record(IEnumerable<(string WaybillNo, int PrintCount)> prints)
        {
            if (prints == null) return;
            lock (Gate)
            {
                var rows = Load();
                DateTime now = DateTime.Now;
                bool changed = false;

                foreach (var (waybillNo, printCount) in prints)
                {
                    if (string.IsNullOrWhiteSpace(waybillNo)) continue;
                    string key = waybillNo.Trim();
                    if (rows.TryGetValue(key, out var old) && old.Count >= printCount) continue;

                    rows[key] = (printCount, now);
                    changed = true;
                }

                if (changed) Save(rows);
            }
        }

        /// <summary>
        /// <c>printMode</c> cho lượt in kế tiếp của một vận đơn đã in <paramref name="printedSoFar"/>
        /// bản. Còn trong ba lượt của JMS thì đi đường in thật để JMS đếm như mọi tab khác;
        /// hết ba lượt thì chỉ còn đường xem trước, và lượt đó chỉ sổ này biết.
        /// </summary>
        public static int NextPrintMode(int printedSoFar) =>
            printedSoFar >= JmsPrintLimit
                ? JmsSendWaybillService.CenterPrintModePreview
                : JmsSendWaybillService.CenterPrintModePrint;

        // ── internals ────────────────────────────────────────

        private static Dictionary<string, (int Count, DateTime At)> Load()
        {
            if (_rows != null) return _rows;
            try
            {
                _rows = File.Exists(FilePath)
                    ? Parse(File.ReadAllLines(FilePath))
                    : NewRows();
            }
            catch (Exception ex)
            {
                // Đọc hỏng thì coi như sổ trắng: thà cho in thêm còn hơn khoá cứng nút IN.
                AppLogger.Warning($"[ReversePrintLedger] Không đọc được sổ: {ex.Message}");
                _rows = NewRows();
            }
            return _rows;
        }

        private static void Save(Dictionary<string, (int Count, DateTime At)> rows)
        {
            DateTime cutoff = DateTime.Now.Date.AddDays(-RetentionDays);
            foreach (string key in rows.Where(p => p.Value.At.Date < cutoff).Select(p => p.Key).ToList())
                rows.Remove(key);

            try
            {
                Directory.CreateDirectory(AppPaths.LogsDir);
                File.WriteAllLines(FilePath, Format(rows), Encoding.UTF8);
            }
            catch (Exception ex)
            {
                // Giữ nguyên bản trong RAM: phiên này vẫn đếm đúng, chỉ mở lại app là mất.
                AppLogger.Warning($"[ReversePrintLedger] Không ghi được sổ: {ex.Message}");
            }
        }

        /// <summary>Phần đọc thuần, tách ra để test không cần chạm đĩa.</summary>
        internal static Dictionary<string, (int Count, DateTime At)> Parse(IEnumerable<string> lines)
        {
            var rows = NewRows();
            foreach (string raw in lines ?? Array.Empty<string>())
            {
                string line = (raw ?? "").Trim();
                if (line.Length == 0 || line[0] == '#') continue;

                string[] cols = line.Split('\t');
                if (cols.Length < 2) continue;

                // Dòng tiêu đề đi chung một nhánh với dòng rác: cột số không đọc ra thì bỏ.
                string waybillNo = cols[0].Trim();
                if (waybillNo.Length == 0 || !int.TryParse(
                        cols[1].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int count))
                    continue;

                DateTime.TryParseExact(
                    cols.Length > 2 ? cols[2].Trim() : "",
                    TimeFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var at);

                rows[waybillNo] = (count, at);
            }
            return rows;
        }

        /// <summary>Phần ghi thuần, tách ra để test đối chiếu vòng ghi–đọc.</summary>
        internal static IEnumerable<string> Format(Dictionary<string, (int Count, DateTime At)> rows) =>
            HeaderComment
                .Append(HeaderLine)
                .Concat(rows
                    .OrderBy(p => p.Key, StringComparer.Ordinal)
                    .Select(p =>
                        $"{p.Key}\t{p.Value.Count}\t{p.Value.At.ToString(TimeFormat, CultureInfo.InvariantCulture)}"));

        private static Dictionary<string, (int Count, DateTime At)> NewRows() =>
            new(StringComparer.OrdinalIgnoreCase);
    }
}

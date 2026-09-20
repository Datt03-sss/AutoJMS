using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using AutoJMS.Diagnostics;

namespace AutoJMS
{
    /// <summary>
    /// Lịch sử in của tab IN ĐƠN &gt; In Reverse, ghi nối tiếp vào
    /// <c>AppData/logs/tab-print-reverse-reprints.tsv</c> — mỗi lượt in một dòng:
    /// <code>2026-09-20 14:32:11	845123456789	đã in 4 lần</code>
    ///
    /// <para>Đây là SỔ GHI, không phải chốt chặn. Owner chốt in không giới hạn: quá ba lượt
    /// JMS đếm thì lượt sau đi đường bản xem trước (<c>printMode=1</c>, JMS không tính lượt)
    /// chứ không chặn ai cả. Sổ chỉ để tra lại "mã này đã in mấy lần rồi" — thứ JMS không
    /// trả lời được, vì chính nó không thấy những lượt đi đường xem trước.</para>
    ///
    /// <para>Không bao giờ ném: hỏng sổ thì mất một dòng lịch sử, còn chặn người dùng in vì
    /// lỗi ghi file thì hỏng cả ca làm việc.</para>
    /// </summary>
    public static class ReversePrintLedger
    {
        private const string FileName = "tab-print-reverse-reprints.tsv";
        private const string TimeFormat = "yyyy-MM-dd HH:mm:ss";

        /// <summary>
        /// Dòng cũ hơn mốc này bị bỏ ở lượt đọc kế tiếp — không dọn thì file chỉ có lớn thêm.
        /// Tách khỏi <c>ReversePrintRetentionDays</c> của thư mục PDF: hai thứ Owner chốt hai
        /// con số khác nhau, gộp lại là một lần đổi kéo theo cả cái kia.
        /// </summary>
        private const int RetentionDays = 7;

        private static readonly string[] HeaderComment =
        {
            "# Lịch sử in của tab IN ĐƠN > In Reverse — app tự ghi, đừng sửa tay.",
            $"# Mỗi lượt in một dòng, tự xoá sau {RetentionDays} ngày.",
        };

        /// <summary>Bóc số bản in ra khỏi phần chữ "đã in N lần".</summary>
        private static readonly Regex CountText = new(@"\d+", RegexOptions.Compiled);

        private static readonly object Gate = new();
        private static Dictionary<string, int> _counts;

        private static string FilePath => Path.Combine(AppPaths.LogsDir, FileName);

        /// <summary>
        /// Số bản đã in mà sổ ghi nhận cho vận đơn này; 0 nếu chưa có dòng nào. Lấy số LỚN
        /// NHẤT trong các dòng của mã đó, không phải số dòng: <c>n</c> của mỗi dòng đã là tổng
        /// tính cả những lượt JMS in trước khi app biết tới mã này.
        /// </summary>
        public static int CountOf(string waybillNo)
        {
            if (string.IsNullOrWhiteSpace(waybillNo)) return 0;
            lock (Gate)
            {
                return Load().TryGetValue(waybillNo.Trim(), out int count) ? count : 0;
            }
        }

        /// <summary>Ghi một dòng cho mỗi vận đơn của lượt in vừa xong, nối vào cuối file.</summary>
        public static void Record(IEnumerable<(string WaybillNo, int PrintCount)> prints)
        {
            if (prints == null) return;
            lock (Gate)
            {
                var counts = Load();
                DateTime now = DateTime.Now;
                var lines = new List<string>();

                foreach (var (waybillNo, printCount) in prints)
                {
                    if (string.IsNullOrWhiteSpace(waybillNo)) continue;
                    string key = waybillNo.Trim();
                    lines.Add(FormatLine(now, key, printCount));
                    if (!counts.TryGetValue(key, out int old) || old < printCount)
                        counts[key] = printCount;
                }

                if (lines.Count == 0) return;
                try
                {
                    Directory.CreateDirectory(AppPaths.LogsDir);
                    if (!File.Exists(FilePath))
                        File.WriteAllLines(FilePath, HeaderComment, Encoding.UTF8);
                    File.AppendAllLines(FilePath, lines, Encoding.UTF8);
                }
                catch (Exception ex)
                {
                    // Giữ nguyên bản trong RAM: phiên này vẫn đếm đúng, chỉ mở lại app là mất.
                    AppLogger.Warning($"[ReversePrintLedger] Không ghi được lịch sử: {ex.Message}");
                }
            }
        }

        // ── internals ────────────────────────────────────────

        internal static string FormatLine(DateTime at, string waybillNo, int printCount) =>
            $"{at.ToString(TimeFormat, CultureInfo.InvariantCulture)}\t{waybillNo}\tđã in {printCount} lần";

        private static Dictionary<string, int> Load()
        {
            if (_counts != null) return _counts;

            var lines = new List<string>();
            try
            {
                if (File.Exists(FilePath)) lines.AddRange(File.ReadAllLines(FilePath));
            }
            catch (Exception ex)
            {
                // Đọc hỏng thì coi như sổ trắng — cột "Số bản in" lùi về số JMS trả về, in vẫn chạy.
                AppLogger.Warning($"[ReversePrintLedger] Không đọc được lịch sử: {ex.Message}");
            }

            var kept = Prune(lines, DateTime.Now.Date.AddDays(-RetentionDays));
            _counts = CountByWaybill(kept);

            // Dọn ngay lúc đọc, và chỉ ghi lại khi thật sự có dòng bị bỏ: mọi lượt ghi sau đó
            // chỉ nối thêm cuối file.
            if (kept.Count != lines.Count(l => !IsComment(l))) Rewrite(kept);
            return _counts;
        }

        /// <summary>Bỏ dòng chú thích, dòng rỗng và mọi dòng cũ hơn <paramref name="cutoff"/>.</summary>
        internal static List<string> Prune(IEnumerable<string> lines, DateTime cutoff) =>
            (lines ?? Array.Empty<string>())
                .Where(l => !IsComment(l))
                .Where(l => DateTime.TryParseExact(
                                (l.Split('\t').FirstOrDefault() ?? "").Trim(),
                                TimeFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var at)
                            && at.Date >= cutoff.Date)
                .ToList();

        /// <summary>Số bản in lớn nhất từng ghi cho mỗi mã.</summary>
        internal static Dictionary<string, int> CountByWaybill(IEnumerable<string> lines)
        {
            var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (string raw in lines ?? Array.Empty<string>())
            {
                string[] cols = (raw ?? "").Split('\t');
                if (cols.Length < 3) continue;

                string waybillNo = cols[1].Trim();
                var match = CountText.Match(cols[2]);
                if (waybillNo.Length == 0 || !match.Success) continue;

                int count = int.Parse(match.Value, CultureInfo.InvariantCulture);
                if (!counts.TryGetValue(waybillNo, out int old) || old < count)
                    counts[waybillNo] = count;
            }
            return counts;
        }

        private static bool IsComment(string line)
        {
            string trimmed = (line ?? "").Trim();
            return trimmed.Length == 0 || trimmed[0] == '#';
        }

        private static void Rewrite(List<string> kept)
        {
            try
            {
                Directory.CreateDirectory(AppPaths.LogsDir);
                File.WriteAllLines(FilePath, HeaderComment.Concat(kept), Encoding.UTF8);
            }
            catch (Exception ex)
            {
                AppLogger.Warning($"[ReversePrintLedger] Không dọn được lịch sử: {ex.Message}");
            }
        }
    }
}

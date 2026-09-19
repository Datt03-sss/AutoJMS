using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using AutoJMS.Diagnostics;

namespace AutoJMS
{
    /// <summary>
    /// Danh sách nhân viên ĐANG LÀM VIỆC của bưu cục, giữ trong một file text cạnh
    /// <c>AutoJMS.json</c> (<see cref="AppPaths.ActiveStaffFile"/>).
    ///
    /// JMS không đánh dấu người đã nghỉ: <c>sysStaff/selectAll</c> vẫn trả về họ, nên ô
    /// "Tên nhân viên" của tab In Reverse xổ ra cả người đã nghỉ. File này là nơi chủ bưu cục
    /// tự chốt ai còn làm.
    ///
    /// <para>File RỖNG = chưa lọc gì cả — mọi nhân viên JMS trả về đều hiện ra. Chỉ khi có ít
    /// nhất một dòng thì bộ lọc mới bật. Chủ bưu cục bổ sung dần, app không cần đổi.</para>
    /// </summary>
    public static class ActiveStaffRoster
    {
        private static readonly string[] HeaderLines =
        {
            "# Danh sách nhân viên ĐANG LÀM VIỆC — lọc ô \"Tên nhân viên\" ở tab IN ĐƠN > In Reverse.",
            "# Mỗi dòng một người: ghi mã nhân viên, hoặc tên, hoặc \"mã|tên\".",
            "# Ví dụ:  01989714|Đinh Thị Chinh",
            "#",
            "# File còn RỖNG = chưa lọc, mọi nhân viên JMS trả về đều hiện ra.",
            "# Thêm dòng đầu tiên là bộ lọc bật: chỉ những người có tên trong file này còn hiện.",
            "",
        };

        /// <summary>
        /// Bỏ khỏi <paramref name="staff"/> những người không có trong file. Đọc thẳng từ đĩa
        /// mỗi lượt tra — file vài chục dòng, rẻ hơn nhiều so với lượt gọi JMS vừa chạy xong,
        /// mà đổi lại chủ bưu cục sửa file là có hiệu lực ngay, không phải khởi động lại app.
        /// </summary>
        public static IReadOnlyList<JmsSendWaybillService.StaffInfo> KeepActive(
            IReadOnlyList<JmsSendWaybillService.StaffInfo> staff)
        {
            EnsureFile();
            try
            {
                return KeepActive(staff, File.ReadAllLines(AppPaths.ActiveStaffFile));
            }
            catch (Exception ex)
            {
                // Đọc hỏng thì thà hiện thừa còn hơn nuốt sạch danh sách của người dùng.
                AppLogger.Warning($"[ActiveStaff] Không đọc được danh sách: {ex.Message}");
                return staff;
            }
        }

        /// <summary>Phần lọc thuần, tách ra để test không cần chạm đĩa.</summary>
        internal static IReadOnlyList<JmsSendWaybillService.StaffInfo> KeepActive(
            IReadOnlyList<JmsSendWaybillService.StaffInfo> staff, IEnumerable<string> lines)
        {
            var roster = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string raw in lines ?? Array.Empty<string>())
            {
                string line = raw ?? "";
                int hash = line.IndexOf('#');
                if (hash >= 0) line = line.Substring(0, hash);

                // Một dòng có thể là "mã", "tên", hoặc "mã|tên" — nhận cả ba để người nhập
                // khỏi phải nhớ định dạng.
                foreach (string part in line.Split('|'))
                {
                    string entry = part.Trim();
                    if (entry.Length > 0) roster.Add(Normalize(entry));
                }
            }

            if (roster.Count == 0) return staff;

            return staff
                .Where(s => roster.Contains(Normalize(s.Code)) || roster.Contains(Normalize(s.Name)))
                .ToList();
        }

        /// <summary>Tạo file rỗng kèm hướng dẫn ngay lần tra nhân viên đầu tiên.</summary>
        private static void EnsureFile()
        {
            try
            {
                string path = AppPaths.ActiveStaffFile;
                if (File.Exists(path)) return;

                Directory.CreateDirectory(Path.GetDirectoryName(path));
                // Có BOM: chủ bưu cục sẽ mở bằng Notepad, không BOM là tiếng Việt vỡ hết dấu.
                File.WriteAllLines(path, HeaderLines, Encoding.UTF8);
                AppLogger.Info($"[ActiveStaff] Đã tạo {path}");
            }
            catch (Exception ex)
            {
                AppLogger.Warning($"[ActiveStaff] Không tạo được danh sách: {ex.Message}");
            }
        }

        // Tên tiếng Việt gõ từ máy khác có thể ở dạng tổ hợp (NFD) trong khi JMS trả NFC —
        // nhìn giống hệt nhau nhưng so chuỗi thì khác. Đưa cả hai về NFC trước khi so.
        private static string Normalize(string text) =>
            string.IsNullOrWhiteSpace(text) ? "" : text.Trim().Normalize(NormalizationForm.FormC);
    }
}

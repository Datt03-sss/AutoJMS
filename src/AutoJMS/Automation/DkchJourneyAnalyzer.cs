using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace AutoJMS
{
    /// <summary>
    /// Hành động DKCH được quyết định từ lịch sử hành trình vận chuyển.
    /// </summary>
    public enum DkchAction
    {
        /// <summary>Đủ điều kiện — được phép bấm "Lưu và thêm mới".</summary>
        Register = 0,

        /// <summary>Đã đăng ký chuyển hoàn trong chu kỳ này — bỏ qua để không đăng ký trùng.</summary>
        SkipAlreadyRegistered = 1,

        /// <summary>
        /// "Chưa quét kiện vấn đề, vi phạm tự ý chuyển hoàn" — đơn đã "Giao lại hàng", sau đó có
        /// "Quét phát hàng" mà chưa có "Quét kiện vấn đề" phía sau → cấm đăng ký, không bấm Lưu.
        /// </summary>
        BlockedPendingProblemScan = 2,

        /// <summary>Không đủ dữ liệu hành trình để quyết định — không được đăng ký mù.</summary>
        BlockedNoData = 3
    }

    /// <summary>
    /// Kết quả phân tích lịch sử hành trình của một mã vận đơn.
    /// </summary>
    public sealed class DkchJourneyDecision
    {
        public DkchAction Action { get; set; } = DkchAction.BlockedNoData;
        public string Reason { get; set; } = "";

        /// <summary>Số lần "Đăng ký chuyển hoàn" xuất hiện trong toàn bộ lịch sử.</summary>
        public int RegisterCount { get; set; }

        /// <summary>Thời điểm "Đăng ký chuyển hoàn" gần nhất (null nếu chưa có).</summary>
        public DateTime? LastRegisterTime { get; set; }

        /// <summary>Thao tác gần nhất sau khi đã lọc bỏ nhiễu (tồn kho / lịch sử cuộc gọi).</summary>
        public string LastEventType { get; set; } = "";
        public string LastEventTime { get; set; } = "";

        /// <summary>Bưu cục của thao tác gần nhất (scanNetworkName).</summary>
        public string LastEventNetwork { get; set; } = "";

        /// <summary>Ghi chú/lý do của thao tác gần nhất (remark1).</summary>
        public string LastEventNote { get; set; } = "";

        /// <summary>
        /// Số lần "Quét phát hàng" (出仓次数 — "ca phát") trong chu kỳ hiện tại, tính từ sau lần
        /// "Đăng ký chuyển hoàn" gần nhất.
        /// <para>
        /// CHỈ để hiển thị/ghi log. KHÔNG dùng để chặn: mỗi đơn có yêu cầu số ca phát riêng và
        /// chỉ JMS biết ngưỡng đó (trả về lỗi <c>999010051 … 出仓次数：1/2</c> khi chưa đủ).
        /// </para>
        /// </summary>
        public int DeliveryAttemptCount { get; set; }

        /// <summary>Số lần "Giao lại hàng" (重派) trong toàn bộ lịch sử — "số lần phát lại".</summary>
        public int RedeliverCount { get; set; }

        /// <summary>Thời điểm "Quét kiện vấn đề" gần nhất (rỗng nếu chưa có).</summary>
        public string ProblemScanTime { get; set; } = "";

        /// <summary>Nguyên nhân kiện vấn đề của lần "Quét kiện vấn đề" gần nhất (remark1).</summary>
        public string ProblemScanReason { get; set; } = "";

        /// <summary>
        /// Chỉ có nghĩa khi <see cref="Action"/> = <see cref="DkchAction.BlockedPendingProblemScan"/>.
        /// <para>
        /// <c>false</c> — chu kỳ phát lại chưa từng "Quét kiện vấn đề":
        /// "Chưa quét kiện vấn đề, chặn đăng ký chuyển hoàn."
        /// </para>
        /// <para>
        /// <c>true</c> — đã "Quét kiện vấn đề" rồi lại "Quét phát hàng" tiếp (tự ý phát thêm ca):
        /// "Chưa quét kiện vấn đề, vi phạm tự ý chuyển hoàn, chặn đăng ký chuyển hoàn."
        /// </para>
        /// </summary>
        public bool SelfDispatchViolation { get; set; }

        /// <summary>Đã có "Đăng ký chuyển hoàn" sau lần "Quét kiện vấn đề" gần nhất.</summary>
        public bool AlreadyRegisteredThisCycle { get; set; }

        /// <summary>
        /// CHỈ hai trạng thái này mới không được bấm Lưu. Mọi trạng thái khác đều phải thử đăng ký.
        /// </summary>
        public bool IsBlocked => Action == DkchAction.BlockedPendingProblemScan
                              || Action == DkchAction.BlockedNoData;

        /// <summary>
        /// Được phép bấm Lưu.
        /// <para>
        /// Bao gồm cả <see cref="DkchAction.SkipAlreadyRegistered"/>: bản chất auto ĐKCH là tăng tốc
        /// thao tác tay, nên mã người dùng nhập LUÔN được thử đăng ký đúng 1 lần. Nếu đơn đã có mốc
        /// đăng ký, JMS sẽ tự từ chối và app đổi sang "Chuyển hoàn lần 2" — chính xác hơn việc app
        /// tự đoán rồi bỏ qua.
        /// </para>
        /// </summary>
        public bool ShouldRegister => !IsBlocked;

        /// <summary>Nhãn ngắn để hiển thị lên đầu tabDKCH_nowTracking.</summary>
        public string Badge => Action switch
        {
            DkchAction.Register => "✅ HỢP LỆ → Đăng ký chuyển hoàn",
            DkchAction.SkipAlreadyRegistered => "⏭ BỎ QUA — " + Reason,
            DkchAction.BlockedPendingProblemScan => "⛔ CHẶN — " + Reason,
            _ => "⚠ KHÔNG XÁC ĐỊNH — " + Reason
        };

        /// <summary>
        /// Tiêu đề ngắn (1 dòng) cho ô kết quả tabDKCH_result — dùng trước khi bấm Lưu.
        /// </summary>
        public string ShortHeadline => Action switch
        {
            DkchAction.Register => "✅ Đủ điều kiện đăng ký chuyển hoàn",
            DkchAction.SkipAlreadyRegistered => "⏭ Đã ghi nhận dữ liệu đăng ký chuyển hoàn",
            DkchAction.BlockedPendingProblemScan => SelfDispatchViolation
                ? "⛔ Chưa quét kiện vấn đề, vi phạm tự ý chuyển hoàn, chặn đăng ký chuyển hoàn"
                : "⛔ Chưa quét kiện vấn đề, chặn đăng ký chuyển hoàn",
            _ => "⚠ Không đủ dữ liệu hành trình"
        };

        /// <summary>Một dòng mô tả thao tác cuối cùng, kèm bưu cục và ghi chú nếu có.</summary>
        public string LastActionLine
        {
            get
            {
                if (string.IsNullOrWhiteSpace(LastEventType)) return "(chưa có thao tác)";

                // Định dạng khớp với ô kết quả: "<thao tác> | <thời gian>" rồi ghi chú thụt vào
                // dòng dưới. Bỏ bưu cục để dòng ngắn, đỡ tràn trong ô hẹp.
                string line = LastEventType;
                if (!string.IsNullOrWhiteSpace(LastEventTime)) line += " | " + LastEventTime;
                if (!string.IsNullOrWhiteSpace(LastEventNote)) line += Environment.NewLine + "   " + LastEventNote;
                return line;
            }
        }
    }

    /// <summary>
    /// Đọc lịch sử hành trình vận chuyển và quyết định có được đăng ký chuyển hoàn hay không.
    /// <para>
    /// Quy tắc nghiệp vụ (chủ sở hữu xác nhận):
    /// </para>
    /// <list type="number">
    /// <item>
    /// CHỈ CHẶN đúng một trạng thái, và chỉ với đơn ĐÃ CÓ "Giao lại hàng" (重派) trong hành trình:
    /// sau "Giao lại hàng" đã có "Quét phát hàng" (出仓扫描) mà chưa có "Quét kiện vấn đề"
    /// (问题件扫描) phía sau → <b>"Chưa quét kiện vấn đề, vi phạm tự ý chuyển hoàn"</b>, không bấm Lưu.
    /// <para>
    /// Áp dụng cho cả chuỗi nhiều ca phát: 重派 → 出仓扫描 → 问题件扫描 → 出仓扫描 vẫn bị chặn,
    /// vì lần "Quét phát hàng" cuối chưa được kiện.
    /// </para>
    /// <para>
    /// KHÔNG chặn theo số ca phát (出仓次数). Mỗi đơn có ngưỡng riêng và chỉ JMS biết — nếu chưa đủ,
    /// server trả <c>999010051: 此单的出仓次数不满足登记条件，出仓次数：1/2</c> và ta hiển thị lỗi đó.
    /// </para>
    /// <para>
    /// Khác bản trước: bản trước chặn MỌI "Quét phát hàng" chưa có "Quét kiện vấn đề" (kể cả
    /// ca phát đầu tiên, chưa từng "Giao lại hàng"), làm nhiều đơn hợp lệ bị bỏ oan.
    /// </para>
    /// </item>
    /// <item>
    /// Mỗi chu kỳ chỉ đăng ký chuyển hoàn ĐÚNG 1 LẦN. Nếu đã có "Đăng ký chuyển hoàn"
    /// sau lần "Quét kiện vấn đề" gần nhất thì bỏ qua — đây là chốt chống spam.
    /// </item>
    /// </list>
    /// <para>
    /// Class này thuần logic (không phụ thuộc WebView2/UI) để có thể kiểm thử độc lập.
    /// Không tin thứ tự phần tử trả về từ API: luôn tự sắp xếp theo uploadTime/scanTime,
    /// giống quy ước sẵn có trong <see cref="WaybillTrackingService"/>.
    /// </para>
    /// </summary>
    public static class DkchJourneyAnalyzer
    {
        private enum Kind
        {
            Other = 0,
            Noise,
            Arrival,
            DispatchScan,   // 出仓扫描 / Quét phát hàng
            ProblemScan,    // 问题件扫描 / Quét kiện vấn đề
            Redeliver,      // 重派 / Giao lại hàng
            ReturnRegister, // 退件登记, 再次登记 / Đăng ký chuyển hoàn
            SignedCpn,      // 快件签收 / Ký nhận CPN
            SignedReturn    // 退件签收 / Ký nhận chuyển hoàn
        }

        /// <summary>
        /// Phân tích danh sách chi tiết hành trình để ra quyết định DKCH.
        /// </summary>
        public static DkchJourneyDecision Analyze(IEnumerable<WaybillDetail> details)
        {
            var list = details?.Where(d => d != null).ToList() ?? new List<WaybillDetail>();
            if (list.Count == 0)
            {
                return new DkchJourneyDecision
                {
                    Action = DkchAction.BlockedNoData,
                    Reason = "Không có dữ liệu hành trình — không đăng ký để tránh sai nghiệp vụ."
                };
            }

            // Sắp xếp cũ → mới, bỏ nhiễu (kiểm tra tồn kho, lịch sử cuộc gọi).
            var timeline = list
                .Select(d => new { Detail = d, EventKind = Classify(d.scanTypeName), Time = ParseTime(d) })
                .Where(x => x.EventKind != Kind.Noise)
                .OrderBy(x => x.Time)
                .ToList();

            if (timeline.Count == 0)
            {
                return new DkchJourneyDecision
                {
                    Action = DkchAction.BlockedNoData,
                    Reason = "Lịch sử chỉ có thao tác nhiễu (tồn kho/cuộc gọi) — không đủ căn cứ."
                };
            }

            var kinds = timeline.Select(x => x.EventKind).ToList();
            int lastDispatch = LastIndexOf(kinds, Kind.DispatchScan);
            int lastProblem = LastIndexOf(kinds, Kind.ProblemScan);
            int lastRegister = LastIndexOf(kinds, Kind.ReturnRegister);

            // "Giao lại hàng" gần nhất NẰM TRƯỚC lần "Quét phát hàng" cuối cùng.
            // Không dùng lastRedeliver: chuỗi 重派 → 出仓扫描 → 重派 vẫn phải bị chặn vì lần
            // "Quét phát hàng" đó đã có "Giao lại hàng" đứng trước mà chưa được kiện.
            int redeliverBeforeDispatch = -1;
            for (int i = 0; i < lastDispatch; i++)
                if (kinds[i] == Kind.Redeliver) redeliverBeforeDispatch = i;

            var last = timeline[timeline.Count - 1];
            var registerEvents = timeline.Where(x => x.EventKind == Kind.ReturnRegister).ToList();

            // "Ca phát" của chu kỳ hiện tại = số "Quét phát hàng" sau lần đăng ký gần nhất.
            int dispatchInCycle = 0;
            for (int i = lastRegister + 1; i < kinds.Count; i++)
                if (kinds[i] == Kind.DispatchScan) dispatchInCycle++;

            var decision = new DkchJourneyDecision
            {
                RegisterCount = registerEvents.Count,
                LastRegisterTime = registerEvents.Count > 0 ? registerEvents[registerEvents.Count - 1].Time : (DateTime?)null,
                LastEventType = last.Detail.scanTypeName ?? "",
                LastEventTime = last.Detail.uploadTime ?? last.Detail.scanTime ?? "",
                LastEventNetwork = last.Detail.scanNetworkName ?? "",
                LastEventNote = last.Detail.remark1 ?? "",
                DeliveryAttemptCount = dispatchInCycle,
                RedeliverCount = kinds.Count(k => k == Kind.Redeliver),
                ProblemScanTime = lastProblem >= 0 ? FormatTime(timeline[lastProblem].Detail) : "",
                ProblemScanReason = lastProblem >= 0 ? (timeline[lastProblem].Detail.remark1 ?? "") : "",
                AlreadyRegisteredThisCycle = lastRegister >= 0 && lastRegister > lastProblem
            };

            // ── Rule 1: "Phát lại có quét phát chưa kiện" → CHẶN ────────────────────────
            // Điều kiện ĐẦY ĐỦ (cả 3 phải đúng):
            //   (a) đã có "Giao lại hàng",
            //   (b) sau đó có "Quét phát hàng",
            //   (c) sau lần "Quét phát hàng" đó CHƯA có "Quét kiện vấn đề".
            // Không có "Giao lại hàng" thì KHÔNG chặn — cứ đăng ký chuyển hoàn.
            if (redeliverBeforeDispatch >= 0 && lastProblem < lastDispatch)
            {
                // Trong chu kỳ phát lại này đã từng "Quét kiện vấn đề" rồi mà vẫn "Quét phát hàng"
                // thêm ca nữa → tự ý phát thêm, nặng hơn trường hợp chưa kiện lần nào.
                bool problemInsideCycle = false;
                for (int i = redeliverBeforeDispatch + 1; i < lastDispatch; i++)
                    if (kinds[i] == Kind.ProblemScan) { problemInsideCycle = true; break; }

                decision.Action = DkchAction.BlockedPendingProblemScan;
                decision.SelfDispatchViolation = problemInsideCycle;
                decision.Reason =
                    (problemInsideCycle
                        ? "Chưa quét kiện vấn đề, vi phạm tự ý chuyển hoàn, chặn đăng ký chuyển hoàn: "
                        : "Chưa quét kiện vấn đề, chặn đăng ký chuyển hoàn: ") +
                    $"sau 'Giao lại hàng' ({FormatTime(timeline[redeliverBeforeDispatch].Detail)}) " +
                    $"đã có 'Quét phát hàng' ({FormatTime(timeline[lastDispatch].Detail)}) " +
                    "nhưng chưa có 'Quét kiện vấn đề' phía sau.";
                return decision;
            }

            // ── Rule 2: đã đăng ký trong chu kỳ này → BỎ QUA (chống spam) ───────────────
            if (decision.AlreadyRegisteredThisCycle)
            {
                decision.Action = DkchAction.SkipAlreadyRegistered;
                decision.Reason =
                    $"Đã ghi nhận dữ liệu đăng ký chuyển hoàn lúc {FormatTime(timeline[lastRegister].Detail)} " +
                    $"(tổng {decision.RegisterCount} lần) — bỏ qua, không đăng ký trùng.";
                return decision;
            }

            // ── Đủ điều kiện ───────────────────────────────────────────────────────────
            decision.Action = DkchAction.Register;
            decision.Reason = lastProblem >= 0
                ? $"Có 'Quét kiện vấn đề' lúc {FormatTime(timeline[lastProblem].Detail)} và chưa đăng ký chuyển hoàn."
                : "Chưa có dấu đăng ký chuyển hoàn và không vướng trạng thái phát lại.";
            return decision;
        }

        /// <summary>
        /// Xác minh sau khi bấm Lưu: lịch sử mới có phát sinh thêm "Đăng ký chuyển hoàn"
        /// so với ảnh chụp trước đó hay không. Dùng khi response bị timeout để tránh Lưu lại 2 lần.
        /// </summary>
        public static bool RegistrationLanded(DkchJourneyDecision before, DkchJourneyDecision after)
        {
            if (after == null) return false;
            if (before == null) return after.RegisterCount > 0;

            if (after.RegisterCount > before.RegisterCount) return true;

            // Cùng số lần nhưng mốc thời gian mới hơn → vẫn coi là đã ghi nhận.
            if (after.LastRegisterTime.HasValue &&
                (!before.LastRegisterTime.HasValue || after.LastRegisterTime > before.LastRegisterTime))
                return true;

            // Trước đó chưa đăng ký, giờ analyzer đã báo "đã đăng ký trong chu kỳ này".
            return !before.AlreadyRegisteredThisCycle && after.AlreadyRegisteredThisCycle;
        }

        private static int LastIndexOf(IEnumerable<Kind> kinds, Kind target)
        {
            int index = -1, i = 0;
            foreach (var k in kinds)
            {
                if (k == target) index = i;
                i++;
            }
            return index;
        }

        private static DateTime ParseTime(WaybillDetail d)
        {
            string raw = !string.IsNullOrWhiteSpace(d.uploadTime) ? d.uploadTime : d.scanTime;

            // InvariantCulture: mốc thời gian JMS luôn là "yyyy-MM-dd HH:mm:ss", không được
            // phụ thuộc Culture của máy chạy (nếu parse trượt, timeline sẽ bị đảo lộn).
            return DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt)
                ? dt
                : DateTime.MinValue;
        }

        private static string FormatTime(WaybillDetail d)
        {
            string raw = d.uploadTime ?? d.scanTime ?? "";
            return string.IsNullOrWhiteSpace(raw) ? "không rõ thời gian" : raw;
        }

        /// <summary>
        /// Phân loại thao tác. API có thể trả về tiếng Trung (thô) hoặc tiếng Việt (đã dịch),
        /// nên phải nhận diện cả hai. Thứ tự kiểm tra rất quan trọng vì nhiều nhãn chồng chuỗi
        /// ("退件登记" / "退件签收" / "退件扫描" đều chứa "退件";
        ///  "Đăng ký chuyển hoàn" / "Xác nhận chuyển hoàn" / "In đơn chuyển hoàn" đều chứa "chuyển hoàn").
        /// </summary>
        private static Kind Classify(string scanTypeName)
        {
            if (string.IsNullOrWhiteSpace(scanTypeName)) return Kind.Other;
            string t = scanTypeName.Trim();

            // Nhiễu — loại khỏi timeline.
            if (Has(t, "库存盘点", "Kiểm tra hàng tồn kho", "hàng tồn kho") ||
                Has(t, "派件电联", "Lịch sử cuộc gọi", "cuộc gọi-phát"))
                return Kind.Noise;

            // Đăng ký chuyển hoàn (kể cả lần 2) — kiểm tra TRƯỚC các nhãn "chuyển hoàn" khác.
            if (Has(t, "退件登记", "再次登记", "Đăng ký chuyển hoàn"))
                return Kind.ReturnRegister;

            if (Has(t, "退件签收", "Ký nhận chuyển hoàn")) return Kind.SignedReturn;
            if (Has(t, "快件签收", "Ký nhận CPN")) return Kind.SignedCpn;

            // Quét kiện vấn đề — kiểm tra trước "Quét phát hàng" cho an toàn.
            if (Has(t, "问题件扫描", "Quét kiện vấn đề", "Kiện vấn đề", "kiện vấn đề"))
                return Kind.ProblemScan;

            if (Has(t, "出仓扫描", "Quét phát hàng")) return Kind.DispatchScan;
            if (Has(t, "重派", "Giao lại hàng")) return Kind.Redeliver;

            if (Has(t, "卸车到件", "到件", "Xuống hàng kiện đến", "Xuống kiện"))
                return Kind.Arrival;

            return Kind.Other;
        }

        private static bool Has(string haystack, params string[] needles)
        {
            foreach (var n in needles)
            {
                if (haystack.IndexOf(n, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            }
            return false;
        }
    }
}

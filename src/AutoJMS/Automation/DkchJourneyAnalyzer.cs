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
        BlockedNoData = 3,

        /// <summary>
        /// "Quét kiện vấn đề" ghi nguyên nhân đổi địa chỉ → đơn CHUYỂN TIẾP tới địa chỉ
        /// mới, không phải chuyển hoàn. Cấm đăng ký, không bấm Lưu.
        /// </summary>
        BlockedForward = 4,

        /// <summary>
        /// Thao tác cuối là "Ký nhận CPN" — hàng đã tới tay khách. Không đăng ký chuyển
        /// hoàn; việc còn lại là in đơn hoàn 1 phần.
        /// </summary>
        BlockedSignedCpn = 5,

        /// <summary>
        /// Thao tác cuối là "Đang chuyển hoàn" — đơn đã trên đường hoàn. Không đăng ký lại;
        /// việc còn lại là in bill chuyển hoàn.
        /// </summary>
        BlockedReturning = 6,

        /// <summary>
        /// Thao tác cuối là "Xuống hàng kiện đến"/"Gỡ bao" mà hành trình CHƯA có
        /// "Quét phát hàng" lần nào — hàng vừa về kho, chưa từng đem đi phát nên không có
        /// gì để hoàn. Cấm đăng ký, không bấm Lưu.
        /// </summary>
        BlockedNewArrival = 7
    }

    /// <summary>
    /// Kết quả phân tích lịch sử hành trình của một mã vận đơn.
    /// </summary>
    /// <summary>Một mốc trong hành trình, đã tách sẵn các phần để vẽ.</summary>
    public sealed class DkchJourneyEntry
    {
        /// <summary>Tên thao tác — "Quét kiện vấn đề", "Quét phát hàng"…</summary>
        public string Type { get; set; } = "";

        /// <summary>Tên bưu tá thực hiện.</summary>
        public string Operator { get; set; } = "";

        /// <summary>Nguyên nhân/ghi chú (remark1) — vẽ ở dòng "↳ …".</summary>
        public string Note { get; set; } = "";

        /// <summary>Giờ hiển thị bên phải, dạng HH:mm.</summary>
        public string Time { get; set; } = "";

        /// <summary>Ngày tháng, dạng dd/MM — dùng cho mốc gần nhất.</summary>
        public string Date { get; set; } = "";

        /// <summary>Mốc cần làm nổi bật (kiện vấn đề / đăng ký chuyển hoàn).</summary>
        public bool Highlight { get; set; }
    }

    public enum DkchStepState
    {
        Pending = 0,
        Done,
        Current
    }

    /// <summary>Một mốc trên thanh "Tiến trình".</summary>
    public sealed class DkchStep
    {
        public string Label { get; set; } = "";
        public DkchStepState State { get; set; } = DkchStepState.Pending;
    }

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
        /// Bị chặn ở nhánh KHÔNG có "Giao lại hàng" — đơn mới quét phát lần đầu mà chưa
        /// kiện vấn đề. Tách riêng để tab2config.json ra được thông điệp khác với nhánh
        /// có "Giao lại hàng".
        /// </summary>
        public bool NoRedeliverBeforeDispatch { get; set; }

        /// <summary>
        /// Tên bưu tá của thao tác gần nhất. Xem OperatorOf: thường là scanByName,
        /// riêng "Quét phát hàng" thì lấy staffName trước.
        /// Thiết kế Newbill in tên này ngay dưới trạng thái, thay cho nhãn "BƯU TÁ".
        /// </summary>
        public string LastEventOperator { get; set; } = "";

        /// <summary>
        /// Số ngày kể từ mốc "kiện đến" gần nhất tới hôm nay. <c>null</c> khi hành trình
        /// không có mốc kiện đến — khi đó huy hiệu "ngày tồn" bị ẩn thay vì hiện số sai.
        /// </summary>
        public int? DaysInStock { get; set; }

        /// <summary>Hành trình đã chuẩn hoá, mới nhất đứng trước — để vẽ danh sách.</summary>
        public List<DkchJourneyEntry> Entries { get; set; } = new List<DkchJourneyEntry>();

        /// <summary>
        /// Các mốc của thanh "Tiến trình". Danh sách mốc và cách tô sẽ được đưa vào
        /// tab2config.json cùng đợt cấu hình các trường hợp báo lỗi; hiện tại giữ cơ bản n/x.
        /// </summary>
        public List<DkchStep> Steps { get; set; } = new List<DkchStep>();

        /// <summary>Số mốc đã đạt / tổng số mốc — phần "n/x" của thanh Tiến trình.</summary>
        public int StepsDone => Steps.Count(s => s.State == DkchStepState.Done);

        /// <summary>
        /// CHỈ sáu trạng thái này mới không được bấm Lưu. Mọi trạng thái khác đều phải thử đăng ký.
        /// </summary>
        public bool IsBlocked => Action == DkchAction.BlockedPendingProblemScan
                              || Action == DkchAction.BlockedNoData
                              || Action == DkchAction.BlockedForward
                              || Action == DkchAction.BlockedSignedCpn
                              || Action == DkchAction.BlockedReturning
                              || Action == DkchAction.BlockedNewArrival;

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
            DkchAction.BlockedForward => "⛔ CHẶN — " + Reason,
            DkchAction.BlockedSignedCpn => "⛔ CHẶN — " + Reason,
            DkchAction.BlockedReturning => "⛔ CHẶN — " + Reason,
            DkchAction.BlockedNewArrival => "⛔ CHẶN — " + Reason,
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
            DkchAction.BlockedForward => "⛔ Đơn chuyển tiếp — không đăng ký chuyển hoàn",
            DkchAction.BlockedSignedCpn => "⛔ Đơn đã ký nhận — không đăng ký chuyển hoàn",
            DkchAction.BlockedReturning => "⛔ Đơn đang chuyển hoàn — không đăng ký lại",
            DkchAction.BlockedNewArrival => "⛔ Đơn mới tới chưa Quét phát — không đăng ký chuyển hoàn",
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
    /// CHẶN mọi đơn đã có "Quét phát hàng" (出仓扫描) mà chưa có "Quét kiện vấn đề" (问题件扫描)
    /// phía sau — kể cả ca phát ĐẦU TIÊN, chưa từng "Giao lại hàng" (重派). Không bấm Lưu.
    /// <para>
    /// Có "Giao lại hàng" đứng trước lần quét phát cuối hay không CHỈ đổi thông điệp, không đổi
    /// việc chặn — xem <see cref="DkchJourneyDecision.NoRedeliverBeforeDispatch"/> và
    /// <see cref="DkchJourneyDecision.SelfDispatchViolation"/>; ba nhánh map sang ba outcome
    /// riêng trong <c>modules/tab2config.json</c>.
    /// </para>
    /// <para>
    /// Ngoại lệ: đơn đã "Ký nhận CPN"/"Ký nhận chuyển hoàn" SAU lần quét phát cuối thì không áp
    /// luật này — hàng đã tới tay khách, việc còn lại là in đơn hoàn một phần.
    /// </para>
    /// <para>
    /// Áp dụng cho cả chuỗi nhiều ca phát: 重派 → 出仓扫描 → 问题件扫描 → 出仓扫描 vẫn bị chặn,
    /// vì lần "Quét phát hàng" cuối chưa được kiện.
    /// </para>
    /// <para>
    /// KHÔNG chặn theo số ca phát (出仓次数). Mỗi đơn có ngưỡng riêng và chỉ JMS biết — nếu chưa đủ,
    /// server trả <c>999010051: 此单的出仓次数不满足登记条件，出仓次数：1/2</c> và ta hiển thị lỗi đó.
    /// </para>
    /// <para>
    /// Khác bản trước: bản trước chỉ chặn khi có "Giao lại hàng" đứng trước lần quét phát, nên
    /// đơn ở ca phát đầu lọt qua Rule 1 rồi bị Rule 2 báo nhầm "Đã đăng ký chuyển hoàn".
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

            // Sắp xếp cũ → mới.
            //
            // TIÊU CHÍ PHỤ rất quan trọng: JMS trả về danh sách MỚI NHẤT TRƯỚC, và nhiều
            // mốc trùng nhau tới từng giây ("Đang chuyển hoàn" và "Đăng ký CH lần 2" cùng
            // 10:55). OrderBy của LINQ ổn định nên khi trùng khoá nó giữ nguyên thứ tự
            // đầu vào — tức là giữ nguyên chiều MỚI→CŨ, ngược với chiều ta đang xếp. Hậu
            // quả: cặp trùng giờ bị lộn, "thao tác cuối" lấy nhầm mốc cũ hơn.
            // Đảo chỉ số gốc làm khoá phụ thì cặp trùng giờ về đúng chiều cũ→mới.
            var indexed = list
                .Select((d, idx) => new { Detail = d, Idx = idx, EventKind = Classify(d.scanTypeName), Time = ParseTime(d) })
                .OrderBy(x => x.Time)
                .ThenByDescending(x => x.Idx)
                .ToList();

            // Danh sách ĐẦY ĐỦ để hiển thị (giống hệt JMS), và bản đã lọc nhiễu
            // (kiểm tra tồn kho, lịch sử cuộc gọi) để ra quyết định nghiệp vụ.
            var timeline = indexed.Where(x => x.EventKind != Kind.Noise).ToList();

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
                LastEventNote = ShowsNote(last.EventKind) ? (last.Detail.remark1 ?? "") : "",
                LastEventOperator = OperatorOf(last.Detail),
                DeliveryAttemptCount = dispatchInCycle,
                RedeliverCount = kinds.Count(k => k == Kind.Redeliver),
                ProblemScanTime = lastProblem >= 0 ? FormatTime(timeline[lastProblem].Detail) : "",
                ProblemScanReason = lastProblem >= 0 ? (timeline[lastProblem].Detail.remark1 ?? "") : "",
                AlreadyRegisteredThisCycle = lastRegister >= 0 && lastRegister > lastProblem
            };

            // ── Dữ liệu để VẼ (không tham gia quyết định nghiệp vụ) ────────────────────
            // Ngày tồn: tính từ mốc "kiện đến" GẦN NHẤT. Không có mốc đó thì để null và
            // huy hiệu sẽ bị ẩn — thà thiếu một huy hiệu còn hơn hiện con số bịa.
            int lastArrival = LastIndexOf(kinds, Kind.Arrival);
            if (lastArrival >= 0)
            {
                var arrivedAt = timeline[lastArrival].Time;
                if (arrivedAt != DateTime.MinValue)
                {
                    // Đếm TRỌN NGÀY theo lịch, tính cả ngày kiện đến và cả hôm nay.
                    // 10/08 23:59 → 12/08 00:00 phải ra 3 (10, 11, 12) chứ không phải 2:
                    // chủ dự án đếm số ngày đơn nằm trong kho, không đếm số lần qua đêm.
                    // So bằng .Date nên giờ trong ngày không ảnh hưởng — 23:59 hay 00:00
                    // cùng ngày đều cho một kết quả.
                    int days = (DateTime.Now.Date - arrivedAt.Date).Days + 1;
                    // Đã cộng 1 thì giá trị nhỏ nhất hợp lý là 1 (đến hôm nay). Ra 0 hoặc
                    // âm chỉ có thể do mốc thời gian ở tương lai (lệch giờ máy / sai dữ
                    // liệu) — lúc đó ẩn huy hiệu, thà thiếu còn hơn hiện số bịa.
                    if (days >= 1) decision.DaysInStock = days;
                }
            }

            // Hành trình: mới nhất đứng trước, DÙNG BẢN ĐÃ LỌC NHIỄU và chỉ tính từ lần
            // hàng VỀ KHO gần nhất. Chặng vận chuyển trước đó (Hà Nội → Lào Cai → …) không
            // giúp gì cho việc quyết định chuyển hoàn mà đẩy các mốc cần xem ra khỏi tầm.
            // Mốc "về kho" gần nhất: "Xuống hàng kiện đến" hoặc "Gỡ bao" tại chính bưu cục
            // (214A02 / (LCI)Kim Tân). Không tìm thấy thì lấy từ đầu — thà hiện đủ còn hơn trống.
            int arrival = 0;
            for (int i = timeline.Count - 1; i >= 0; i--)
            {
                var d = timeline[i].Detail;
                string type = d.scanTypeName ?? "";
                bool isArrival = type.Contains("Xuống hàng kiện đến") || type.Contains("Xuống kiện")
                              || type.Contains("Gỡ bao") || type.Contains("卸车到件")
                              || type.Contains("到件") || type.Contains("拆包");
                if (!isArrival) continue;
                string net = d.scanNetworkName ?? "";
                string netCode = d.scanNetworkCode ?? "";
                if (net.Contains("Kim Tân") || net.Contains("(LCI)") || netCode == "214A02")
                {
                    arrival = i;
                    break;
                }
            }
            for (int i = timeline.Count - 1; i >= arrival; i--)
            {
                var item = timeline[i];
                string stamp = FormatTime(item.Detail);
                decision.Entries.Add(new DkchJourneyEntry
                {
                    Type = item.Detail.scanTypeName ?? "",
                    Operator = OperatorOf(item.Detail),
                    Note = ShowsNote(item.EventKind) ? (item.Detail.remark1 ?? "") : "",
                    // Ép văn hoá bất biến: máy có DateSeparator khác sẽ ra "09-08" / "08.44".
                    Time = item.Time != DateTime.MinValue
                        ? item.Time.ToString("HH':'mm", CultureInfo.InvariantCulture) : stamp,
                    Date = item.Time != DateTime.MinValue
                        ? item.Time.ToString("dd'/'MM", CultureInfo.InvariantCulture) : "",
                    Highlight = item.EventKind == Kind.ProblemScan || item.EventKind == Kind.ReturnRegister
                });
            }

            decision.Steps = BuildSteps(kinds);

            // ── Rule 0: TRẠNG THÁI ĐƠN — ba ca không đăng ký chuyển hoàn được ─────────
            // Cả ba xét TRƯỚC mọi luật đếm mốc quét, vì chúng nói đơn ĐANG Ở ĐÂU chứ không
            // nói thiếu mốc nào. Đặt sau thì đơn đã kiện vấn đề đầy đủ sẽ rơi xuống nhánh
            // "Đủ điều kiện" ở cuối và app bấm Lưu — đúng cái phải tránh.
            //
            // Thứ tự trong nhóm: hai luật theo THAO TÁC CUỐI đứng trước luật theo nguyên
            // nhân kiện vấn đề, vì thao tác cuối là sự thật mới nhất. Đơn đổi địa chỉ rồi
            // vẫn có thể ký nhận; lúc đó "đã ký nhận" mới là trạng thái đúng để báo.

            // (a) Ký nhận CPN — hàng đã tới tay khách, còn lại là in đơn hoàn 1 phần.
            if (last.EventKind == Kind.SignedCpn)
            {
                decision.Action = DkchAction.BlockedSignedCpn;
                decision.Reason =
                    $"Đơn đã ký nhận lúc {FormatTime(last.Detail)} — không đăng ký chuyển hoàn.";
                return decision;
            }

            // (b) Đang chuyển hoàn — đơn đã trên đường hoàn, còn lại là in bill.
            if (IsReturningName(last.Detail.scanTypeName))
            {
                decision.Action = DkchAction.BlockedReturning;
                decision.Reason =
                    $"Đơn đang chuyển hoàn (mốc {FormatTime(last.Detail)}) — không đăng ký lại.";
                return decision;
            }

            // (c) Hàng vừa về kho mà CHƯA TỪNG "Quét phát hàng" → chưa có gì để hoàn.
            //     Chỉ chặn ở nhánh này. Nếu đã từng quét phát thì để logic bình thường
            //     quyết định (Rule 1/2) — chủ dự án chốt vậy; riêng dòng gợi ý được đè
            //     thành "Kiểm tra lại hành trình" qua bảng actionRecommends trong config,
            //     nên không phải nhân bản câu đó vào từng case.
            if (last.EventKind == Kind.Arrival && lastDispatch < 0)
            {
                decision.Action = DkchAction.BlockedNewArrival;
                decision.Reason =
                    $"Đơn mới tới ({FormatTime(last.Detail)}) và chưa có 'Quét phát hàng' " +
                    "lần nào — chưa đăng ký chuyển hoàn được.";
                return decision;
            }

            // (d) Kiện vấn đề vì ĐỔI ĐỊA CHỈ → đơn chuyển tiếp.
            if (lastProblem >= 0 && IsForwardReason(decision.ProblemScanReason))
            {
                decision.Action = DkchAction.BlockedForward;
                decision.Reason =
                    "Đơn chuyển tiếp: 'Quét kiện vấn đề' lúc " +
                    $"{FormatTime(timeline[lastProblem].Detail)} ghi nguyên nhân " +
                    $"'{decision.ProblemScanReason.Trim()}' — không đăng ký chuyển hoàn.";
                return decision;
            }

            // ── Rule 1: "Quét phát hàng mà chưa kiện vấn đề" → CHẶN ────────────────────
            // Sau một lần "Quét phát hàng" mà chưa có "Quét kiện vấn đề" thì KHÔNG thể
            // đăng ký chuyển hoàn. Trước đây chỉ chặn khi có thêm "Giao lại hàng" đứng
            // trước, nên đơn ca phát đầu lọt qua rồi báo nhầm "Đã đăng ký chuyển hoàn".
            // Hai nhánh, thông điệp khác nhau:
            //   (a) có "Giao lại hàng" trước đó → như cũ (kèm nhánh vi phạm tự ý phát thêm)
            //   (b) không có "Giao lại hàng"    → "Chưa kiện vấn đề không thể đăng ký chuyển hoàn"
            // Đơn ĐÃ KÝ NHẬN sau lần quét phát thì không áp luật "chưa kiện vấn đề":
            // hàng đã tới tay khách, nghiệp vụ chuyển sang in đơn hoàn một phần, không
            // cần kiện vấn đề nữa. Chỉ tính lần ký nhận ĐỨNG SAU lần quét phát cuối.
            int lastSigned = Math.Max(LastIndexOf(kinds, Kind.SignedCpn),
                                      LastIndexOf(kinds, Kind.SignedReturn));
            bool signedAfterDispatch = lastSigned >= 0 && lastSigned > lastDispatch;

            if (lastDispatch >= 0 && lastProblem < lastDispatch && !signedAfterDispatch)
            {
                decision.NoRedeliverBeforeDispatch = redeliverBeforeDispatch < 0;
                // Trong chu kỳ phát lại này đã từng "Quét kiện vấn đề" rồi mà vẫn "Quét phát hàng"
                // thêm ca nữa → tự ý phát thêm, nặng hơn trường hợp chưa kiện lần nào.
                bool problemInsideCycle = false;
                for (int i = Math.Max(0, redeliverBeforeDispatch) + 1; i < lastDispatch; i++)
                    if (kinds[i] == Kind.ProblemScan) { problemInsideCycle = true; break; }

                decision.Action = DkchAction.BlockedPendingProblemScan;
                decision.SelfDispatchViolation = redeliverBeforeDispatch >= 0 && problemInsideCycle;

                if (redeliverBeforeDispatch < 0)
                {
                    decision.Reason =
                        "Chưa kiện vấn đề không thể đăng ký chuyển hoàn: " +
                        $"đã có 'Quét phát hàng' ({FormatTime(timeline[lastDispatch].Detail)}) " +
                        "nhưng chưa có 'Quét kiện vấn đề' phía sau.";
                    return decision;
                }

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

        /// <summary>
        /// Bộ mốc rỗng để thanh "Tiến trình" vẫn hiện đủ 6 ô lúc chưa xử lý mã nào,
        /// thay vì co lại thành "0/1" như khi truyền danh sách rỗng.
        /// </summary>
        public static List<DkchStep> DefaultSteps()
        {
            var steps = BuildSteps(new List<Kind>());
            foreach (var s in steps) s.State = DkchStepState.Pending;
            return steps;
        }

        /// <summary>
        /// Nguyên nhân kiện vấn đề khiến đơn thành CHUYỂN TIẾP thay vì chuyển hoàn.
        /// Khách đổi địa chỉ thì hàng đi tiếp tới địa chỉ mới — đăng ký chuyển hoàn là sai
        /// nghiệp vụ. Để dạng danh sách cho dễ mở rộng; CHƯA có biến thể tiếng Trung vì
        /// chưa thấy trong dữ liệu thật, không đoán bừa rồi thành cấu hình chết.
        /// </summary>
        private static readonly string[] ForwardReasons =
        {
            "Thay đổi địa chỉ giao hàng"
        };

        /// <summary>
        /// Tên thao tác nghĩa là "đơn đang trên đường chuyển hoàn". Phải so THEO TÊN vì
        /// Classify xếp thao tác này vào Kind.Other — nó không phải mốc nghiệp vụ nào.
        /// "Đang" khác "Đăng" nên chuỗi này không đụng "Đăng ký chuyển hoàn".
        /// CHƯA có biến thể tiếng Trung vì chưa thấy trong dữ liệu thật.
        /// </summary>
        private static readonly string[] ReturningNames =
        {
            "Đang chuyển hoàn"
        };

        private static bool IsReturningName(string scanTypeName)
        {
            if (string.IsNullOrWhiteSpace(scanTypeName)) return false;
            foreach (string phrase in ReturningNames)
                if (scanTypeName.IndexOf(phrase, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            return false;
        }

        private static bool IsForwardReason(string reason)
        {
            if (string.IsNullOrWhiteSpace(reason)) return false;
            foreach (string phrase in ForwardReasons)
                if (reason.IndexOf(phrase, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            return false;
        }

        /// <summary>
        /// Thao tác nào được phép hiện dòng "↳ …". Chỉ "Quét kiện vấn đề" và "Giao lại
        /// hàng" — ở hai thao tác đó remark1 là nguyên nhân thật, cần đọc. Các thao tác
        /// khác cũng có remark1 nhưng là ghi chú hệ thống ("Trả về địa chỉ người gửi"),
        /// hiện ra chỉ làm hành trình dài và loãng.
        /// Một vị từ dùng cho CẢ hành trình lẫn thẻ kết quả, để không thành hai quy tắc
        /// rời nhau rồi lệch nhau về sau.
        /// </summary>
        private static bool ShowsNote(Kind kind)
            => kind == Kind.ProblemScan || kind == Kind.Redeliver;

        /// <summary>
        /// Tên bưu tá. Mặc định lấy scanByName, NHƯNG riêng "Quét phát hàng" thì đảo lại
        /// lấy staffName trước — ở thao tác đó scanByName là người đứng máy quét trong kho,
        /// còn người thật sự đi phát nằm ở staffName. Dùng Classify thay vì so chuỗi để
        /// "出仓扫描" cũng được tính, không phải viết lại danh sách tên thao tác lần nữa.
        /// Hai chiều đều có dự phòng: trường ưu tiên rỗng thì vẫn lấy trường còn lại,
        /// thà hiện tên kia còn hơn để trống hàng bưu tá.
        /// </summary>
        private static string OperatorOf(WaybillDetail d)
        {
            if (d == null) return "";

            bool dispatch = Classify(d.scanTypeName) == Kind.DispatchScan;
            string first = dispatch ? d.staffName : d.scanByName;
            string second = dispatch ? d.scanByName : d.staffName;

            if (!string.IsNullOrWhiteSpace(first)) return first.Trim();
            if (!string.IsNullOrWhiteSpace(second)) return second.Trim();
            return "";
        }

        /// <summary>
        /// Sáu mốc của thanh "Tiến trình". Đây mới là bản CƠ BẢN: mốc nào có mặt trong
        /// hành trình thì tô Done, mốc chưa đạt đầu tiên tô Current, còn lại để trống.
        /// Danh sách mốc sẽ chuyển sang tab2config.json cùng đợt cấu hình các case lỗi.
        /// </summary>
        private static List<DkchStep> BuildSteps(List<Kind> kinds)
        {
            // Nhãn MỘT TỪ để vừa 8 ô trong panel hẹp, không phải xuống dòng hay cắt chữ.
            // Ba mốc đầu giống nhau với mọi đơn. Từ ĐKCH trở đi luồng còn rẽ nhánh —
            // XNCH và IN chưa có quy tắc nhận diện nên tạm để trống, sẽ chốt cùng đợt
            // cấu hình các trường hợp báo lỗi trong tab2config.json.
            var plan = new (string Label, Kind[] Any)[]
            {
                ("ĐẾN",  new[] { Kind.Arrival }),
                ("PHÁT", new[] { Kind.DispatchScan }),
                ("KIỆN", new[] { Kind.ProblemScan }),
                ("ĐKCH", new[] { Kind.ReturnRegister }),
                ("XNCH", new Kind[0]),                                  // chờ quy tắc
                ("PL",   new[] { Kind.Redeliver }),
                ("IN",   new Kind[0]),                                  // chờ quy tắc
                ("KÝ",   new[] { Kind.SignedReturn, Kind.SignedCpn })
            };

            var steps = new List<DkchStep>();
            bool currentMarked = false;
            foreach (var (label, any) in plan)
            {
                bool done = any.Length > 0 && kinds != null && kinds.Any(k => any.Contains(k));
                DkchStepState state;
                if (done) state = DkchStepState.Done;
                // Mốc chưa có quy tắc (any rỗng) thì để Pending, KHÔNG chiếm chỗ "đang tới"
                // — nếu không thì XNCH sẽ vĩnh viễn là mốc hiện hành và che mất mốc thật.
                else if (any.Length > 0 && !currentMarked) { state = DkchStepState.Current; currentMarked = true; }
                else state = DkchStepState.Pending;
                steps.Add(new DkchStep { Label = label, State = state });
            }
            return steps;
        }

        private static DateTime ParseTime(WaybillDetail d)
        {
            // scanTime = lúc THAO TÁC xảy ra, đúng thứ JMS hiển thị trên bảng hành trình.
            // uploadTime = lúc bản ghi lên tới máy chủ, lệch vài giây tới vài phút
            // ("Quét phát hàng" 07:29:07 nhưng upload 07:30:03) nên chỉ dùng làm dự phòng.
            string raw = !string.IsNullOrWhiteSpace(d.scanTime) ? d.scanTime : d.uploadTime;

            // InvariantCulture: mốc thời gian JMS luôn là "yyyy-MM-dd HH:mm:ss", không được
            // phụ thuộc Culture của máy chạy (nếu parse trượt, timeline sẽ bị đảo lộn).
            return DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt)
                ? dt
                : DateTime.MinValue;
        }

        private static string FormatTime(WaybillDetail d)
        {
            string raw = d.scanTime ?? d.uploadTime ?? "";
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
            // "Đăng ký CH lần 2" trước đây rơi xuống Kind.Other vì không chứa cụm
            // "Đăng ký chuyển hoàn" — hệ quả là chip ĐKCH đếm thiếu và lastRegister trỏ
            // sai mốc. Nó đúng là một lần đăng ký nên phải nhận diện ở đây.
            if (Has(t, "退件登记", "再次登记", "Đăng ký chuyển hoàn", "Đăng ký CH lần 2", "Đăng ký CH lần"))
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

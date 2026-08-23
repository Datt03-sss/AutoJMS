using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace AutoJMS
{
    public class NoDataWaybillException : Exception { }
    public class NeedSwitchToDkch1Exception : Exception { }
    public class NeedSwitchToDkch2Exception : Exception { }

    /// <summary>Chế độ hướng dẫn của tab DKCH — chọn ở mục DATA.</summary>
    public enum DkchGuideMode
    {
        /// <summary>Không hiển thị dòng đề xuất thao tác tiếp theo.</summary>
        Normal = 0,

        /// <summary>Hiển thị dòng đề xuất — chỉ khi nhập LẺ 1 mã, để hướng dẫn từng bước.</summary>
        Newbie = 1
    }

    /// <summary>Mức độ của thông báo hiển thị trên ô kết quả tabDKCH_result.</summary>
    public enum DkchResultLevel
    {
        /// <summary>Thông tin trung tính — thao tác cuối cùng khi chưa bấm Lưu.</summary>
        Info = 0,

        /// <summary>Đang chờ xử lý trong hàng đợi.</summary>
        Pending = 1,

        /// <summary>Đăng ký chuyển hoàn thành công.</summary>
        Success = 2,

        /// <summary>Bị chặn/bỏ qua theo nghiệp vụ, hoặc không xác minh được.</summary>
        Warning = 3,

        /// <summary>JMS từ chối hoặc lỗi thao tác.</summary>
        Error = 4
    }

    /// <summary>
    /// Nội dung hiển thị trên ô kết quả DKCH (nằm giữa ô nhập mã và ô lịch sử hành trình).
    /// <list type="number">
    /// <item><c>LastAction</c> — thao tác cuối cùng, lấy từ lần đọc lịch sử DUY NHẤT của chu kỳ.</item>
    /// <item><c>Message</c> — thông điệp kết quả/lỗi (in đậm, tô màu theo <c>Level</c>).</item>
    /// <item><c>ActRecommend</c> — đề xuất bước tiếp theo, chỉ vẽ khi <c>ShowRecommend</c>.</item>
    /// <item><c>Stats</c> — số lần đã ĐKCH · số lần phát lại.</item>
    /// </list>
    /// Dòng nào rỗng thì bỏ hẳn. Chi tiết đầy đủ (mã lỗi, nguyên văn tiếng Trung) nằm ở log.
    /// </summary>
    public sealed class DkchResultInfo
    {
        public string Waybill { get; set; } = "";
        public DkchResultLevel Level { get; set; } = DkchResultLevel.Info;

        /// <summary>DÒNG 1 — cả dòng thao tác cuối cùng đã ghép (dùng cho log).</summary>
        public string LastAction { get; set; } = "";

        /// <summary>DÒNG 1, phần TÊN THAO TÁC — in đậm, tô màu nổi bật.</summary>
        public string LastActionType { get; set; } = "";

        /// <summary>DÒNG 1, phần THỜI GIAN — chữ mờ, không in đậm.</summary>
        public string LastActionTime { get; set; } = "";

        /// <summary>DÒNG 2 — nguyên nhân kiện vấn đề. In đậm, tô màu nổi bật. Rỗng thì bỏ dòng.</summary>
        public string LastActionNote { get; set; } = "";

        /// <summary>DÒNG 2 — thông điệp kết quả/lỗi, in đậm và tô màu theo <see cref="Level"/>.</summary>
        public string Message { get; set; } = "";

        /// <summary>
        /// DÒNG 3 — đề xuất thao tác tiếp theo. Chỉ vẽ khi <see cref="ShowRecommend"/> = true
        /// (chế độ Newbie VÀ người dùng nhập lẻ 1 mã).
        /// </summary>
        public string ActRecommend { get; set; } = "";

        /// <summary>true nếu được phép vẽ dòng 3 — do DkchManager quyết định theo chế độ + số mã nhập.</summary>
        public bool ShowRecommend { get; set; }

        /// <summary>DÒNG 4 — số lần đã ĐKCH + số lần phát lại, gộp trên CÙNG 1 dòng.</summary>
        public string Stats { get; set; } = "";

        /// <summary>Tiền tố hiển thị trước <see cref="ActRecommend"/> (mặc định "→ ").</summary>
        public string ActionPrefix { get; set; } = "→ ";

        /// <summary>Id của case trong tab2config.json đã khớp — để tra khi cấu hình sai.</summary>
        public string CaseId { get; set; } = "";

        /// <summary>true nếu đây là kết quả SAU khi đã bấm Lưu.</summary>
        public bool AfterSave { get; set; }

        // ── Dữ liệu cho panel Newbill (thiết kế mới) ────────────────────────────────
        // Chỉ phục vụ hiển thị; không tham gia quyết định nghiệp vụ.

        /// <summary>Tên bưu tá của thao tác gần nhất — in đậm ngay dưới trạng thái.</summary>
        public string Operator { get; set; } = "";

        /// <summary>Số ngày tồn; <c>null</c> thì ẩn huy hiệu thay vì hiện số bịa.</summary>
        public int? DaysInStock { get; set; }

        /// <summary>Số lần đã đăng ký chuyển hoàn — chip "ĐKCH n".</summary>
        public int RegisterCount { get; set; }

        /// <summary>Số lần phát lại — chip "Phát lại n".</summary>
        public int RedeliverCount { get; set; }

        /// <summary>Nội dung dải "⛔ Vi phạm quy trình". Rỗng thì không vẽ dải.</summary>
        public string Violation { get; set; } = "";

        /// <summary>Các mốc của thanh "Tiến trình".</summary>
        public List<DkchStep> Steps { get; set; } = new List<DkchStep>();

        /// <summary>Hành trình đã chuẩn hoá, mới nhất đứng trước.</summary>
        public List<DkchJourneyEntry> Entries { get; set; } = new List<DkchJourneyEntry>();
    }

    /// <summary>
    /// Kết quả của một lần bấm "Lưu và thêm mới".
    /// <list type="bullet">
    /// <item><c>Success</c>: server trả <c>succ=true</c> (hoặc "Thao tác thành công" + code=1).</item>
    /// <item><c>Failed</c>: server trả <c>succ=false</c>/<c>fail=true</c> — CHẮC CHẮN chưa ghi nhận,
    /// đây là trạng thái DUY NHẤT được phép lặp/đổi mode.</item>
    /// <item><c>Unverified</c>: ĐÃ bấm nhưng không đọc được phản hồi — bắt buộc xác minh lại bằng
    /// lịch sử hành trình, KHÔNG được bấm Lưu lần nữa một cách mù quáng.</item>
    /// </list>
    /// </summary>
    public enum DkchSaveOutcome
    {
        Success = 0,
        Unverified = 1,
        Failed = 2
    }

    /// <summary>
    /// Kết quả bấm Lưu kèm thông điệp gốc từ JMS, để hiển thị nguyên văn lên ô kết quả
    /// (ví dụ "Chưa đủ ca phát", "Đã đăng ký chuyển hoàn") thay vì thông báo chung chung.
    /// </summary>
    public sealed class DkchSaveResult
    {
        public DkchSaveOutcome Outcome { get; set; } = DkchSaveOutcome.Unverified;

        /// <summary>Thông điệp <c>msg</c> do JMS trả về (đã bỏ tiền tố "code:").</summary>
        public string Message { get; set; } = "";

        /// <summary>Tỉ số "x/y" bóc từ msg (出仓次数：1/2 → "1/2"). Rỗng nếu không có.</summary>
        public string Ratio { get; set; } = "";

        /// <summary>Mã <c>code</c> do JMS trả về.</summary>
        public string Code { get; set; } = "";

        /// <summary>
        /// <c>msg</c> nguyên văn của JMS, kể cả tiếng Trung
        /// ("999010051:此单的出仓次数不满足登记条件，出仓次数：1/2") — hiển thị kèm để đối chiếu.
        /// </summary>
        public string RawMessage { get; set; } = "";

        /// <summary>Server xác nhận thất bại (<c>succ=false</c>/<c>fail=true</c>).</summary>
        public bool ServerConfirmedFailure => Outcome == DkchSaveOutcome.Failed;
    }

    public static class WebViewAutomation
    {
        /// <summary>
        /// Cửa sổ chờ phản hồi khi Lưu. Giá trị cũ (1500ms) là nguyên nhân trực tiếp gây
        /// đăng ký trùng: JMS trả lời chậm → timeout → code cũ ép Lưu lại lần nữa.
        /// </summary>
        private const int SaveVerifyTimeoutMs = 8000;

        /// <summary>
        /// Bỏ BOM (U+FEFF) ở đầu chuỗi. Capture thực tế của JMS có BOM; <c>JsonDocument.Parse</c>
        /// coi BOM là ký tự không hợp lệ và ném lỗi, khiến toàn bộ nhánh phân tích JSON bị bỏ qua.
        /// </summary>
        private static string StripBom(string json)
            => string.IsNullOrEmpty(json) ? json : json.TrimStart('\uFEFF', '\u200B').TrimStart();

        /// <summary>
        /// Đọc một cờ boolean từ JSON, chịu được cả <c>"succ":true</c>, <c>"succ": "true"</c>,
        /// <c>"succ":1</c> và khoảng trắng bất kỳ. Trả về null nếu không có thuộc tính đó.
        /// </summary>
        public static bool? ReadJsonFlag(string json, string propertyName)
        {
            if (string.IsNullOrEmpty(json)) return null;
            json = StripBom(json);

            try
            {
                using (JsonDocument doc = JsonDocument.Parse(json))
                {
                    if (doc.RootElement.ValueKind == JsonValueKind.Object &&
                        doc.RootElement.TryGetProperty(propertyName, out var el))
                    {
                        switch (el.ValueKind)
                        {
                            case JsonValueKind.True: return true;
                            case JsonValueKind.False: return false;
                            case JsonValueKind.String:
                                return bool.TryParse(el.GetString(), out bool sb) ? sb : (bool?)null;
                            case JsonValueKind.Number:
                                return el.TryGetInt32(out int n) ? n != 0 : (bool?)null;
                        }
                    }
                }
            }
            catch { /* JSON không chuẩn → dùng regex bên dưới */ }

            var m = Regex.Match(json, "\"" + Regex.Escape(propertyName) + "\"\\s*:\\s*\"?(true|false)\"?",
                                RegexOptions.IgnoreCase);
            if (m.Success)
                return string.Equals(m.Groups[1].Value, "true", StringComparison.OrdinalIgnoreCase);

            return null;
        }

        /// <summary>Response báo thất bại rõ ràng: <c>succ=false</c> hoặc <c>fail=true</c>.</summary>
        public static bool IsFailureResponse(string json)
        {
            if (string.IsNullOrEmpty(json)) return false;
            return ReadJsonFlag(json, "succ") == false || ReadJsonFlag(json, "fail") == true;
        }

        /// <summary>
        /// Response báo đăng ký chuyển hoàn thành công: <c>succ=true</c>, <c>fail=false</c>
        /// kèm <c>code=1</c> hoặc thông điệp "Thao tác thành công".
        /// </summary>
        public static bool IsSaveSuccessResponse(string json)
        {
            if (string.IsNullOrEmpty(json)) return false;
            if (IsFailureResponse(json)) return false;

            // ƯU TIÊN cờ succ: đăng ký chuyển hoàn hợp lệ luôn trả succ=true.
            // An toàn vì response đã được lọc theo LooksLikeActionEnvelope trước khi vào đây,
            // nên không thể nhặt nhầm response của một request khác.
            if (ReadJsonFlag(json, "succ") == true) return true;

            // DỰ PHÒNG khi response thiếu hẳn cờ succ (một số capture chỉ có code + msg):
            // phải khớp thông điệp thành công VÀ code=1.
            bool msgOk = json.IndexOf("Thao tác thành công", StringComparison.OrdinalIgnoreCase) >= 0
                      || json.IndexOf("操作成功", StringComparison.Ordinal) >= 0;
            if (!msgOk) return false;

            // (?![0-9]) để "code":137043004 không bị khớp thành code = 1.
            return Regex.IsMatch(json, "\"code\"\\s*:\\s*\"?1\"?(?![0-9])");
        }

        /// <summary>
        /// Chỉ nhận response có dạng "phong bì kết quả thao tác" (nhỏ, có <c>succ</c>/<c>msg</c>)
        /// và KHÔNG phải response tra cứu hành trình.
        /// <para>
        /// Cần thiết vì <see cref="WaitForApiResponseAsync"/> nghe MỌI response của trang: nếu
        /// không lọc thì một request không liên quan bay qua trong lúc chờ sẽ bị hiểu nhầm là
        /// "đã lưu" (hoặc "đã lỗi") → nguồn gốc đăng ký trùng và báo sai kết quả.
        /// </para>
        /// </summary>
        /// <summary>
        /// Phân loại thất bại của thao tác LƯU: chỉ trả về exception cho các trường hợp CẦN ĐỔI MODE
        /// (hoặc vận đơn không tồn tại). Các lỗi nghiệp vụ khác trả về <c>null</c> để phía gọi đóng gói
        /// vào <see cref="DkchSaveResult"/> và hiển thị nguyên văn — KHÔNG bấm Lưu lại.
        /// <para>
        /// Tách khỏi <see cref="CheckAndThrowIfError"/> (vẫn dùng cho bước Tìm kiếm) vì hàm đó ném
        /// exception cho MỌI lỗi có <c>msg</c>, làm nhánh <see cref="DkchSaveOutcome.Failed"/> không
        /// bao giờ chạy và thông điệp thật của JMS bị thay bằng "Lỗi thao tác" chung chung.
        /// </para>
        /// </summary>
        private static Exception ClassifySaveFailure(string json)
        {
            if (!IsFailureResponse(json)) return null;

            // (1) Lỗi nghiệp vụ đã biết (vd 999010051 "chưa đủ ca phát"): đổi mode KHÔNG giải quyết
            //     được gì → trả null để đóng gói vào DkchSaveResult.Failed và dừng tại đây.
            //     Phải kiểm TRƯỚC mọi heuristic bên dưới, nếu không mã lỗi có dấu ':' sẽ bị hiểu
            //     nhầm là "cần đổi sang DKCH2" và phát sinh thêm 1 lần bấm Lưu.
            if (IsKnownBusinessRejection(json)) return null;

            string msg = ExtractRawMessage(json);
            string code = ExtractErrorCode(json);
            string rawCode = ExtractCode(json);

            // MÃ SỐ trước, TỪ KHOÁ VĂN BẢN sau.
            // Mã lỗi không phụ thuộc ngôn ngữ giao diện nên đáng tin hơn; nếu để từ khoá văn bản
            // chạy trước, một thông điệp tiếng Trung có chứa từ khoá của nhóm khác sẽ ăn tranh
            // và làm sai hướng đổi mode.
            if (code == "999006328") return new NeedSwitchToDkch1Exception();
            if (code == "137043004" || code == "999006082") return new NeedSwitchToDkch2Exception();

            if (MessageHas(json, "needDkch1")) return new NeedSwitchToDkch1Exception();
            if (MessageHas(json, "needDkch2")) return new NeedSwitchToDkch2Exception();
            if (MessageHas(json, "noData")) return new NoDataWaybillException();

            // Heuristic cuối: mã dạng chuỗi có dấu ':' -> coi là cần DKCH2 (giữ như bản cũ).
            if (rawCode.Contains(":")) return new NeedSwitchToDkch2Exception();

            return null;
        }

        public static bool LooksLikeActionEnvelope(string uri, string json)
        {
            if (string.IsNullOrEmpty(json)) return false;

            // Response tra cứu hành trình dài hàng trăm KB; phong bì kết quả chỉ vài trăm byte.
            if (json.Length > 4096) return false;

            if (!string.IsNullOrEmpty(uri) &&
                (uri.IndexOf("podTracking", StringComparison.OrdinalIgnoreCase) >= 0 ||
                 uri.IndexOf("keywordList", StringComparison.OrdinalIgnoreCase) >= 0))
                return false;

            return json.IndexOf("\"succ\"", StringComparison.OrdinalIgnoreCase) >= 0
                || json.IndexOf("\"msg\"", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>Đọc <c>msg</c> từ response, bỏ tiền tố "code:" của JMS ("1:Thao tác thành công").</summary>
        public static string ExtractMessage(string json)
        {
            if (string.IsNullOrEmpty(json)) return "";
            json = StripBom(json);

            string msg = null;
            try
            {
                using (JsonDocument doc = JsonDocument.Parse(json))
                {
                    if (doc.RootElement.ValueKind == JsonValueKind.Object &&
                        doc.RootElement.TryGetProperty("msg", out var m) &&
                        m.ValueKind == JsonValueKind.String)
                        msg = m.GetString() ?? "";
                }
            }
            catch { }

            if (msg == null)
            {
                var rx = Regex.Match(json, "\"msg\"\\s*:\\s*\"([^\"]*)\"");
                if (!rx.Success) return "";
                msg = rx.Groups[1].Value;
            }

            // Bỏ tiền tố mã của JMS ("1:Thao tác thành công") — áp dụng cho CẢ hai nhánh trên.
            var prefix = Regex.Match(msg, "^\\s*[0-9]+\\s*:\\s*");
            if (prefix.Success) msg = msg.Substring(prefix.Length);
            return msg.Trim();
        }

        /// <summary>Đọc <c>code</c> từ response (chuỗi hoặc số).</summary>
        public static string ExtractCode(string json)
        {
            if (string.IsNullOrEmpty(json)) return "";
            json = StripBom(json);
            try
            {
                using (JsonDocument doc = JsonDocument.Parse(json))
                {
                    if (doc.RootElement.ValueKind == JsonValueKind.Object &&
                        doc.RootElement.TryGetProperty("code", out var c))
                        return c.ToString();
                }
            }
            catch { }

            var rx = Regex.Match(json, "\"code\"\\s*:\\s*\"?([^\",}]+)\"?");
            return rx.Success ? rx.Groups[1].Value.Trim() : "";
        }

        /// <summary>
        /// <c>msg</c> nguyên văn, GIỮ tiền tố mã ("999010051:此单的出仓次数不满足登记条件，出仓次数：1/2")
        /// để hiển thị đúng những gì JMS trả về.
        /// </summary>
        public static string ExtractRawMessage(string json)
        {
            if (string.IsNullOrEmpty(json)) return "";
            json = StripBom(json);
            try
            {
                using (JsonDocument doc = JsonDocument.Parse(json))
                {
                    if (doc.RootElement.ValueKind == JsonValueKind.Object &&
                        doc.RootElement.TryGetProperty("msg", out var m) &&
                        m.ValueKind == JsonValueKind.String)
                        return (m.GetString() ?? "").Trim();
                }
            }
            catch { }

            var rx = Regex.Match(json, "\"msg\"\\s*:\\s*\"([^\"]*)\"");
            return rx.Success ? rx.Groups[1].Value.Trim() : "";
        }

        /// <summary>
        /// Mã lỗi dạng SỐ. Ưu tiên trường <c>code</c>; nếu <c>code</c> không phải số thuần
        /// (ví dụ JMS nhồi cả "999010051:此单…" vào đó) thì lấy phần số đứng đầu, rồi mới đến
        /// tiền tố số của <c>msg</c>. Nhờ vậy việc phân loại lỗi không phụ thuộc JMS đặt mã ở đâu.
        /// </summary>
        public static string ExtractErrorCode(string json)
        {
            string code = ExtractCode(json);
            var m = Regex.Match(code ?? "", "^\\s*([0-9]+)");
            if (m.Success) return m.Groups[1].Value;

            m = Regex.Match(ExtractRawMessage(json), "^\\s*([0-9]+)\\s*:");
            return m.Success ? m.Groups[1].Value : (code ?? "").Trim();
        }

        /// <summary>
        /// Mã lỗi nghiệp vụ "chưa đủ ca": 999010051 = 出仓次数 (đơn phát lại thiếu ca phát),
        /// 999010052 = 问题件次数 (đơn mới thiếu ca kiện vấn đề). Mã số không phụ thuộc ngôn ngữ.
        /// </summary>
        private static readonly string[] NotEnoughShiftCodes = { "999010051", "999010052" };

        /// <summary>
        /// Bóc tỉ số "x/y" trong thông điệp JMS. Bao gồm cả hai loại điều kiện đăng ký:
        /// <c>出仓次数：1/2</c> (số ca phát — đơn giao lại) và
        /// <c>问题件次数：1/3</c> (số ca kiện vấn đề — đơn mới).
        /// Dấu hai chấm có thể là ： (U+FF1A) hoặc :.
        /// </summary>
        public static string ExtractRatio(string json)
        {
            string msg = ExtractRawMessage(json);
            if (string.IsNullOrWhiteSpace(msg)) return "";

            var m = Regex.Match(msg, "次数\\s*[：:]\\s*([0-9]+\\s*/\\s*[0-9]+)");

            // Dự phòng cho thông điệp đã dịch ("ca phát: 1/2"). Chặn 1-3 chữ số và dùng lookaround
            // để không nhặt nhầm ngày tháng kiểu 2026/07/29.
            if (!m.Success)
                m = Regex.Match(msg, "(?<![0-9/])([0-9]{1,3}\\s*/\\s*[0-9]{1,3})(?![0-9/])");

            return m.Success ? m.Groups[1].Value.Replace(" ", "") : "";
        }

        /// <summary>
        /// Lỗi nghiệp vụ ĐÃ BIẾT của JMS: chắc chắn CHƯA ghi nhận, nhưng đổi mode DKCH1/DKCH2 cũng
        /// vô ích — phải dừng và báo cho người dùng, tuyệt đối không bấm Lưu lại.
        /// </summary>
        public static bool IsKnownBusinessRejection(string json)
        {
            if (!IsFailureResponse(json)) return false;

            string code = ExtractErrorCode(json);
            string msg = ExtractRawMessage(json);

            return NotEnoughShiftCodes.Contains(code)
                || msg.IndexOf("出仓次数", StringComparison.Ordinal) >= 0      // số ca phát (đơn giao lại)
                || msg.IndexOf("问题件次数", StringComparison.Ordinal) >= 0     // số ca kiện vấn đề (đơn mới)
                || msg.IndexOf("不满足登记条件", StringComparison.Ordinal) >= 0
                || msg.IndexOf("chưa đủ ca", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>
        /// Dịch thông điệp lỗi của JMS sang tiếng Việt cho ô kết quả. Không nhận diện được thì trả
        /// về nguyên văn (thà hiện tiếng Trung còn hơn thay bằng câu chung chung vô nghĩa).
        /// </summary>
        public static string TranslateJmsMessage(string json)
        {
            string msg = ExtractRawMessage(json);
            if (string.IsNullOrWhiteSpace(msg)) return "";

            if (IsKnownBusinessRejection(json))
            {
                string ratio = ExtractRatio(json);
                string what = msg.IndexOf("问题件次数", StringComparison.Ordinal) >= 0
                    ? "Đơn mới chưa đủ ca"        // 问题件次数 — thiếu ca kiện vấn đề (đơn mới)
                    : "Đơn phát lại chưa đủ ca";  // 出仓次数 — thiếu ca phát (đơn đã giao lại)
                return string.IsNullOrEmpty(ratio) ? what : $"{what} ({ratio})";
            }

            if (msg.IndexOf("操作成功", StringComparison.Ordinal) >= 0) return "Thao tác thành công";

            return ExtractMessage(json);
        }

        private static async Task<string> ExecuteScriptSafeAsync(WebView2 webView, string script)
        {
            if (webView.InvokeRequired)
                return await (Task<string>)webView.Invoke(new Func<Task<string>>(async () => await webView.ExecuteScriptAsync(script)));
            return await webView.ExecuteScriptAsync(script);
        }

        /// <summary>
        /// Chờ response khớp <paramref name="predicate"/>. Predicate nhận (uri, body) để phía gọi
        /// lọc được đúng endpoint — chỉ dựa vào body là không đủ, xem <see cref="LooksLikeActionEnvelope"/>.
        /// </summary>
        private static async Task<string> WaitForApiResponseAsync(WebView2 webView, Func<string, string, bool> predicate, int timeoutMs = 10000)
        {
            var tcs = new TaskCompletionSource<string>();
            EventHandler<CoreWebView2WebResourceResponseReceivedEventArgs> handler = async (sender, args) =>
            {
                if (args.Response.StatusCode == 200)
                {
                    try
                    {
                        string uri = "";
                        try { uri = args.Request?.Uri ?? ""; } catch { }

                        var stream = await args.Response.GetContentAsync();
                        if (stream != null)
                        {
                            using (var reader = new StreamReader(stream))
                            {
                                string json = await reader.ReadToEndAsync();
                                if (predicate(uri, json))
                                    tcs.TrySetResult(json);
                            }
                        }
                    }
                    catch { }
                }
            };

            if (webView.InvokeRequired)
                webView.Invoke(new Action(() => webView.CoreWebView2.WebResourceResponseReceived += handler));
            else
                webView.CoreWebView2.WebResourceResponseReceived += handler;

            var completedTask = await Task.WhenAny(tcs.Task, Task.Delay(timeoutMs));

            if (webView.InvokeRequired)
                webView.Invoke(new Action(() => webView.CoreWebView2.WebResourceResponseReceived -= handler));
            else
                webView.CoreWebView2.WebResourceResponseReceived -= handler;

            if (completedTask != tcs.Task)
                throw new TimeoutException("Timeout: Không có phản hồi");

            return await tcs.Task;
        }

        private static void CheckAndThrowIfError(string json)
        {
            // Phân loại lỗi giữ NGUYÊN như bản cũ (NeedSwitchToDkch1/2, NoDataWaybill).
            // Chỉ thay điều kiện nhận diện thất bại bằng parser chịu được "succ": "false".
            if (IsFailureResponse(json))
            {
                Exception exToThrow = null;
                try
                {
                    // Chỉ dùng try-catch để an toàn khi phân tích JSON
                    using (JsonDocument doc = JsonDocument.Parse(json))
                    {
                        var root = doc.RootElement;
                        string msg = root.TryGetProperty("msg", out var m) ? m.GetString() : "";
                        string code = root.TryGetProperty("code", out var c) ? c.ToString() : "";

                        // MÃ SỐ trước, TỪ KHOÁ VĂN BẢN sau — xem giải thích ở ClassifySaveFailure.
                        if (code == "999006328")
                            exToThrow = new NeedSwitchToDkch1Exception();
                        else if (code == "137043004" || code == "999006082")
                            exToThrow = new NeedSwitchToDkch2Exception();
                        else if (MessageHas(json, "needDkch1"))
                            exToThrow = new NeedSwitchToDkch1Exception();
                        else if (MessageHas(json, "needDkch2"))
                            exToThrow = new NeedSwitchToDkch2Exception();
                        else if (MessageHas(json, "noData"))
                            exToThrow = new NoDataWaybillException();
                        else if (code.Contains(":"))
                            exToThrow = new NeedSwitchToDkch2Exception();
                        else if (!string.IsNullOrEmpty(msg))
                            exToThrow = new Exception($"Lỗi: {msg}");
                    }
                }
                catch { } // Bỏ qua nếu lỗi format JSON

                // Ném lỗi một cách gọn gàng ở ngoài khối try-catch
                if (exToThrow != null)
                {
                    throw exToThrow;
                }
            }
        }

        public static async Task FillWaybillAsync(WebView2 webView, string waybill, CancellationToken token)
        {
            // Tiêu đề panel khác nhau theo ngôn ngữ JMS (vd "Thông tin người gửi" / "原单收寄件人信息"),
            // nên lấy danh sách từ tab2config.json và khớp theo chuỗi con.
            string headerList = "[" + string.Join(",",
                Tab2Config.Current.CollapseHeaderList().Select(EscapeJsString)) + "]";

            string collapseJs = $@"
            (function() {{
                var wanted = {headerList}.map(function(x) {{ return x.toLowerCase(); }});
                var headers = document.querySelectorAll('.el-collapse-item__header.is-active');
                for (var i = 0; i < headers.length; i++) {{
                    var text = (headers[i].innerText || '').toLowerCase();
                    for (var k = 0; k < wanted.length; k++) {{
                        if (wanted[k] && text.indexOf(wanted[k]) >= 0) {{ headers[i].click(); break; }}
                    }}
                }}
            }})();";
            await ExecuteScriptSafeAsync(webView, collapseJs);
            string js = $@"
            (function() {{
                var container = document.querySelector('div[id^=""el-collapse-content-""]');
                var input = container ? container.querySelector('input') : null;
                if (!input) {{
                    var inputs = document.querySelectorAll('input[type=text], input:not([type])');
                    for(var i=0; i<inputs.length; i++) {{
                        if(inputs[i].offsetParent !== null && !inputs[i].disabled) {{
                            input = inputs[i]; break;
                        }}
                    }}
                }}
                if (input) {{
                    if (input.value.trim().toUpperCase() === '{waybill}'.toUpperCase()) return 'already_filled';
                    
                    // Ép VueJS nhận diện sự thay đổi
                    var nativeInputValueSetter = Object.getOwnPropertyDescriptor(window.HTMLInputElement.prototype, 'value').set;
                    nativeInputValueSetter.call(input, '{waybill}');
                    
                    input.dispatchEvent(new Event('input', {{ bubbles: true }}));
                    input.dispatchEvent(new Event('change', {{ bubbles: true }}));
                    input.blur();
                    return 'filled';
                }}
                return 'not_found';
            }})();";

            string res = await ExecuteScriptSafeAsync(webView, js);
            if (res.Contains("not_found")) throw new Exception("Không tìm thấy ô nhập mã vận đơn.");
        }

        /// <summary>
        /// Chọn dropdown "Loại đơn" theo DANH SÁCH nhãn ứng viên — dùng nhãn nào đang có trên trang.
        /// <para>
        /// JMS đã từng đổi nhãn (DKCH1: "Chuyển hoàn" → "Từ chối"). Danh sách lấy từ
        /// <c>modules/tab2config.json</c> nên khi JMS đổi tên chỉ cần sửa file, không build lại app.
        /// Nếu không nhãn nào khớp, exception sẽ kèm DANH SÁCH các mục đang có trên trang để bạn
        /// biết tên mới mà điền vào config.
        /// </para>
        /// </summary>
        /// <returns>Nhãn đã chọn được.</returns>
        public static async Task<string> CheckAndSelectDropdownAsync(
            WebView2 webView, IList<string> candidates, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();

            if (candidates == null || candidates.Count == 0)
                throw new Exception("Chưa cấu hình nhãn dropdown 'Loại đơn' (dropdownOptions trong tab2config.json).");

            // Mảng JS các nhãn cần thử, giữ đúng thứ tự ưu tiên trong cấu hình.
            string jsList = "[" + string.Join(",", candidates.Select(EscapeJsString)) + "]";

            string js = $@"
            (async function() {{
                try {{
                    var wanted = {jsList}.map(function(x) {{ return x.toLowerCase().trim(); }});

                    // Kiểm tra và bấm phải nhìn CÙNG MỘT ô. Bản trước đọc
                    // '.el-select .el-input__inner' (ô select ĐẦU TIÊN trên trang, có thể là
                    // ô khác) rồi lại bấm vào '.el-select .el-input__inner[readonly]'. Hai
                    // selector lệch nhau nên nhánh 'đã đúng rồi' gần như không bao giờ trúng,
                    // và mỗi mã đều mở dropdown bấm lại một lần — chậm mà không cần thiết.
                    var inputs = document.querySelectorAll('.el-select .el-input__inner[readonly]');
                    for (var i = 0; i < inputs.length; i++) {{
                        var v = (inputs[i].value || '').toLowerCase().trim();
                        if (v && wanted.indexOf(v) >= 0) return 'already|' + inputs[i].value;
                    }}

                    let ddInput = inputs.length > 0 ? inputs[0] : null;
                    if (!ddInput) return 'no_input|';

                    ddInput.dispatchEvent(new MouseEvent('mousedown', {{ bubbles: true }}));
                    ddInput.click();

                    let seen = [];
                    let maxRetries = 20;
                    while (maxRetries > 0) {{
                        await new Promise(r => setTimeout(r, 100));

                        let visibleDropdowns = document.querySelectorAll('.el-select-dropdown:not([style*=""display: none""])');
                        for (let dd of visibleDropdowns) {{
                            let items = dd.querySelectorAll('li.el-select-dropdown__item');
                            for (let item of items) {{
                                let text = item.innerText.trim();
                                if (seen.indexOf(text) < 0) seen.push(text);
                                if (item.classList.contains('is-disabled')) continue;

                                if (wanted.indexOf(text.toLowerCase()) >= 0) {{
                                    item.scrollIntoView({{ block: 'center' }});
                                    item.dispatchEvent(new MouseEvent('mouseenter', {{ bubbles: true }}));
                                    item.dispatchEvent(new MouseEvent('mousedown', {{ bubbles: true }}));
                                    item.click();
                                    item.dispatchEvent(new MouseEvent('mouseup', {{ bubbles: true }}));
                                    return 'ok|' + text;
                                }}
                            }}
                        }}
                        maxRetries--;
                    }}
                    return 'item_not_found|' + seen.join(' / ');
                }} catch (e) {{
                    return 'error|' + e.message;
                }}
            }})();";

            string raw = UnwrapJs(await ExecuteScriptSafeAsync(webView, js));
            int sep = raw.IndexOf('|');
            string status = sep < 0 ? raw : raw.Substring(0, sep);
            string detail = sep < 0 ? "" : raw.Substring(sep + 1);

            switch (status)
            {
                case "already":
                case "ok":
                    if (status == "ok") await Task.Delay(120, token);
                    return string.IsNullOrWhiteSpace(detail) ? candidates[0] : detail;

                case "no_input":
                    throw new Exception("Không tìm thấy ô Dropdown 'Loại đơn'.");

                case "item_not_found":
                    throw new Exception(
                        $"Không tìm thấy mục nào trong [{string.Join(" / ", candidates)}]. " +
                        $"Các mục JMS đang có: [{detail}]. " +
                        "Cập nhật 'dropdownOptions' trong modules/tab2config.json (không cần build lại app).");

                case "error":
                    throw new Exception($"Lỗi chọn dropdown 'Loại đơn': {detail}");

                default:
                    // Giá trị trả về không nhận diện được (ví dụ WebView2 không await Promise của
                    // hàm async nên trả về "{}"): KHÔNG ném lỗi để không làm chết cả lượt xử lý —
                    // giữ hành vi của bản cũ, chỉ ghi log để còn lần theo.
                    AppLogger.Warning($"[DKCH] dropdown trả về không nhận diện được: {raw}");
                    return candidates[0];
            }
        }

        /// <summary>
        /// Thông điệp JMS có chứa từ khoá nào của nhóm <paramref name="group"/> không.
        /// Xét CẢ msg nguyên văn (còn tiếng Trung) và msg đã cắt tiền tố mã.
        /// </summary>
        private static bool MessageHas(string json, string group)
        {
            var keys = Tab2Config.Current.MessageKeys(group);
            if (keys.Count == 0) return false;

            string raw = ExtractRawMessage(json);
            string trimmed = ExtractMessage(json);

            foreach (var k in keys)
            {
                if (!string.IsNullOrWhiteSpace(raw) && raw.IndexOf(k, StringComparison.OrdinalIgnoreCase) >= 0) return true;
                if (!string.IsNullOrWhiteSpace(trimmed) && trimmed.IndexOf(k, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            }
            return false;
        }

        /// <summary>
        /// Giải mã giá trị <c>ExecuteScriptAsync</c> trả về (luôn là JSON) thành chuỗi thật.
        /// <c>Trim('"')</c> là chưa đủ: chữ tiếng Trung có thể về dưới dạng \uXXXX, làm thông báo
        /// "các mục JMS đang có" — thứ dùng để biết nhãn mới — trở nên không đọc được.
        /// </summary>
        private static string UnwrapJs(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return "";
            try { return JsonSerializer.Deserialize<string>(raw) ?? ""; }
            catch { return raw.Trim('"'); }
        }

        /// <summary>Chuỗi JS an toàn trong dấu nháy đơn.</summary>
        private static string EscapeJsString(string value)
        {
            string s = (value ?? "").Replace("\\", "\\\\").Replace("'", "\\'").Replace("\r", "").Replace("\n", "");
            return "'" + s + "'";
        }

        public static async Task ClickSearchAsync(WebView2 webView, string expectedWaybill, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();

            string clickJs = @"(function() {
                var container = document.querySelector('div[id^=""el-collapse-content-""]');
                if (container) {
                    var btn = container.querySelector('button.el-button--primary');
                    if (btn) {
                        // Kiểm tra xem nút có bị VueJS khóa không
                        if (btn.disabled || btn.classList.contains('is-disabled')) return 'disabled';
                        btn.click(); 
                        return 'clicked';
                    }
                }
                return 'not_found';
            })();";

            var responseTask = WaitForApiResponseAsync(webView,
                (uri, json) => json.Contains($"\"waybillNo\":\"{expectedWaybill}\"")
                            || (LooksLikeActionEnvelope(uri, json) && IsFailureResponse(json)),
                timeoutMs: 3000); // Cho web 3s để phản hồi

            string clickRes = UnwrapJs(await ExecuteScriptSafeAsync(webView, clickJs));

            // NẾU BỊ KHÓA, QUĂNG LỖI NGAY ĐỂ ĐƯỢC CHẠY LẠI
            if (clickRes == "disabled") throw new Exception("Nút Tìm kiếm đang bị mờ. Vue chưa nhận dữ liệu.");
            if (clickRes == "not_found") throw new Exception("Không tìm thấy nút Tìm kiếm.");

            try
            {
                string jsonResult = await responseTask;
                CheckAndThrowIfError(jsonResult);
            }
            catch (TimeoutException)
            {
                string errorJs = "document.querySelector('.el-message--error') ? document.querySelector('.el-message--error').innerText : ''";
                string error = UnwrapJs(await ExecuteScriptSafeAsync(webView, errorJs));
                if (!string.IsNullOrEmpty(error)) throw new Exception($"Lỗi UI: {error}");

                throw new Exception("Timeout: Đã bấm nhưng Web không trả về API.");
            }
        }

        /// <summary>
        /// Bấm "Lưu và thêm mới" ĐÚNG MỘT LẦN rồi đọc phản hồi.
        /// <para>
        /// Khác biệt so với bản cũ: timeout KHÔNG còn ném exception để "kích hoạt switch DKCH2".
        /// Hành vi đó khiến một lần Lưu chậm-nhưng-thành-công bị Lưu lại → đăng ký trùng.
        /// Nay trả về <see cref="DkchSaveOutcome.Unverified"/> để phía gọi tự xác minh
        /// bằng lịch sử hành trình.
        /// </para>
        /// Vẫn ném <see cref="NeedSwitchToDkch1Exception"/>/<see cref="NeedSwitchToDkch2Exception"/>
        /// khi server báo <c>succ=false</c> — lúc đó chắc chắn CHƯA lưu nên đổi mode là an toàn.
        /// </summary>
        public static async Task<DkchSaveResult> ClickSaveAndVerifyAsync(WebView2 webView, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();

            var responseTask = WaitForApiResponseAsync(webView,
                (uri, json) => LooksLikeActionEnvelope(uri, json)
                            && (IsSaveSuccessResponse(json) || IsFailureResponse(json)),
                timeoutMs: SaveVerifyTimeoutMs);

            // Trang JMS chạy được ở tiếng Việt VÀ tiếng Trung ("Lưu và thêm mới" / "保存并新增"),
            // nên khớp theo DANH SÁCH nhãn từ tab2config.json, xét cả title lẫn chữ trên nút.
            var saveTitles = Tab2Config.Current.SaveButtonTitleList();
            string saveList = "[" + string.Join(",", saveTitles.Select(EscapeJsString)) + "]";

            string saveJs = $@"
            (function() {{
                var wanted = {saveList}.map(function(x) {{ return x.toLowerCase().trim(); }});
                var buttons = document.querySelectorAll('button');
                var seen = [];
                for (var i = 0; i < buttons.length; i++) {{
                    var b = buttons[i];
                    var title = (b.getAttribute('title') || '').toLowerCase().trim();
                    var label = (b.innerText || '').toLowerCase().trim();
                    if (label && seen.indexOf(label) < 0) seen.push(label);

                    if (wanted.indexOf(title) >= 0 || wanted.indexOf(label) >= 0) {{
                        if (b.disabled || b.classList.contains('is-disabled')) return 'disabled|';
                        b.click();
                        return 'clicked|' + (b.getAttribute('title') || b.innerText || '').trim();
                    }}
                }}
                return 'not_found|' + seen.join(' / ');
            }})();";

            string clickRaw = UnwrapJs(await ExecuteScriptSafeAsync(webView, saveJs));
            int clickSep = clickRaw.IndexOf('|');
            string clickStatus = clickSep < 0 ? clickRaw : clickRaw.Substring(0, clickSep);
            string clickDetail = clickSep < 0 ? "" : clickRaw.Substring(clickSep + 1);

            if (clickStatus == "not_found")
                throw new Exception(
                    $"Không tìm thấy nút Lưu trong [{string.Join(" / ", saveTitles)}]. " +
                    $"Các nút JMS đang có: [{clickDetail}]. " +
                    "Cập nhật 'saveButtonTitles' trong modules/tab2config.json (không cần build lại app).");

            if (clickStatus == "disabled")
                throw new Exception("Nút Lưu đang bị mờ — Vue chưa nhận dữ liệu đơn.");

            try
            {
                string jsonResult = await responseTask;
                string msg = TranslateJmsMessage(jsonResult);
                string raw = ExtractRawMessage(jsonResult);
                string code = ExtractErrorCode(jsonResult);
                string ratio = ExtractRatio(jsonResult);

                // CHỈ ném khi cần đổi mode / vận đơn không tồn tại. Lỗi nghiệp vụ khác đi tiếp
                // xuống nhánh Failed bên dưới để giữ nguyên văn thông điệp của JMS.
                var modeSwitch = ClassifySaveFailure(jsonResult);
                if (modeSwitch != null) throw modeSwitch;

                if (IsSaveSuccessResponse(jsonResult))
                    return new DkchSaveResult
                    {
                        Outcome = DkchSaveOutcome.Success,
                        Message = string.IsNullOrEmpty(msg) ? "Thao tác thành công" : msg,
                        RawMessage = raw,
                        Ratio = ratio,
                        Code = code
                    };

                // succ=false / fail=true nhưng không thuộc nhóm đổi mode → thất bại xác định,
                // KHÔNG lặp lại, chỉ báo nguyên văn thông điệp của JMS lên UI.
                if (IsFailureResponse(jsonResult))
                    return new DkchSaveResult
                    {
                        Outcome = DkchSaveOutcome.Failed,
                        Message = string.IsNullOrEmpty(msg) ? "JMS từ chối đăng ký (succ=false)." : msg,
                        RawMessage = raw,
                        Ratio = ratio,
                        Code = code
                    };

                // Có phản hồi nhưng không khớp mẫu nào → coi là chưa xác minh, để caller kiểm tra.
                return new DkchSaveResult
                {
                    Outcome = DkchSaveOutcome.Unverified,
                    Message = msg,
                    RawMessage = raw,
                    Ratio = ratio,
                    Code = code
                };
            }
            catch (TimeoutException)
            {
                string errorJs = "document.querySelector('.el-message--error') ? document.querySelector('.el-message--error').innerText : ''";
                string error = UnwrapJs(await ExecuteScriptSafeAsync(webView, errorJs));

                // Có banner lỗi trên UI → chắc chắn chưa lưu, nhưng KHÔNG lặp Lưu vì đây không
                // phải phản hồi succ=false của server. Báo lỗi để caller ghi nhận và dừng.
                if (!string.IsNullOrEmpty(error)) throw new Exception($"Lỗi UI: {error}");

                // Không rõ kết quả: KHÔNG bấm lại. Caller sẽ đối chiếu lịch sử hành trình.
                return new DkchSaveResult
                {
                    Outcome = DkchSaveOutcome.Unverified,
                    Message = "Không đọc được phản hồi từ JMS trong 8 giây."
                };
            }
        }
    }

    public class DkchManager
    {

        public event Action<string> OnLog;
        public event Action<string> OnStatusUpdate;
        public event Action<string> OnCurrentWaybillChanged;
        public event Action<int> OnSaveCountChanged;
        public event Action<string> OnTrackingHistoryChanged;
        public event Action<string> OnWaybillCompleted;

        /// <summary>Nội dung cho ô kết quả tabDKCH_result (thao tác cuối / kết quả sau Lưu).</summary>
        public event Action<DkchResultInfo> OnResultChanged;

        private PeriodicTimer _mainLoadTimer;
        private CancellationTokenSource _dkchCts;
        private bool _isRunning = false;
        private bool _isProcessing = false;
        private string _currentMode;
        private int _lastProcessedIndex = 0;
        private int _saveCount = 0;
        private List<string> _priorityQueue = new List<string>();
        private object _queueLock = new object();

        /// <summary>Mã đã Lưu thành công trong phiên — chốt cuối chống đăng ký trùng.</summary>
        private readonly HashSet<string> _savedInSession = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Mã đã xử lý xong trong phiên (kể cả bị bỏ qua) — chống lặp vô hạn.</summary>
        private readonly HashSet<string> _processedInSession = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        private int _skipCount = 0;

        /// <summary>Chế độ hướng dẫn (Normal/Newbie) — người dùng chọn ở mục DATA của tab DKCH.</summary>
        public DkchGuideMode GuideMode { get; set; } = DkchGuideMode.Normal;

        /// <summary>Số mã trong lượt nhập tay gần nhất. 1 = nhập lẻ, >1 = danh sách.</summary>
        private int _manualBatchSize = 1;

        /// <summary>
        /// Đánh dấu bắt đầu một lượt nhập tay gồm <paramref name="count"/> mã. Dòng đề xuất chỉ hiện
        /// khi Newbie + nhập lẻ 1 mã; nhập danh sách thì hành xử như Normal.
        /// </summary>
        public void BeginManualBatch(int count) => _manualBatchSize = count < 1 ? 1 : count;

        private bool ShouldShowRecommend => GuideMode == DkchGuideMode.Newbie && _manualBatchSize == 1;

        /// <summary>
        /// Chốt cờ "được hiện dòng đề xuất" theo TỪNG mã ngay lúc xếp hàng. Nếu đọc
        /// <see cref="ShouldShowRecommend"/> lúc publish thì một mã lẻ đang xử lý sẽ bị đổi kết quả
        /// khi người dùng vừa dán thêm danh sách, và mã lấy từ Google Sheet sẽ thừa hưởng oan.
        /// </summary>
        private readonly Dictionary<string, bool> _showRecommendFor =
            new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Cờ áp dụng cho mã đang xử lý (mỗi lúc chỉ có 1 mã nhờ chốt _isProcessing).</summary>
        private bool _showRecommendCurrent;
        private static readonly Regex WaybillRegex = new Regex("^[A-Za-z0-9]{1,20}$", RegexOptions.Compiled);
        private DateTime _lastSheetFetchTime = DateTime.MinValue;
        private List<string> _cachedSheetData = new List<string>();
        // Dependencies
        private WebView2 _webView;
        private ITrackingService _trackingService;
        private Func<(bool useSheet, string sheetName, int rowCount)> _settingsGetter;

        public bool IsRunning => _isRunning;

        public void SetWebView(WebView2 webView) => _webView = webView;
        public void SetTrackingService(ITrackingService service) => _trackingService = service;
        public void SetSettingsGetter(Func<(bool useSheet, string sheetName, int rowCount)> getter) => _settingsGetter = getter;


        /// <summary>
        /// Xếp mã vào hàng đợi ưu tiên.
        /// <para>
        /// Mã người dùng nhập LUÔN được nhận, kể cả khi đã đăng ký thành công trước đó trong phiên —
        /// nhập lại là thao tác có chủ đích, app không được tự bỏ qua. Chỉ từ chối khi mã không hợp
        /// lệ hoặc đang CHỜ trong hàng đợi (thêm nữa cũng không sinh thêm lượt Lưu nào).
        /// </para>
        /// </summary>
        public bool AddPriorityWaybill(string waybill)
        {
            if (!IsValidWaybill(waybill)) return false;
            string code = waybill.Trim();

            lock (_queueLock)
            {
                if (_priorityQueue.Any(x => string.Equals(x, code, StringComparison.OrdinalIgnoreCase)))
                {
                    OnLog?.Invoke($"{code}: Đang chờ trong hàng đợi — không xếp thêm.");
                    return false;
                }
                _priorityQueue.Add(code);
                _showRecommendFor[code] = ShouldShowRecommend;
            }
            return true;
        }

        /// <summary>Ghi nhận "đã xử lý trong phiên" — luôn dưới <c>_queueLock</c> vì HashSet không thread-safe.</summary>
        private void MarkProcessed(string waybill)
        {
            lock (_queueLock) _processedInSession.Add(waybill);
        }

        private static bool IsValidWaybill(string waybill)
        {
            if (string.IsNullOrWhiteSpace(waybill)) return false;
            return WaybillRegex.IsMatch(waybill.Trim());
        }

        public void StartDaemon()
        {
            // 200ms thay vì 500ms: đây là độ trễ trước khi một mã vừa nhập được nhặt ra khỏi hàng đợi.
            _mainLoadTimer = new PeriodicTimer(TimeSpan.FromMilliseconds(200));
            _ = Task.Run(async () =>
            {
                while (await _mainLoadTimer.WaitForNextTickAsync())
                {
                    try
                    {
                        if (!_isRunning || _isProcessing) continue;

                        bool hasPriorityJob = false;
                        lock (_queueLock) hasPriorityJob = _priorityQueue.Count > 0;

                        List<string> sheetData = new List<string>();
                        bool useSheet = false;
                        string sheetName = "";
                        int rowCount = 0;

                        if (!hasPriorityJob && _settingsGetter != null)
                        {
                            var sets = _settingsGetter();
                            useSheet = sets.useSheet;
                            sheetName = sets.sheetName;
                            rowCount = sets.rowCount;

                            if (useSheet)
                            {
                                //Giới hạn 15 giây mới Request 1 lần
                                if ((DateTime.Now - _lastSheetFetchTime).TotalSeconds >= 15)
                                {
                                    _cachedSheetData = GoogleSheetService.ReadColumn(sheetName, rowCount);
                                    _lastSheetFetchTime = DateTime.Now;
                                }

                                sheetData = _cachedSheetData;
                            }
                            else
                            {

                            }
                        }

                        bool hasNewJob = hasPriorityJob || (useSheet && _lastProcessedIndex < sheetData.Count);

                        if (hasNewJob && _dkchCts != null && !_dkchCts.IsCancellationRequested)
                        {
                            _isProcessing = true;
                            try
                            {
                                await ProcessAutomationQueueAsync(sheetData);
                            }
                            finally { _isProcessing = false; }
                        }
                    }
                    catch { _isProcessing = false; }
                }
            });
        }

        public async Task StartAsync(string mode)
        {
            string targetUrl = AppConfig.Current.BuildJmsUrl("app/operatingPlatformIndex/returnAndForwardMaintainAddSite");
            string current = _webView.Source?.ToString() ?? "";
            if (!current.Contains("returnAndForwardMaintainAddSite"))
            {
                _webView.Source = new Uri(targetUrl);
                await Task.Delay(1000);
            }

            Stop();

            _currentMode = mode;
            _dkchCts = new CancellationTokenSource();
            _isRunning = true;
            _isProcessing = false;

            lock (_queueLock)
            {
                _priorityQueue.Clear();
                _savedInSession.Clear();
                _processedInSession.Clear();
                _showRecommendFor.Clear();
            }

            _lastProcessedIndex = 0;
            _saveCount = 0;
            _skipCount = 0;
            OnSaveCountChanged?.Invoke(0);
            _lastSheetFetchTime = DateTime.MinValue;
            OnLog?.Invoke($"=== START {_currentMode} ===");
        }

        public void Stop()
        {
            _isRunning = false;
            _dkchCts?.Cancel();
            _dkchCts = null;
            _isProcessing = false;
        }

        private async Task ProcessAutomationQueueAsync(List<string> sheetData)
        {
            var tokenSource = _dkchCts;
            if (tokenSource == null || tokenSource.IsCancellationRequested) return;

            // Xử lý hàng đợi ưu tiên trước
            while (true)
            {
                if (tokenSource.IsCancellationRequested) return;

                string waybill = null;
                lock (_queueLock)
                {
                    if (_priorityQueue.Count > 0) waybill = _priorityQueue[0];
                }

                if (waybill == null) break;
                if (!IsValidWaybill(waybill))
                {
                    RemoveFromQueue(waybill);
                    continue;
                }

                OnCurrentWaybillChanged?.Invoke(waybill);
                await ExecuteOneWaybill(waybill, tokenSource.Token);
                OnWaybillCompleted?.Invoke(waybill);

                // Xóa theo GIÁ TRỊ, không phải RemoveAt(0): nếu người dùng vừa nhập mã mới
                // trong lúc đang xử lý thì RemoveAt(0) sẽ xóa oan phần tử khác.
                RemoveFromQueue(waybill);
            }

            // Xử lý dữ liệu từ sheet
            if (_lastProcessedIndex < sheetData.Count)
            {
                while (_lastProcessedIndex < sheetData.Count)
                {
                    if (tokenSource.IsCancellationRequested) return;

                    bool hasNewInput = false;
                    lock (_queueLock) hasNewInput = _priorityQueue.Count > 0;
                    if (hasNewInput) return;

                    string waybill = sheetData[_lastProcessedIndex];
                    if (!IsValidWaybill(waybill))
                    {
                        _lastProcessedIndex++;
                        continue;
                    }

                    // CHỈ nguồn Google Sheet mới chống trùng theo phiên: sheet được đọc lại mỗi 15s
                    // nên không chặn thì cùng một dòng sẽ bị đăng ký lặp vô hạn.
                    // Mã người dùng NHẬP TAY thì không đi qua nhánh này — nhập lại là làm lại.
                    bool alreadyDone;
                    lock (_queueLock) alreadyDone = _processedInSession.Contains(waybill.Trim());
                    if (alreadyDone)
                    {
                        OnLog?.Invoke($"↷ [{_currentMode}] Row {_lastProcessedIndex + 1}: {waybill} — dòng sheet đã xử lý trong phiên, bỏ qua.");
                        _lastProcessedIndex++;
                        continue;
                    }

                    OnLog?.Invoke($"▶ [{_currentMode}] Row {_lastProcessedIndex + 1}: {waybill}");
                    OnCurrentWaybillChanged?.Invoke(waybill);

                    await ExecuteOneWaybill(waybill, tokenSource.Token);
                    MarkProcessed(waybill.Trim());
                    OnWaybillCompleted?.Invoke(waybill);
                    _lastProcessedIndex++;
                }
            }
        }


        private void RemoveFromQueue(string waybill)
        {
            lock (_queueLock)
                _priorityQueue.RemoveAll(x => string.Equals(x, waybill, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Xử lý một mã vận đơn. Nguyên tắc: <b>đọc lịch sử hành trình trước, chỉ bấm Lưu khi
        /// nghiệp vụ cho phép, và bấm Lưu tối đa 1 lần cho mỗi mã.</b>
        /// <para>
        /// Đã bỏ ma trận retry 2×2 của bản cũ (2 vòng ngoài × 2 lần đổi mode = tối đa 4 lần
        /// bấm Lưu cho cùng một đơn). Nay chỉ đổi mode DKCH1↔DKCH2 đúng 1 lần và CHỈ khi server
        /// trả về <c>succ=false</c>/<c>fail=true</c> — tức chắc chắn lần trước chưa ghi nhận.
        /// Timeout, lỗi UI hay lỗi lạ đều KHÔNG lặp.
        /// </para>
        /// </summary>
        private async Task<bool> ExecuteOneWaybill(string waybill, CancellationToken token)
        {
            // ── Bước 1: đọc lịch sử hành trình ĐÚNG MỘT LẦN cho cả chu kỳ ─────────────────
            // Dữ liệu này dùng cho: quyết định nghiệp vụ, dòng 1 (thao tác cuối cùng), dòng 4
            // (số lần ĐKCH / phát lại) và ô lịch sử. Sau khi Lưu KHÔNG tracking lại nữa.
            lock (_queueLock)
            {
                _showRecommendCurrent = _showRecommendFor.TryGetValue(waybill, out bool flag) && flag;
                _showRecommendFor.Remove(waybill);
            }

            OnStatusUpdate?.Invoke("1/3 Đọc lịch sử hành trình...");
            var gate = await EvaluateJourneyAsync(waybill);
            PublishJourney(waybill, gate);

            // ── Bước 2: nhận diện case. CHỈ 2 trạng thái chặn mới không bấm Lưu ───────────
            if (gate.Decision.IsBlocked)
            {
                _skipCount++;
                PublishDecision(waybill, gate.Decision);
                OnLog?.Invoke($"{waybill}: {gate.Decision.Badge}");
                OnStatusUpdate?.Invoke($"Chặn — không bấm Lưu. (Đã chặn: {_skipCount})");
                return false;
            }

            // Theo dõi MODE bằng khoá "DKCH1"/"DKCH2", KHÔNG bằng chuỗi nhãn tiếng Việt: JMS đã
            // từng đổi nhãn ("Chuyển hoàn" -> "Từ chối") và sẽ còn đổi. Nhãn thật lấy từ
            // tab2config.json ngay trước khi chọn dropdown.
            string modeKey = string.Equals(_currentMode, "DKCH2", StringComparison.OrdinalIgnoreCase)
                ? "DKCH2" : "DKCH1";
            bool modeSwitched = false;
            string pickedOption = "";

            // ── Bước 3: bấm Lưu. Tối đa 2 lượt, lượt 2 CHỈ khi JMS báo sai mode ──────────
            for (int pass = 0; pass < 2; pass++)
            {
                if (token.IsCancellationRequested) return false;

                try
                {
                    pickedOption = await PrepareFormAsync(waybill, modeKey, token);

                    OnStatusUpdate?.Invoke($"3/3 Lưu ({pickedOption})...");
                    var save = await WebViewAutomation.ClickSaveAndVerifyAsync(_webView, token);

                    if (save.Outcome == DkchSaveOutcome.Success)
                    {
                        MarkSaved(waybill, $"Đăng ký chuyển hoàn thành công ({pickedOption}). JMS: {save.Message}");
                        PublishFromCatalog(DkchResultLevel.Success,
                            BuildContext(waybill, "afterSave", "success", gate.Decision, save, modeKey));

                        // Nghỉ sau khi Lưu THÀNH CÔNG: JMS còn phải dựng lại form cho mã kế
                        // tiếp. Bắn mã mới vào giữa lúc đó thì ô mã hoặc dropdown chưa sẵn
                        // sàng. Chỉ nghỉ ở nhánh thành công — nhánh lỗi không có form mới nào
                        // để chờ, nghỉ thêm chỉ làm chậm.
                        await Task.Delay(DkchAfterSaveDelayMs, token);
                        return true;
                    }

                    // Server xác nhận thất bại (succ=false/fail=true) và không thuộc nhóm đổi mode
                    // → báo nguyên văn thông điệp JMS, KHÔNG bấm Lưu lại.
                    if (save.Outcome == DkchSaveOutcome.Failed)
                    {
                        string codeSuffix = string.IsNullOrEmpty(save.Code) ? "" : $" (code {save.Code})";

                        // Chi tiết đầy đủ (mã lỗi + nguyên văn tiếng Trung) chỉ vào log.
                        OnLog?.Invoke($"{waybill}: ❌ JMS từ chối — {save.Message}{codeSuffix} | {save.RawMessage}");

                        PublishFromCatalog(DkchResultLevel.Error,
                            BuildContext(waybill, "afterSave", "failed", gate.Decision, save, modeKey));
                        return false;
                    }

                    // Không đọc được phản hồi trong thời gian chờ → báo cho người dùng, KHÔNG Lưu lại
                    // và KHÔNG tracking lại (giữ tốc độ). Nhập lại mã đó là xử lý được ngay.
                    OnLog?.Invoke($"{waybill}: ⚠ Không đọc được phản hồi Lưu — cần kiểm tra tay (không Lưu lại).");
                    PublishFromCatalog(DkchResultLevel.Warning,
                        BuildContext(waybill, "afterSave", "unverified", gate.Decision, save, modeKey));
                    return false;
                }
                catch (OperationCanceledException)
                {
                    return false;
                }
                catch (NoDataWaybillException)
                {
                    OnLog?.Invoke($"{waybill}: JMS báo không có dữ liệu.");
                    PublishFromCatalog(DkchResultLevel.Error,
                        BuildContext(waybill, "afterSave", "noData", gate.Decision, null, modeKey));
                    return false;
                }
                catch (NeedSwitchToDkch2Exception)
                {
                    if (modeSwitched)
                    {
                        OnLog?.Invoke($"{waybill}: Đã đổi mode 1 lần vẫn lỗi — dừng.");
                        PublishFromCatalog(DkchResultLevel.Error,
                            BuildContext(waybill, "afterSave", "modeSwitchFailed", gate.Decision, null, modeKey));
                        return false;
                    }
                    // Đang chạy sẵn DKCH2 thì "đổi mode" là vô nghĩa — lượt 2 sẽ lặp y nguyên
                    // request, chỉ tốn thêm 1 lần bấm Lưu. Dừng và báo lỗi.
                    if (modeKey == "DKCH2")
                    {
                        OnLog?.Invoke($"{waybill}: JMS báo cần DKCH2 nhưng đang chạy DKCH2 — dừng.");
                        PublishFromCatalog(DkchResultLevel.Error,
                            BuildContext(waybill, "afterSave", "modeSwitchFailed", gate.Decision, null, "DKCH2"));
                        return false;
                    }

                    modeSwitched = true;
                    modeKey = "DKCH2";
                    // Không publish trạng thái đổi mode: đây là bước trung gian, kết quả thật
                    // của lượt 2 sẽ được publish ngay sau đó.
                    OnLog?.Invoke($"{waybill}: server báo cần DKCH2 (succ=false → chưa lưu) → đổi mode, thử lại 1 lần.");
                }
                catch (NeedSwitchToDkch1Exception)
                {
                    if (modeSwitched)
                    {
                        OnLog?.Invoke($"{waybill}: Đã đổi mode 1 lần vẫn lỗi — dừng.");
                        PublishFromCatalog(DkchResultLevel.Error,
                            BuildContext(waybill, "afterSave", "modeSwitchFailed", gate.Decision, null, modeKey));
                        return false;
                    }
                    if (modeKey == "DKCH1")
                    {
                        OnLog?.Invoke($"{waybill}: JMS báo cần DKCH1 nhưng đang chạy DKCH1 — dừng.");
                        PublishFromCatalog(DkchResultLevel.Error,
                            BuildContext(waybill, "afterSave", "modeSwitchFailed", gate.Decision, null, "DKCH1"));
                        return false;
                    }
                    modeSwitched = true;
                    modeKey = "DKCH1";
                    OnLog?.Invoke($"{waybill}: server báo cần DKCH1 (succ=false → chưa lưu) → đổi mode, thử lại 1 lần.");
                }
                catch (Exception ex)
                {
                    // KHÔNG "ép chuyển DKCH2" khi gặp lỗi lạ — đó là nguồn gốc spam đăng ký.
                    OnLog?.Invoke($"{waybill}: Lỗi — {ex.Message} (không Lưu lại).");
                    PublishFromCatalog(DkchResultLevel.Error,
                        BuildContext(waybill, "afterSave", "error", gate.Decision, null, modeKey, ex.Message));
                    return false;
                }
            }

            return false;
        }

        private void MarkSaved(string waybill, string reason)
        {
            lock (_queueLock)
            {
                _savedInSession.Add(waybill);
                MarkProcessed(waybill);
            }
            _saveCount++;
            OnSaveCountChanged?.Invoke(_saveCount);
            OnLog?.Invoke($"{waybill}: ✅ {reason}");
        }

        /// <summary>Kết quả đọc lịch sử: quyết định + đoạn text để hiển thị.</summary>
        private sealed class JourneyGate
        {
            public DkchJourneyDecision Decision { get; set; }
            public string HistoryText { get; set; } = "";
        }

        /// <summary>
        /// Gọi podTracking MỘT lần, dùng chung kết quả cho cả quyết định nghiệp vụ và hiển thị
        /// (bản cũ chỉ lấy text để hiển thị rồi bỏ đi, không dùng để quyết định).
        /// </summary>
        private async Task<JourneyGate> EvaluateJourneyAsync(string waybill)
        {
            if (_trackingService == null)
            {
                return new JourneyGate
                {
                    HistoryText = "Chưa khởi tạo Tracking Service.",
                    Decision = new DkchJourneyDecision
                    {
                        Action = DkchAction.BlockedNoData,
                        Reason = "Chưa khởi tạo Tracking Service — không đủ căn cứ để đăng ký."
                    }
                };
            }

            try
            {
                var details = await _trackingService.GetWaybillDetailsAsync(waybill);
                if (details == null)
                {
                    return new JourneyGate
                    {
                        HistoryText = "Không lấy được lịch sử hành trình (lỗi kết nối JMS hoặc hết phiên).",
                        Decision = new DkchJourneyDecision
                        {
                            Action = DkchAction.BlockedNoData,
                            Reason = "Không lấy được lịch sử hành trình — dừng để tránh đăng ký mù."
                        }
                    };
                }

                return new JourneyGate
                {
                    Decision = DkchJourneyAnalyzer.Analyze(details),
                    HistoryText = _trackingService.BuildDkchHistoryText(waybill, details)
                };
            }
            catch (Exception ex)
            {
                return new JourneyGate
                {
                    HistoryText = $"Lỗi đọc lịch sử: {ex.Message}",
                    Decision = new DkchJourneyDecision
                    {
                        Action = DkchAction.BlockedNoData,
                        Reason = $"Lỗi đọc lịch sử hành trình: {ex.Message}"
                    }
                };
            }
        }

        /// <summary>
        /// Đẩy lịch sử hành trình lên tabDKCH_nowTracking — CHỈ phần lịch sử, không thêm header.
        /// <para>
        /// Bản trước in thêm 4 dòng tóm tắt (quét kiện vấn đề / nguyên nhân / số lần ĐKCH / số lần
        /// phát lại) nhưng chúng trùng hoàn toàn với ô tabDKCH_result ngay phía trên. Số lần ĐKCH và
        /// số lần phát lại nay nằm gọn trên 1 dòng ở tabDKCH_result; ô này thuần lịch sử hành trình.
        /// </para>
        /// </summary>
        private void PublishJourney(string waybill, JourneyGate gate)
        {
            OnTrackingHistoryChanged?.Invoke(
                string.IsNullOrWhiteSpace(gate.HistoryText)
                    ? "(Không có đoạn hành trình từ lần về kho Kim Tân/(LCI) gần nhất.)"
                    : gate.HistoryText);
        }

        /// <summary>
        /// Ánh xạ kết luận nghiệp vụ sang mức hiển thị của ô kết quả.
        /// <para>
        /// "Đã ghi nhận đăng ký chuyển hoàn" là XANH LÁ (Success), không phải cảnh báo: đơn đã ở
        /// đúng trạng thái mong muốn, việc bỏ qua là kết quả đúng chứ không phải sự cố.
        /// </para>
        /// </summary>
        private static DkchResultLevel LevelOf(DkchAction action) => action switch
        {
            DkchAction.Register => DkchResultLevel.Info,
            DkchAction.SkipAlreadyRegistered => DkchResultLevel.Success,
            DkchAction.BlockedPendingProblemScan => DkchResultLevel.Error,
            DkchAction.BlockedForward => DkchResultLevel.Error,
            DkchAction.BlockedSignedCpn => DkchResultLevel.Error,
            DkchAction.BlockedReturning => DkchResultLevel.Error,
            DkchAction.BlockedNewArrival => DkchResultLevel.Error,
            _ => DkchResultLevel.Warning
        };

        /// <summary>
        /// Đẩy kết luận nghiệp vụ (trước khi bấm Lưu) lên ô kết quả.
        /// <para>
        /// Đơn HỢP LỆ: tiêu đề "Thao tác cuối cùng" kèm dòng thao tác — người dùng cần thấy đơn
        /// đang ở đâu trước khi app bấm Lưu.
        /// </para>
        /// <para>
        /// Đơn BỊ CHẶN/BỎ QUA: chỉ ghi lỗi. Không kèm thao tác cuối cùng — đơn sẽ không được Lưu
        /// nên trạng thái hành trình không còn là thông tin cần thiết (đã có sẵn ở ô lịch sử bên dưới).
        /// </para>
        /// </summary>
        private void PublishDecision(string waybill, DkchJourneyDecision decision)
        {
            string outcome = decision.Action switch
            {
                DkchAction.Register => "readyToRegister",
                DkchAction.SkipAlreadyRegistered => "skipped",
                // Ba nhánh chặn, ba thông điệp: tự ý phát thêm ca · phát lại chưa kiện ·
                // đơn chưa từng giao lại mà đã quét phát.
                DkchAction.BlockedPendingProblemScan =>
                    decision.SelfDispatchViolation ? "blockedViolation"
                    : decision.NoRedeliverBeforeDispatch ? "blockedNoProblemScan"
                    : "blocked",
                // Kiện vấn đề vì đổi địa chỉ → đơn đi tiếp, không chuyển hoàn.
                DkchAction.BlockedForward => "blockedForward",
                // Thao tác cuối đã kết thúc luồng chuyển hoàn → không đăng ký, chỉ báo việc còn lại.
                DkchAction.BlockedSignedCpn => "blockedSignedCpn",
                DkchAction.BlockedReturning => "blockedReturning",
                // Hàng vừa về kho, chưa từng quét phát → chưa có gì để hoàn.
                DkchAction.BlockedNewArrival => "blockedNewArrival",
                _ => "noData"
            };

            PublishFromCatalog(LevelOf(decision.Action),
                BuildContext(waybill, "beforeSave", outcome, decision, null, ModeOfCurrentRun()));
        }

        /// <summary>
        /// Tra <c>modules/tab2config.json</c> rồi đẩy 4 dòng lên ô kết quả tabDKCH_result:
        /// <list type="number">
        /// <item>thao tác cuối cùng (từ lần đọc lịch sử duy nhất của chu kỳ)</item>
        /// <item>thông điệp kết quả/lỗi</item>
        /// <item>đề xuất thao tác tiếp theo — chỉ khi Newbie + nhập lẻ 1 mã</item>
        /// <item>số lần đã ĐKCH · số lần phát lại</item>
        /// </list>
        /// </summary>
        private void PublishFromCatalog(DkchResultLevel level, DkchResultContext ctx)
        {
            var text = Tab2Config.Current.Resolve(ctx);

            OnResultChanged?.Invoke(new DkchResultInfo
            {
                Waybill = ctx.Waybill ?? "",
                Level = level,
                LastAction = ctx.LastAction ?? "",
                LastActionType = ctx.LastActionType ?? "",
                LastActionTime = ctx.LastActionTime ?? "",
                LastActionNote = ctx.LastActionNote ?? "",
                Message = text.Result ?? "",
                ActRecommend = text.ActRecommend ?? "",
                ShowRecommend = _showRecommendCurrent,
                Stats = text.Stats ?? "",
                ActionPrefix = Tab2Config.Current.ActionPrefix ?? "→ ",
                CaseId = text.CaseId ?? "",
                AfterSave = string.Equals(ctx.Phase, "afterSave", StringComparison.OrdinalIgnoreCase),

                Operator = ctx.Journey?.LastEventOperator ?? "",
                DaysInStock = ctx.Journey?.DaysInStock,
                RegisterCount = ctx.RegisterCount,
                RedeliverCount = ctx.RedeliverCount,
                Violation = ctx.Journey != null && ctx.Journey.SelfDispatchViolation
                    ? "Đã quét kiện vấn đề rồi lại quét phát hàng tiếp — tự ý phát thêm ca."
                    : "",
                Steps = ctx.Journey?.Steps ?? new List<DkchStep>(),
                Entries = ctx.Journey?.Entries ?? new List<DkchJourneyEntry>()
            });
        }

        /// <summary>Dựng dữ kiện cho tab2config từ quyết định nghiệp vụ + phản hồi JMS.</summary>
        private DkchResultContext BuildContext(string waybill, string phase, string outcome,
                                               DkchJourneyDecision decision, DkchSaveResult save,
                                               string mode, string errorMessage = "")
        {
            var ctx = new DkchResultContext
            {
                Waybill = waybill ?? "",
                Phase = phase,
                Outcome = outcome,
                Mode = mode ?? "DKCH1",
                ErrorMessage = errorMessage ?? ""
            };

            if (decision != null)
            {
                ctx.Journey = decision;
                ctx.HasJourney = true;
                ctx.RegisterCount = decision.RegisterCount;
                ctx.RedeliverCount = decision.RedeliverCount;
                ctx.DispatchCount = decision.DeliveryAttemptCount;
                ctx.LastAction = decision.LastActionLine;
                ctx.LastActionType = decision.LastEventType;
                ctx.LastActionTime = decision.LastEventTime;
                ctx.LastActionNote = decision.LastEventNote;
                ctx.ProblemScanTime = decision.ProblemScanTime;
                ctx.ProblemScanReason = decision.ProblemScanReason;
            }

            if (save != null)
            {
                ctx.JmsCode = save.Code;
                ctx.JmsMessage = save.Message;
                ctx.JmsRawMessage = save.RawMessage;
                ctx.Ratio = save.Ratio;
            }

            return ctx;
        }

        /// <summary>Mode của phiên đang chạy — dùng khi chưa vào vòng xử lý một mã cụ thể.</summary>
        private string ModeOfCurrentRun()
            => string.Equals(_currentMode, "DKCH2", StringComparison.OrdinalIgnoreCase) ? "DKCH2" : "DKCH1";

        /// <summary>
        /// Chọn mode → điền mã → tìm kiếm. Không bấm Lưu ở đây.
        /// Trả về nhãn dropdown thật sự đã chọn được (để ghi log/hiển thị).
        /// </summary>
        /// <summary>
        /// Nghỉ sau mỗi lần Lưu thành công, để JMS kịp dựng lại form trống cho mã kế tiếp.
        /// </summary>
        private const int DkchAfterSaveDelayMs = 200;

        private async Task<string> PrepareFormAsync(string waybill, string modeKey, CancellationToken token)
        {
            var options = Tab2Config.Current.DropdownOptionsFor(modeKey);
            // Các nhịp chờ ở đây nhân với số mã trong lượt nhập, nên cắt xuống mức tối thiểu
            // mà Vue vẫn kịp nhận dữ liệu (trước: 100 + 100 = 200ms mỗi đơn).
            string picked = await WebViewAutomation.CheckAndSelectDropdownAsync(_webView, options, token);
            await Task.Delay(40, token);

            OnStatusUpdate?.Invoke("2/3 Điền mã + tìm kiếm...");
            await WebViewAutomation.FillWaybillAsync(_webView, waybill, token);
            await Task.Delay(40, token);

            await WebViewAutomation.ClickSearchAsync(_webView, waybill, token);
            return picked;
        }



    }
}

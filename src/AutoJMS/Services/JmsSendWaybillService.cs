using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AutoJMS.Diagnostics;

namespace AutoJMS
{
    /// <summary>
    /// Màn "Quản lý vận đơn gửi" (<c>sendWaybillSite</c>) của JMS — nguồn dữ liệu duy nhất
    /// của tab con "In Reverse".
    ///
    /// In Reverse tra được theo hai lối — <b>nhân viên lấy hàng + khoảng thời gian</b>, hoặc
    /// <b>mã vận đơn gõ thẳng</b> — nhưng cả hai đều không đi qua
    /// <see cref="PrintService.SearchAndLoadAsync"/> (tracking + SafetyGuard) mà nạp thẳng
    /// kết quả vào lưới.
    ///
    /// Mọi request ở đây đi qua <see cref="JmsApiClient"/> để dùng chung hàng đợi 12 slot,
    /// token hiện hành và một lượt retry sau khi refresh token.
    /// </summary>
    public static class JmsSendWaybillService
    {
        // Breadcrumb của màn sendWaybillSite, chép nguyên văn từ cURL. Dấu ">" phân cấp
        // cũng percent-encode thành %3E — y như mọi hằng RouterNameList khác trong repo.
        private const string SendWaybillRouterNameList =
            "%E7%BD%91%E7%82%B9%E7%BB%8F%E8%90%A5%3E%E8%BF%90%E5%8D%95%E7%AE%A1%E7%90%86%3E%E5%AF%84%E4%BB%B6%E8%BF%90%E5%8D%95%E7%AE%A1%E7%90%86";
        private const string SendWaybillRouteName = "sendWaybillSite";

        private const string StaffEndpoint = "basicdata/sysStaff/selectAll";
        private const string NetworkEndpoint = "basicdata/network/select/all";
        private const string ShippingListEndpoint = "networkmanagement/omsWaybill/shippingWaybillList";

        /// <summary>Endpoint in của màn gửi — payload khác hẳn luồng in mặc định.</summary>
        public const string CenterPrintEndpoint = "networkmanagement/print/waybillCenterPrint";
        public const string CenterPrintRouteName = SendWaybillRouteName;
        public const string CenterPrintRouterNameList = SendWaybillRouterNameList;

        private const string TimeFormat = "yyyy-MM-dd HH:mm:ss";

        // size=20 đúng như giao diện JMS. Lưới In Reverse cũng hiện 20 đơn mỗi trang, nên một
        // trang API là một trang lưới. Hai bài test ghim Content-Length 1114 và 341 chỉ đúng
        // khi số này còn 2 ký tự. Lấy từng trang cho tới khi trang không còn đầy, chặn trên để
        // một bộ lọc quá rộng không kéo về vô hạn.
        private const int PageSize = 20;
        private const int MaxPages = 50;

        // networkId của bưu cục không đổi trong suốt phiên, mà tra nó tốn một lượt mạng.
        private static string _cachedNetworkId;
        private static string _cachedNetworkIdForSite;
        private static string _cachedFinanceCode;

        public sealed class StaffInfo
        {
            public string Code { get; init; } = "";
            public string Name { get; init; } = "";
            public string Display => string.IsNullOrWhiteSpace(Code) ? Name : $"{Name} ({Code})";
        }

        /// <summary>
        /// Tra nhân viên theo tên cho bưu cục đang đăng nhập. Trả danh sách rỗng khi JMS
        /// không trả bản ghi nào — gọi hàm này không bao giờ ném vì lỗi nghiệp vụ.
        /// </summary>
        public static async Task<IReadOnlyList<StaffInfo>> SearchStaffAsync(
            string name, string siteCode, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(name)) return Array.Empty<StaffInfo>();

            string url = AppConfig.Current.BuildJmsApiUrl(StaffEndpoint)
                       + "?name=" + Uri.EscapeDataString(name.Trim())
                       + "&networkLevel=3";

            // networkId chỉ để thu hẹp kết quả về đúng bưu cục. Không tra được thì vẫn gọi:
            // JMS đã biết bưu cục của phiên qua authToken.
            string networkId = await ResolveNetworkIdAsync(siteCode, ct).ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(networkId))
                url += "&networkId=" + Uri.EscapeDataString(networkId);

            string body = await ReadAsync(HttpMethod.Get, url, null, "SearchStaff", ct).ConfigureAwait(false);
            var unique = ParseStaff(body);

            AppLogger.Info($"[SendWaybill] SearchStaff name={name} results={unique.Count}");
            return unique;
        }

        /// <summary>
        /// Bóc danh sách nhân viên từ body của <c>sysStaff/selectAll</c>. Tách riêng khỏi
        /// phần gọi mạng để test bằng response thật.
        /// </summary>
        internal static IReadOnlyList<StaffInfo> ParseStaff(string body)
        {
            if (string.IsNullOrWhiteSpace(body)) return Array.Empty<StaffInfo>();

            var staff = new List<StaffInfo>();
            try
            {
                using var doc = JsonDocument.Parse(body);
                foreach (var item in EnumerateRecords(doc.RootElement))
                {
                    string code = FirstText(item, "code");
                    string staffName = FirstText(item, "name");
                    if (string.IsNullOrWhiteSpace(code) && string.IsNullOrWhiteSpace(staffName)) continue;
                    staff.Add(new StaffInfo { Code = code, Name = staffName });
                }
            }
            catch (Exception ex)
            {
                AppLogger.Warning($"[SendWaybill] SearchStaff parse failed: {ex.Message}");
                return Array.Empty<StaffInfo>();
            }

            // Cùng một người có thể xuất hiện nhiều dòng (nhiều vai trò) — lưới chọn chỉ cần
            // mỗi mã một lần.
            return staff
                .Where(s => !string.IsNullOrWhiteSpace(s.Code))
                .GroupBy(s => s.Code, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .ToList();
        }

        /// <summary>
        /// Danh sách vận đơn nhân viên <paramref name="collectStaffCode"/> đã lấy trong
        /// khoảng thời gian đã chọn, đã map sẵn sang <see cref="TrackingRow"/> để nạp thẳng
        /// vào lưới của tab IN ĐƠN.
        /// </summary>
        public static async Task<IReadOnlyList<TrackingRow>> SearchShippingWaybillsAsync(
            string collectStaffCode,
            DateTime timeFrom,
            DateTime timeTo,
            string customerCodes,
            CancellationToken ct = default)
        {
            string financeCode = await ResolveFinanceCodeAsync(ct).ConfigureAwait(false);
            var rows = await CollectShippingPagesAsync(
                current => BuildListForm(current, collectStaffCode, timeFrom, timeTo, customerCodes, financeCode),
                ct).ConfigureAwait(false);

            AppLogger.Info($"[SendWaybill] ShippingWaybillList staff={collectStaffCode} rows={rows.Count} " +
                           $"from={timeFrom.ToString(TimeFormat, CultureInfo.InvariantCulture)} " +
                           $"to={timeTo.ToString(TimeFormat, CultureInfo.InvariantCulture)}");
            return rows;
        }

        /// <summary>
        /// Danh sách vận đơn tra thẳng theo mã — người dùng gõ hoặc quét mã vào ô "Mã vận đơn"
        /// rồi bấm Tìm kiếm. Không cần nhân viên lẫn khoảng thời gian: cURL của giao diện JMS
        /// cho luồng này chỉ gửi ba trường, xem <see cref="BuildWaybillListForm"/>.
        /// </summary>
        public static async Task<IReadOnlyList<TrackingRow>> SearchShippingWaybillsByNoAsync(
            string waybillNos,
            CancellationToken ct = default)
        {
            var rows = await CollectShippingPagesAsync(
                current => BuildWaybillListForm(current, waybillNos), ct).ConfigureAwait(false);

            AppLogger.Info($"[SendWaybill] ShippingWaybillList byNo={waybillNos} rows={rows.Count}");
            return rows;
        }

        /// <summary>
        /// Vòng lấy trang dùng chung cho hai lối tra (theo nhân viên và theo mã): chúng chỉ
        /// khác nhau ở bộ trường của form, còn phân trang, lọc trùng và bóc bản ghi thì giống hệt.
        /// </summary>
        private static async Task<List<TrackingRow>> CollectShippingPagesAsync(
            Func<int, HttpContent> formFactory,
            CancellationToken ct)
        {
            string url = AppConfig.Current.BuildJmsApiUrl(ShippingListEndpoint);
            var rows = new List<TrackingRow>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            for (int page = 1; page <= MaxPages; page++)
            {
                int current = page;
                string body = await ReadAsync(
                    HttpMethod.Post,
                    url,
                    () => formFactory(current),
                    "ShippingWaybillList",
                    ct).ConfigureAwait(false);

                if (string.IsNullOrWhiteSpace(body)) break;

                // JMS báo lỗi nghiệp vụ trong thân HTTP 200 rồi để data=null. Không đọc
                // code/msg thì MỌI lỗi đều hiện ra thành "Không có đơn nào trong khoảng
                // thời gian này" — sai hoàn toàn và không cách nào lần ra.
                string error = ReadBusinessError(body);
                if (error != null) throw new InvalidOperationException(error);

                int before = rows.Count;
                int recordsInPage = 0;
                try
                {
                    using var doc = JsonDocument.Parse(body);
                    foreach (var item in EnumerateRecords(doc.RootElement))
                    {
                        recordsInPage++;
                        var row = MapRow(item);
                        if (row == null || !seen.Add(row.WaybillNo)) continue;
                        rows.Add(row);
                    }
                }
                catch (Exception ex)
                {
                    AppLogger.Warning($"[SendWaybill] ShippingWaybillList parse failed (page {page}): {ex.Message}");
                    break;
                }

                // Trang chưa đầy nghĩa là đã hết dữ liệu; trang đầy nhưng không thêm được dòng
                // nào (toàn trùng) thì tiếp tục cũng vô nghĩa.
                if (recordsInPage < PageSize || rows.Count == before) break;
            }

            return rows;
        }

        /// <summary>
        /// Thông báo lỗi nghiệp vụ JMS nhét trong thân HTTP 200, hoặc <c>null</c> nếu phản
        /// hồi bình thường. JMS không dùng mã HTTP cho lỗi nghiệp vụ: nó trả 200 kèm
        /// <c>succ:false</c> / <c>code</c> khác 1 và <c>data:null</c> — ví dụ thật là
        /// <c>code:121003005 msg:"运单打印次数超过3次"</c> (quá 3 lượt in).
        /// </summary>
        internal static string ReadBusinessError(string body)
        {
            if (string.IsNullOrWhiteSpace(body)) return null;
            try
            {
                using var doc = JsonDocument.Parse(body);
                var root = doc.RootElement;
                if (root.ValueKind != JsonValueKind.Object) return null;

                bool failed =
                    (root.TryGetProperty("succ", out var succ) && succ.ValueKind == JsonValueKind.False)
                    || (root.TryGetProperty("code", out var code)
                        && code.ValueKind == JsonValueKind.Number
                        && code.GetInt64() != 1);
                if (!failed) return null;

                string msg = FirstText(root, "msg");
                string codeText = root.TryGetProperty("code", out var c) ? c.ToString() : "?";
                return string.IsNullOrWhiteSpace(msg)
                    ? $"JMS từ chối yêu cầu (code {codeText})."
                    : $"JMS: {msg} (code {codeText})";
            }
            catch
            {
                // Thân không phải JSON thì để phía gọi bóc như cũ rồi tự báo lỗi parse.
                return null;
            }
        }

        /// <summary>Chỉ dựng PDF để xem trước — JMS không tính vào ba lượt in của vận đơn.</summary>
        public const int CenterPrintModePreview = 1;

        /// <summary>In thật: lượt này JMS đếm, quá ba lần là trả code 121003005.</summary>
        public const int CenterPrintModePrint = 2;

        /// <summary>Payload cho <see cref="CenterPrintEndpoint"/> — xem mục 2.4 của spec.</summary>
        public static string BuildCenterPrintPayload(
            IEnumerable<string> waybillNos, int printMode = CenterPrintModePrint)
        {
            return JsonSerializer.Serialize(new Dictionary<string, object>
            {
                { "printMode", printMode },
                { "waybillNos", (waybillNos ?? Enumerable.Empty<string>()).ToList() },
                { "countryId", "1" }
            });
        }

        /// <summary>
        /// Form tra theo mã vận đơn: ĐÚNG 3 trường, ĐÚNG thứ tự của cURL giao diện JMS
        /// (waybillNos, current, size) — Content-Length 341 với một mã 12 ký tự.
        /// <para>
        /// Đây là bộ trường KHÁC hẳn <see cref="BuildListForm"/>: tra theo mã thì giao diện
        /// JMS không gửi nhân viên, mốc thời gian hay mã tài chính. Đừng gộp hai form làm một
        /// rồi để trường rỗng — thừa trường thì JMS trả code:1 với danh sách rỗng, không kêu
        /// một tiếng nào.
        /// </para>
        /// </summary>
        internal static MultipartFormDataContent BuildWaybillListForm(int current, string waybillNos)
        {
            var form = NewBrowserStyleForm();
            Add(form, "waybillNos", waybillNos ?? "");
            Add(form, "current", current.ToString(CultureInfo.InvariantCulture));
            Add(form, "size", PageSize.ToString(CultureInfo.InvariantCulture));
            return form;
        }

        internal static MultipartFormDataContent BuildListForm(
            int current,
            string collectStaffCode,
            DateTime timeFrom,
            DateTime timeTo,
            string customerCodes,
            string pickFinanceCode)
        {
            string from = timeFrom.ToString(TimeFormat, CultureInfo.InvariantCulture);
            string to = timeTo.ToString(TimeFormat, CultureInfo.InvariantCulture);

            // ĐÚNG 10 trường, ĐÚNG thứ tự của cURL giao diện JMS — không hơn một trường nào.
            // Trường rỗng (waybillNos/customerCodes) vẫn phải gửi. Thêm trường lạ thì JMS trả
            // code:1 kèm danh sách rỗng, không báo lỗi — xem test ghim Content-Length 1114.
            var form = NewBrowserStyleForm();
            Add(form, "current", current.ToString(CultureInfo.InvariantCulture));
            Add(form, "size", PageSize.ToString(CultureInfo.InvariantCulture));
            // pickFinanceCode là mã TÀI CHÍNH của bưu cục (vd 208001 "Thái Nguyên"), KHÔNG
            // phải mã bưu cục mà SiteContextProvider.Get() trả về — cùng một dòng dữ liệu có
            // pickFinanceCode=208001 nhưng pickNetworkCode=214A02.
            Add(form, "pickFinanceCode", pickFinanceCode ?? "");
            Add(form, "collectStaffCode", collectStaffCode ?? "");
            Add(form, "timeStart", from);
            Add(form, "timeEnd", to);
            Add(form, "waybillNos", "");
            Add(form, "customerCodes", customerCodes ?? "");
            // Giao diện JMS gửi cả hai cặp mốc và KHÔNG gửi searchTimeType — không có trường
            // nào chọn cặp nào có hiệu lực, cứ gửi trùng giá trị như nó.
            Add(form, "inputTimeStart", from);
            Add(form, "inputTimeEnd", to);
            return form;
        }

        /// <summary>
        /// MultipartFormDataContent bắt chước đúng cách trình duyệt sinh body, vì backend JMS
        /// bóc form theo đúng chuẩn RFC 7578 chứ không đoán:
        /// <list type="bullet">
        /// <item>boundary trong header KHÔNG bọc nháy (.NET mặc định bọc: <c>boundary="..."</c>).</item>
        /// <item>tên trường PHẢI bọc nháy — <c>name="current"</c>, không phải <c>name=current</c>.</item>
        /// </list>
        /// Sai một trong hai thì server bỏ qua toàn bộ trường, trả <c>code:1</c> với danh sách
        /// rỗng — không hề báo lỗi, nên rất dễ tưởng là "hôm nay không có đơn".
        /// </summary>
        private static MultipartFormDataContent NewBrowserStyleForm()
        {
            string boundary = "----WebKitFormBoundary" + Guid.NewGuid().ToString("N").Substring(0, 16);
            var form = new MultipartFormDataContent(boundary);

            var contentType = form.Headers.ContentType;
            if (contentType != null)
            {
                foreach (var p in contentType.Parameters)
                {
                    if (string.Equals(p.Name, "boundary", StringComparison.OrdinalIgnoreCase))
                    {
                        p.Value = boundary;   // gán lại để bỏ cặp nháy .NET tự thêm
                        break;
                    }
                }
            }

            return form;
        }

        private static void Add(MultipartFormDataContent form, string name, string value)
        {
            var part = new StringContent(value ?? "");
            // Trình duyệt không gửi Content-Type cho từng phần text thường.
            part.Headers.ContentType = null;
            part.Headers.ContentDisposition = new ContentDispositionHeaderValue("form-data")
            {
                Name = "\"" + name + "\""
            };
            form.Add(part);
        }

        /// <summary>
        /// networkId nội bộ của bưu cục, suy từ mã bưu cục qua <c>network/select/all</c>.
        /// Trả chuỗi rỗng nếu không tra được — phía gọi coi đó là "bỏ qua bộ lọc này".
        /// </summary>
        private static async Task<string> ResolveNetworkIdAsync(string siteCode, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(siteCode)) return "";
            if (string.Equals(_cachedNetworkIdForSite, siteCode, StringComparison.OrdinalIgnoreCase))
                return _cachedNetworkId ?? "";

            string url = AppConfig.Current.BuildJmsApiUrl(NetworkEndpoint)
                       + "?current=1&size=10&name=" + Uri.EscapeDataString(siteCode) + "&queryLevel=3";

            string body = await ReadAsync(HttpMethod.Get, url, null, "ResolveNetworkId", ct).ConfigureAwait(false);
            string id = "";
            string finance = "";
            try
            {
                using var doc = JsonDocument.Parse(body ?? "");
                var first = EnumerateRecords(doc.RootElement).FirstOrDefault();
                if (first.ValueKind == JsonValueKind.Object)
                {
                    id = FirstText(first, "id", "networkId");
                    // pickFinanceCode của shippingWaybillList lấy từ đây. Bản ghi bưu cục
                    // gọi nó là "financial center": 214A02 có financialCenterDesc="Thái
                    // Nguyên", đúng bằng pickFinanceName trong dòng vận đơn. KHÔNG có
                    // trường nào tên "financeCode" cả — đã kiểm bằng response thật.
                    finance = FirstText(first, "financialCenterCode");

                    // Không thấy thì liệt kê tên trường (CHỈ tên, không kèm giá trị) để lần
                    // chạy sau biết phải đọc ở đâu — khỏi phải đi xin lại cURL.
                    if (string.IsNullOrWhiteSpace(finance))
                    {
                        AppLogger.Warning("[SendWaybill] ResolveNetworkId: không thấy financialCenterCode. Các trường có trong bản ghi: "
                            + string.Join(", ", first.EnumerateObject().Select(p => p.Name)));
                    }
                }
            }
            catch (Exception ex)
            {
                AppLogger.Warning($"[SendWaybill] ResolveNetworkId parse failed: {ex.Message}");
            }

            _cachedNetworkIdForSite = siteCode;
            _cachedNetworkId = id;
            _cachedFinanceCode = finance;
            AppLogger.Info($"[SendWaybill] ResolveNetworkId site={siteCode} networkId={(string.IsNullOrEmpty(id) ? "<none>" : id)} " +
                           $"financeCode={(string.IsNullOrEmpty(finance) ? "<none>" : finance)}");
            return id;
        }

        /// <summary>
        /// Mã tài chính của bưu cục đang đăng nhập, dùng cho <c>pickFinanceCode</c>. Trả
        /// chuỗi rỗng nếu chưa tra được — phía gọi vẫn gửi request, chỉ là thiếu bộ lọc này.
        /// </summary>
        private static async Task<string> ResolveFinanceCodeAsync(CancellationToken ct)
        {
            await ResolveNetworkIdAsync(SiteContextProvider.Get(), ct).ConfigureAwait(false);
            return _cachedFinanceCode ?? "";
        }

        private static async Task<string> ReadAsync(
            HttpMethod method, string url, Func<HttpContent> contentFactory, string what, CancellationToken ct)
        {
            try
            {
                await DumpRequestAsync(method, url, contentFactory, what, ct).ConfigureAwait(false);

                using var resp = await JmsApiClient.SendAsync(
                    method, url, contentFactory,
                    routeName: SendWaybillRouteName,
                    routerNameList: SendWaybillRouterNameList,
                    ct: ct).ConfigureAwait(false);

                if (resp == null)
                {
                    AppLogger.Warning($"[SendWaybill] {what}: null response");
                    return null;
                }

                string body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

                // Nửa còn lại của cặp dump: request đúng từng byte mà vẫn ra rỗng thì câu
                // trả lời nằm ở đây. Cắt bớt vì một trang 20 đơn dài vài KB, nhưng code/msg
                // của JMS luôn ở ngay đầu thân nên không bao giờ bị cắt mất.
                AppLogger.Info($"[SendWaybill] <<< {what} RESPONSE HTTP {(int)resp.StatusCode} "
                               + TokenRedactor.RedactText(Preview(body)));

                if (!resp.IsSuccessStatusCode)
                {
                    AppLogger.Warning($"[SendWaybill] {what}: HTTP {(int)resp.StatusCode}");
                    return null;
                }
                return body;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                AppLogger.Warning($"[SendWaybill] {what} failed: {ex.Message}");
                return null;
            }
        }

        private static string Preview(string body)
        {
            if (string.IsNullOrEmpty(body)) return "<empty>";
            const int max = 2000;
            return body.Length <= max ? body : body.Substring(0, max) + "...[cắt bớt]";
        }

        /// <summary>
        /// In nguyên văn request sắp gửi ra log để đối chiếu từng dòng với cURL của giao
        /// diện JMS — cả phân hệ này hỏng âm thầm (HTTP 200, <c>code:1</c>, danh sách rỗng)
        /// nên body sai không tự lộ ra ở đâu khác.
        /// <para>
        /// Header lấy từ <see cref="JmsApiClient.JmsHeaders"/> chứ không chép tay, để cái in
        /// ra đúng là cái gửi đi. Toàn bộ khối qua <see cref="TokenRedactor.RedactText"/>:
        /// authToken là chuỗi 32 hex nên bị che thành <c>first6******last4</c>.
        /// </para>
        /// </summary>
        private static async Task DumpRequestAsync(
            HttpMethod method, string url, Func<HttpContent> contentFactory, string what, CancellationToken ct)
        {
            try
            {
                var dump = new StringBuilder();
                dump.Append("[SendWaybill] >>> ").Append(what).AppendLine(" REQUEST");
                dump.Append(method.Method).Append(' ').AppendLine(url);

                foreach (var h in JmsApiClient.JmsHeaders(
                             JmsAuthTokenService.CurrentToken, SendWaybillRouteName, SendWaybillRouterNameList))
                    dump.Append(h.Key).Append(": ").AppendLine(h.Value);

                // HttpContent chỉ đọc được một lần, nên dựng một bản RIÊNG để in — bản gửi
                // đi vẫn do contentFactory sinh mới lúc SendAsync gọi.
                using (var probe = contentFactory?.Invoke())
                {
                    if (probe != null)
                    {
                        foreach (var h in probe.Headers)
                            dump.Append(h.Key).Append(": ").AppendLine(string.Join(", ", h.Value));
                        dump.AppendLine();
                        dump.Append(await probe.ReadAsStringAsync(ct).ConfigureAwait(false));
                    }
                }

                AppLogger.Info(TokenRedactor.RedactText(dump.ToString()));
            }
            catch (Exception ex)
            {
                // Log hỏng thì thôi, tuyệt đối không được làm chết luôn request thật.
                AppLogger.Warning($"[SendWaybill] {what}: dump request failed: {ex.Message}");
            }
        }

        /// <summary>
        /// JMS trả lúc thì <c>data.records</c>, lúc thì <c>data</c> là mảng thẳng — cả hai
        /// dạng đều gặp trong phân hệ này.
        /// </summary>
        private static IEnumerable<JsonElement> EnumerateRecords(JsonElement root)
        {
            if (root.ValueKind != JsonValueKind.Object) yield break;
            if (!root.TryGetProperty("data", out var data)) yield break;

            if (data.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in data.EnumerateArray()) yield return item;
                yield break;
            }

            if (data.ValueKind != JsonValueKind.Object) yield break;
            foreach (string key in new[] { "records", "list", "rows" })
            {
                if (!data.TryGetProperty(key, out var arr) || arr.ValueKind != JsonValueKind.Array) continue;
                foreach (var item in arr.EnumerateArray()) yield return item;
                yield break;
            }
        }

        internal static TrackingRow MapRow(JsonElement item)
        {
            string waybill = FirstText(item, "waybillNo");
            if (string.IsNullOrWhiteSpace(waybill)) return null;

            return new TrackingRow
            {
                WaybillNo = waybill.Trim(),
                NhanVienNhanHang = FirstText(item, "collectStaffName"),
                DiaChiLayHang = FirstText(item, "senderDetailedAddress"),
                TenNguoiGui = FirstText(item, "senderName"),
                ThoiGianNhanHang = FirstText(item, "collectTime"),
                NoiDungHangHoa = FirstText(item, "goodsName"),
                PrintCount = ParseInt(FirstText(item, "printsNumber")),
                PrintSenderNetworkCode = FirstText(item, "terminalDispatchCode")
            };
        }

        private static int ParseInt(string value)
            => int.TryParse((value ?? "").Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int n) ? n : 0;

        private static string FirstText(JsonElement item, params string[] names)
        {
            if (item.ValueKind != JsonValueKind.Object) return "";
            foreach (string name in names)
            {
                if (!item.TryGetProperty(name, out var value)) continue;
                string text = value.ValueKind switch
                {
                    JsonValueKind.String => value.GetString() ?? "",
                    JsonValueKind.Null or JsonValueKind.Undefined => "",
                    _ => value.ToString()
                };
                if (!string.IsNullOrWhiteSpace(text)) return text.Trim();
            }
            return "";
        }
    }
}

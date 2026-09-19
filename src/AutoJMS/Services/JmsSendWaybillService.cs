using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace AutoJMS
{
    /// <summary>
    /// Màn "Quản lý vận đơn gửi" (<c>sendWaybillSite</c>) của JMS — nguồn dữ liệu duy nhất
    /// của tab con "In Reverse".
    ///
    /// Khác với ba tab con còn lại, In Reverse không tra theo mã vận đơn mà tra theo
    /// <b>nhân viên lấy hàng + khoảng thời gian</b>, nên nó không đi qua
    /// <see cref="PrintService.SearchAndLoadAsync"/> (tracking + SafetyGuard) mà nạp thẳng
    /// kết quả vào lưới.
    ///
    /// Mọi request ở đây đi qua <see cref="JmsApiClient"/> để dùng chung hàng đợi 12 slot,
    /// token hiện hành và một lượt retry sau khi refresh token.
    /// </summary>
    public static class JmsSendWaybillService
    {
        // Breadcrumb của màn sendWaybillSite, chép nguyên văn từ cURL: dấu ">" để trần,
        // chỉ phần chữ Hán là đã percent-encode.
        private const string SendWaybillRouterNameList =
            "%E7%BD%91%E7%82%B9%E7%BB%8F%E8%90%A5>%E8%BF%90%E5%8D%95%E7%AE%A1%E7%90%86>%E5%AF%84%E4%BB%B6%E8%BF%90%E5%8D%95%E7%AE%A1%E7%90%86";
        private const string SendWaybillRouteName = "sendWaybillSite";

        private const string StaffEndpoint = "basicdata/sysStaff/selectAll";
        private const string NetworkEndpoint = "basicdata/network/select/all";
        private const string ShippingListEndpoint = "networkmanagement/omsWaybill/shippingWaybillList";

        /// <summary>Endpoint in của màn gửi — payload khác hẳn luồng in mặc định.</summary>
        public const string CenterPrintEndpoint = "networkmanagement/print/waybillCenterPrint";
        public const string CenterPrintRouteName = SendWaybillRouteName;
        public const string CenterPrintRouterNameList = SendWaybillRouterNameList;

        private const string TimeFormat = "yyyy-MM-dd HH:mm:ss";

        // Giữ đúng size=20 như giao diện JMS: đây là request DUY NHẤT đã biết chắc chạy được,
        // nên không tự ý nống lên. Lấy từng trang cho tới khi trang không còn đầy, chặn trên
        // để một bộ lọc quá rộng không kéo về vô hạn.
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
            string url = AppConfig.Current.BuildJmsApiUrl(ShippingListEndpoint);
            string financeCode = await ResolveFinanceCodeAsync(ct).ConfigureAwait(false);
            var rows = new List<TrackingRow>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            for (int page = 1; page <= MaxPages; page++)
            {
                int current = page;
                string body = await ReadAsync(
                    HttpMethod.Post,
                    url,
                    () => BuildListForm(current, collectStaffCode, timeFrom, timeTo, customerCodes, financeCode),
                    "ShippingWaybillList",
                    ct).ConfigureAwait(false);

                if (string.IsNullOrWhiteSpace(body)) break;

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

            AppLogger.Info($"[SendWaybill] ShippingWaybillList staff={collectStaffCode} rows={rows.Count} " +
                           $"from={timeFrom.ToString(TimeFormat, CultureInfo.InvariantCulture)} " +
                           $"to={timeTo.ToString(TimeFormat, CultureInfo.InvariantCulture)}");
            return rows;
        }

        /// <summary>Payload cho <see cref="CenterPrintEndpoint"/> — xem mục 2.4 của spec.</summary>
        public static string BuildCenterPrintPayload(IEnumerable<string> waybillNos)
        {
            return JsonSerializer.Serialize(new Dictionary<string, object>
            {
                { "printMode", 2 },
                { "waybillNos", (waybillNos ?? Enumerable.Empty<string>()).ToList() },
                { "countryId", "1" }
            });
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

            // Giữ ĐÚNG thứ tự và đủ 11 trường như cURL của giao diện JMS. Thiếu một trường
            // rỗng (waybillNos/customerCodes) là backend trả 500; thiếu searchTimeType thì
            // nó không biết lọc theo mốc thời gian nào và trả danh sách rỗng.
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
            // 1 = lọc theo thời gian NHẬN HÀNG (timeStart/timeEnd). Giao diện JMS luôn gửi
            // cả hai cặp mốc, searchTimeType mới là thứ quyết định cặp nào có hiệu lực.
            Add(form, "searchTimeType", "1");
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
                    // Cùng một bản ghi bưu cục CÓ THỂ mang mã tài chính của chi nhánh bao
                    // ngoài. Tên trường chưa xác nhận bằng response thật, nên đọc kiểu
                    // "được thì tốt": không thấy thì để rỗng, request vẫn chạy như cũ.
                    finance = FirstText(first, "financeCode");
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

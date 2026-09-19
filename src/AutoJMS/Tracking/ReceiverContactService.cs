using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace AutoJMS
{
    /// <summary>Tên + số điện thoại + địa chỉ người nhận của một vận đơn, lấy thẳng từ JMS.</summary>
    public sealed class ReceiverContact
    {
        public string Name { get; init; } = "";

        /// <summary>Số đầy đủ khi JMS chịu trả; rỗng nếu mọi nguồn đều che bằng '*'.</summary>
        public string Phone { get; init; } = "";

        /// <summary>Dạng che đúng như nhãn gốc in ra, ví dụ <c>******1886</c>.</summary>
        public string MaskedPhone { get; init; } = "";

        /// <summary>Địa chỉ nhận hàng đầy đủ như nhãn gốc in; rỗng nếu JMS không trả.</summary>
        public string Address { get; init; } = "";

        /// <summary>Mã tuyến gốc chưa tách, ví dụ <c>330-L214A02-001</c>; rỗng nếu JMS không trả.</summary>
        public string TerminalDispatchCode { get; init; } = "";

        public bool HasUnmaskedPhone => !string.IsNullOrEmpty(Phone);
    }

    /// <summary>
    /// Kéo tên + SĐT + địa chỉ người nhận cho "In lại đơn".
    ///
    /// Cố tình KHÔNG gọi lại code trong <c>FullStackOperation.WaybillDetail</c>: form đó bị
    /// khoá theo tier ULTRA, còn tab IN ĐƠN thì BASE cũng dùng được — phụ thuộc sang đó là
    /// BASE mất tính năng. Hai endpoint và quy tắc "ưu tiên giá trị chưa bị che" thì giữ
    /// nguyên như bên ấy vì đã chạy thật.
    /// </summary>
    public static class ReceiverContactService
    {
        // Trả sender/receiver chưa che khi tài khoản có quyền.
        private const string ReverseEndpoint =
            "https://jmsgw.jtexpress.vn/servicequality/integration/getWaybillsByReverse?type=1&waybillId=";

        // Nguồn vá lại các trường bị che ở endpoint trên (POST, data.details {}).
        private const string OrderDetailEndpoint =
            "https://jmsgw.jtexpress.vn/operatingplatform/order/getOrderDetail";

        private const string ReceiverUserAgent =
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36";

        /// <summary>Số chữ số cuối mà JMS để lộ khi che (<c>******1886</c>).</summary>
        private const int VisibleTailDigits = 4;

        private static readonly HttpClient _http = CreateClient();

        private static HttpClient CreateClient()
        {
            var handler = new HttpClientHandler
            {
                AutomaticDecompression = DecompressionMethods.GZip
                    | DecompressionMethods.Deflate
                    | DecompressionMethods.Brotli
            };
            return new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(18) };
        }

        /// <summary>
        /// Trả về tên + SĐT + địa chỉ của người nhận, hoặc <c>null</c> khi không lấy được.
        /// Không bao giờ ném: preview bản in vẫn phải dựng được dù JMS im lặng.
        /// </summary>
        public static async Task<ReceiverContact> FetchAsync(string waybillNo, CancellationToken ct)
        {
            string code = (waybillNo ?? "").Trim();
            if (code.Length == 0) return null;

            try
            {
                string token = await JmsAuthTokenService.ResolveTokenAsync(ct).ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(token))
                {
                    AppLogger.Warning("[ReceiverContact] không có authToken, bỏ qua việc lấy người nhận.");
                    return null;
                }

                string encoded = Uri.EscapeDataString(code);
                string name = "";
                string phone = "";
                string maskedPhone = "";
                string address = "";
                string dispatchCode = "";

                string reverseJson = await GetJsonAsync(ReverseEndpoint + encoded, token, ct).ConfigureAwait(false);
                MergeFrom(reverseJson, isOrderDetail: false, ref name, ref phone, ref maskedPhone, ref address, ref dispatchCode);

                // Chỉ gọi thêm khi vẫn thiếu — mỗi request thừa là một lần chạm rate-limit JMS.
                // Mã tuyến nằm trong danh sách này vì trên thực tế chỉ getOrderDetail trả nó.
                if (name.Length == 0 || phone.Length == 0 || address.Length == 0 || dispatchCode.Length == 0)
                {
                    string orderJson = await PostOrderDetailAsync(code, token, ct).ConfigureAwait(false);
                    MergeFrom(orderJson, isOrderDetail: true, ref name, ref phone, ref maskedPhone, ref address, ref dispatchCode);
                }

                if (name.Length == 0 && phone.Length == 0 && maskedPhone.Length == 0
                    && address.Length == 0 && dispatchCode.Length == 0)
                    return null;

                if (maskedPhone.Length == 0 && phone.Length > 0) maskedPhone = Mask(phone);

                // Không ghi giá trị địa chỉ vào log: đó là dữ liệu cá nhân của người nhận.
                AppLogger.Info(
                    $"[ReceiverContact] waybill={code} name={(name.Length > 0 ? "có" : "trống")} " +
                    $"phone={(phone.Length > 0 ? "đầy đủ" : "chỉ bản che")} " +
                    $"address={(address.Length > 0 ? "có" : "trống")} " +
                    $"maTuyen={(dispatchCode.Length > 0 ? dispatchCode : "trống")}");

                return new ReceiverContact
                {
                    Name = name,
                    Phone = phone,
                    MaskedPhone = maskedPhone,
                    Address = address,
                    TerminalDispatchCode = dispatchCode
                };
            }
            catch (OperationCanceledException)
            {
                return null;
            }
            catch (Exception ex)
            {
                AppLogger.Warning($"[ReceiverContact] lấy người nhận thất bại waybill={code}: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Đọc tên/SĐT/địa chỉ/mã tuyến từ một phản hồi và điền vào chỗ còn trống. Ưu tiên giá
        /// trị chưa bị che: một bản đầy đủ đến sau vẫn thay được bản che đã nhận trước đó.
        /// </summary>
        private static void MergeFrom(
            string json, bool isOrderDetail,
            ref string name, ref string phone, ref string maskedPhone, ref string address,
            ref string dispatchCode)
        {
            if (string.IsNullOrWhiteSpace(json)) return;

            try
            {
                using var doc = JsonDocument.Parse(json);
                if (!doc.RootElement.TryGetProperty("data", out var data)) return;

                JsonElement node = data;
                if (isOrderDetail)
                {
                    if (data.ValueKind != JsonValueKind.Object
                        || !data.TryGetProperty("details", out var details)
                        || details.ValueKind != JsonValueKind.Object)
                        return;
                    node = details;
                }
                else if (data.ValueKind != JsonValueKind.Object)
                {
                    return;
                }

                string foundName = Field(node, "receiverName");
                if (foundName.Length > 0 && (name.Length == 0 || (IsMasked(name) && !IsMasked(foundName))))
                    name = foundName;

                // Tên trường lấy đúng bộ mà FullStackOperation.WaybillDetail đang dùng thật
                // (receiverDetailedAddress), thêm hai biến thể ArrivalMonitor từng gặp.
                string foundAddress = FirstNonEmpty(
                    Field(node, "receiverDetailedAddress"),
                    Field(node, "receiverFullAddress"),
                    Field(node, "receiverAddress"));
                if (foundAddress.Length > 0
                    && (address.Length == 0 || (IsMasked(address) && !IsMasked(foundAddress))))
                    address = foundAddress;

                // Đọc trước nhánh SĐT: hàm thoát sớm ở đó khi đơn không có số điện thoại.
                if (dispatchCode.Length == 0) dispatchCode = Field(node, "terminalDispatchCode");

                string foundPhone = FirstNonEmpty(Field(node, "receiverMobilePhone"), Field(node, "receiverTelphone"));
                if (foundPhone.Length == 0) return;

                if (IsMasked(foundPhone))
                {
                    if (maskedPhone.Length == 0) maskedPhone = foundPhone;
                }
                else if (phone.Length == 0)
                {
                    phone = foundPhone;
                }
            }
            catch (Exception ex)
            {
                AppLogger.Warning($"[ReceiverContact] không đọc được phản hồi: {ex.Message}");
            }
        }

        private static async Task<string> GetJsonAsync(string url, string token, CancellationToken ct)
        {
            return await SendWithAuthRetryAsync(
                tok =>
                {
                    var request = new HttpRequestMessage(HttpMethod.Get, url);
                    ApplyHeaders(request, tok, "integratedComprehensive");
                    return request;
                },
                token, ct).ConfigureAwait(false);
        }

        private static async Task<string> PostOrderDetailAsync(string code, string token, CancellationToken ct)
        {
            string bodyJson = JsonSerializer.Serialize(new { waybillNo = code, countryId = "1" });

            return await SendWithAuthRetryAsync(
                tok =>
                {
                    var request = new HttpRequestMessage(HttpMethod.Post, OrderDetailEndpoint);
                    ApplyHeaders(request, tok, "trackingExpress");
                    request.Content = new StringContent(bodyJson, Encoding.UTF8, "application/json");
                    return request;
                },
                token, ct).ConfigureAwait(false);
        }

        /// <summary>
        /// Gửi một lần; gặp câu trả lời "hết phiên" thì xin token mới từ WebView và gửi lại
        /// đúng một lần. Không bao giờ escalate sang <c>HandleExpired()</c> — một cú 401
        /// thoáng qua không được phép bắt Owner đăng nhập lại giữa lúc đang in.
        ///
        /// <paramref name="buildRequest"/> phải dựng request MỚI mỗi lần gọi: một
        /// HttpRequestMessage không gửi lại được lần hai.
        /// </summary>
        private static async Task<string> SendWithAuthRetryAsync(
            Func<string, HttpRequestMessage> buildRequest, string token, CancellationToken ct)
        {
            var attempt = await SendOnceAsync(buildRequest, token, ct).ConfigureAwait(false);
            if (!JmsResponseClassifier.IsAuthExpired(attempt.StatusCode, attempt.Body))
                return attempt.IsSuccess ? attempt.Body : null;

            AppLogger.Warning($"[ReceiverContact] auth-expired lần đầu; http={attempt.StatusCode}");

            string refreshed = await JmsAuthTokenService.ForceRefreshFromWebViewAsync().ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(refreshed) || string.Equals(refreshed, token, StringComparison.Ordinal))
                return null;

            var retry = await SendOnceAsync(buildRequest, refreshed, ct).ConfigureAwait(false);
            return retry.IsSuccess ? retry.Body : null;
        }

        private static async Task<(int StatusCode, bool IsSuccess, string Body)> SendOnceAsync(
            Func<string, HttpRequestMessage> buildRequest, string token, CancellationToken ct)
        {
            using var request = buildRequest(token);
            using var response = await _http
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct)
                .ConfigureAwait(false);

            string body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            return ((int)response.StatusCode, response.IsSuccessStatusCode, body ?? "");
        }

        private static void ApplyHeaders(HttpRequestMessage request, string token, string routeName)
        {
            request.Headers.TryAddWithoutValidation("Accept", "application/json, text/plain, */*");
            request.Headers.TryAddWithoutValidation("Origin", "https://jms.jtexpress.vn");
            request.Headers.TryAddWithoutValidation("Referer", "https://jms.jtexpress.vn/");
            request.Headers.TryAddWithoutValidation("authToken", token ?? "");
            request.Headers.TryAddWithoutValidation("lang", "VN");
            request.Headers.TryAddWithoutValidation("langType", "VN");
            request.Headers.TryAddWithoutValidation("routeName", routeName);
            request.Headers.TryAddWithoutValidation("timezone", "GMT+0700");
            request.Headers.TryAddWithoutValidation("User-Agent", ReceiverUserAgent);
        }

        // ── helpers ──────────────────────────────────────────────

        /// <summary>Che hệt cách JMS che: chỉ chừa <see cref="VisibleTailDigits"/> ký tự cuối.</summary>
        public static string Mask(string phone)
        {
            string text = (phone ?? "").Trim();
            if (text.Length == 0) return "";
            if (text.Length <= VisibleTailDigits) return text;

            return new string('*', text.Length - VisibleTailDigits)
                   + text.Substring(text.Length - VisibleTailDigits);
        }

        private static bool IsMasked(string value) => (value ?? "").IndexOf('*') >= 0;

        private static string Field(JsonElement el, string propertyName)
        {
            if (!el.TryGetProperty(propertyName, out var v)) return "";
            switch (v.ValueKind)
            {
                case JsonValueKind.String: return (v.GetString() ?? "").Trim();
                case JsonValueKind.Number: return v.ToString().Trim();
                case JsonValueKind.Null:
                case JsonValueKind.Undefined: return "";
                default: return "";
            }
        }

        private static string FirstNonEmpty(params string[] values)
        {
            foreach (var value in values)
                if (!string.IsNullOrEmpty(value)) return value;
            return "";
        }
    }
}

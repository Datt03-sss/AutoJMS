using AutoJMS.FullStack.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AutoJMS.Diagnostics.AppCapture;

namespace AutoJMS.FullStack.Services
{
    public sealed class FullStackTrackingJourneyService : IFullStackTrackingJourneyService
    {
        private const string RouterNameList =
            "%E6%93%8D%E4%BD%9C%E5%B9%B3%E5%8F%B0%3E%E5%BF%AB%E4%BB%B6%E6%9F%A5%E8%AF%A2%3E%E5%BF%AB%E4%BB%B6%E8%B7%9F%E8%B8%AA";
        private const string TrackingRouteName = "trackingExpress";
        private const string JmsOrigin = "https://jms.jtexpress.vn";
        private const string DefaultUserAgent =
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36";

        private static readonly HttpClient Http = CreateHttpClient();

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true
        };

        public async Task<FullStackTrackingJourneyResult> FetchJourneyAsync(
            string waybillNo,
            string authToken,
            CancellationToken cancellationToken)
        {
            waybillNo = NormalizeWaybill(waybillNo);
            if (string.IsNullOrWhiteSpace(waybillNo))
                return new FullStackTrackingJourneyResult { Message = "Missing waybill." };

            if (string.IsNullOrWhiteSpace(authToken))
                return new FullStackTrackingJourneyResult
                {
                    WaybillNo = waybillNo,
                    Message = "Missing JMS authToken."
                };

            var endpoint = AppConfig.Current.BuildJmsApiUrl("operatingplatform/podTracking/inner/query/keywordList");
            var payload = new { keywordList = new[] { waybillNo }, trackingTypeEnum = "WAYBILL", countryId = "1" };
            var payloadJson = JsonSerializer.Serialize(payload, JsonOptions);
            var fetchedAt = DateTime.Now;

            var httpResult = await PostTrackingKeywordListAsync(
                endpoint,
                payloadJson,
                authToken,
                cancellationToken).ConfigureAwait(false);

            var rawJson = httpResult.Body ?? string.Empty;
            var status = httpResult.StatusCode;
            LogRawResponse(waybillNo, endpoint, status, rawJson);
            if (JmsResponseClassifier.IsAuthExpired(status, rawJson))
            {
                return new FullStackTrackingJourneyResult
                {
                    WaybillNo = waybillNo,
                    ApiEndpoint = endpoint,
                    HttpStatusCode = status,
                    RawJson = rawJson ?? string.Empty,
                    FetchedAt = fetchedAt,
                    AuthExpired = true,
                    Message = "AuthToken không hợp lệ hoặc đã hết hạn"
                };
            }

            if (!httpResult.IsSuccessStatusCode)
            {
                return new FullStackTrackingJourneyResult
                {
                    WaybillNo = waybillNo,
                    ApiEndpoint = endpoint,
                    HttpStatusCode = status,
                    RawJson = rawJson ?? string.Empty,
                    FetchedAt = fetchedAt,
                    Message = $"Tracking API HTTP {status}"
                };
            }

            var viewModel = ParseTrackingJourney(waybillNo, rawJson ?? string.Empty, out var matched, out var message);
            viewModel.FetchedAt = fetchedAt;
            viewModel.Source = "Fresh Tracking API";
            LogParsedResponse(waybillNo, endpoint, status, rawJson ?? string.Empty, viewModel);

            return new FullStackTrackingJourneyResult
            {
                Success = matched,
                IsWaybillMatched = matched,
                WaybillNo = waybillNo,
                RawJson = rawJson ?? string.Empty,
                ApiEndpoint = endpoint,
                HttpStatusCode = status,
                FetchedAt = fetchedAt,
                ViewModel = viewModel,
                Message = matched ? $"Đã cập nhật {viewModel.Rows.Count:N0} sự kiện" : message
            };
        }

        private static HttpClient CreateHttpClient()
        {
            var handler = new HttpClientHandler
            {
                AutomaticDecompression = DecompressionMethods.GZip
                    | DecompressionMethods.Deflate
                    | DecompressionMethods.Brotli
            };

            var captureHandler = new AppHttpCaptureHandler(handler, "FullStackTrackingJourneyService");
            return new HttpClient(captureHandler)
            {
                Timeout = TimeSpan.FromSeconds(60)
            };
        }

        private static async Task<TrackingHttpResult> PostTrackingKeywordListAsync(
            string endpoint,
            string payloadJson,
            string authToken,
            CancellationToken cancellationToken)
        {
            var first = await SendTrackingRequestOnceAsync(
                endpoint,
                payloadJson,
                authToken,
                cancellationToken).ConfigureAwait(false);

            if (!JmsResponseClassifier.IsAuthExpired(first.StatusCode, first.Body))
                return first;

            AppLogger.Warning(
                "[FullStackTrackingJourney] auth-expired first attempt " +
                $"endpoint={ShortEndpoint(endpoint)}; http={first.StatusCode}; body={Preview(first.Body, 240)}");

            var refreshedToken = await JmsAuthTokenService.ForceRefreshFromWebViewAsync().ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(refreshedToken))
                return first;

            return await SendTrackingRequestOnceAsync(
                endpoint,
                payloadJson,
                refreshedToken,
                cancellationToken).ConfigureAwait(false);
        }

        private static async Task<TrackingHttpResult> SendTrackingRequestOnceAsync(
            string endpoint,
            string payloadJson,
            string authToken,
            CancellationToken cancellationToken)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
            {
                Content = new StringContent(payloadJson ?? "{}", Encoding.UTF8)
            };

            request.Content.Headers.ContentType = MediaTypeHeaderValue.Parse("application/json;charset=UTF-8");
            request.Headers.TryAddWithoutValidation("authToken", authToken ?? string.Empty);
            request.Headers.TryAddWithoutValidation("lang", "VN");
            request.Headers.TryAddWithoutValidation("langType", "VN");
            request.Headers.TryAddWithoutValidation("timezone", "GMT+0700");
            request.Headers.TryAddWithoutValidation("routeName", TrackingRouteName);
            request.Headers.TryAddWithoutValidation("routerNameList", RouterNameList);
            request.Headers.TryAddWithoutValidation("Accept", "application/json, text/plain, */*");
            request.Headers.TryAddWithoutValidation("Origin", JmsOrigin);
            request.Headers.TryAddWithoutValidation("Referer", JmsOrigin + "/");
            request.Headers.TryAddWithoutValidation("User-Agent", DefaultUserAgent);

            using var response = await Http
                .SendAsync(request, HttpCompletionOption.ResponseContentRead, cancellationToken)
                .ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            return new TrackingHttpResult(
                (int)response.StatusCode,
                response.IsSuccessStatusCode,
                body ?? string.Empty);
        }

        private static WaybillJourneyViewModel ParseTrackingJourney(
            string waybillNo,
            string rawJson,
            out bool matched,
            out string message)
        {
            matched = false;
            message = string.Empty;
            var vm = new WaybillJourneyViewModel { WaybillNo = waybillNo };
            if (string.IsNullOrWhiteSpace(rawJson))
            {
                matched = true;
                return vm;
            }

            using var document = JsonDocument.Parse(rawJson);
            var root = document.RootElement;
            vm.ResponseCode = TryGetInt(root, "code");
            vm.ResponseMessage = PickFirstRawText(root, "msg", "message");
            if (TryGetBool(root, "succ", out var succ) && !succ)
            {
                message = PickFirstRawText(root, "msg", "message");
                return vm;
            }

            var details = SelectDetails(root, waybillNo, out matched, out var matchedKeyword, out var dataCount, out var validationWarning);
            vm.MatchedKeyword = matchedKeyword;
            vm.DataCount = dataCount;
            vm.ValidationWarning = validationWarning;

            if (!matched)
            {
                message = "Response không khớp mã vận đơn đang chọn.";
                return vm;
            }

            var rows = new List<WaybillJourneyRow>();
            foreach (var detail in details)
            {
                var eventTimeText = PickFirstRawText(detail, "scanTime");
                var uploadedAtText = PickFirstRawText(detail, "uploadTime");
                var scanNetworkName = PickFirstRawText(detail, "scanNetworkName");
                var scanNetworkCode = PickFirstRawText(detail, "scanNetworkCode");
                var imgType = TryGetInt(detail, "imgType");

                rows.Add(new WaybillJourneyRow
                {
                    ActionTime = ParseNullableDate(eventTimeText),
                    UploadedAt = ParseNullableDate(uploadedAtText),
                    ActionType = PickFirstRawText(detail, "scanTypeName"),
                    Description = PickFirstRawText(detail, "waybillTrackingContent"),
                    SiteInfo = BuildSite(scanNetworkName, scanNetworkCode, "--"),
                    ContainerCode = PickFirstRawText(detail, "packageNumber", "taskCode"),
                    ScanSource = PickFirstRawText(detail, "scanSource", "source", "remark6"),
                    Weight = PickFirstRawText(detail, "weight"),
                    ConvertedWeight = PickFirstRawText(detail, "volumeWeight", "convertedWeight"),
                    AttachmentText = imgType.HasValue ? "Xem" : "--",
                    ImgType = imgType,
                    SiteName = scanNetworkName == "--" ? string.Empty : scanNetworkName,
                    SiteCode = scanNetworkCode == "--" ? string.Empty : scanNetworkCode,
                    PackageNumber = PickFirstRawText(detail, "packageNumber"),
                    TaskCode = PickFirstRawText(detail, "taskCode"),
                    RawJson = detail.GetRawText()
                });
            }

            rows = rows
                .OrderByDescending(x => x.ActionTime ?? DateTime.MinValue)
                .ToList();
            for (var i = 0; i < rows.Count; i++)
                rows[i].Stt = i + 1;

            vm.Rows = rows;
            return vm;
        }

        private static IReadOnlyList<JsonElement> SelectDetails(
            JsonElement root,
            string waybillNo,
            out bool matched,
            out string matchedKeyword,
            out int dataCount,
            out string validationWarning)
        {
            matched = false;
            matchedKeyword = string.Empty;
            dataCount = 0;
            validationWarning = string.Empty;

            if (!TryGetProperty(root, "data", out var data) || data.ValueKind != JsonValueKind.Array)
            {
                validationWarning = "Response không có root.data[].";
                return Array.Empty<JsonElement>();
            }

            dataCount = data.GetArrayLength();
            foreach (var item in data.EnumerateArray())
            {
                var keyword = NormalizeWaybill(PickFirstRawText(item, "keyword"));
                if (!IsSameWaybill(keyword, waybillNo))
                    continue;

                var details = TryGetProperty(item, "details", out var detailArray) && detailArray.ValueKind == JsonValueKind.Array
                    ? detailArray.EnumerateArray().ToList()
                    : new List<JsonElement>();

                if (!ValidateDetailsWaybill(details, waybillNo, out validationWarning))
                {
                    return Array.Empty<JsonElement>();
                }

                matched = true;
                matchedKeyword = keyword;
                return details;
            }

            foreach (var item in data.EnumerateArray())
            {
                var details = TryGetProperty(item, "details", out var detailArray) && detailArray.ValueKind == JsonValueKind.Array
                    ? detailArray.EnumerateArray().ToList()
                    : new List<JsonElement>();

                if (details.Count == 0 || !HasMatchingDetailMarker(details, waybillNo))
                    continue;

                matched = true;
                matchedKeyword = NormalizeWaybill(PickFirstRawText(item, "keyword"));
                validationWarning = "data[].keyword không exact, đã validate bằng details[].waybillNo/billCode.";
                return details;
            }

            if (dataCount == 1)
            {
                var item = data.EnumerateArray().First();
                var keyword = NormalizeWaybill(PickFirstRawText(item, "keyword"));
                var details = TryGetProperty(item, "details", out var detailArray) && detailArray.ValueKind == JsonValueKind.Array
                    ? detailArray.EnumerateArray().ToList()
                    : new List<JsonElement>();

                if ((string.IsNullOrWhiteSpace(keyword) || keyword == "--")
                    && details.Count > 0
                    && ValidateDetailsWaybill(details, waybillNo, out validationWarning))
                {
                    matched = true;
                    matchedKeyword = "--";
                    validationWarning = "Response chỉ có 1 data item và không có keyword; đã bind sau khi kiểm tra details marker.";
                    return details;
                }
            }

            validationWarning = "Không tìm thấy data[].keyword khớp mã vận đơn đang chọn.";
            return Array.Empty<JsonElement>();
        }

        private static bool ValidateDetailsWaybill(
            IReadOnlyList<JsonElement> details,
            string waybillNo,
            out string validationWarning)
        {
            validationWarning = string.Empty;
            foreach (var detail in details)
            {
                var markers = GetWaybillMarkers(detail).ToList();
                if (markers.Count > 0 && !markers.Any(x => IsSameWaybill(x, waybillNo)))
                {
                    validationWarning = "details[].waybillNo/billCode không khớp mã đang chọn.";
                    return false;
                }
            }

            return true;
        }

        private static bool HasMatchingDetailMarker(IReadOnlyList<JsonElement> details, string waybillNo)
            => details.Any(detail => GetWaybillMarkers(detail).Any(marker => IsSameWaybill(marker, waybillNo)));

        private static IEnumerable<string> GetWaybillMarkers(JsonElement detail)
        {
            var markerNames = new[] { "waybillNo", "billCode", "keyword" };
            foreach (var name in markerNames)
            {
                var marker = NormalizeWaybill(PickFirstRawText(detail, name));
                if (!string.IsNullOrWhiteSpace(marker) && marker != "--")
                    yield return marker;
            }
        }

        private static string PickFirstRawText(JsonElement node, params string[] names)
        {
            foreach (var name in names ?? Array.Empty<string>())
            {
                if (!TryGetProperty(node, name, out var prop))
                    continue;

                var value = prop.ValueKind == JsonValueKind.String
                    ? prop.GetString()
                    : prop.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined
                        ? string.Empty
                        : prop.ToString();

                value = NormalizeWhitespaceOnly(value);
                if (!string.IsNullOrWhiteSpace(value))
                    return value;
            }

            return "--";
        }

        private static string NormalizeWhitespaceOnly(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;
            return string.Join(" ", value.Split(new[] { '\r', '\n', '\t', ' ' }, StringSplitOptions.RemoveEmptyEntries)).Trim();
        }

        private static bool TryGetProperty(JsonElement element, string name, out JsonElement value)
        {
            if (element.ValueKind == JsonValueKind.Object)
            {
                foreach (var property in element.EnumerateObject())
                {
                    if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
                    {
                        value = property.Value;
                        return true;
                    }
                }
            }

            value = default;
            return false;
        }

        private static int? TryGetInt(JsonElement element, string name)
        {
            if (!TryGetProperty(element, name, out var prop))
                return null;
            if (prop.ValueKind == JsonValueKind.Number && prop.TryGetInt32(out var number))
                return number;
            if (prop.ValueKind == JsonValueKind.String && int.TryParse(prop.GetString(), out number))
                return number;
            return null;
        }

        private static bool TryGetBool(JsonElement element, string name, out bool value)
        {
            value = false;
            if (!TryGetProperty(element, name, out var prop))
                return false;
            if (prop.ValueKind == JsonValueKind.True || prop.ValueKind == JsonValueKind.False)
            {
                value = prop.GetBoolean();
                return true;
            }

            return false;
        }

        private static string BuildSite(string name, string code, string fallback)
        {
            var hasName = !string.IsNullOrWhiteSpace(name) && name != "--";
            var hasCode = !string.IsNullOrWhiteSpace(code) && code != "--";
            if (hasName && hasCode) return $"{name} | {code}";
            if (hasName) return name;
            if (hasCode) return code;
            return string.IsNullOrWhiteSpace(fallback) ? "--" : fallback;
        }

        private static bool HasAttachment(string value)
        {
            if (string.IsNullOrWhiteSpace(value) || value == "--")
                return false;
            if (string.Equals(value, "0", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(value, "false", StringComparison.OrdinalIgnoreCase))
                return false;
            return true;
        }

        private static DateTime? ParseNullableDate(string value)
        {
            if (string.IsNullOrWhiteSpace(value) || value == "--") return null;
            if (DateTime.TryParse(value, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AssumeLocal, out var invariant))
                return invariant;
            if (DateTime.TryParse(value, System.Globalization.CultureInfo.CurrentCulture, System.Globalization.DateTimeStyles.AssumeLocal, out var local))
                return local;
            return null;
        }

        private static string NormalizeWaybill(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;
            value = value.Trim().ToUpperInvariant();
            return value == "--" ? "--" : value;
        }

        private static bool IsSameWaybill(string left, string right)
        {
            left = NormalizeWaybill(left);
            right = NormalizeWaybill(right);
            if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right) || left == "--" || right == "--")
                return false;
            if (string.Equals(left, right, StringComparison.OrdinalIgnoreCase))
                return true;

            var leftBase = StripWaybillSuffix(left);
            var rightBase = StripWaybillSuffix(right);
            return !string.IsNullOrWhiteSpace(leftBase)
                && !string.IsNullOrWhiteSpace(rightBase)
                && string.Equals(leftBase, rightBase, StringComparison.OrdinalIgnoreCase);
        }

        private static string StripWaybillSuffix(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;
            var dash = value.IndexOf('-');
            return dash > 0 ? value.Substring(0, dash) : value;
        }

        private static void LogRawResponse(string waybillNo, string endpoint, int status, string rawJson)
        {
            AppLogger.Info(
                "[FullStackTrackingJourney] raw " +
                $"waybill={waybillNo}; endpoint={ShortEndpoint(endpoint)}; http={status}; " +
                $"bodyLength={(rawJson?.Length ?? 0)}; bodyPreview={Preview(rawJson, 1000)}");
        }

        private static void LogParsedResponse(
            string waybillNo,
            string endpoint,
            int status,
            string rawJson,
            WaybillJourneyViewModel viewModel)
        {
            var rows = viewModel?.Rows ?? new List<WaybillJourneyRow>();
            var first = rows.Count > 0 ? rows[0] : null;
            var last = rows.Count > 0 ? rows[^1] : null;
            AppLogger.Info(
                "[FullStackTrackingJourney] parsed " +
                $"waybill={waybillNo}; endpoint={ShortEndpoint(endpoint)}; http={status}; " +
                $"bodyLength={(rawJson?.Length ?? 0)}; root.code={(viewModel?.ResponseCode?.ToString() ?? "--")}; " +
                $"dataCount={(viewModel?.DataCount.ToString() ?? "--")}; matchedKeyword={viewModel?.MatchedKeyword ?? "--"}; " +
                $"detailsCount={rows.Count}; parsedRows={rows.Count}; first={FormatEvent(first)}; last={FormatEvent(last)}; " +
                $"currentJourneyWaybillAtBind={waybillNo}; validation={Preview(viewModel?.ValidationWarning, 240)}");
        }

        private static string FormatEvent(WaybillJourneyRow row)
        {
            if (row == null) return "--";
            return $"{row.ActionTime:yyyy-MM-dd HH:mm:ss} {Preview(row.Description, 180)}";
        }

        private static string ShortEndpoint(string endpoint)
        {
            if (string.IsNullOrWhiteSpace(endpoint))
                return string.Empty;
            var marker = "operatingplatform/";
            var index = endpoint.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            return index >= 0 ? endpoint.Substring(index) : endpoint;
        }

        private static string Preview(string value, int maxLength)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;
            value = value.Replace('\r', ' ').Replace('\n', ' ').Trim();
            return value.Length <= maxLength ? value : value.Substring(0, maxLength) + "...";
        }

        private sealed class TrackingHttpResult
        {
            public TrackingHttpResult(int statusCode, bool isSuccessStatusCode, string body)
            {
                StatusCode = statusCode;
                IsSuccessStatusCode = isSuccessStatusCode;
                Body = body ?? string.Empty;
            }

            public int StatusCode { get; }
            public bool IsSuccessStatusCode { get; }
            public string Body { get; }
        }
    }
}

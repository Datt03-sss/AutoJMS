using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace AutoJMS
{
    // Waybill detail panel: on open, pull the rich detail from JMS and push it to
    // the WebView2 dashboard so the right panel can show the sender / receiver /
    // order information groups. Sources merged (a field prefers any UNMASKED value):
    //   1) commonWaybillListByWaybillNos -> order, goods, COD, networks (contact masked)
    //   2) getWaybillsByReverse (type=1)  -> unmasked sender/receiver phones, receiver
    //      name and detailed addresses.
    //   3) getOrderDetail (POST)          -> fallback for fields masked with "*" when the
    //      main list is not viewable (no permission).
    public partial class FullStackOperation
    {
        private const string WaybillDetailEndpoint =
            "https://jmsgw.jtexpress.vn/servicequality/thirdService/waybill/commonWaybillListByWaybillNos?waybillNos=";

        private const string WaybillReverseEndpoint =
            "https://jmsgw.jtexpress.vn/servicequality/integration/getWaybillsByReverse?type=1&waybillId=";

        private const string WaybillOrderDetailEndpoint =
            "https://jmsgw.jtexpress.vn/operatingplatform/order/getOrderDetail";

        // "Lịch sử kiện vấn đề" (abnormal-piece scan history).
        private const string IssueHistoryEndpoint =
            "https://jmsgw.jtexpress.vn/operatingplatform/abnormalPieceScanList/pageList";

        // Problem-piece detail — returns the displayable POD image URLs for a waybill.
        private const string ProblemPieceEndpoint =
            "https://jmsgw.jtexpress.vn/servicequality/problemPiece/detailWithServiceRecordCount?waybillNo=";

        // Resolves a signed media URL for a single scan (from the journey record fields).
        private const string PodImgPathEndpoint =
            "https://jmsgw.jtexpress.vn/operatingplatform/podTracking/img/path";

        // Journey scan imgTypes that carry order-related media (images / videos).
        private static readonly int[] OrderImageTypes = { 5, 11, 13, 14 };

        // "Bưu cục tiếp nhận" for the issue-registration form.
        private const string ReceiverNetworkEndpoint =
            "https://jmsgw.jtexpress.vn/servicequality/comboBox/queryReceiverNetwork";

        // Resolve a network code -> canonical { code, name } (normalization).
        private const string NetworkSelectEndpoint =
            "https://jmsgw.jtexpress.vn/basicdata/network/select/all";

        // Register a problem-piece issue.
        private const string ProblemRegistrationEndpoint =
            "https://jmsgw.jtexpress.vn/servicequality/problemPiece/registration";

        private const string WaybillDetailUserAgent =
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36";

        private static readonly HttpClient _waybillDetailHttpClient = CreateWaybillDetailHttpClient();

        private string _activeDetailWaybillNo = string.Empty;
        private CancellationTokenSource _detailLoadCts;

        private static HttpClient CreateWaybillDetailHttpClient()
        {
            var handler = new HttpClientHandler
            {
                AutomaticDecompression = DecompressionMethods.GZip
                    | DecompressionMethods.Deflate
                    | DecompressionMethods.Brotli
            };
            return new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(18)
            };
        }

        private static void ApplyWaybillDetailHeaders(HttpRequestMessage request, string authToken, string routeName = "integratedComprehensive")
        {
            request.Headers.TryAddWithoutValidation("Accept", "application/json, text/plain, */*");
            request.Headers.TryAddWithoutValidation("Origin", "https://jms.jtexpress.vn");
            request.Headers.TryAddWithoutValidation("Referer", "https://jms.jtexpress.vn/");
            request.Headers.TryAddWithoutValidation("authToken", authToken);
            request.Headers.TryAddWithoutValidation("lang", "VN");
            request.Headers.TryAddWithoutValidation("langType", "VN");
            request.Headers.TryAddWithoutValidation("routeName", routeName);
            request.Headers.TryAddWithoutValidation("timezone", "GMT+0700");
            request.Headers.TryAddWithoutValidation("User-Agent", WaybillDetailUserAgent);
        }

        private async Task<string> FetchWaybillDetailJsonAsync(string url, string authToken, CancellationToken ct)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            ApplyWaybillDetailHeaders(request, authToken);

            using var response = await _waybillDetailHttpClient
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct)
                .ConfigureAwait(true);

            var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(true);
            if (!response.IsSuccessStatusCode)
            {
                AppLogger.Warning($"[WaybillDetail] HTTP {(int)response.StatusCode} for {url}");
                return null;
            }
            return body;
        }

        // getOrderDetail is a POST with a JSON body and routeName "trackingExpress".
        private async Task<string> FetchWaybillOrderDetailJsonAsync(string code, string authToken, CancellationToken ct)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, WaybillOrderDetailEndpoint);
            ApplyWaybillDetailHeaders(request, authToken, "trackingExpress");

            var bodyJson = JsonSerializer.Serialize(new { waybillNo = code, countryId = "1" });
            request.Content = new StringContent(bodyJson, System.Text.Encoding.UTF8, "application/json");

            using var response = await _waybillDetailHttpClient
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct)
                .ConfigureAwait(true);

            var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(true);
            if (!response.IsSuccessStatusCode)
            {
                AppLogger.Warning($"[WaybillDetail] getOrderDetail HTTP {(int)response.StatusCode} for {code}");
                return null;
            }
            return body;
        }

        private async Task FetchAndPostWaybillDetailAsync(string waybillNo)
        {
            if (string.IsNullOrWhiteSpace(waybillNo)) return;
            var code = waybillNo.Trim();
            _activeDetailWaybillNo = code;

            try { _detailLoadCts?.Cancel(); } catch { }
            var cts = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token);
            _detailLoadCts = cts;

            try
            {
                var token = await ResolveJourneyAuthTokenAsync(cts.Token).ConfigureAwait(true);
                if (string.IsNullOrWhiteSpace(token))
                {
                    AppLogger.Warning("[WaybillDetail] No authToken; skip detail fetch.");
                    return;
                }

                var encoded = Uri.EscapeDataString(code);
                string mainJson = await FetchWaybillDetailJsonAsync(WaybillDetailEndpoint + encoded, token, cts.Token)
                    .ConfigureAwait(true);

                string reverseJson = null;
                try
                {
                    reverseJson = await FetchWaybillDetailJsonAsync(WaybillReverseEndpoint + encoded, token, cts.Token)
                        .ConfigureAwait(true);
                }
                catch (Exception exRev)
                {
                    AppLogger.Warning($"[WaybillDetail] reverse fetch failed for {code}: {exRev.Message}");
                }

                // Fallback source for fields masked with "*" (no view permission on the main list).
                string orderDetailJson = null;
                try
                {
                    orderDetailJson = await FetchWaybillOrderDetailJsonAsync(code, token, cts.Token)
                        .ConfigureAwait(true);
                }
                catch (Exception exOrder)
                {
                    AppLogger.Warning($"[WaybillDetail] getOrderDetail fetch failed for {code}: {exOrder.Message}");
                }

                // Ignore stale responses (user opened another waybill meanwhile).
                if (cts.IsCancellationRequested ||
                    !string.Equals(_activeDetailWaybillNo, code, StringComparison.OrdinalIgnoreCase))
                    return;

                var detail = BuildWaybillDetailDto(code, mainJson, reverseJson, orderDetailJson);
                if (detail == null)
                {
                    AppLogger.Warning($"[WaybillDetail] no detail parsed for {code}");
                    return;
                }

                PostWaybillDetailToWebView2(detail);
            }
            catch (OperationCanceledException)
            {
                // Superseded by a newer selection.
            }
            catch (Exception ex)
            {
                AppLogger.Error($"[WaybillDetail] FetchAndPostWaybillDetailAsync failed for {code}", ex);
            }
            finally
            {
                if (ReferenceEquals(_detailLoadCts, cts)) _detailLoadCts = null;
                cts.Dispose();
            }
        }

        private object BuildWaybillDetailDto(string code, string mainJson, string reverseJson, string orderDetailJson)
        {
            JsonElement main = default;
            bool hasMain = false;
            if (!string.IsNullOrWhiteSpace(mainJson))
            {
                try
                {
                    using var doc = JsonDocument.Parse(mainJson);
                    var root = doc.RootElement;
                    if (root.TryGetProperty("data", out var dataArr)
                        && dataArr.ValueKind == JsonValueKind.Array
                        && dataArr.GetArrayLength() > 0)
                    {
                        main = dataArr[0].Clone();
                        hasMain = true;
                    }
                }
                catch (Exception ex)
                {
                    AppLogger.Warning($"[WaybillDetail] parse main failed: {ex.Message}");
                }
            }

            JsonElement rev = default;
            bool hasRev = false;
            if (!string.IsNullOrWhiteSpace(reverseJson))
            {
                try
                {
                    using var doc = JsonDocument.Parse(reverseJson);
                    var root = doc.RootElement;
                    if (root.TryGetProperty("data", out var dataObj)
                        && dataObj.ValueKind == JsonValueKind.Object)
                    {
                        rev = dataObj.Clone();
                        hasRev = true;
                    }
                }
                catch (Exception ex)
                {
                    AppLogger.Warning($"[WaybillDetail] parse reverse failed: {ex.Message}");
                }
            }

            // getOrderDetail: data.details {} — replaces fields masked with "*" on the main list.
            JsonElement ord = default;
            bool hasOrd = false;
            if (!string.IsNullOrWhiteSpace(orderDetailJson))
            {
                try
                {
                    using var doc = JsonDocument.Parse(orderDetailJson);
                    var root = doc.RootElement;
                    if (root.TryGetProperty("data", out var dataObj)
                        && dataObj.ValueKind == JsonValueKind.Object
                        && dataObj.TryGetProperty("details", out var det)
                        && det.ValueKind == JsonValueKind.Object)
                    {
                        ord = det.Clone();
                        hasOrd = true;
                    }
                }
                catch (Exception ex)
                {
                    AppLogger.Warning($"[WaybillDetail] parse orderDetail failed: {ex.Message}");
                }
            }

            if (!hasMain && !hasRev && !hasOrd) return null;

            // For every field pick the first non-empty, UNMASKED ('*'-free) value across the
            // available sources; reverse is preferred for contact, orderDetail fills masked gaps.
            string senderPhone = PickField(
                Field(rev, hasRev, "senderMobilePhone"), Field(rev, hasRev, "senderTelphone"),
                Field(ord, hasOrd, "senderMobilePhone"),
                Field(main, hasMain, "senderMobilePhone"), Field(main, hasMain, "senderTelphone"));
            string receiverPhone = PickField(
                Field(rev, hasRev, "receiverMobilePhone"), Field(rev, hasRev, "receiverTelphone"),
                Field(ord, hasOrd, "receiverMobilePhone"),
                Field(main, hasMain, "receiverMobilePhone"), Field(main, hasMain, "receiverTelphone"));
            string receiverName = PickField(
                Field(rev, hasRev, "receiverName"), Field(ord, hasOrd, "receiverName"), Field(main, hasMain, "receiverName"));
            string senderName = PickField(
                Field(main, hasMain, "senderName"), Field(ord, hasOrd, "senderName"));
            string customerName = PickField(
                Field(main, hasMain, "customerName"), Field(ord, hasOrd, "customerName"));
            string senderAddress = PickField(
                Field(rev, hasRev, "senderDetailedAddress"), Field(ord, hasOrd, "senderDetailedAddress"), Field(main, hasMain, "senderDetailedAddress"));
            string receiverAddress = PickField(
                Field(rev, hasRev, "receiverDetailedAddress"), Field(ord, hasOrd, "receiverDetailedAddress"), Field(main, hasMain, "receiverDetailedAddress"));
            string pickNetworkName = PickField(
                Field(main, hasMain, "pickNetworkName"), Field(ord, hasOrd, "pickNetworkName"));
            string dispatchNetworkName = PickField(
                Field(main, hasMain, "dispatchNetworkName"), Field(ord, hasOrd, "dispatchNetworkName"));
            string goodsTypeName = PickField(
                Field(main, hasMain, "goodsTypeName"), Field(ord, hasOrd, "goodsTypeName"));
            string goodsName = PickField(
                Field(main, hasMain, "goodsName"), Field(ord, hasOrd, "goodsName"));
            string packageChargeWeight = PickField(
                Field(main, hasMain, "packageChargeWeight"), Field(ord, hasOrd, "packageChargeWeight"));
            string codMoney = PickField(
                Field(main, hasMain, "codMoney"), Field(ord, hasOrd, "codMoney"));
            // "Mã đoạn 1" is taken from getOrderDetail first, then the main list as fallback.
            string terminalDispatchCode = PickField(
                Field(ord, hasOrd, "terminalDispatchCode"), Field(main, hasMain, "terminalDispatchCode"));
            string goodsValue = PickField(Field(main, hasMain, "goodsValue"));
            string partSign = PickField(Field(main, hasMain, "partSign"));
            string oldCod = PickField(Field(main, hasMain, "oldCod"), Field(ord, hasOrd, "codMoney"));
            string remarks = PickField(Field(main, hasMain, "remarks"));

            return new
            {
                code,
                // Thông tin gửi hàng
                pickNetworkName = Dash(pickNetworkName),
                customerName = Dash(customerName),
                senderName = Dash(senderName),
                senderAddress = Dash(senderAddress),
                senderPhone = Dash(senderPhone),
                // Thông tin nhận hàng
                dispatchNetworkName = Dash(dispatchNetworkName),
                receiverName = Dash(receiverName),
                receiverAddress = Dash(receiverAddress),
                receiverPhone = Dash(receiverPhone),
                // Thông tin đơn hàng
                goodsTypeName = Dash(goodsTypeName),
                goodsName = Dash(goodsName),
                goodsValue = Dash(goodsValue),
                partSign = Dash(partSign),
                packageChargeWeight = Dash(packageChargeWeight),
                codMoney = Dash(codMoney),
                oldCod = Dash(oldCod),
                terminalDispatchCode = Dash(terminalDispatchCode),
                remarks = Dash(remarks)
            };
        }

        private void PostWaybillDetailToWebView2(object detail)
        {
            if (_webView == null || !_webViewInitialized || _webView.CoreWebView2 == null) return;
            try
            {
                var payload = new { type = "WAYBILL_DETAIL", detail };
                var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions { PropertyNamingPolicy = null });
                _webView.CoreWebView2.PostWebMessageAsJson(json);
                AppLogger.Info("[WaybillDetail] posted WAYBILL_DETAIL to WebView2");
            }
            catch (Exception ex)
            {
                AppLogger.Error("Post WAYBILL_DETAIL failed", ex);
            }
        }

        // Read a property as a display string; numbers are stringified, null/absent -> null.
        private static string Field(JsonElement el, bool present, string propertyName)
        {
            if (!present) return null;
            if (!el.TryGetProperty(propertyName, out var v)) return null;
            switch (v.ValueKind)
            {
                case JsonValueKind.String: return v.GetString();
                case JsonValueKind.Number: return v.ToString();
                case JsonValueKind.True: return "1";
                case JsonValueKind.False: return "0";
                case JsonValueKind.Null:
                case JsonValueKind.Undefined: return null;
                default: return v.ToString();
            }
        }

        // Pick the first non-empty value that is not masked (contains no '*'); if every
        // candidate is masked, fall back to the first non-empty (masked) value; else null.
        private static string PickField(params string[] values)
        {
            if (values == null) return null;
            string firstNonEmpty = null;
            foreach (var v in values)
            {
                if (string.IsNullOrWhiteSpace(v)) continue;
                var t = v.Trim();
                if (firstNonEmpty == null) firstNonEmpty = t;
                if (t.IndexOf('*') < 0) return t;
            }
            return firstNonEmpty;
        }

        private async Task FetchAndPostIssueHistoryAsync(string waybillNo)
        {
            if (string.IsNullOrWhiteSpace(waybillNo)) return;
            var code = waybillNo.Trim();
            try
            {
                var token = await ResolveJourneyAuthTokenAsync(_cts.Token).ConfigureAwait(true);
                if (string.IsNullOrWhiteSpace(token))
                {
                    AppLogger.Warning("[IssueHistory] No authToken; skip.");
                    return;
                }

                var bodyJson = JsonSerializer.Serialize(new { current = 1, size = 100, waybillId = code, countryId = "1" });
                using var request = new HttpRequestMessage(HttpMethod.Post, IssueHistoryEndpoint);
                ApplyWaybillDetailHeaders(request, token, "trackingExpress");
                request.Content = new StringContent(bodyJson, System.Text.Encoding.UTF8, "application/json");

                using var response = await _waybillDetailHttpClient
                    .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, _cts.Token)
                    .ConfigureAwait(true);

                var respBody = await response.Content.ReadAsStringAsync(_cts.Token).ConfigureAwait(true);
                if (!response.IsSuccessStatusCode)
                {
                    AppLogger.Warning($"[IssueHistory] HTTP {(int)response.StatusCode} for {code}");
                    return;
                }

                var issues = ParseIssueHistory(respBody);
                var podImages = await FetchProblemPieceImagesAsync(code, token).ConfigureAwait(true);
                PostIssueHistoryToWebView2(code, issues, podImages);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                AppLogger.Error($"[IssueHistory] fetch failed for {code}", ex);
            }
        }

        private List<object> ParseIssueHistory(string json)
        {
            var list = new List<object>();
            try
            {
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                JsonElement container = root.TryGetProperty("data", out var data) ? data : root;

                JsonElement records = default;
                bool hasRecords = false;
                foreach (var key in new[] { "records", "list", "rows", "items", "resultList", "data" })
                {
                    if (container.ValueKind == JsonValueKind.Object
                        && container.TryGetProperty(key, out var arr)
                        && arr.ValueKind == JsonValueKind.Array)
                    {
                        records = arr;
                        hasRecords = true;
                        break;
                    }
                }
                if (!hasRecords && container.ValueKind == JsonValueKind.Array)
                {
                    records = container;
                    hasRecords = true;
                }

                if (hasRecords)
                {
                    foreach (var rec in records.EnumerateArray())
                    {
                        list.Add(new
                        {
                            abnormalPieceName = Dash(Field(rec, true, "abnormalPieceName")),
                            scanNetworkCode = Dash(Field(rec, true, "scanNetworkCode")),
                            scanNetworkName = Dash(Field(rec, true, "scanNetworkName")),
                            remark = Dash(Field(rec, true, "remark")),
                            scanTime = Dash(Field(rec, true, "scanTime")),
                            operatorCode = Dash(Field(rec, true, "operatorCode")),
                            operatorName = Dash(Field(rec, true, "operatorName")),
                            images = ExtractImages(rec)
                        });
                    }
                }
                else
                {
                    AppLogger.Warning("[IssueHistory] no record array. Raw(≤600): " + Truncate(json, 600));
                }
                AppLogger.Info($"[IssueHistory] parsed {list.Count} issues");
            }
            catch (Exception ex)
            {
                AppLogger.Error("[IssueHistory] parse failed", ex);
            }
            return list;
        }

        private static List<string> ExtractImages(JsonElement rec)
        {
            var imgs = new List<string>();
            JsonElement im = default;
            bool found = rec.TryGetProperty("images", out im);
            if (!found)
            {
                foreach (var k in new[] { "imageList", "podImages", "picUrls", "imageUrls", "pics", "image" })
                {
                    if (rec.TryGetProperty(k, out im)) { found = true; break; }
                }
            }
            if (!found) return imgs;

            if (im.ValueKind == JsonValueKind.Array)
            {
                foreach (var x in im.EnumerateArray())
                {
                    if (x.ValueKind == JsonValueKind.String)
                    {
                        var s = x.GetString();
                        if (!string.IsNullOrWhiteSpace(s)) imgs.Add(s.Trim());
                    }
                    else if (x.ValueKind == JsonValueKind.Object)
                    {
                        foreach (var uk in new[] { "url", "imageUrl", "picUrl", "fileUrl", "path", "ossUrl" })
                        {
                            if (x.TryGetProperty(uk, out var uv) && uv.ValueKind == JsonValueKind.String)
                            {
                                var s = uv.GetString();
                                if (!string.IsNullOrWhiteSpace(s)) { imgs.Add(s.Trim()); break; }
                            }
                        }
                    }
                }
            }
            else if (im.ValueKind == JsonValueKind.String)
            {
                var s = im.GetString();
                if (!string.IsNullOrWhiteSpace(s))
                {
                    foreach (var part in s.Split(new[] { ',', ';', '|' }, StringSplitOptions.RemoveEmptyEntries))
                    {
                        if (!string.IsNullOrWhiteSpace(part)) imgs.Add(part.Trim());
                    }
                }
            }
            return imgs;
        }

        private void PostIssueHistoryToWebView2(string code, List<object> issues, List<string> podImages)
        {
            if (_webView == null || !_webViewInitialized || _webView.CoreWebView2 == null) return;
            try
            {
                var payload = new { type = "ISSUE_HISTORY", code, issues, podImages = podImages ?? new List<string>() };
                var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions { PropertyNamingPolicy = null });
                _webView.CoreWebView2.PostWebMessageAsJson(json);
                AppLogger.Info($"[IssueHistory] posted {issues.Count} issues, {(podImages?.Count ?? 0)} POD images to WebView2");
            }
            catch (Exception ex)
            {
                AppLogger.Error("Post ISSUE_HISTORY failed", ex);
            }
        }

        // Fetch problem-piece detail and harvest all image URLs from the response
        // (pattern-based, so it works regardless of the exact field names).
        private async Task<List<string>> FetchProblemPieceImagesAsync(string code, string authToken)
        {
            var urls = new List<string>();
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, ProblemPieceEndpoint + Uri.EscapeDataString(code));
                ApplyWaybillDetailHeaders(request, authToken);

                using var response = await _waybillDetailHttpClient
                    .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, _cts.Token)
                    .ConfigureAwait(true);

                var body = await response.Content.ReadAsStringAsync(_cts.Token).ConfigureAwait(true);
                if (!response.IsSuccessStatusCode)
                {
                    AppLogger.Warning($"[ProblemPiece] HTTP {(int)response.StatusCode} for {code}");
                    return urls;
                }

                using var doc = JsonDocument.Parse(body);
                HarvestImageUrls(doc.RootElement, urls);
                AppLogger.Info($"[ProblemPiece] harvested {urls.Count} image url(s) for {code}");
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                AppLogger.Error($"[ProblemPiece] fetch failed for {code}", ex);
            }
            return urls;
        }

        private static void HarvestImageUrls(JsonElement el, List<string> urls)
        {
            switch (el.ValueKind)
            {
                case JsonValueKind.String:
                    var s = el.GetString();
                    if (LooksLikeImage(s) && !urls.Contains(s.Trim())) urls.Add(s.Trim());
                    break;
                case JsonValueKind.Array:
                    foreach (var x in el.EnumerateArray()) HarvestImageUrls(x, urls);
                    break;
                case JsonValueKind.Object:
                    foreach (var p in el.EnumerateObject()) HarvestImageUrls(p.Value, urls);
                    break;
            }
        }

        private static bool LooksLikeImage(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return false;
            var t = s.Trim().ToLowerInvariant();
            bool ext = t.Contains(".jpg") || t.Contains(".jpeg") || t.Contains(".png") || t.Contains(".webp") || t.Contains(".gif") || t.Contains(".bmp");
            bool scan = t.Contains("scan_problem") || t.Contains("jmsvn-file");
            return (t.StartsWith("http") && ext) || (scan && (ext || t.StartsWith("http")));
        }

        // "Hình ảnh" — order-related media. Reads the journey raw JSON, finds every scan
        // record carrying media (imgType in {5,11,13,14}) and resolves each via img/path.
        private async Task GatherAndPostOrderImagesAsync(string waybillNo, string journeyRawJson)
        {
            if (string.IsNullOrWhiteSpace(waybillNo) || string.IsNullOrWhiteSpace(journeyRawJson)) return;
            var code = waybillNo.Trim();
            try
            {
                var token = await ResolveJourneyAuthTokenAsync(_cts.Token).ConfigureAwait(true);
                if (string.IsNullOrWhiteSpace(token)) return;

                var records = new List<(string Waybill, string ScanTime, string ScanByCode, int ImgType)>();
                using (var doc = JsonDocument.Parse(journeyRawJson))
                {
                    CollectMediaScanRecords(doc.RootElement, code, records);
                }
                if (records.Count == 0)
                {
                    PostOrderImagesToWebView2(code, new List<string>());
                    return;
                }

                var seen = new HashSet<string>();
                var urls = new List<string>();
                foreach (var r in records)
                {
                    var key = r.Waybill + "|" + r.ScanTime + "|" + r.ScanByCode + "|" + r.ImgType;
                    if (!seen.Add(key)) continue;
                    var got = await FetchImgPathAsync(r.Waybill, r.ScanTime, r.ScanByCode, r.ImgType, token).ConfigureAwait(true);
                    foreach (var u in got) if (!urls.Contains(u)) urls.Add(u);
                }

                AppLogger.Info($"[OrderImages] {code}: records={records.Count}, urls={urls.Count}");
                PostOrderImagesToWebView2(code, urls);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                AppLogger.Error($"[OrderImages] gather failed for {code}", ex);
            }
        }

        private static void CollectMediaScanRecords(JsonElement el, string fallbackWaybill,
            List<(string Waybill, string ScanTime, string ScanByCode, int ImgType)> recs)
        {
            switch (el.ValueKind)
            {
                case JsonValueKind.Array:
                    foreach (var x in el.EnumerateArray()) CollectMediaScanRecords(x, fallbackWaybill, recs);
                    break;
                case JsonValueKind.Object:
                    if (el.TryGetProperty("imgType", out var itProp)
                        && itProp.ValueKind == JsonValueKind.Number
                        && itProp.TryGetInt32(out var imgType)
                        && Array.IndexOf(OrderImageTypes, imgType) >= 0)
                    {
                        var scanTime = Field(el, true, "scanTime");
                        var scanByCode = Field(el, true, "scanByCode");
                        if (!string.IsNullOrWhiteSpace(scanTime) && !string.IsNullOrWhiteSpace(scanByCode))
                        {
                            var wb = Field(el, true, "waybillNo") ?? Field(el, true, "billCode") ?? fallbackWaybill;
                            recs.Add((wb, scanTime.Trim(), scanByCode.Trim(), imgType));
                        }
                    }
                    foreach (var p in el.EnumerateObject()) CollectMediaScanRecords(p.Value, fallbackWaybill, recs);
                    break;
            }
        }

        private async Task<List<string>> FetchImgPathAsync(string waybill, string scanTime, string scanByCode, int imgType, string token)
        {
            var urls = new List<string>();
            try
            {
                var bodyJson = JsonSerializer.Serialize(new { waybillNo = waybill, scanTime, scanByCode, imgType, countryId = "1" });
                using var request = new HttpRequestMessage(HttpMethod.Post, PodImgPathEndpoint);
                ApplyWaybillDetailHeaders(request, token, "trackingExpress");
                request.Content = new StringContent(bodyJson, System.Text.Encoding.UTF8, "application/json");

                using var response = await _waybillDetailHttpClient
                    .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, _cts.Token)
                    .ConfigureAwait(true);

                var body = await response.Content.ReadAsStringAsync(_cts.Token).ConfigureAwait(true);
                if (!response.IsSuccessStatusCode) return urls;

                using var doc = JsonDocument.Parse(body);
                if (doc.RootElement.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array)
                {
                    foreach (var x in data.EnumerateArray())
                    {
                        if (x.ValueKind == JsonValueKind.String)
                        {
                            var s = x.GetString();
                            if (!string.IsNullOrWhiteSpace(s)) urls.Add(s.Trim());
                        }
                    }
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                AppLogger.Warning($"[OrderImages] img/path failed (imgType={imgType}): {ex.Message}");
            }
            return urls;
        }

        private void PostOrderImagesToWebView2(string code, List<string> images)
        {
            if (_webView == null || !_webViewInitialized || _webView.CoreWebView2 == null) return;
            try
            {
                var payload = new { type = "ORDER_IMAGES", code, images = images ?? new List<string>() };
                var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions { PropertyNamingPolicy = null });
                _webView.CoreWebView2.PostWebMessageAsJson(json);
                AppLogger.Info($"[OrderImages] posted {(images?.Count ?? 0)} media url(s) to WebView2");
            }
            catch (Exception ex)
            {
                AppLogger.Error("Post ORDER_IMAGES failed", ex);
            }
        }

        private async Task FetchAndPostReceiverNetworkAsync(string waybillNo)
        {
            if (string.IsNullOrWhiteSpace(waybillNo)) return;
            var code = waybillNo.Trim();
            try
            {
                var token = await ResolveJourneyAuthTokenAsync(_cts.Token).ConfigureAwait(true);
                if (string.IsNullOrWhiteSpace(token)) return;

                var bodyJson = JsonSerializer.Serialize(new { waybillNo = code, defaultReceiver = 2, countryId = "1" });
                using var request = new HttpRequestMessage(HttpMethod.Post, ReceiverNetworkEndpoint);
                ApplyWaybillDetailHeaders(request, token, "batchProblem");
                request.Content = new StringContent(bodyJson, System.Text.Encoding.UTF8, "application/json");

                using var response = await _waybillDetailHttpClient
                    .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, _cts.Token)
                    .ConfigureAwait(true);

                var body = await response.Content.ReadAsStringAsync(_cts.Token).ConfigureAwait(true);
                if (!response.IsSuccessStatusCode)
                {
                    AppLogger.Warning($"[ReceiverNetwork] HTTP {(int)response.StatusCode} for {code}");
                    return;
                }

                string netCode = null, netName = null;
                try
                {
                    using var doc = JsonDocument.Parse(body);
                    if (doc.RootElement.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Object)
                    {
                        netCode = Field(data, true, "code");
                        netName = Field(data, true, "name");
                    }
                }
                catch (Exception ex)
                {
                    AppLogger.Warning($"[ReceiverNetwork] parse failed: {ex.Message}");
                }

                PostReceiverNetworkToWebView2(code, netCode, netName);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                AppLogger.Error($"[ReceiverNetwork] fetch failed for {code}", ex);
            }
        }

        private void PostReceiverNetworkToWebView2(string waybillNo, string netCode, string netName)
        {
            if (_webView == null || !_webViewInitialized || _webView.CoreWebView2 == null) return;
            try
            {
                var payload = new { type = "RECEIVER_NETWORK", code = waybillNo, netCode = netCode ?? "", netName = netName ?? "" };
                var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions { PropertyNamingPolicy = null });
                _webView.CoreWebView2.PostWebMessageAsJson(json);
                AppLogger.Info($"[ReceiverNetwork] posted code={netCode} to WebView2");
            }
            catch (Exception ex)
            {
                AppLogger.Error("Post RECEIVER_NETWORK failed", ex);
            }
        }

        // Normalize a network code to its canonical { code, name } via basicdata/network/select/all.
        private async Task FetchAndPostNetworkInfoAsync(string networkCode)
        {
            if (string.IsNullOrWhiteSpace(networkCode)) return;
            var reqCode = networkCode.Trim();
            try
            {
                var token = await ResolveJourneyAuthTokenAsync(_cts.Token).ConfigureAwait(true);
                if (string.IsNullOrWhiteSpace(token)) return;

                var url = NetworkSelectEndpoint + "?current=1&size=10&name=" + Uri.EscapeDataString(reqCode) + "&queryLevel=3";
                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                ApplyWaybillDetailHeaders(request, token, "batchProblem");

                using var response = await _waybillDetailHttpClient
                    .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, _cts.Token)
                    .ConfigureAwait(true);

                var body = await response.Content.ReadAsStringAsync(_cts.Token).ConfigureAwait(true);
                if (!response.IsSuccessStatusCode)
                {
                    AppLogger.Warning($"[NetworkInfo] HTTP {(int)response.StatusCode} for {reqCode}");
                    return;
                }

                string netCode = null, netName = null;
                try
                {
                    using var doc = JsonDocument.Parse(body);
                    if (doc.RootElement.TryGetProperty("data", out var data)
                        && data.ValueKind == JsonValueKind.Object
                        && data.TryGetProperty("records", out var recs)
                        && recs.ValueKind == JsonValueKind.Array
                        && recs.GetArrayLength() > 0)
                    {
                        var r0 = recs[0];
                        netCode = Field(r0, true, "code");
                        netName = Field(r0, true, "name");
                    }
                }
                catch (Exception ex)
                {
                    AppLogger.Warning($"[NetworkInfo] parse failed: {ex.Message}");
                }

                PostNetworkInfoToWebView2(reqCode, netCode, netName);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                AppLogger.Error($"[NetworkInfo] fetch failed for {reqCode}", ex);
            }
        }

        private void PostNetworkInfoToWebView2(string reqCode, string netCode, string netName)
        {
            if (_webView == null || !_webViewInitialized || _webView.CoreWebView2 == null) return;
            try
            {
                var payload = new { type = "NETWORK_INFO", reqCode = reqCode, netCode = netCode ?? reqCode, netName = netName ?? "" };
                var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions { PropertyNamingPolicy = null });
                _webView.CoreWebView2.PostWebMessageAsJson(json);
                AppLogger.Info($"[NetworkInfo] posted {reqCode} -> {netName}");
            }
            catch (Exception ex)
            {
                AppLogger.Error("Post NETWORK_INFO failed", ex);
            }
        }

        // Typeahead search for "Bưu cục tiếp nhận": name/code -> list of { code, name }.
        private async Task FetchAndPostNetworkSearchAsync(string query)
        {
            var q = (query ?? string.Empty).Trim();
            if (q.Length == 0) return;
            try
            {
                var token = await ResolveJourneyAuthTokenAsync(_cts.Token).ConfigureAwait(true);
                if (string.IsNullOrWhiteSpace(token)) return;

                var url = NetworkSelectEndpoint + "?current=1&size=10&name=" + Uri.EscapeDataString(q) + "&queryLevel=3";
                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                ApplyWaybillDetailHeaders(request, token, "batchProblem");

                using var response = await _waybillDetailHttpClient
                    .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, _cts.Token)
                    .ConfigureAwait(true);

                var body = await response.Content.ReadAsStringAsync(_cts.Token).ConfigureAwait(true);
                if (!response.IsSuccessStatusCode)
                {
                    AppLogger.Warning($"[NetworkSearch] HTTP {(int)response.StatusCode} for '{q}'");
                    return;
                }

                var results = new List<object>();
                try
                {
                    using var doc = JsonDocument.Parse(body);
                    if (doc.RootElement.TryGetProperty("data", out var data)
                        && data.ValueKind == JsonValueKind.Object
                        && data.TryGetProperty("records", out var recs)
                        && recs.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var rec in recs.EnumerateArray())
                        {
                            var c = Field(rec, true, "code");
                            if (string.IsNullOrWhiteSpace(c)) continue;
                            results.Add(new { code = c, name = Field(rec, true, "name") ?? "" });
                        }
                    }
                }
                catch (Exception ex)
                {
                    AppLogger.Warning($"[NetworkSearch] parse failed: {ex.Message}");
                }

                PostNetworkSearchResultsToWebView2(q, results);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                AppLogger.Error($"[NetworkSearch] fetch failed for '{q}'", ex);
            }
        }

        private void PostNetworkSearchResultsToWebView2(string query, List<object> results)
        {
            if (_webView == null || !_webViewInitialized || _webView.CoreWebView2 == null) return;
            try
            {
                var payload = new { type = "NETWORK_SEARCH_RESULTS", query = query, results = results };
                var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions { PropertyNamingPolicy = null });
                _webView.CoreWebView2.PostWebMessageAsJson(json);
                AppLogger.Info($"[NetworkSearch] posted {results.Count} result(s) for '{query}'");
            }
            catch (Exception ex)
            {
                AppLogger.Error("Post NETWORK_SEARCH_RESULTS failed", ex);
            }
        }

        private async Task SubmitIssueRegistrationAsync(
            string waybillNo, string level1, string level2, string level2Code, string level2Name, string network, string description)
        {
            var code = (waybillNo ?? "").Trim();
            try
            {
                var token = await ResolveJourneyAuthTokenAsync(_cts.Token).ConfigureAwait(true);
                if (string.IsNullOrWhiteSpace(token))
                {
                    AppLogger.Warning("[IssueRegister] no authToken; abort.");
                    PostIssueResultToWebView2(false, "Chưa có authToken JMS.", "");
                    return;
                }

                int l1 = 0, l2 = 0;
                int.TryParse((level1 ?? "").Trim(), out l1);
                int.TryParse((level2 ?? "").Trim(), out l2);

                var body = new
                {
                    waybillNo = code,
                    replyContent = "",
                    problemPieceId = "",
                    probleTypeSubjectId = l1,
                    probleTypeSubjectId2 = l2,
                    receiveNetworkCode = network ?? "",
                    replyContentImg = new string[0],
                    replyStatus = 0,
                    probleTypeId = "",
                    probleDescription = description ?? "",
                    uploadDataProp = "success",
                    videoList = new object[0],
                    secondLevelTypeId = l2,
                    secondLevelTypeCode = level2Code ?? "",
                    secondLevelTypeName = level2Name ?? "",
                    paths = "",
                    countryId = "1"
                };
                var bodyJson = JsonSerializer.Serialize(body);

                AppLogger.Info("[IssueRegister] POST " + ProblemRegistrationEndpoint);
                AppLogger.Info("[IssueRegister] headers: Content-Type=application/json;charset=UTF-8, authToken=" + MaskToken(token) + ", lang=VN, langType=VN, routeName=batchProblem, timezone=GMT+0700");
                AppLogger.Info("[IssueRegister] request body: " + bodyJson);

                using var request = new HttpRequestMessage(HttpMethod.Post, ProblemRegistrationEndpoint);
                ApplyWaybillDetailHeaders(request, token, "batchProblem");
                request.Content = new StringContent(bodyJson, System.Text.Encoding.UTF8, "application/json");

                using var response = await _waybillDetailHttpClient
                    .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, _cts.Token)
                    .ConfigureAwait(true);

                var respBody = await response.Content.ReadAsStringAsync(_cts.Token).ConfigureAwait(true);
                AppLogger.Info($"[IssueRegister] response HTTP {(int)response.StatusCode}: {respBody}");

                bool success = false;
                string msg = null;
                try
                {
                    using var doc = JsonDocument.Parse(respBody);
                    var root = doc.RootElement;
                    bool succ = root.TryGetProperty("succ", out var sp) && sp.ValueKind == JsonValueKind.True;
                    bool codeOk = root.TryGetProperty("code", out var cp) && cp.ValueKind == JsonValueKind.Number && cp.GetInt32() == 1;
                    success = response.IsSuccessStatusCode && (succ || codeOk);
                    if (root.TryGetProperty("msg", out var mp)) msg = mp.GetString();
                }
                catch (Exception ex)
                {
                    AppLogger.Warning("[IssueRegister] response parse failed: " + ex.Message);
                }

                PostIssueResultToWebView2(success, msg ?? (success ? "Thao tác thành công" : "Đăng ký thất bại"), Truncate(respBody, 500));
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                AppLogger.Error($"[IssueRegister] submit failed for {code}", ex);
                PostIssueResultToWebView2(false, "Lỗi gửi đăng ký: " + ex.Message, "");
            }
        }

        private void PostIssueResultToWebView2(bool success, string msg, string raw)
        {
            if (_webView == null || !_webViewInitialized || _webView.CoreWebView2 == null) return;
            try
            {
                var payload = new { type = "ISSUE_RESULT", success = success, msg = msg ?? "", raw = raw ?? "" };
                var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions { PropertyNamingPolicy = null });
                _webView.CoreWebView2.PostWebMessageAsJson(json);
                AppLogger.Info($"[IssueRegister] posted ISSUE_RESULT success={success}");
            }
            catch (Exception ex)
            {
                AppLogger.Error("Post ISSUE_RESULT failed", ex);
            }
        }

        private static string MaskToken(string t)
            => string.IsNullOrEmpty(t) ? "" : (t.Length <= 8 ? "****" : (t.Substring(0, 4) + "..." + t.Substring(t.Length - 4)));
    }
}

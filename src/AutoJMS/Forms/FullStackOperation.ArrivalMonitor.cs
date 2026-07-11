using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace AutoJMS
{
    // "Hàng đến" queues sourced from the JMS bigdata arrival-monitor (_detail):
    //   • "Đã đến"        -> dateType 2 (by arrival date), JumpType "arrivalNum", today window.
    //   • "Chưa quét đến" -> dateType 1, JumpType "noArrivalNum", last 7 days.
    // The queue count comes from the response "total". The parser is defensive about
    // field names and logs the raw JSON so the mapping can be confirmed live.
    public partial class FullStackOperation
    {
        private const string ArrivalMonitorEndpoint =
            "https://jmsgw.jtexpress.vn/businessindicator/bigdataReport/detail/bus_op_arrival_monitor_detail";
        private const string ArrivalStationCode = "214A02";

        private int _arrivalArrivedTotal = 0;
        private List<object> _arrivalNotScanned = new();
        private int _arrivalNotScannedTotal = 0;
        private DateTime _arrivalLastFetch = DateTime.MinValue;

        private async Task RefreshArrivalMonitorAsync(CancellationToken ct)
        {
            // Short TTL so rapid successive refreshes (star toggle, note save) don't hammer JMS.
            if ((DateTime.Now - _arrivalLastFetch).TotalSeconds < 30
                && (_arrivalArrivedTotal > 0 || _arrivalNotScanned.Count > 0))
                return;

            try
            {
                var token = await ResolveJourneyAuthTokenAsync(ct).ConfigureAwait(true);
                if (string.IsNullOrWhiteSpace(token))
                {
                    AppLogger.Warning("[ArrivalMonitor] No authToken; skip arrival fetch.");
                    return;
                }

                var today = DateTime.Now.Date;
                string todayStart = today.ToString("yyyy-MM-dd") + " 00:00:00";
                string todayEnd = today.ToString("yyyy-MM-dd") + " 23:59:59";
                string weekStart = today.AddDays(-7).ToString("yyyy-MM-dd") + " 00:00:00";

                // "Đã đến" — count only (size 1; total is independent of page size), no list.
                var arrived = await FetchArrivalBucketAsync(
                    token, 2, "arrivalNum", todayStart, todayEnd, 1, "Đã đến", "hang_den_all", ct).ConfigureAwait(true);

                // "Chưa quét đến" — list (up to 100) + count.
                var notScanned = await FetchArrivalBucketAsync(
                    token, 1, "noArrivalNum", weekStart, todayEnd, 100, "Chưa quét đến", "not_scanned_in", ct).ConfigureAwait(true);

                _arrivalArrivedTotal = arrived.Total;
                _arrivalNotScanned = notScanned.List;
                _arrivalNotScannedTotal = notScanned.Total;
                _arrivalLastFetch = DateTime.Now;

                AppLogger.Info($"[ArrivalMonitor] arrivedTotal={_arrivalArrivedTotal}; " +
                               $"notScanned list={_arrivalNotScanned.Count} total={_arrivalNotScannedTotal}");
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                AppLogger.Error("[ArrivalMonitor] refresh failed", ex);
            }
        }

        private async Task<(List<object> List, int Total)> FetchArrivalBucketAsync(
            string token, int dateType, string jumpType, string startTime, string endTime,
            int size, string subLabel, string subKey, CancellationToken ct)
        {
            try
            {
                var body = new
                {
                    current = 1,
                    size = size,
                    arrivalSationCode = ArrivalStationCode,
                    dateType,
                    JumpType = jumpType,
                    startTime,
                    endTime,
                    countryId = "1"
                };
                var bodyJson = JsonSerializer.Serialize(body);

                using var request = new HttpRequestMessage(HttpMethod.Post, ArrivalMonitorEndpoint);
                request.Headers.TryAddWithoutValidation("Accept", "application/json, text/plain, */*");
                request.Headers.TryAddWithoutValidation("Origin", "https://jms.jtexpress.vn");
                request.Headers.TryAddWithoutValidation("Referer", "https://jms.jtexpress.vn/");
                request.Headers.TryAddWithoutValidation("authToken", token);
                request.Headers.TryAddWithoutValidation("lang", "VN");
                request.Headers.TryAddWithoutValidation("langType", "VN");
                request.Headers.TryAddWithoutValidation("routeName", "ArriveMonitor|crisbiIndex");
                request.Headers.TryAddWithoutValidation("timezone", "GMT+0700");
                request.Headers.TryAddWithoutValidation("User-Agent", WaybillDetailUserAgent);
                request.Content = new StringContent(bodyJson, Encoding.UTF8, "application/json");

                using var response = await _waybillDetailHttpClient
                    .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct)
                    .ConfigureAwait(true);

                var respBody = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(true);
                if (!response.IsSuccessStatusCode)
                {
                    AppLogger.Warning($"[ArrivalMonitor] {jumpType} HTTP {(int)response.StatusCode}");
                    return (new List<object>(), 0);
                }

                return ParseArrivalBucket(respBody, subLabel, subKey);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                AppLogger.Error($"[ArrivalMonitor] {jumpType} fetch failed", ex);
                return (new List<object>(), 0);
            }
        }

        private (List<object> List, int Total) ParseArrivalBucket(string json, string subLabel, string subKey)
        {
            var list = new List<object>();
            int total = 0;
            try
            {
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                bool hasData = root.TryGetProperty("data", out var data);
                total = ReadTotal(root);
                if (total == 0 && hasData) total = ReadTotal(data);

                JsonElement container = hasData ? data : root;
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
                        var code = FirstProp(rec, "waybillNo", "waybillNum", "waybillCode", "wayBillNo", "billCode", "waybill", "mainWaybillNo");
                        if (string.IsNullOrWhiteSpace(code)) continue;
                        list.Add(BuildArrivalDto(rec, code.Trim(), subLabel, subKey));
                    }
                    if (total == 0) total = list.Count;
                }
                else
                {
                    AppLogger.Warning($"[ArrivalMonitor] {subKey}: no record array. Raw(≤600): " + Truncate(json, 600));
                }

                AppLogger.Info($"[ArrivalMonitor] {subKey} parsed records={list.Count}, total={total}");
            }
            catch (Exception ex)
            {
                AppLogger.Error($"[ArrivalMonitor] {subKey} parse failed", ex);
            }
            return (list, total);
        }

        private static int ReadTotal(JsonElement el)
        {
            if (el.ValueKind != JsonValueKind.Object) return 0;
            foreach (var key in new[] { "total", "totalNum", "totalCount", "count", "totalSize", "arrivalNum", "noArrivalNum" })
            {
                if (el.TryGetProperty(key, out var t))
                {
                    if (t.ValueKind == JsonValueKind.Number && t.TryGetInt32(out var tv)) return tv;
                    if (t.ValueKind == JsonValueKind.String && int.TryParse(t.GetString(), out var ts)) return ts;
                }
            }
            return 0;
        }

        private object BuildArrivalDto(JsonElement rec, string code, string subLabel, string subKey)
        {
            string recipient = FirstProp(rec, "receiverName", "recipientName", "customerName", "consigneeName");
            string addr = FirstProp(rec, "receiverFullAddress", "receiverAddress", "receiverDetailedAddress", "address");
            string source = FirstProp(rec, "sendNetworkName", "dispatchNetworkName", "arrivalSationName", "arrivalStationName", "networkName");
            string cod = FirstProp(rec, "codMoney", "cod", "collectionMoney");
            string status = FirstProp(rec, "waybillStatusName", "statusName", "scanStatusName", "scanStatus");
            string lastScan = FirstProp(rec, "arrivalTime", "scanTime", "createTime", "orderTime", "sendTime", "collectTime", "expectArrivalTime");

            var mains = new List<string> { "hang-den" };
            var subs = new List<string> { subLabel };
            var subKeys = new List<string> { subKey };

            return new
            {
                code,
                recipient = Dash(recipient),
                phone = "-",
                addr = Dash(addr),
                source = Dash(source),
                cod = string.IsNullOrWhiteSpace(cod) ? "0 đ" : cod.Trim(),
                sla = "-",
                slaColor = "#6b7588",
                service = "Chuyển phát",
                weight = "-",
                pkgs = "1",
                orderNo = code,
                sender = "-",
                staff = "-",
                senderAddr = "-",
                content = "-",
                payMethod = "-",
                ward = "-",
                lastScan = Dash(lastScan),
                status = string.IsNullOrWhiteSpace(status) ? subLabel : status.Trim(),
                op = subLabel,
                issueReason = "-",
                mains = mains,
                mainKeys = mains,
                subs = subs,
                subKeys = subKeys,
                isStar = false
            };
        }

        private static string FirstProp(JsonElement el, params string[] names)
        {
            if (el.ValueKind != JsonValueKind.Object) return null;
            foreach (var n in names)
            {
                if (el.TryGetProperty(n, out var v))
                {
                    if (v.ValueKind == JsonValueKind.String)
                    {
                        var s = v.GetString();
                        if (!string.IsNullOrWhiteSpace(s)) return s;
                    }
                    else if (v.ValueKind == JsonValueKind.Number)
                    {
                        return v.ToString();
                    }
                }
            }
            return null;
        }

        private static string Truncate(string s, int max)
            => string.IsNullOrEmpty(s) ? string.Empty : (s.Length > max ? s.Substring(0, max) : s);
    }
}

using AutoJMS.Data;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace AutoJMS
{
    // Source #2 inventory list: JMS "Chỉ số vận hành → Khâu phát → Thống kê kiểm kho → tồn 1 ngày
    // (today→today) → click Số đơn tồn". Endpoints + exact payloads captured 2026-07-16:
    //
    //   aggregate (has scanAgentCode + reserveNum):
    //     POST bigdataReport/detail/opt_stocktaking_total
    //     { current, size, dimension:"Network", scanNetworkCode, startDate, endDate, startTime, endTime, countryId }
    //
    //   waybill list ("Số đơn tồn" drill-down — what we need):
    //     POST bigdataReport/detail/opt_stocktaking_ret_detail
    //     { current, size, scanAgentCode, scanNetworkCode, isFlag:"1", startDate, endDate, countryId }
    //     -> data.records[].billcode, data.total, data.pages
    //
    // scanNetworkCode = AppConfig.ActionSiteCode ("214A02"). scanAgentCode (branch, "208001") is not
    // in config, so it is resolved once from opt_stocktaking_total. PageSize fixed at 100; pages 2..N
    // fetched concurrently (page 1 gives total pages).
    public static class StockCheckSyncService
    {
        private const int PageSize = 100;
        private const int MaxRetriesPerPage = 3;
        private const int PageConcurrency = 5;
        private const string RouteName = "MonitoringBi|crisbiIndex";
        private const string RouterNameList =
            "%E7%BB%8F%E8%90%A5%E6%8C%87%E6%A0%87%3E%E6%B4%BE%E4%BB%B6%E7%AB%AF%3E%E7%9B%98%E5%BA%93%E6%8A%A5%E8%A1%A8";
        private const string TotalPath = "businessindicator/bigdataReport/detail/opt_stocktaking_total";
        private const string DetailPath = "businessindicator/bigdataReport/detail/opt_stocktaking_ret_detail";

        private static string GetNetworkCode()
        {
            if (!string.IsNullOrWhiteSpace(AppConfig.Current.ActionSiteCode))
                return AppConfig.Current.ActionSiteCode.Trim();
            return "214A02";
        }

        /// <summary>
        /// Fetches the full "Số đơn tồn" waybill list for today (tồn 1 ngày) with parallel paging.
        /// </summary>
        public static async Task<List<string>> FetchStockCheckWaybillsAsync(CancellationToken ct = default)
        {
            if (!JmsAuthStateService.HasToken && !AuthStateService.Instance.IsAuthenticated)
            {
                AppLogger.Warning("[StockCheckSync] No authToken; skip.");
                return new List<string>();
            }

            var waybills = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var collectLock = new object();
            string detailUrl = AppConfig.Current.BuildJmsApiUrl(DetailPath);
            string networkCode = GetNetworkCode();
            string startDate = DateTime.Now.ToString("yyyy-MM-dd 00:00:00");
            string endDate = DateTime.Now.ToString("yyyy-MM-dd 23:59:59");

            // Resolve the branch/agent code (scanAgentCode) once from the aggregate endpoint.
            string agentCode = await ResolveAgentCodeAsync(networkCode, startDate, endDate, ct).ConfigureAwait(false);
            AppLogger.Info($"[StockCheckSync] start network={networkCode}, agent={agentCode}, window={startDate}..{endDate}, pageSize={PageSize}");

            void Absorb(List<string> codes)
            {
                lock (collectLock)
                {
                    foreach (var raw in codes)
                    {
                        var bc = raw?.Trim();
                        if (!string.IsNullOrWhiteSpace(bc)) waybills.Add(bc.ToUpperInvariant());
                    }
                }
            }

            var first = await FetchOnePageAsync(detailUrl, agentCode, networkCode, startDate, endDate, 1, ct).ConfigureAwait(false);
            if (!first.Ok)
            {
                AppLogger.Error($"[StockCheckSync] page 1 failed: {first.ErrorMessage}");
                return new List<string>();
            }
            Absorb(first.Codes);

            int totalPages = first.Pages > 0
                ? first.Pages
                : (first.Total > 0 ? (int)Math.Ceiling(first.Total / (double)PageSize) : -1);
            AppLogger.Info($"[StockCheckSync] total={first.Total}, pages={first.Pages}, computedPages={totalPages}");

            if (first.Codes.Count > 0 && totalPages > 1)
            {
                var pageNumbers = Enumerable.Range(2, totalPages - 1).ToList();
                await Parallel.ForEachAsync(
                    pageNumbers,
                    new ParallelOptions { MaxDegreeOfParallelism = PageConcurrency, CancellationToken = ct },
                    async (page, token) =>
                    {
                        var pr = await FetchOnePageAsync(detailUrl, agentCode, networkCode, startDate, endDate, page, token).ConfigureAwait(false);
                        if (!pr.Ok) { AppLogger.Warning($"[StockCheckSync] page={page} skipped: {pr.ErrorMessage}"); return; }
                        Absorb(pr.Codes);
                    }).ConfigureAwait(false);
            }
            else if (first.Codes.Count > 0 && totalPages < 0)
            {
                int page = 2;
                while (!ct.IsCancellationRequested)
                {
                    var pr = await FetchOnePageAsync(detailUrl, agentCode, networkCode, startDate, endDate, page, ct).ConfigureAwait(false);
                    if (!pr.Ok) { AppLogger.Warning($"[StockCheckSync] page={page} stop (fallback): {pr.ErrorMessage}"); break; }
                    if (pr.Codes.Count == 0) break;
                    Absorb(pr.Codes);
                    page++;
                }
            }

            AppLogger.Info($"[StockCheckSync] done, unique waybills={waybills.Count}");
            return waybills.ToList();
        }

        // Aggregate call — returns scanAgentCode (branch) for the network. Best-effort; on any
        // failure returns "" and the detail call proceeds with scanNetworkCode only.
        private static async Task<string> ResolveAgentCodeAsync(string networkCode, string startDate, string endDate, CancellationToken ct)
        {
            try
            {
                string url = AppConfig.Current.BuildJmsApiUrl(TotalPath);
                var payload = new Dictionary<string, object>
                {
                    { "current", 1 }, { "size", 20 }, { "dimension", "Network" },
                    { "scanNetworkCode", networkCode },
                    { "startDate", startDate }, { "endDate", endDate },
                    { "startTime", startDate }, { "endTime", endDate },
                    { "countryId", "1" }
                };
                using var res = await JmsApiClient.PostJsonAsync(
                    url, JsonSerializer.Serialize(payload, AppConfig.CreateJsonOptions()),
                    routeName: RouteName, routerNameList: RouterNameList,
                    origin: "https://jms.jtexpress.vn", ct: ct).ConfigureAwait(false);
                if (res == null || !res.IsSuccessStatusCode) return "";
                var json = await res.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(json)) return "";
                using var doc = JsonDocument.Parse(json);
                var records = FindRecordsArray(doc.RootElement);
                if (records.ValueKind == JsonValueKind.Array)
                    foreach (var r in records.EnumerateArray())
                        if (r.ValueKind == JsonValueKind.Object && r.TryGetProperty("scanAgentCode", out var a))
                        {
                            var v = a.ValueKind == JsonValueKind.String ? a.GetString() : a.GetRawText();
                            if (!string.IsNullOrWhiteSpace(v)) return v.Trim();
                        }
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) { AppLogger.Warning($"[StockCheckSync] resolve agentCode failed: {ex.Message}"); }
            return "";
        }

        private sealed class PageFetch
        {
            public bool Ok;
            public string ErrorMessage;
            public List<string> Codes = new();
            public int Pages;
            public int Total;
        }

        private static object BuildDetailPayload(int page, string agentCode, string networkCode, string startDate, string endDate)
        {
            var p = new Dictionary<string, object>
            {
                { "current", page },
                { "size", PageSize },
                { "scanNetworkCode", networkCode },
                { "isFlag", "1" },
                { "startDate", startDate },
                { "endDate", endDate },
                { "countryId", "1" }
            };
            if (!string.IsNullOrWhiteSpace(agentCode)) p["scanAgentCode"] = agentCode;
            return p;
        }

        private static async Task<PageFetch> FetchOnePageAsync(string url, string agentCode, string networkCode, string startDate, string endDate, int page, CancellationToken ct)
        {
            for (int retry = 1; retry <= MaxRetriesPerPage; retry++)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    var body = JsonSerializer.Serialize(BuildDetailPayload(page, agentCode, networkCode, startDate, endDate), AppConfig.CreateJsonOptions());
                    using var res = await JmsApiClient.PostJsonAsync(
                        url, body,
                        routeName: RouteName,
                        routerNameList: RouterNameList,
                        origin: "https://jms.jtexpress.vn",
                        ct: ct).ConfigureAwait(false);
                    if (res == null) throw new Exception("JMS API transport error (null response).");
                    if ((int)res.StatusCode == 401) return new PageFetch { Ok = false, ErrorMessage = "AUTH_ERROR (401)" };
                    var json = await res.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                    if (!res.IsSuccessStatusCode) throw new Exception($"HTTP {(int)res.StatusCode}: {Truncate(json, 200)}");
                    if (string.IsNullOrWhiteSpace(json)) throw new Exception("API JMS trả về body rỗng");

                    using var doc = JsonDocument.Parse(json);
                    var root = doc.RootElement;
                    bool succ = root.TryGetProperty("succ", out var s) && s.ValueKind == JsonValueKind.True;
                    int code = root.TryGetProperty("code", out var c) && c.ValueKind == JsonValueKind.Number && c.TryGetInt32(out var cv) ? cv : -1;
                    if (!succ && code != 1)
                    {
                        string msg = root.TryGetProperty("msg", out var m) && m.ValueKind == JsonValueKind.String ? m.GetString() : "";
                        return new PageFetch { Ok = false, ErrorMessage = $"succ=false code={code} msg={msg}" };
                    }

                    var records = FindRecordsArray(root);
                    var result = new PageFetch
                    {
                        Ok = true,
                        Total = FindInt(root, new[] { new[] { "data", "total" }, new[] { "total" } }),
                        Pages = FindInt(root, new[] { new[] { "data", "pages" }, new[] { "pages" } })
                    };
                    if (records.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var record in records.EnumerateArray())
                        {
                            var wb = ExtractBillcode(record);
                            if (!string.IsNullOrWhiteSpace(wb)) result.Codes.Add(wb);
                        }
                    }
                    AppLogger.Info($"[StockCheckSync] page={page} records={result.Codes.Count} pages={result.Pages} total={result.Total}");
                    return result;
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    if (retry >= MaxRetriesPerPage)
                    {
                        AppLogger.Error($"[StockCheckSync] page={page} failed after {retry} retries.", ex);
                        return new PageFetch { Ok = false, ErrorMessage = ex.Message };
                    }
                    var delay = TimeSpan.FromSeconds(Math.Min(10, retry * 2));
                    AppLogger.Warning($"[StockCheckSync] page={page} retry={retry}/{MaxRetriesPerPage} after {delay.TotalSeconds}s: {ex.Message}");
                    await Task.Delay(delay, ct).ConfigureAwait(false);
                }
            }
            return new PageFetch { Ok = false, ErrorMessage = "exhausted retries" };
        }

        private static JsonElement FindRecordsArray(JsonElement root)
        {
            string[][] candidates =
            {
                new[] { "data", "records" }, new[] { "data", "list" }, new[] { "data", "rows" },
                new[] { "data", "result" }, new[] { "records" }, new[] { "rows" }, new[] { "list" }
            };
            foreach (var cand in candidates)
                if (TryGetByPath(root, cand, out var el) && el.ValueKind == JsonValueKind.Array) return el;
            return default;
        }

        private static int FindInt(JsonElement root, string[][] candidates)
        {
            foreach (var cand in candidates)
                if (TryGetByPath(root, cand, out var el) && el.ValueKind == JsonValueKind.Number && el.TryGetInt32(out var v)) return v;
            return 0;
        }

        private static bool TryGetByPath(JsonElement root, string[] path, out JsonElement result)
        {
            result = root;
            foreach (var key in path)
            {
                if (result.ValueKind != JsonValueKind.Object || !result.TryGetProperty(key, out var next))
                {
                    result = default;
                    return false;
                }
                result = next;
            }
            return true;
        }

        private static string ExtractBillcode(JsonElement record)
        {
            if (record.ValueKind != JsonValueKind.Object) return null;
            string[] fields = { "billcode", "billCode", "waybillNo", "waybill_no", "mailNo", "waybillCode" };
            foreach (var f in fields)
            {
                if (record.TryGetProperty(f, out var prop))
                {
                    string v = prop.ValueKind == JsonValueKind.String ? prop.GetString()
                             : prop.ValueKind == JsonValueKind.Number ? prop.GetRawText()
                             : null;
                    if (!string.IsNullOrWhiteSpace(v)) return v.Trim();
                }
            }
            return null;
        }

        private static string Truncate(string text, int max)
            => string.IsNullOrEmpty(text) ? string.Empty : (text.Length <= max ? text : text.Substring(0, max));
    }
}

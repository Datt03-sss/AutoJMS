using AutoJMS.Data;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace AutoJMS
{
    // Source #2 inventory list: JMS "Chỉ số vận hành → Khâu phát → Thống kê kiểm kho".
    // Flow surveyed 2026-07-16:
    //   * aggregate  -> POST bigdataReport/detail/opt_stocktaking_sum
    //   * "Số đơn tồn" drill-down (the waybill list we need)
    //                -> POST bigdataReport/detail/opt_stocktaking_ret_detail
    // Window = "Thời gian tồn 1 ngày" (today 00:00:00 -> today 23:59:59). PageSize fixed at 100.
    // Same bigdataReport/detail framework as source #1, so the request/response shapes mirror
    // take_ret_mon_detail_doris2. Pages 2..N are fetched concurrently (page 1 gives total pages).
    //
    // NOTE: the exact request field set for opt_stocktaking_ret_detail could not be captured from
    // the cross-origin console frame; it is inferred from the sibling source-#1 payload. The succ
    // check + tolerant parser make a wrong field set fail safe (returns empty, logged) rather than
    // crash. Verify the first real run via the [StockCheckSync] page logs and adjust BuildPayload
    // if records come back 0 while the console shows a non-zero "Số đơn tồn".
    public static class StockCheckSyncService
    {
        private const int PageSize = 100;
        private const int MaxRetriesPerPage = 3;
        private const int PageConcurrency = 5;
        private const string StockCheckRouteName = "MonitoringBi";
        private const string InventoryDimension = "3";

        private static string GetActionSiteCode()
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
            string url = AppConfig.Current.BuildJmsApiUrl("businessindicator/bigdataReport/detail/opt_stocktaking_ret_detail");
            string actionSiteCode = GetActionSiteCode();
            string startDate = DateTime.Now.ToString("yyyy-MM-dd 00:00:00");
            string endDate = DateTime.Now.ToString("yyyy-MM-dd 23:59:59");

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

            AppLogger.Info($"[StockCheckSync] start actionSiteCode={actionSiteCode}, window={startDate}..{endDate}, pageSize={PageSize}");

            var first = await FetchOnePageAsync(url, actionSiteCode, startDate, endDate, 1, ct).ConfigureAwait(false);
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
                        var pr = await FetchOnePageAsync(url, actionSiteCode, startDate, endDate, page, token).ConfigureAwait(false);
                        if (!pr.Ok) { AppLogger.Warning($"[StockCheckSync] page={page} skipped: {pr.ErrorMessage}"); return; }
                        Absorb(pr.Codes);
                    }).ConfigureAwait(false);
            }
            else if (first.Codes.Count > 0 && totalPages < 0)
            {
                int page = 2;
                while (!ct.IsCancellationRequested)
                {
                    var pr = await FetchOnePageAsync(url, actionSiteCode, startDate, endDate, page, ct).ConfigureAwait(false);
                    if (!pr.Ok) { AppLogger.Warning($"[StockCheckSync] page={page} stop (fallback): {pr.ErrorMessage}"); break; }
                    if (pr.Codes.Count == 0) break;
                    Absorb(pr.Codes);
                    page++;
                }
            }

            AppLogger.Info($"[StockCheckSync] done, unique waybills={waybills.Count}");
            return waybills.ToList();
        }

        private sealed class PageFetch
        {
            public bool Ok;
            public string ErrorMessage;
            public List<string> Codes = new();
            public int Pages;
            public int Total;
        }

        private static object BuildPayload(int page, string actionSiteCode, string startDate, string endDate) =>
            new Dictionary<string, object>
            {
                { "current", page },
                { "size", PageSize },
                { "dimension", InventoryDimension },
                { "isFlag", "1" },
                { "actionSiteCode", actionSiteCode },
                { "startDate", startDate },
                { "endDate", endDate },
                { "countryId", "1" }
            };

        private static async Task<PageFetch> FetchOnePageAsync(string url, string actionSiteCode, string startDate, string endDate, int page, CancellationToken ct)
        {
            for (int retry = 1; retry <= MaxRetriesPerPage; retry++)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    var body = JsonSerializer.Serialize(BuildPayload(page, actionSiteCode, startDate, endDate), AppConfig.CreateJsonOptions());
                    using var res = await JmsApiClient.PostJsonAsync(
                        url, body,
                        routeName: StockCheckRouteName,
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
            foreach (var c in candidates)
                if (TryGetByPath(root, c, out var el) && el.ValueKind == JsonValueKind.Array) return el;
            return default;
        }

        private static int FindInt(JsonElement root, string[][] candidates)
        {
            foreach (var c in candidates)
                if (TryGetByPath(root, c, out var el) && el.ValueKind == JsonValueKind.Number && el.TryGetInt32(out var v)) return v;
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

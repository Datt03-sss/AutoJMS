using AutoJMS.Data;
using AutoJMS.FullStack.Models;
using AutoJMS.FullStack.Repositories;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace AutoJMS.FullStack.Services
{
    public sealed class FullStackInventorySyncService
    {
        private const int PageSize = 100;
        private const int MaxRetriesPerPage = 3;
        // Pages 2..N are fetched concurrently (page 1 first gives the total page count).
        private const int PageConcurrency = 5;
        private const string InventoryRouteName = "DetentionMonitoringDB";
        private const string InventoryRouterNameList = "%E7%BB%8F%E8%90%A5%E6%8C%87%E6%A0%87%3E%E6%B4%BE%E4%BB%B6%E7%AB%AF%3E%E7%95%99%E4%BB%93%E7%9B%91%E6%8E%A7DB";
        private const string InventoryDimension = "3";

        private readonly IFullStackWaybillRepository _repository;

        public FullStackInventorySyncService(IFullStackWaybillRepository repository)
        {
            _repository = repository;
        }

        public Task<FullStackSyncResult> SyncInventoryAsync(DateTime? startOverride = null, DateTime? endOverride = null, CancellationToken ct = default)
            => SyncInventoryAsync(startOverride, endOverride, null, ct);

        // onPageCodes (optional): invoked right after each inventory page is fetched, with the new
        // waybill codes found on that page. Lets a caller start tracking those codes immediately
        // (producer/consumer) while the next page is still downloading.
        public async Task<FullStackSyncResult> SyncInventoryAsync(
            DateTime? startOverride,
            DateTime? endOverride,
            Func<IReadOnlyList<string>, CancellationToken, Task> onPageCodes,
            CancellationToken ct = default)
        {
            DateTime startedAt = DateTime.UtcNow;
            DateTime endDate = endOverride ?? DateTime.Today.AddDays(1).AddSeconds(-1);
            DateTime startDate = startOverride ?? endDate.Date.AddDays(-30);
            string actionSiteCode = GetActionSiteCode();

            if (string.IsNullOrWhiteSpace(actionSiteCode) || actionSiteCode == "0000")
            {
                AppLogger.Warning("[FullStackSync] FULLSTACK_SYNC_PARAM_ERROR: actionSiteCode is missing or 0000.");
                return new FullStackSyncResult
                {
                    Success = false,
                    ErrorCode = "SITE_CODE_MISSING",
                    ErrorMessage = "Chưa có mã bưu cục để đồng bộ tồn kho.",
                    StartedAt = startedAt,
                    FinishedAt = DateTime.UtcNow
                };
            }

            if (!JmsAuthStateService.HasToken && !AuthStateService.Instance.IsAuthenticated)
            {
                AppLogger.Warning("[FullStackSync] FULLSTACK_SYNC_AUTH_ERROR: No authToken available.");
                return new FullStackSyncResult
                {
                    Success = false,
                    ErrorCode = "AUTH_ERROR",
                    ErrorMessage = "Đang chờ đăng nhập / authToken",
                    StartedAt = startedAt,
                    FinishedAt = DateTime.UtcNow
                };
            }

            string dbgTok = JmsAuthStateService.HasToken ? (JmsAuthStateService.CurrentToken ?? "") : "";
            string dbgTokMask = dbgTok.Length >= 8 ? $"{dbgTok.Substring(0, 4)}...{dbgTok.Substring(dbgTok.Length - 4)}" : (dbgTok.Length == 0 ? "<none>" : "<short>");
            AppLogger.Info($"[FullStackSync] inventory sync started actionSiteCode={actionSiteCode}, startDate={startDate:yyyy-MM-dd HH:mm:ss}, endDate={endDate:yyyy-MM-dd HH:mm:ss}, token={dbgTokMask}");

            var fetchResult = await FetchInventoryAsync(actionSiteCode, startDate, endDate, onPageCodes, ct).ConfigureAwait(false);

            if (!fetchResult.Success)
            {
                return new FullStackSyncResult
                {
                    Success = false,
                    IsNoData = fetchResult.IsNoData,
                    ErrorCode = fetchResult.ErrorCode,
                    ErrorMessage = fetchResult.ErrorMessage,
                    StartedAt = startedAt,
                    FinishedAt = DateTime.UtcNow
                };
            }

            var run = new InventoryRun
            {
                ActionSiteCode = actionSiteCode,
                StartDate = startDate,
                EndDate = endDate,
                StartedAt = startedAt,
                FinishedAt = DateTime.UtcNow,
                TotalPages = fetchResult.TotalPages,
                TotalRecords = fetchResult.Items.Count,
                Status = fetchResult.IsNoData ? "NO_DATA" : "SUCCESS",
                Source = "JMS",
                CreatedAt = DateTime.UtcNow
            };

            // If it's literally NO_DATA, we apply it so we record the run, but we may want to skip overwriting if the repo doesn't support NO_DATA clearing.
            // But applying the run with 0 items is fine. The repository should ideally handle NO_DATA smoothly.
            var result = await _repository.ApplyInventoryRunAsync(run, fetchResult.Items, ct).ConfigureAwait(false);
            result.Success = true;
            result.IsNoData = fetchResult.IsNoData;
            AppLogger.Info($"[FullStackSync] inventory sync finished runId={result.RunId}, fetched={result.TotalFetched}, new={result.NewWaybills}, still={result.StillInInventory}, left={result.LeftInventory}");

            return result;
        }

        private async Task<InventoryFetchResult> FetchInventoryAsync(string actionSiteCode, DateTime startDate, DateTime endDate, Func<IReadOnlyList<string>, CancellationToken, Task> onPageCodes, CancellationToken ct)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var items = new List<InventoryFetchItem>();
            var collectLock = new object();
            string url = AppConfig.Current.BuildJmsApiUrl("businessindicator/bigdataReport/detail/take_ret_mon_detail_doris2");
            string start = startDate.ToString("yyyy-MM-dd HH:mm:ss");
            string end = endDate.ToString("yyyy-MM-dd HH:mm:ss");

            // Collect new codes from a page under lock, then hand them to the consumer (tracking).
            async Task AbsorbPageAsync(int page, IReadOnlyList<string> waybills)
            {
                List<string> newCodes = new();
                lock (collectLock)
                {
                    foreach (var raw in waybills)
                    {
                        if (string.IsNullOrWhiteSpace(raw)) continue;
                        var wb = raw.Trim().ToUpperInvariant();
                        if (seen.Add(wb))
                        {
                            items.Add(new InventoryFetchItem { WaybillNo = wb, PageNo = page });
                            newCodes.Add(wb);
                        }
                    }
                }
                if (onPageCodes != null && newCodes.Count > 0)
                {
                    try { await onPageCodes(newCodes, ct).ConfigureAwait(false); }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception cbEx) { AppLogger.Warning($"[FullStackSync] onPageCodes callback error page={page}: {cbEx.Message}"); }
                }
            }

            // Page 1 first — it establishes total page count for parallel fetching.
            var first = await FetchOnePageAsync(url, actionSiteCode, start, end, 1, ct).ConfigureAwait(false);
            if (!first.Ok)
                return new InventoryFetchResult { Success = false, ErrorCode = first.ErrorCode, ErrorMessage = first.ErrorMessage };

            int totalRecords = first.Total;
            await AbsorbPageAsync(1, first.Waybills).ConfigureAwait(false);

            if (first.IsNoData || first.Waybills.Count == 0)
            {
                return new InventoryFetchResult
                {
                    Success = true,
                    IsNoData = items.Count == 0,
                    Items = items,
                    TotalPages = 1,
                    TotalRecords = totalRecords > 0 ? totalRecords : items.Count
                };
            }

            // Determine total pages: prefer reported pages, else derive from total, else -1 (unknown).
            int totalPages = first.Pages > 0
                ? first.Pages
                : (first.Total > 0 ? (int)Math.Ceiling(first.Total / (double)PageSize) : -1);

            if (totalPages > 1)
            {
                // Fetch pages 2..N concurrently (bounded), absorbing each as it arrives.
                var pageNumbers = Enumerable.Range(2, totalPages - 1).ToList();
                await Parallel.ForEachAsync(
                    pageNumbers,
                    new ParallelOptions { MaxDegreeOfParallelism = PageConcurrency, CancellationToken = ct },
                    async (page, token) =>
                    {
                        var pr = await FetchOnePageAsync(url, actionSiteCode, start, end, page, token).ConfigureAwait(false);
                        if (!pr.Ok)
                        {
                            // Don't abort the whole sync for one bad page — log and continue.
                            AppLogger.Warning($"[FullStackSync] page={page} skipped: {pr.ErrorCode}/{pr.ErrorMessage}");
                            return;
                        }
                        if (pr.Total > 0) System.Threading.Interlocked.Exchange(ref totalRecords, pr.Total);
                        await AbsorbPageAsync(page, pr.Waybills).ConfigureAwait(false);
                    }).ConfigureAwait(false);
            }
            else if (totalPages < 0)
            {
                // Unknown page count: fall back to sequential paging until an empty page.
                int page = 2;
                while (!ct.IsCancellationRequested)
                {
                    var pr = await FetchOnePageAsync(url, actionSiteCode, start, end, page, ct).ConfigureAwait(false);
                    if (!pr.Ok)
                    {
                        AppLogger.Warning($"[FullStackSync] page={page} stop (fallback): {pr.ErrorCode}/{pr.ErrorMessage}");
                        break;
                    }
                    if (pr.Waybills.Count == 0) break;
                    if (pr.Total > 0) totalRecords = pr.Total;
                    await AbsorbPageAsync(page, pr.Waybills).ConfigureAwait(false);
                    page++;
                }
            }

            AppLogger.Info($"[FullStackSync] fetch done pages~{Math.Max(totalPages, 1)} collected={items.Count} totalRecords={totalRecords}");
            return new InventoryFetchResult
            {
                Success = true,
                IsNoData = items.Count == 0,
                Items = items,
                TotalPages = totalPages > 0 ? totalPages : 1,
                TotalRecords = totalRecords > 0 ? totalRecords : items.Count
            };
        }

        private sealed class PageFetch
        {
            public bool Ok;
            public string ErrorCode;
            public string ErrorMessage;
            public List<string> Waybills = new();
            public int Pages;
            public int Total;
            public bool IsNoData;
        }

        // Fetches a single inventory page with retries. Returns the raw billcodes on the page
        // (deduplication is done by the caller under a lock).
        private async Task<PageFetch> FetchOnePageAsync(string url, string actionSiteCode, string start, string end, int page, CancellationToken ct)
        {
            for (int retry = 1; retry <= MaxRetriesPerPage; retry++)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    var payload = new Dictionary<string, object>
                    {
                        { "current", page },
                        { "size", PageSize },
                        { "dimension", InventoryDimension },
                        { "isFlag", "1" },
                        { "actionSiteCode", actionSiteCode },
                        { "startDate", start },
                        { "endDate", end },
                        { "countryId", "1" }
                    };
                    var body = JsonSerializer.Serialize(payload, AppConfig.CreateJsonOptions());

                    using var res = await JmsApiClient.PostJsonAsync(
                        url, body,
                        routeName: InventoryRouteName,
                        routerNameList: InventoryRouterNameList,
                        origin: "https://jms.jtexpress.vn",
                        ct: ct).ConfigureAwait(false);

                    if (res == null) throw new Exception("JMS API transport error (null response).");
                    if ((int)res.StatusCode == 401)
                        return new PageFetch { Ok = false, ErrorCode = "AUTH_ERROR", ErrorMessage = "JMS auth expired or invalid." };

                    var json = await res.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                    if (!res.IsSuccessStatusCode) throw new Exception($"HTTP {(int)res.StatusCode}: {Truncate(json, 200)}");
                    if (string.IsNullOrWhiteSpace(json)) throw new Exception("API JMS trả về body rỗng");

                    using var doc = JsonDocument.Parse(json);
                    var root = doc.RootElement;

                    int jsonCode = root.TryGetProperty("code", out var codeEl) && codeEl.ValueKind == JsonValueKind.Number && codeEl.TryGetInt32(out var cVal) ? cVal : -1;
                    string jsonMsg = root.TryGetProperty("msg", out var msgEl) && msgEl.ValueKind == JsonValueKind.String ? (msgEl.GetString() ?? "") : "";
                    bool succFlag = root.TryGetProperty("succ", out var succEl) && succEl.ValueKind == JsonValueKind.True;
                    if (jsonCode != 1 && !succFlag)
                        return new PageFetch { Ok = false, ErrorCode = "JMS_ERROR", ErrorMessage = string.IsNullOrEmpty(jsonMsg) ? $"JMS code={jsonCode}" : jsonMsg };

                    var recordsNode = FindRecordsArray(root, out _);
                    int detectedTotal = FindTotal(root, out _);
                    int detectedPages = FindPages(root);
                    var result = new PageFetch { Ok = true, Pages = detectedPages, Total = detectedTotal };

                    if (recordsNode.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var record in recordsNode.EnumerateArray())
                        {
                            var waybill = ExtractBillcode(record);
                            if (!string.IsNullOrWhiteSpace(waybill)) result.Waybills.Add(waybill);
                        }
                    }
                    else if (detectedTotal > 0)
                    {
                        return new PageFetch { Ok = false, ErrorCode = "PARSER_ERROR", ErrorMessage = "Có dữ liệu nhưng không tìm thấy mảng records." };
                    }
                    else
                    {
                        result.IsNoData = true;
                    }

                    AppLogger.Info($"[FullStackSync] page={page} records={result.Waybills.Count} pages={detectedPages} total={detectedTotal}");
                    return result;
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    if (retry >= MaxRetriesPerPage)
                    {
                        AppLogger.Error($"[FullStackSync] FULLSTACK_SYNC_HTTP_ERROR: page={page} failed after retry={retry}", ex);
                        return new PageFetch { Ok = false, ErrorCode = "HTTP_ERROR", ErrorMessage = ex.Message };
                    }
                    var delay = TimeSpan.FromSeconds(Math.Min(10, retry * 2));
                    AppLogger.Warning($"[FullStackSync] page={page} retry={retry}/{MaxRetriesPerPage} after {delay.TotalSeconds}s: {ex.Message}");
                    await Task.Delay(delay, ct).ConfigureAwait(false);
                }
            }
            return new PageFetch { Ok = false, ErrorCode = "HTTP_ERROR", ErrorMessage = "exhausted retries" };
        }

        private static IEnumerable<string> EnumerateKeys(JsonElement obj)
        {
            if (obj.ValueKind != JsonValueKind.Object) yield break;
            foreach (var p in obj.EnumerateObject()) yield return p.Name;
        }

        private static JsonElement FindRecordsArray(JsonElement root, out string path)
        {
            string[][] candidates =
            {
                new[] { "data", "records" },
                new[] { "data", "list" },
                new[] { "data", "rows" },
                new[] { "data", "data" },
                new[] { "data", "result" },
                new[] { "records" },
                new[] { "rows" },
                new[] { "list" },
                new[] { "data" }
            };
            foreach (var c in candidates)
            {
                if (TryGetByPath(root, c, out var el) && el.ValueKind == JsonValueKind.Array)
                {
                    path = string.Join(".", c);
                    return el;
                }
            }
            path = "(none)";
            return default;
        }

        private static int FindTotal(JsonElement root, out string path)
        {
            string[][] candidates =
            {
                new[] { "data", "total" },
                new[] { "data", "totalRecords" },
                new[] { "data", "recordsTotal" },
                new[] { "data", "count" },
                new[] { "total" },
                new[] { "totalRecords" }
            };
            foreach (var c in candidates)
            {
                if (TryGetByPath(root, c, out var el) && el.ValueKind == JsonValueKind.Number && el.TryGetInt32(out var v))
                {
                    path = string.Join(".", c);
                    return v;
                }
            }
            path = "(none)";
            return 0;
        }

        private static int FindPages(JsonElement root)
        {
            string[][] candidates =
            {
                new[] { "data", "pages" },
                new[] { "data", "totalPage" },
                new[] { "data", "totalPages" },
                new[] { "pages" },
                new[] { "totalPages" }
            };
            foreach (var c in candidates)
            {
                if (TryGetByPath(root, c, out var el) && el.ValueKind == JsonValueKind.Number && el.TryGetInt32(out var v))
                    return v;
            }
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
            string[] fields = { "billcode", "billCode", "waybillNo", "waybill_no", "mailNo" };
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

        private static string GetActionSiteCode()
        {
            if (!string.IsNullOrWhiteSpace(AppConfig.Current.ActionSiteCode))
                return AppConfig.Current.ActionSiteCode.Trim();
            return "214A02";
        }

        private static string Truncate(string text, int max)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;
            return text.Length <= max ? text : text.Substring(0, max);
        }
    }
}

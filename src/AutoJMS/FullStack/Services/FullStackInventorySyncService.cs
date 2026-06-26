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
        private const string InventoryRouteName = "DetentionMonitoringDB";
        private const string InventoryRouterNameList = "%E7%BB%8F%E8%90%A5%E6%8C%87%E6%A0%87%3E%E6%B4%BE%E4%BB%B6%E7%AB%AF%3E%E7%95%99%E4%BB%93%E7%9B%91%E6%8E%A7DB";
        private const string InventoryDimension = "3";

        private readonly IFullStackWaybillRepository _repository;

        public FullStackInventorySyncService(IFullStackWaybillRepository repository)
        {
            _repository = repository;
        }

        public async Task<FullStackSyncResult> SyncInventoryAsync(DateTime? startOverride = null, DateTime? endOverride = null, CancellationToken ct = default)
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

            var fetchResult = await FetchInventoryAsync(actionSiteCode, startDate, endDate, ct).ConfigureAwait(false);

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

        private async Task<InventoryFetchResult> FetchInventoryAsync(string actionSiteCode, DateTime startDate, DateTime endDate, CancellationToken ct)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var items = new List<InventoryFetchItem>();
            string url = AppConfig.Current.BuildJmsApiUrl("businessindicator/bigdataReport/detail/take_ret_mon_detail_doris2");
            string start = startDate.ToString("yyyy-MM-dd HH:mm:ss");
            string end = endDate.ToString("yyyy-MM-dd HH:mm:ss");
            int currentPage = 1;
            int? totalPages = null;
            int totalRecords = 0;
            bool isNoData = false;

            while (!ct.IsCancellationRequested)
            {
                bool pageSuccess = false;
                int recordsOnPage = 0;
                int pagesOnPage = 0;

                for (int retry = 1; retry <= MaxRetriesPerPage && !pageSuccess; retry++)
                {
                    ct.ThrowIfCancellationRequested();
                    try
                    {
                        var payload = new Dictionary<string, object>
                        {
                            { "current", currentPage },
                            { "size", PageSize },
                            { "dimension", InventoryDimension },
                            { "isFlag", "1" },
                            { "actionSiteCode", actionSiteCode },
                            { "startDate", start },
                            { "endDate", end },
                            { "countryId", "1" }
                        };

                        var body = JsonSerializer.Serialize(payload, AppConfig.CreateJsonOptions());

                        string dbgTok = JmsAuthStateService.HasToken ? (JmsAuthStateService.CurrentToken ?? "") : "";
                        string dbgTokMask = dbgTok.Length >= 8 ? $"{dbgTok.Substring(0, 4)}...{dbgTok.Substring(dbgTok.Length - 4)}" : (dbgTok.Length == 0 ? "<none>" : "<short>");

                        AppLogger.Info($"[FullStackSync] endpoint={url}");
                        AppLogger.Info($"[FullStackSync] method=POST");
                        AppLogger.Info($"[FullStackSync] actionSiteCode={actionSiteCode}");
                        AppLogger.Info($"[FullStackSync] startDate={start}");
                        AppLogger.Info($"[FullStackSync] endDate={end}");
                        AppLogger.Info($"[FullStackSync] page={currentPage}");
                        AppLogger.Info($"[FullStackSync] pageSize={PageSize}");
                        AppLogger.Info($"[FullStackSync] dimension={InventoryDimension}");
                        AppLogger.Info($"[FullStackSync] isFlag=1");
                        AppLogger.Info($"[FullStackSync] tokenSource=memory/webview/config");
                        AppLogger.Info($"[FullStackSync] tokenPresent={!string.IsNullOrEmpty(dbgTok)}");

                        using var res = await JmsApiClient.PostJsonAsync(
                            url,
                            body,
                            routeName: InventoryRouteName,
                            routerNameList: InventoryRouterNameList,
                            origin: "https://jms.jtexpress.vn",
                            ct: ct).ConfigureAwait(false);

                        if (res == null)
                        {
                            AppLogger.Error("[FullStackSync] FULLSTACK_SYNC_HTTP_ERROR: null response.");
                            throw new Exception("JMS API transport error (null response).");
                        }

                        AppLogger.Info($"[FullStackSync] httpStatus={(int)res.StatusCode}");

                        if ((int)res.StatusCode == 401)
                        {
                            AppLogger.Warning("[FullStackSync] FULLSTACK_SYNC_AUTH_ERROR: 401 Unauthorized.");
                            return new InventoryFetchResult { Success = false, ErrorCode = "AUTH_ERROR", ErrorMessage = "JMS auth expired or invalid." };
                        }

                        var json = await res.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

                        AppLogger.Info($"[FullStackSync] rawLength={json.Length}");
                        AppLogger.Info($"[FullStackSync] rawFirst500={Truncate(json, 500)}");

                        if (!res.IsSuccessStatusCode)
                        {
                            AppLogger.Error($"[FullStackSync] FULLSTACK_SYNC_HTTP_ERROR: HTTP {(int)res.StatusCode}");
                            throw new Exception($"HTTP {(int)res.StatusCode}: {Truncate(json, 300)}");
                        }

                        if (string.IsNullOrWhiteSpace(json))
                        {
                            AppLogger.Error("[FullStackSync] FULLSTACK_SYNC_HTTP_ERROR: empty body");
                            throw new Exception("API JMS trả về body rỗng");
                        }

                        using var doc = JsonDocument.Parse(json);
                        var root = doc.RootElement;

                        int jsonCode = root.TryGetProperty("code", out var codeEl) && codeEl.ValueKind == JsonValueKind.Number && codeEl.TryGetInt32(out var cVal) ? cVal : -1;
                        string jsonMsg = root.TryGetProperty("msg", out var msgEl) && msgEl.ValueKind == JsonValueKind.String ? (msgEl.GetString() ?? "") : "";
                        bool succFlag = root.TryGetProperty("succ", out var succEl) && succEl.ValueKind == JsonValueKind.True;
                        AppLogger.Info($"[FullStackSync] json.code={jsonCode}");
                        AppLogger.Info($"[FullStackSync] json.msg={jsonMsg}");

                        if (jsonCode != 1 && !succFlag)
                        {
                            AppLogger.Error($"[FullStackSync] FULLSTACK_SYNC_JMS_ERROR: code={jsonCode}, msg={jsonMsg}");
                            return new InventoryFetchResult { Success = false, ErrorCode = "JMS_ERROR", ErrorMessage = string.IsNullOrEmpty(jsonMsg) ? $"JMS code={jsonCode}" : jsonMsg };
                        }

                        var recordsNode = FindRecordsArray(root, out string detectedRecordsPath);
                        int detectedTotal = FindTotal(root, out string detectedTotalPath);
                        int detectedPages = FindPages(root);
                        AppLogger.Info($"[FullStackSync] detectedRecordsPath={detectedRecordsPath}");
                        AppLogger.Info($"[FullStackSync] detectedTotalPath={detectedTotalPath}");

                        if (recordsNode.ValueKind == JsonValueKind.Array)
                        {
                            recordsOnPage = recordsNode.GetArrayLength();
                            foreach (var record in recordsNode.EnumerateArray())
                            {
                                var waybill = ExtractBillcode(record);
                                if (string.IsNullOrWhiteSpace(waybill)) continue;
                                waybill = waybill.Trim().ToUpperInvariant();
                                if (seen.Add(waybill))
                                    items.Add(new InventoryFetchItem { WaybillNo = waybill, PageNo = currentPage });
                            }
                        }
                        else if (detectedTotal > 0)
                        {
                            AppLogger.Error($"[FullStackSync] FULLSTACK_SYNC_PARSER_ERROR: total={detectedTotal} but no records array. keys=[{string.Join(",", EnumerateKeys(root))}]");
                            return new InventoryFetchResult { Success = false, ErrorCode = "PARSER_ERROR", ErrorMessage = "Có dữ liệu nhưng không tìm thấy mảng records." };
                        }
                        else
                        {
                            AppLogger.Warning("[FullStackSync] FULLSTACK_SYNC_NO_DATA: total=0 and no records.");
                            isNoData = true;
                            recordsOnPage = 0;
                        }

                        if (detectedTotal > 0) totalRecords = detectedTotal;
                        pagesOnPage = detectedPages;

                        AppLogger.Info($"[FullStackSync] page={currentPage} records={recordsOnPage} pages={pagesOnPage} collected={items.Count}");
                        Debug.WriteLine($"[FullStackSync] page={currentPage}, records={recordsOnPage}, pages={pagesOnPage}, collected={items.Count}");
                        pageSuccess = true;
                    }
                    catch (Exception ex)
                    {
                        if (retry >= MaxRetriesPerPage)
                        {
                            AppLogger.Error($"[FullStackSync] FULLSTACK_SYNC_HTTP_ERROR: page={currentPage} failed after retry={retry}", ex);
                            return new InventoryFetchResult { Success = false, ErrorCode = "HTTP_ERROR", ErrorMessage = ex.Message };
                        }

                        var delay = TimeSpan.FromSeconds(Math.Min(10, retry * 2));
                        AppLogger.Warning($"[FullStackSync] page={currentPage} retry={retry}/{MaxRetriesPerPage} after {delay.TotalSeconds}s: {ex.Message}");
                        await Task.Delay(delay, ct).ConfigureAwait(false);
                    }
                }

                if (pageSuccess)
                {
                    if (recordsOnPage == 0) break;
                    if (totalPages == null && pagesOnPage > 0) totalPages = pagesOnPage;
                    if (totalPages.HasValue && currentPage >= totalPages.Value) break;
                }

                currentPage++;
                await Task.Delay(250, ct).ConfigureAwait(false);
            }

            return new InventoryFetchResult
            {
                Success = true,
                IsNoData = isNoData || items.Count == 0,
                Items = items,
                TotalPages = totalPages ?? currentPage,
                TotalRecords = totalRecords > 0 ? totalRecords : items.Count
            };
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

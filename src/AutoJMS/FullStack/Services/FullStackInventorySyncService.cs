using AutoJMS.Data;
using AutoJMS.FullStack.Models;
using AutoJMS.FullStack.Repositories;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
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
            if (!JmsAuthStateService.HasToken && !AuthStateService.Instance.IsAuthenticated)
                throw new InvalidOperationException("Đang chờ đăng nhập / authToken");

            DateTime startedAt = DateTime.UtcNow;
            DateTime endDate = endOverride ?? DateTime.Today.AddDays(1).AddSeconds(-1);
            DateTime startDate = startOverride ?? endDate.Date.AddDays(-30);
            string actionSiteCode = GetActionSiteCode();

            string dbgTok = JmsAuthStateService.HasToken ? (JmsAuthStateService.CurrentToken ?? "") : "";
            string dbgTokMask = dbgTok.Length >= 8 ? $"{dbgTok.Substring(0, 4)}...{dbgTok.Substring(dbgTok.Length - 4)}" : (dbgTok.Length == 0 ? "<none>" : "<short>");
            AppLogger.Info($"[FullStackSync] inventory sync started actionSiteCode={actionSiteCode}, startDate={startDate:yyyy-MM-dd HH:mm:ss}, endDate={endDate:yyyy-MM-dd HH:mm:ss}, token={dbgTokMask}");

            var fetchResult = await FetchInventoryAsync(actionSiteCode, startDate, endDate, ct).ConfigureAwait(false);
            var run = new InventoryRun
            {
                ActionSiteCode = actionSiteCode,
                StartDate = startDate,
                EndDate = endDate,
                StartedAt = startedAt,
                FinishedAt = DateTime.UtcNow,
                TotalPages = fetchResult.TotalPages,
                TotalRecords = fetchResult.Items.Count,
                Status = "SUCCESS",
                Source = "JMS",
                CreatedAt = DateTime.UtcNow
            };

            var result = await _repository.ApplyInventoryRunAsync(run, fetchResult.Items, ct).ConfigureAwait(false);
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
                        AppLogger.Info($"[FullStackSync] REQUEST page={currentPage} url={url} body={body}");

                        using var res = await JmsApiClient.PostJsonAsync(
                            url,
                            body,
                            routeName: InventoryRouteName,
                            routerNameList: InventoryRouterNameList,
                            origin: "https://jms.jtexpress.vn",
                            ct: ct).ConfigureAwait(false);

                        if (res == null) throw new Exception("JMS API transport error (null response).");
                        if ((int)res.StatusCode == 401) throw new UnauthorizedAccessException("JMS auth expired.");

                        var json = await res.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                        AppLogger.Info($"[FullStackSync] RESPONSE page={currentPage} status={(int)res.StatusCode} body={Truncate(json, 1000)}");
                        if (!res.IsSuccessStatusCode)
                            throw new Exception($"HTTP {(int)res.StatusCode}: {Truncate(json, 300)}");
                        if (string.IsNullOrWhiteSpace(json))
                            throw new Exception("API JMS trả về body rỗng");

                        using var doc = JsonDocument.Parse(json);
                        var root = doc.RootElement;
                        if (!root.TryGetProperty("succ", out var succ) || !succ.GetBoolean())
                            throw new Exception($"API JMS trả về succ=false: {Truncate(json, 300)}");
                        if (!root.TryGetProperty("data", out var data) || !data.TryGetProperty("records", out var recordsNode))
                            throw new Exception("API JMS không có trường data.records");

                        recordsOnPage = recordsNode.ValueKind == JsonValueKind.Array ? recordsNode.GetArrayLength() : 0;
                        totalRecords = data.TryGetProperty("total", out var totalProp) && totalProp.TryGetInt32(out var total) ? total : totalRecords;
                        pagesOnPage = data.TryGetProperty("pages", out var pagesProp) && pagesProp.TryGetInt32(out var pages) ? pages : 0;

                        foreach (var record in recordsNode.EnumerateArray())
                        {
                            var waybill = ExtractBillcode(record);
                            if (string.IsNullOrWhiteSpace(waybill)) continue;
                            waybill = waybill.Trim().ToUpperInvariant();
                            if (seen.Add(waybill))
                            {
                                items.Add(new InventoryFetchItem { WaybillNo = waybill, PageNo = currentPage });
                            }
                        }

                        AppLogger.Info($"[FullStackSync] page={currentPage}, records={recordsOnPage}, pages={pagesOnPage}, collected={items.Count}");
                        Debug.WriteLine($"[FullStackSync] page={currentPage}, records={recordsOnPage}, pages={pagesOnPage}, collected={items.Count}");
                        pageSuccess = true;
                    }
                    catch (Exception ex)
                    {
                        if (retry >= MaxRetriesPerPage)
                        {
                            AppLogger.Error($"[FullStackSync] failed page={currentPage} after retry={retry}", ex);
                            throw;
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
                Items = items,
                TotalPages = totalPages ?? currentPage,
                TotalRecords = totalRecords > 0 ? totalRecords : items.Count
            };
        }

        private static string ExtractBillcode(JsonElement record)
        {
            if (record.ValueKind != JsonValueKind.Object) return null;
            if (!record.TryGetProperty("billcode", out var billcodeProp)) return null;
            return billcodeProp.GetString()?.Trim();
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

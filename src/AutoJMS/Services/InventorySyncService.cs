using AutoJMS.Data;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.IO;
using System.Windows.Forms;

namespace AutoJMS
{
    public static class InventorySyncService
    {
        private const int LeaseSeconds = 1800; // 30 phút
        private const int PageSize = 100;
        private const int MaxRetriesPerPage = 3;
        private const string InventoryRouteName = "DetentionMonitoringDB";
        private const string InventoryRouterNameList = "%E7%BB%8F%E8%90%A5%E6%8C%87%E6%A0%87%3E%E6%B4%BE%E4%BB%B6%E7%AB%AF%3E%E7%95%99%E4%BB%93%E7%9B%91%E6%8E%A7DB";
        private const string InventoryDimension = "3";

        private static string GetActionSiteCode()
        {
            if (!string.IsNullOrWhiteSpace(AppConfig.Current.ActionSiteCode))
                return AppConfig.Current.ActionSiteCode.Trim();
            return "214A02";
        }

        public static async Task<List<string>> FetchInventoryWaybillsManualAsync(CancellationToken ct = default)
        {
            Debug.WriteLine("[InventorySync] Manual fetch bắt đầu.");
            if (!JmsAuthStateService.HasToken)
            {
                Debug.WriteLine("[InventorySync] Không thể lấy tồn kho vì chưa có AuthToken.");
                AppLogger.Info("[InventorySync] Không thể lấy tồn kho vì chưa có AuthToken.");
                return new List<string>();
            }

            var waybills = await FetchAllInventoryWaybillsWithRetryAsync(ct);
            Debug.WriteLine($"[InventorySync] Manual fetch xong, count={waybills.Count}");
            await DumpBillcodesToFileAsync(waybills, ct);
            return waybills;
        }

        public static async Task RunInventorySyncAsync(CancellationToken ct = default)
        {
            if (!JmsAuthStateService.HasToken)
            {
                AppLogger.Info("[InventorySync] Bỏ qua vì chưa có AuthToken.");
                return;
            }

            AppLogger.Info("[InventorySync] Kiểm tra Lease Lock...");

            // 1. Thử xin quyền (Lock) từ Supabase Database
            bool acquired = await SupabaseDbService.TryAcquireInventoryLeaseAsync(LeaseSeconds);
            if (!acquired)
            {
                AppLogger.Info("[InventorySync] Máy khác đang giữ quyền sync tồn kho.");
                return;
            }

            AppLogger.Info("[InventorySync] Đã chiếm quyền. Bắt đầu kéo tồn kho JMS.");

            // 2. Kích hoạt luồng Heartbeat để liên tục gia hạn Lock khi đang chạy
            using var heartbeatCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            var heartbeatTask = Task.Run(() => LeaseHeartbeatLoopAsync(heartbeatCts.Token), heartbeatCts.Token);

            bool success = false;

            try
            {
                // 3. Kéo toàn bộ danh sách Tồn kho (Có cơ chế Retry nếu rớt mạng)
                List<string> inventoryWaybills = await FetchAllInventoryWaybillsWithRetryAsync(ct);

                if (inventoryWaybills.Count > 0 && !ct.IsCancellationRequested)
                {
                    await DumpBillcodesToFileAsync(inventoryWaybills, ct);
                    // 4. Bơm danh sách lên Database (DB sẽ tự động chỉ thêm mã mới bằng ON CONFLICT DO NOTHING)
                    int inserted = await SupabaseDbService.UpsertNewWaybillsOnlyAsync(inventoryWaybills);
                    AppLogger.Info($"[InventorySync] Hoàn tất. Lấy {inventoryWaybills.Count} mã, thêm mới {inserted} mã.");
                }

                success = true;
            }
            catch (Exception ex)
            {
                AppLogger.Error("[InventorySync] Lỗi kéo tồn kho", ex);
            }
            finally
            {
                // 5. Ngắt luồng Heartbeat
                heartbeatCts.Cancel();
                try 
                { 
                    await heartbeatTask; 
                } 
                catch { }

                // 6. Trả lại Lock cho hệ thống
                if (success)
                {
                    await SupabaseDbService.CompleteInventorySyncAsync();
                }
                else
                {
                    await SupabaseDbService.ReleaseInventoryLeaseAsync();
                }
            }
        }

        private static async Task LeaseHeartbeatLoopAsync(CancellationToken ct)
        {
            try
            {
                while (!ct.IsCancellationRequested)
                {
                    // Đợi 1 phút rồi gia hạn Lock một lần
                    await Task.Delay(TimeSpan.FromMinutes(1), ct);
                    
                    if (ct.IsCancellationRequested) break;

                    await SupabaseDbService.RefreshInventoryLeaseAsync(LeaseSeconds);
                }
            }
            catch (OperationCanceledException)
            {
                // Bỏ qua lỗi khi bị hủy task
            }
            catch (Exception ex)
            {
                AppLogger.Warning($"[InventorySync] Heartbeat lỗi: {ex.Message}");
            }
        }

        private static async Task<List<string>> FetchAllInventoryWaybillsWithRetryAsync(CancellationToken ct)
        {
            var waybills = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var firstSeenPageByBillcode = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            string url = AppConfig.Current.BuildJmsApiUrl("businessindicator/bigdataReport/detail/take_ret_mon_detail_doris2");
            string actionSiteCode = GetActionSiteCode();
            string startDate = DateTime.Now.AddMonths(-1).ToString("yyyy-MM-dd 00:00:00");
            string endDate = DateTime.Now.ToString("yyyy-MM-dd 23:59:59");
            int currentPage = 1;
            int? firstTotalPages = null;

            AppLogger.Info($"[InventorySync] Start fetch. actionSiteCode={actionSiteCode}, startDate={startDate}, endDate={endDate}, pageSize={PageSize}, dimension={InventoryDimension}, isFlag=1");
            Debug.WriteLine($"[InventorySync] Start fetch. actionSiteCode={actionSiteCode}, startDate={startDate}, endDate={endDate}, pageSize={PageSize}, dimension={InventoryDimension}, isFlag=1");

            while (!ct.IsCancellationRequested)
            {
                bool pageSuccess = false;
                int recordsOnPage = 0;
                int totalOnPage = 0;
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
                            { "startDate", startDate },
                            { "endDate", endDate },
                            { "countryId", "1" }
                        };

                        using var res = await JmsApiClient.PostJsonAsync(
                            url,
                            JsonSerializer.Serialize(payload, AppConfig.CreateJsonOptions()),
                            routeName: InventoryRouteName,
                            routerNameList: InventoryRouterNameList,
                            origin: "https://jms.jtexpress.vn",
                            ct: ct);
                        if (res == null) throw new Exception("JMS API transport error (null response).");
                        if ((int)res.StatusCode == 401) throw new UnauthorizedAccessException("JMS auth expired.");
                        var json = await res.Content.ReadAsStringAsync(ct);

                        AppLogger.Info($"[InventorySync] Page={currentPage}, HTTP={(int)res.StatusCode}, BodyLen={(json?.Length ?? 0)}");
                        Debug.WriteLine($"[InventorySync] Page={currentPage}, HTTP={(int)res.StatusCode}, BodyLen={(json?.Length ?? 0)}");

                        if (!res.IsSuccessStatusCode)
                        {
                            throw new Exception($"HTTP {(int)res.StatusCode}: {Truncate(json, 300)}");
                        }

                        if (string.IsNullOrWhiteSpace(json))
                            throw new Exception("API JMS trả về body rỗng");

                        using var doc = JsonDocument.Parse(json);
                        var root = doc.RootElement;

                        if (!root.TryGetProperty("succ", out var succ) || !succ.GetBoolean())
                        {
                            throw new Exception($"API JMS trả về succ=false: {Truncate(json, 300)}");
                        }

                        if (!root.TryGetProperty("data", out var data) || !data.TryGetProperty("records", out var recordsNode))
                            throw new Exception("API JMS không có trường data.records");

                        recordsOnPage = recordsNode.ValueKind == JsonValueKind.Array ? recordsNode.GetArrayLength() : 0;
                        totalOnPage = data.TryGetProperty("total", out var totalProp) && totalProp.TryGetInt32(out var total) ? total : 0;
                        pagesOnPage = data.TryGetProperty("pages", out var pagesProp) && pagesProp.TryGetInt32(out var pages) ? pages : 0;

                        var records = recordsNode.EnumerateArray().ToList();
                        foreach (var record in records)
                        {
                            ExtractBillcodes(record, currentPage, waybills, firstSeenPageByBillcode);
                        }

                        AppLogger.Info($"[InventorySync] Page {currentPage}: records={recordsOnPage}, total={totalOnPage}, pages={pagesOnPage}, collected={waybills.Count}");
                        Debug.WriteLine($"[InventorySync] Page {currentPage}: records={recordsOnPage}, total={totalOnPage}, pages={pagesOnPage}, collected={waybills.Count}");
                        pageSuccess = true;
                    }
                    catch (Exception ex)
                    {
                        if (retry >= MaxRetriesPerPage)
                        {
                            AppLogger.Error($"[InventorySync] Dừng sync do không thể lấy trang {currentPage} sau {retry} lần thử.", ex);
                            throw new Exception($"Failed to fetch inventory page {currentPage} after {MaxRetriesPerPage} retries.", ex);
                        }

                        var delay = TimeSpan.FromSeconds(Math.Min(10, retry * 2));
                        AppLogger.Warning($"[InventorySync] Lỗi trang {currentPage}. Thử lại {retry}/{MaxRetriesPerPage} sau {delay.TotalSeconds}s: {ex.Message}");
                        await Task.Delay(delay, ct);
                    }
                }

                if (pageSuccess)
                {
                    if (recordsOnPage == 0)
                    {
                        AppLogger.Info($"[InventorySync] Trang {currentPage} rỗng, dừng.");
                        break;
                    }

                    if (firstTotalPages == null && pagesOnPage > 0)
                    {
                        firstTotalPages = pagesOnPage;
                        AppLogger.Info($"[InventorySync] API báo total={totalOnPage}, pages={pagesOnPage}, pageSize={PageSize}");
                    }

                    if (currentPage >= 1 && firstTotalPages.HasValue && currentPage >= firstTotalPages.Value)
                    {
                        AppLogger.Info($"[InventorySync] Đã đến trang cuối theo API pages={firstTotalPages.Value}.");
                        break;
                    }
                }

                currentPage++;
                await Task.Delay(250, ct);
            }

            AppLogger.Info($"[InventorySync] Hoàn tất quét tồn kho. Tổng mã lấy được: {waybills.Count}");
            AppLogger.Info($"[InventorySync] Tổng billcode unique={waybills.Count}");
            Debug.WriteLine($"[InventorySync] Hoàn tất quét tồn kho. Tổng mã lấy được: {waybills.Count}");
            return waybills.ToList();
        }

        private static void ExtractBillcodes(JsonElement record, int pageNumber, HashSet<string> waybills, Dictionary<string, int> firstSeenPageByBillcode)
        {
            if (record.ValueKind != JsonValueKind.Object)
                return;

            if (!record.TryGetProperty("billcode", out var billcodeProp))
                return;

            var billcode = billcodeProp.GetString()?.Trim();
            if (string.IsNullOrWhiteSpace(billcode))
                return;

            if (waybills.Add(billcode))
            {
                firstSeenPageByBillcode[billcode] = pageNumber;
                return;
            }

            if (!firstSeenPageByBillcode.TryGetValue(billcode, out var firstPage))
            {
                firstPage = -1;
            }

            AppLogger.Warning($"[InventorySync][DUP] billcode={billcode} xuất hiện lại ở page={pageNumber}, trang đầu tiên={firstPage}");
            Debug.WriteLine($"[InventorySync][DUP] billcode={billcode} xuất hiện lại ở page={pageNumber}, trang đầu tiên={firstPage}");
        }

        private static async Task DumpBillcodesToFileAsync(IEnumerable<string> billcodes, CancellationToken ct)
        {
            try
            {
                var dir = Path.Combine(AppPaths.DownloadsDir, "InventoryDebug");
                Directory.CreateDirectory(dir);

                var fileName = $"inventory_billcodes_{DateTime.Now:yyyyMMdd_HHmmss}.txt";
                var path = Path.Combine(dir, fileName);

                var unique = billcodes?.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList() ?? new List<string>();
                await File.WriteAllLinesAsync(path, unique, Encoding.UTF8, ct);
                AppLogger.Info($"[InventorySync] Đã xuất billcode ra file: {path}");
            }
            catch (Exception ex)
            {
                AppLogger.Warning($"[InventorySync] Không thể xuất billcode ra file: {ex.Message}");
            }
        }

        private static string Truncate(string text, int max)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;
            return text.Length <= max ? text : text.Substring(0, max);
        }
    }
}



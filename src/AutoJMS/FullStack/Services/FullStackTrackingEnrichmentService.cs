using AutoJMS.Data;
using AutoJMS.FullStack.Models;
using AutoJMS.FullStack.Repositories;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace AutoJMS.FullStack.Services
{
    public sealed class FullStackTrackingEnrichmentService
    {
        private const int BatchSize = 40;
        // Order-detail is 1 HTTP request per waybill and is the dominant cost of a sync.
        // Run it (and the bulk tracking calls) up to 8-wide to match the real JMS console speed.
        private const int MaxDegreeOfParallelism = 8;
        private static readonly JsonSerializerOptions JsonOptions = AppConfig.CreateJsonOptions();

        private readonly IFullStackWaybillRepository _repository;

        public FullStackTrackingEnrichmentService(IFullStackWaybillRepository repository)
        {
            _repository = repository;
        }

        public async Task EnrichAsync(IEnumerable<string> waybills, CancellationToken ct = default)
        {
            await EnrichWithResultAsync(waybills, ct).ConfigureAwait(false);
        }

        public async Task<IReadOnlyList<FullStackJourneyResult>> EnrichWithResultAsync(IEnumerable<string> waybills, CancellationToken ct = default)
        {
            var list = waybills?
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim().ToUpperInvariant())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList() ?? new List<string>();
            if (list.Count == 0) return Array.Empty<FullStackJourneyResult>();
            if (!JmsAuthStateService.HasToken && !AuthStateService.Instance.IsAuthenticated)
                throw new InvalidOperationException("Đang chờ đăng nhập / authToken");

            AppLogger.Info($"[FullStackTracking] enrichment started count={list.Count}");

            var dict = new ConcurrentDictionary<string, WaybillDbModel>(StringComparer.OrdinalIgnoreCase);
            var eventDict = new ConcurrentDictionary<string, List<TrackingEvent>>(StringComparer.OrdinalIgnoreCase);
            var queryCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var aliasMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var allCodes = ExpandWaybillCandidates(list);

            foreach (var wb in allCodes)
            {
                ct.ThrowIfCancellationRequested();
                queryCodes.Add(wb);
                dict[wb] = CreateEmptyRow(wb);

                int dash = wb.IndexOf('-');
                if (dash > 0)
                {
                    string root = wb[..dash].Trim();
                    if (!string.IsNullOrWhiteSpace(root))
                    {
                        queryCodes.Add(root);
                        aliasMap[wb] = root;
                        if (!dict.ContainsKey(root))
                            dict[root] = CreateEmptyRow(root);
                    }
                }
            }

            var codeList = queryCodes.ToList();

            // Stage 1 — bulk tracking status (up to 40 waybills per request), parallel across batches.
            // This is what drives the dashboard queues, so it lands first and fast.
            var batches = codeList.Chunk(BatchSize).ToList();
            await Parallel.ForEachAsync(
                batches,
                new ParallelOptions { MaxDegreeOfParallelism = MaxDegreeOfParallelism, CancellationToken = ct },
                async (batch, token) =>
                {
                    await ProcessTrackingBatchAsync(batch.ToArray(), dict, eventDict, token).ConfigureAwait(false);
                }).ConfigureAwait(false);

            // Cache-once: order detail (sender/COD/weight/address/mã đoạn) is static per waybill, so
            // only fetch it for codes not already cached in the DB. Re-syncs of the same inventory then
            // cost just the bulk tracking calls above. Cached detail is preserved on merge (see Keep()).
            var cached = await _repository.GetWaybillsWithDetailAsync(codeList, ct).ConfigureAwait(false);
            var detailCodes = codeList.Where(c => !cached.Contains(c)).ToList();
            AppLogger.Info($"[FullStackTracking] order-detail fetch={detailCodes.Count} new, skipped={cached.Count} cached");

            // Stage 2 — order detail (1 request per waybill, the bottleneck), fully parallel across
            // the new codes at the full degree of parallelism instead of serially within a batch.
            await Parallel.ForEachAsync(
                detailCodes,
                new ParallelOptions { MaxDegreeOfParallelism = MaxDegreeOfParallelism, CancellationToken = ct },
                async (code, token) =>
                {
                    await CallOrderDetailAsync(code, dict, token).ConfigureAwait(false);
                }).ConfigureAwait(false);

            foreach (var kv in aliasMap)
            {
                if (dict.TryGetValue(kv.Value, out var src) && dict.TryGetValue(kv.Key, out var dst))
                {
                    dst.NhanVienNhanHang = src.NhanVienNhanHang ?? dst.NhanVienNhanHang;
                    dst.TenNguoiGui = src.TenNguoiGui ?? dst.TenNguoiGui;
                    dst.DiaChiLayHang = src.DiaChiLayHang ?? dst.DiaChiLayHang;
                }
            }

            var finalRows = new List<WaybillDbModel>();
            foreach (var wb in list)
            {
                if (!dict.TryGetValue(wb, out var row))
                    row = CreateEmptyRow(wb);

                row.WaybillNo = wb;
                if (!string.IsNullOrEmpty(row.ThaoTacCuoi) &&
                    row.ThaoTacCuoi.Contains("Ký nhận", StringComparison.OrdinalIgnoreCase))
                {
                    row.IsActive = false;
                }

                finalRows.Add(ToTrackingRowModel(row));
            }

            await _repository.UpsertTrackingRowsAsync(finalRows, ct).ConfigureAwait(false);
            var finalEvents = BuildFinalEvents(list, eventDict, aliasMap);
            await _repository.UpsertTrackingEventsAsync(finalEvents, ct).ConfigureAwait(false);
            try
            {
                await new JourneyHistoryService().StoreTrackingEventsAsync(finalEvents, ct).ConfigureAwait(false);
            }
            catch (System.Exception jhEx)
            {
                AppLogger.Warning($"[FullStackTracking] journey_history persist skipped: {jhEx.Message}");
            }
            await _repository.MarkEnrichedAsync(list, ct).ConfigureAwait(false);
            AppLogger.Info($"[FullStackTracking] enrichment finished count={finalRows.Count}");

            return list.Select(x =>
            {
                int eventCount = finalEvents.Count(e => string.Equals(e.WaybillNo, x, StringComparison.OrdinalIgnoreCase));
                return new FullStackJourneyResult
                {
                    WaybillNo = x,
                    Enriched = true,
                    TrackingEventCount = eventCount,
                    EnrichedAt = DateTime.UtcNow,
                    Message = eventCount > 0 ? $"Đã kéo {eventCount:N0} tracking events" : "Đã enrich summary, chưa có tracking event chi tiết"
                };
            }).ToList();
        }

        private static List<TrackingEvent> BuildFinalEvents(
            IReadOnlyList<string> requestedWaybills,
            ConcurrentDictionary<string, List<TrackingEvent>> eventDict,
            Dictionary<string, string> aliasMap)
        {
            var finalEvents = new List<TrackingEvent>();
            foreach (var waybillNo in requestedWaybills)
            {
                if (eventDict.TryGetValue(waybillNo, out var directEvents))
                {
                    finalEvents.AddRange(CloneEventsForWaybill(directEvents, waybillNo));
                    continue;
                }

                if (aliasMap.TryGetValue(waybillNo, out var root) && eventDict.TryGetValue(root, out var rootEvents))
                    finalEvents.AddRange(CloneEventsForWaybill(rootEvents, waybillNo));
            }

            return finalEvents;
        }

        private static IEnumerable<TrackingEvent> CloneEventsForWaybill(IEnumerable<TrackingEvent> events, string waybillNo)
        {
            foreach (var item in events ?? Array.Empty<TrackingEvent>())
            {
                yield return new TrackingEvent
                {
                    WaybillNo = waybillNo,
                    EventTime = item.EventTime,
                    Action = item.Action,
                    Status = item.Status,
                    SiteCode = item.SiteCode,
                    SiteName = item.SiteName,
                    OperatorCode = item.OperatorCode,
                    OperatorName = item.OperatorName,
                    RawJson = item.RawJson,
                    CreatedAt = item.CreatedAt
                };
            }
        }

        private static async Task ProcessTrackingBatchAsync(string[] batch, ConcurrentDictionary<string, WaybillDbModel> dict, CancellationToken ct)
        {
            await ProcessTrackingBatchAsync(batch, dict, null, ct).ConfigureAwait(false);
        }

        private static async Task ProcessTrackingBatchAsync(
            string[] batch,
            ConcurrentDictionary<string, WaybillDbModel> dict,
            ConcurrentDictionary<string, List<TrackingEvent>> eventDict,
            CancellationToken ct)
        {
            string url = AppConfig.Current.BuildJmsApiUrl("operatingplatform/podTracking/inner/query/keywordList");
            var payload = new Dictionary<string, object>
            {
                { "keywordList", batch },
                { "trackingTypeEnum", "WAYBILL" },
                { "countryId", "1" }
            };

            using var res = await JmsApiClient.PostJsonAsync(
                url,
                JsonSerializer.Serialize(payload, JsonOptions),
                routeName: "trackingExpress",
                ct: ct).ConfigureAwait(false);
            if (res == null || !res.IsSuccessStatusCode) return;

            var json = await res.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(json)) return;

            var result = JsonSerializer.Deserialize<WaybillHistoryResponse>(json, JsonOptions);
            if (result?.succ != true || result.data == null) return;

            foreach (var item in result.data)
            {
                var wb = item.keyword ?? item.billCode ?? string.Empty;
                if (string.IsNullOrWhiteSpace(wb)) continue;
                if (dict.TryGetValue(wb, out var row))
                {
                    lock (row)
                    {
                        ApplyTrackingData(row, item.details);
                    }
                }

                if (eventDict != null)
                {
                    var events = BuildTrackingEvents(wb, item.details);
                    eventDict.AddOrUpdate(wb, events, (_, existing) =>
                    {
                        lock (existing)
                        {
                            existing.AddRange(events);
                            return existing;
                        }
                    });
                }
            }
        }

        private static async Task CallOrderDetailAsync(string waybill, ConcurrentDictionary<string, WaybillDbModel> dict, CancellationToken ct)
        {
            var url = AppConfig.Current.BuildJmsApiUrl("operatingplatform/order/getOrderDetail");
            var payload = new { waybillNo = waybill };
            using var response = await JmsApiClient.PostJsonAsync(
                url,
                JsonSerializer.Serialize(payload, JsonOptions),
                routeName: "trackingExpress",
                ct: ct).ConfigureAwait(false);
            if (response == null || !response.IsSuccessStatusCode) return;

            var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(json)) return;

            var result = JsonSerializer.Deserialize<OrderDetailResponse>(json, JsonOptions);
            if (result?.succ != true || result.data?.details == null) return;

            if (dict.TryGetValue(waybill, out var row))
            {
                lock (row)
                {
                    ApplyOrderDetail(row, result.data.details);
                }
            }
        }

        private static void ApplyOrderDetail(WaybillDbModel row, OrderDetailInfo info)
        {
            if (row == null || info == null) return;

            row.NhanVienNhanHang = info.staffName ?? row.NhanVienNhanHang;
            row.DiaChiLayHang = info.senderDetailedAddress ?? row.DiaChiLayHang;
            row.ThoiGianNhanHang = info.pickTime ?? row.ThoiGianNhanHang;
            row.NoiDungHangHoa = info.goodsName ?? row.NoiDungHangHoa;
            row.CODThucTe = info.codMoney ?? row.CODThucTe;
            row.TenNguoiGui = info.customerName ?? row.TenNguoiGui;
            row.TrongLuong = info.packageChargeWeight?.ToString() ?? row.TrongLuong;
            row.PTTT = info.paymentModeName ?? row.PTTT;
            row.DiaChiNhanHang = info.receiverDetailedAddress ?? row.DiaChiNhanHang;
            row.Phuong = info.destinationName ?? row.Phuong;

            if (!string.IsNullOrEmpty(info.terminalDispatchCode))
            {
                row.MaDoanFull = info.terminalDispatchCode;
                var parts = info.terminalDispatchCode.Split('-');
                row.MaDoan1 = parts.Length > 0 ? parts[0] : row.MaDoan1;
                row.MaDoan2 = parts.Length > 1 ? parts[1] : row.MaDoan2;
                row.MaDoan3 = parts.Length > 2 ? parts[2] : row.MaDoan3;
            }
        }

        private static void ApplyTrackingData(WaybillDbModel row, List<WaybillDetail> details)
        {
            if (row == null) return;
            if (details == null || details.Count == 0)
            {
                row.LastTrackedAt = DateTime.UtcNow;
                row.NextTrackAt = DateTime.UtcNow.AddMinutes(row.TrackingIntervalMins <= 0 ? 30 : row.TrackingIntervalMins);
                return;
            }

            row.DauChuyenHoan = details.Any(d => d.status == "已审核") ? "Có" : "Không";

            var staffNameFirst = details
                .Where(d => (d.scanTypeName?.Contains("Nhận hàng") == true || d.scanTypeName?.Contains("Lấy hàng") == true || d.status == "已揽件") &&
                            (!string.IsNullOrEmpty(d.staffName) || !string.IsNullOrEmpty(d.scanByName)))
                .OrderBy(d => DateTime.TryParse(!string.IsNullOrEmpty(d.uploadTime) ? d.uploadTime : (d.scanTime ?? "9999-12-31"), out DateTime dt) ? dt : DateTime.MaxValue)
                .FirstOrDefault();
            if (staffNameFirst != null) row.NhanVienNhanHang = staffNameFirst.staffName ?? staffNameFirst.scanByName ?? "";

            var giaoLaiGanNhat = details.Where(d => d.scanTypeName != null && d.scanTypeName.Contains("Giao lại hàng"))
                .OrderByDescending(d => DateTime.TryParse(d.uploadTime ?? d.scanTime ?? "", out DateTime dt) ? dt : DateTime.MinValue)
                .FirstOrDefault();
            row.ThoiGianYeuCauPhatLai = giaoLaiGanNhat?.remark2 ?? "";

            var vanDe = details.Where(d => d.scanTypeName?.Contains("vấn đề") == true || d.scanTypeName?.Contains("Kiện vấn đề") == true)
                .OrderByDescending(d => DateTime.TryParse(d.uploadTime ?? d.scanTime ?? "", out DateTime dt) ? dt : DateTime.MinValue)
                .FirstOrDefault();
            if (vanDe != null)
            {
                row.NhanVienKienVanDe = vanDe.scanByName ?? "";
                row.NguyenNhanKienVanDe = vanDe.remark1 ?? "";
            }

            WaybillDetail latest = null;
            DateTime maxTime = DateTime.MinValue;
            foreach (var d in details)
            {
                string type = d.scanTypeName ?? "";
                if (type == "Kiểm tra hàng tồn kho" || type.Contains("Lịch sử cuộc gọi") || type.Contains("cuộc gọi-phát")) continue;
                string timeStr = d.uploadTime ?? d.scanTime;
                if (string.IsNullOrEmpty(timeStr)) continue;
                if (DateTime.TryParse(timeStr, out DateTime dt) && dt > maxTime)
                {
                    maxTime = dt;
                    latest = d;
                }
            }

            latest ??= details.LastOrDefault();
            if (latest != null)
            {
                row.TrangThaiHienTai = latest.waybillTrackingContent ?? "";
                row.ThaoTacCuoi = latest.scanTypeName ?? "";
                row.ThoiGianThaoTac = latest.uploadTime ?? latest.scanTime ?? "";
                row.BuuCucThaoTac = latest.scanNetworkName ?? "";
                row.NguoiThaoTac = latest.scanByName ?? "";
            }

            row.LastTrackedAt = DateTime.UtcNow;
            var interval = row.TrackingIntervalMins <= 0 ? 30 : row.TrackingIntervalMins;
            row.NextTrackAt = DateTime.UtcNow.AddMinutes(interval);
        }

        private static List<TrackingEvent> BuildTrackingEvents(string waybillNo, List<WaybillDetail> details)
        {
            var result = new List<TrackingEvent>();
            if (string.IsNullOrWhiteSpace(waybillNo) || details == null || details.Count == 0)
                return result;

            foreach (var detail in details)
            {
                if (detail == null) continue;
                var timeText = detail.uploadTime ?? detail.scanTime;
                DateTime? eventTime = DateTime.TryParse(timeText, out var parsed) ? parsed : null;
                result.Add(new TrackingEvent
                {
                    WaybillNo = waybillNo.Trim().ToUpperInvariant(),
                    EventTime = eventTime,
                    Action = detail.scanTypeName ?? string.Empty,
                    Status = detail.waybillTrackingContent ?? detail.status ?? string.Empty,
                    SiteName = detail.scanNetworkName ?? string.Empty,
                    OperatorName = detail.scanByName ?? detail.staffName ?? string.Empty,
                    RawJson = JsonSerializer.Serialize(detail, JsonOptions),
                    CreatedAt = DateTime.UtcNow
                });
            }

            return result;
        }

        private static List<string> ExpandWaybillCandidates(IEnumerable<string> waybills)
        {
            var result = new List<string>();
            foreach (var wb in waybills)
            {
                if (string.IsNullOrWhiteSpace(wb)) continue;
                string clean = wb.Trim().ToUpperInvariant();
                result.Add(clean);
                int dash = clean.IndexOf('-');
                if (dash > 0)
                {
                    string root = clean[..dash];
                    if (!string.IsNullOrWhiteSpace(root)) result.Add(root);
                }
            }
            return result.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }

        private static WaybillDbModel CreateEmptyRow(string waybillNo) => new()
        {
            WaybillNo = waybillNo,
            TrangThaiHienTai = "empty",
            ThaoTacCuoi = "empty",
            ThoiGianThaoTac = "empty",
            ThoiGianYeuCauPhatLai = "empty",
            NhanVienKienVanDe = "empty",
            NguyenNhanKienVanDe = "empty",
            BuuCucThaoTac = "empty",
            NguoiThaoTac = "empty",
            DauChuyenHoan = "empty",
            DiaChiNhanHang = "empty",
            Phuong = "empty",
            NoiDungHangHoa = "empty",
            CODThucTe = "empty",
            PTTT = "empty",
            NhanVienNhanHang = "empty",
            DiaChiLayHang = "empty",
            ThoiGianNhanHang = "empty",
            TenNguoiGui = "empty",
            TrongLuong = "empty",
            MaDoanFull = "empty",
            MaDoan1 = "empty",
            MaDoan2 = "empty",
            MaDoan3 = "empty",
            PrintCount = 0,
            IsActive = true,
            TrackingIntervalMins = 30,
            LastTrackedAt = DateTime.UtcNow,
            NextTrackAt = DateTime.UtcNow.AddMinutes(30)
        };

        private static WaybillDbModel ToTrackingRowModel(WaybillDbModel row) => new()
        {
            WaybillNo = row.WaybillNo,
            TrangThaiHienTai = CleanValue(row.TrangThaiHienTai) ?? "empty",
            ThaoTacCuoi = CleanValue(row.ThaoTacCuoi) ?? "empty",
            ThoiGianThaoTac = CleanValue(row.ThoiGianThaoTac) ?? "empty",
            ThoiGianYeuCauPhatLai = CleanValue(row.ThoiGianYeuCauPhatLai) ?? "empty",
            NhanVienKienVanDe = CleanValue(row.NhanVienKienVanDe) ?? "empty",
            NguyenNhanKienVanDe = CleanValue(row.NguyenNhanKienVanDe) ?? "empty",
            BuuCucThaoTac = CleanValue(row.BuuCucThaoTac) ?? "empty",
            NguoiThaoTac = CleanValue(row.NguoiThaoTac) ?? "empty",
            DauChuyenHoan = CleanValue(row.DauChuyenHoan) ?? "empty",
            DiaChiNhanHang = CleanValue(row.DiaChiNhanHang) ?? "empty",
            Phuong = CleanValue(row.Phuong) ?? "empty",
            NoiDungHangHoa = CleanValue(row.NoiDungHangHoa) ?? "empty",
            CODThucTe = CleanValue(row.CODThucTe) ?? "empty",
            PTTT = CleanValue(row.PTTT) ?? "empty",
            NhanVienNhanHang = CleanValue(row.NhanVienNhanHang) ?? "empty",
            DiaChiLayHang = CleanValue(row.DiaChiLayHang) ?? "empty",
            ThoiGianNhanHang = CleanValue(row.ThoiGianNhanHang) ?? "empty",
            TenNguoiGui = CleanValue(row.TenNguoiGui) ?? "empty",
            TrongLuong = CleanValue(row.TrongLuong) ?? "empty",
            MaDoanFull = CleanValue(row.MaDoanFull) ?? "empty",
            MaDoan1 = CleanValue(row.MaDoan1) ?? "empty",
            MaDoan2 = CleanValue(row.MaDoan2) ?? "empty",
            MaDoan3 = CleanValue(row.MaDoan3) ?? "empty",
            PrintCount = row.PrintCount,
            IsActive = row.IsActive,
            TrackingIntervalMins = row.TrackingIntervalMins,
            LastTrackedAt = row.LastTrackedAt,
            NextTrackAt = row.NextTrackAt
        };

        private static string CleanValue(string value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}

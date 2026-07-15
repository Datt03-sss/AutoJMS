using AutoJMS.Data;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace AutoJMS
{
    [System.Reflection.Obfuscation(Exclude = true, ApplyToMembers = true)]
    public static class DatabaseTracking
    {
        private const int BatchSize = 40;
        private const int MaxDegreeOfParallelism = 3;
        // Order-detail ("Thông tin đơn hàng") is essentially static per waybill (sender,
        // address, COD, goods, weight...). Fetch it once per waybill, then skip on later
        // cycles so large inventories don't re-download it every sync. Concurrency is
        // globally bounded to protect against JMS IP-locking.
        private const int OrderDetailConcurrency = 3;
        private static readonly SemaphoreSlim _orderDetailGate = new(OrderDetailConcurrency, OrderDetailConcurrency);
        private static readonly HashSet<string> _orderDetailDone = new(StringComparer.OrdinalIgnoreCase);
        private static readonly object _orderDetailLock = new();
        private static readonly JsonSerializerOptions JsonOptions = AppConfig.CreateJsonOptions();
        private static readonly Dictionary<string, string> _cloudRowFingerprintCache = new(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, string> _phatLaiRowFingerprintCache = new(StringComparer.OrdinalIgnoreCase);
        private static readonly object _fingerprintLock = new();

        public static async Task RunBackgroundTrackingAsync(IEnumerable<string> waybills, string sourceKey = "CLOUD", CancellationToken ct = default)
        {
            if (waybills == null) return;

            // 1. Chuẩn hóa danh sách mã vận đơn đầu vào
            var list = waybills
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim().ToUpperInvariant())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (list.Count == 0 || !JmsAuthStateService.HasToken)
                return;

            AppLogger.Info($"[DatabaseTracking] Bắt đầu tracking {list.Count} đơn.");

            var dict = new Dictionary<string, WaybillDbModel>(StringComparer.OrdinalIgnoreCase);
            var queryCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var aliasMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var allCodes = ExpandWaybillCandidates(list);

            // 2. Xử lý mã có chứa hậu tố (VD: -001) để lấy đúng mã gốc đi hỏi API
            foreach (var wb in allCodes)
            {
                ct.ThrowIfCancellationRequested();

                queryCodes.Add(wb);
                dict[wb] = CreateEmptyRow(wb);

                if (wb.Contains("-"))
                {
                    var original = wb.Split('-')[0].Trim();
                    if (!string.IsNullOrWhiteSpace(original))
                    {
                        queryCodes.Add(original);
                        aliasMap[wb] = original;

                        if (!dict.ContainsKey(original))
                        {
                            dict[original] = CreateEmptyRow(original);
                        }
                    }
                }
            }

            // 3. Chia nhỏ danh sách thành các Batch (mỗi Batch 40 mã)
            var batches = queryCodes.ToList().Chunk(BatchSize).ToList();

            // 4. Gọi API JMS song song (Giới hạn tối đa 3 luồng để chống khóa IP)
            await Parallel.ForEachAsync(
                batches,
                new ParallelOptions
                {
                    MaxDegreeOfParallelism = MaxDegreeOfParallelism,
                    CancellationToken = ct
                },
                async (batch, token) =>
                {
                    await ProcessTrackingBatchAsync(batch.ToArray(), dict, token);
                    await ProcessOrderDetailBatchAsync(batch.ToArray(), dict, token);
                });

            // 5. Đồng bộ dữ liệu từ mã gốc sang mã có hậu tố
            foreach (var kv in aliasMap)
            {
                if (dict.TryGetValue(kv.Value, out var src) && dict.TryGetValue(kv.Key, out var dst))
                {
                    dst.NhanVienNhanHang = src.NhanVienNhanHang ?? "";
                    dst.TenNguoiGui = src.TenNguoiGui ?? "";
                    dst.DiaChiLayHang = src.DiaChiLayHang ?? "";
                }
            }

            // 6. Gói ghém kết quả cuối cùng để đẩy lên Database
            var finalUploadList = new List<WaybillDbModel>();
            var fingerprintMap = sourceKey.Equals("PHATLAI", StringComparison.OrdinalIgnoreCase)
                ? _phatLaiRowFingerprintCache
                : _cloudRowFingerprintCache;

            foreach (var wb in list)
            {
                if (!dict.TryGetValue(wb, out var row))
                    row = CreateEmptyRow(wb);

                if (!string.IsNullOrEmpty(row.ThaoTacCuoi) &&
                    row.ThaoTacCuoi.Contains("Ký nhận", StringComparison.OrdinalIgnoreCase))
                {
                    row.IsActive = false;
                }

                row.WaybillNo = wb;
                var normalized = ToTrackingRowModel(row);
                var fingerprint = BuildRowFingerprint(normalized);

                bool shouldSkip;
                lock (_fingerprintLock)
                {
                    shouldSkip = fingerprintMap.TryGetValue(wb, out var oldFingerprint) && string.Equals(oldFingerprint, fingerprint, StringComparison.Ordinal);
                    if (!shouldSkip)
                    {
                        fingerprintMap[wb] = fingerprint;
                    }
                }

                if (shouldSkip) continue;
                finalUploadList.Add(normalized);
            }

            if (finalUploadList.Count > 0 && !ct.IsCancellationRequested)
            {
                await SupabaseDbService.UpsertManyWaybillsAsync(finalUploadList);
                AppLogger.Info($"[DatabaseTracking] Đã update lên Database {finalUploadList.Count} đơn.");
            }
        }

        private static async Task ProcessTrackingBatchAsync(
            string[] batch,
            Dictionary<string, WaybillDbModel> dict,
            CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();

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
                ct: ct);
            if (res == null) return;
            if (!res.IsSuccessStatusCode) return;

            var json = await res.Content.ReadAsStringAsync(ct);
            if (string.IsNullOrWhiteSpace(json)) return;

            var result = JsonSerializer.Deserialize<WaybillHistoryResponse>(json, JsonOptions);
            if (result?.succ != true || result.data == null) return;

            foreach (var item in result.data)
            {
                var wb = item.keyword ?? item.billCode ?? string.Empty;
                if (string.IsNullOrWhiteSpace(wb)) continue;

                if (dict.TryGetValue(wb, out var row))
                {
                    ApplyTrackingData(row, item.details);
                }
            }
        }

        private static async Task ProcessOrderDetailBatchAsync(
            string[] batch,
            Dictionary<string, WaybillDbModel> dict,
            CancellationToken ct)
        {
            // Skip waybills whose static order detail was already fetched in a prior cycle.
            string[] todo;
            lock (_orderDetailLock)
            {
                todo = batch.Where(w => !_orderDetailDone.Contains(w)).ToArray();
            }
            if (todo.Length == 0) return;

            // Fetch the remaining details concurrently (globally bounded by _orderDetailGate).
            var tasks = todo.Select(async waybill =>
            {
                await _orderDetailGate.WaitAsync(ct).ConfigureAwait(false);
                try
                {
                    bool applied = await CallOrderDetailAsync(waybill, dict, ct).ConfigureAwait(false);
                    if (applied)
                    {
                        lock (_orderDetailLock) { _orderDetailDone.Add(waybill); }
                    }
                }
                finally
                {
                    _orderDetailGate.Release();
                }
            });
            await Task.WhenAll(tasks).ConfigureAwait(false);
        }

        private static async Task<bool> CallOrderDetailAsync(string waybill, Dictionary<string, WaybillDbModel> dict, CancellationToken ct)
        {
            var url = AppConfig.Current.BuildJmsApiUrl("operatingplatform/order/getOrderDetail");
            var payload = new { waybillNo = waybill };
            using var response = await JmsApiClient.PostJsonAsync(
                url,
                JsonSerializer.Serialize(payload, JsonOptions),
                routeName: "trackingExpress",
                ct: ct);
            if (response == null) return false;
            if (!response.IsSuccessStatusCode) return false;

            var json = await response.Content.ReadAsStringAsync(ct);
            if (string.IsNullOrWhiteSpace(json)) return false;

            var result = JsonSerializer.Deserialize<OrderDetailResponse>(json, JsonOptions);
            if (result?.succ != true || result.data?.details == null) return false;

            if (dict.TryGetValue(waybill, out var row))
            {
                ApplyOrderDetail(row, result.data.details);
            }
            return true;
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

        private static WaybillDbModel CreateEmptyRow(string waybillNo)
        {
            return new WaybillDbModel
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

            var staffNameFisrt = details
                .Where(d => (d.scanTypeName?.Contains("Nhận hàng") == true || d.scanTypeName?.Contains("Lấy hàng") == true || d.status == "已揽件") &&
                            (!string.IsNullOrEmpty(d.staffName) || !string.IsNullOrEmpty(d.scanByName)))
                .OrderBy(d => DateTime.TryParse(!string.IsNullOrEmpty(d.uploadTime) ? d.uploadTime : (d.scanTime ?? "9999-12-31"), out DateTime dt) ? dt : DateTime.MaxValue)
                .FirstOrDefault();
            if (staffNameFisrt != null) row.NhanVienNhanHang = staffNameFisrt.staffName ?? staffNameFisrt.scanByName ?? "";

            var giaoLaiGanNhat = details.Where(d => d.scanTypeName != null && d.scanTypeName.Contains("Giao lại hàng"))
                .OrderByDescending(d => DateTime.TryParse(d.uploadTime ?? d.scanTime ?? "", out DateTime dt) ? dt : DateTime.MinValue).FirstOrDefault();
            row.ThoiGianYeuCauPhatLai = giaoLaiGanNhat?.remark2 ?? "";

            var vanDe = details.Where(d => d.scanTypeName?.Contains("vấn đề") == true || d.scanTypeName?.Contains("Kiện vấn đề") == true)
                .OrderByDescending(d => DateTime.TryParse(d.uploadTime ?? d.scanTime ?? "", out DateTime dt) ? dt : DateTime.MinValue).FirstOrDefault();
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

        private static bool IsNoiseDetail(WaybillDetail d)
        {
            var type = d?.scanTypeName ?? string.Empty;
            return type.Contains("Kiểm tra hàng tồn kho", StringComparison.OrdinalIgnoreCase)
                || type.Contains("Lịch sử cuộc gọi", StringComparison.OrdinalIgnoreCase)
                || type.Contains("cuộc gọi-phát", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsProblemDetail(WaybillDetail d)
        {
            var type = d?.scanTypeName ?? string.Empty;
            return type.Contains("vấn đề", StringComparison.OrdinalIgnoreCase) || type.Contains("Kiện vấn đề", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsReDeliverDetail(WaybillDetail d)
        {
            var type = d?.scanTypeName ?? string.Empty;
            return type.Contains("Giao lại hàng", StringComparison.OrdinalIgnoreCase) || type.Contains("重派", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsArrivalDetail(WaybillDetail d)
        {
            var type = d?.scanTypeName ?? string.Empty;
            var network = d?.scanNetworkName ?? string.Empty;
            return type.Contains("Xuống hàng kiện đến", StringComparison.OrdinalIgnoreCase)
                || type.Contains("Xuống kiện", StringComparison.OrdinalIgnoreCase)
                || type.Contains("到件", StringComparison.OrdinalIgnoreCase)
                || type.Contains("卸车到件", StringComparison.OrdinalIgnoreCase)
                || network.Contains("Kim Tân", StringComparison.OrdinalIgnoreCase)
                || network.Contains("LCI", StringComparison.OrdinalIgnoreCase);
        }

        private static DateTime GetDetailTime(WaybillDetail d)
        {
            if (DateTime.TryParse(d?.uploadTime ?? d?.scanTime ?? string.Empty, out var dt)) return dt;
            return DateTime.MinValue;
        }

        private static string GetDetailTimeText(WaybillDetail d) => d?.uploadTime ?? d?.scanTime ?? string.Empty;

        private static string CleanValue(string value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        private static string CleanType(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return null;
            return value.Replace("卸车到件", "Xuống hàng kiện đến").Replace("库存盘点", "Kiểm tra hàng tồn kho").Trim();
        }

        private static WaybillDbModel ToTrackingRowModel(WaybillDbModel row)
        {
            return new WaybillDbModel
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
        }

        private static string Normalize(string value) => string.IsNullOrWhiteSpace(value) ? "empty" : value;

        private static string BuildRowFingerprint(WaybillDbModel row)
        {
            var sb = new System.Text.StringBuilder();
            sb.Append(row.WaybillNo ?? string.Empty).Append('|')
              .Append(row.TrangThaiHienTai ?? string.Empty).Append('|')
              .Append(row.ThaoTacCuoi ?? string.Empty).Append('|')
              .Append(row.ThoiGianThaoTac ?? string.Empty).Append('|')
              .Append(row.ThoiGianYeuCauPhatLai ?? string.Empty).Append('|')
              .Append(row.NhanVienKienVanDe ?? string.Empty).Append('|')
              .Append(row.NguyenNhanKienVanDe ?? string.Empty).Append('|')
              .Append(row.BuuCucThaoTac ?? string.Empty).Append('|')
              .Append(row.NguoiThaoTac ?? string.Empty).Append('|')
              .Append(row.DauChuyenHoan ?? string.Empty).Append('|')
              .Append(row.DiaChiNhanHang ?? string.Empty).Append('|')
              .Append(row.Phuong ?? string.Empty).Append('|')
              .Append(row.NoiDungHangHoa ?? string.Empty).Append('|')
              .Append(row.CODThucTe ?? string.Empty).Append('|')
              .Append(row.PTTT ?? string.Empty).Append('|')
              .Append(row.NhanVienNhanHang ?? string.Empty).Append('|')
              .Append(row.DiaChiLayHang ?? string.Empty).Append('|')
              .Append(row.ThoiGianNhanHang ?? string.Empty).Append('|')
              .Append(row.TenNguoiGui ?? string.Empty).Append('|')
              .Append(row.TrongLuong ?? string.Empty).Append('|')
              .Append(row.MaDoanFull ?? string.Empty).Append('|')
              .Append(row.MaDoan1 ?? string.Empty).Append('|')
              .Append(row.MaDoan2 ?? string.Empty).Append('|')
              .Append(row.MaDoan3 ?? string.Empty).Append('|')
              .Append(row.PrintCount).Append('|')
              .Append(row.IsActive).Append('|')
              .Append(row.TrackingIntervalMins);
            return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(sb.ToString())));
        }
    }
}

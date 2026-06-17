using AutoJMS.Data;
using Postgrest.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AutoJMS.Diagnostics;

namespace AutoJMS
{
    public static class SupabaseDbService
    {
        private static Supabase.Client _client;
        private static readonly SemaphoreSlim _initGate = new(1, 1);
        private static readonly object _configLock = new();

        private const string SUPABASE_URL = "https://bnsnnrlwfzxemmizknwy.supabase.co";
        private static string _configuredUrl;
        private static string _configuredKey;

        public static string MachineId { get; private set; } = LoadOrCreateMachineId();

        public static void Configure(string supabaseUrl, string anonKey)
        {
            lock (_configLock)
            {
                string normalizedUrl = NormalizeUrl(supabaseUrl);
                string normalizedKey = string.IsNullOrWhiteSpace(anonKey) ? "" : anonKey.Trim();

                bool changed =
                    !string.Equals(_configuredUrl ?? "", normalizedUrl, StringComparison.OrdinalIgnoreCase) ||
                    !string.Equals(_configuredKey ?? "", normalizedKey, StringComparison.Ordinal);

                _configuredUrl = normalizedUrl;
                _configuredKey = normalizedKey;

                if (changed)
                    _client = null;

                AppLogger.Info(
                    $"SupabaseDbService configured url={MaskUrl(ResolveUrl())}, anonKey={(string.IsNullOrWhiteSpace(ResolveKey()) ? "<missing>" : TokenRedactor.MaskToken(ResolveKey()))}");
            }
        }

        private static string LoadOrCreateMachineId()
        {
            return Environment.MachineName + "_" + Guid.NewGuid().ToString("N");
        }

        public static async Task InitializeAsync()
        {
            if (_client != null) return;
            await _initGate.WaitAsync();
            try
            {
                if (_client != null) return;
                string url = ResolveUrl();
                string key = ResolveKey();
                if (string.IsNullOrWhiteSpace(url))
                    throw new InvalidOperationException("Supabase project URL is not configured.");
                if (string.IsNullOrWhiteSpace(key))
                    throw new InvalidOperationException("Supabase anon key is not configured. Set it from the license server or AUTOJMS_SUPABASE_ANON_KEY.");

                var options = new Supabase.SupabaseOptions { AutoConnectRealtime = true };
                _client = new Supabase.Client(url, key, options);
                await _client.InitializeAsync();
            }
            finally
            {
                _initGate.Release();
            }
        }

        private static async Task<T> RpcAsync<T>(string fn, object args)
        {
            if (_client == null) await InitializeAsync();
            var res = await _client.Rpc(fn, args);
            // Parse chuỗi trả về thành kiểu T
            if (typeof(T) == typeof(bool)) return (T)(object)(res.Content != null && res.Content.Trim() == "true");
            if (typeof(T) == typeof(int)) return (T)(object)(int.Parse(res.Content ?? "0"));
            return default;
        }

        public static Task<bool> TryAcquireInventoryLeaseAsync(int leaseSeconds = 1800) =>
            RpcAsync<bool>("try_acquire_inventory_lease", new { p_owner_id = MachineId, p_lease_seconds = leaseSeconds });

        public static Task<bool> RefreshInventoryLeaseAsync(int leaseSeconds = 1800) =>
            RpcAsync<bool>("refresh_inventory_lease", new { p_owner_id = MachineId, p_lease_seconds = leaseSeconds });

        public static Task<bool> ReleaseInventoryLeaseAsync() =>
            RpcAsync<bool>("release_inventory_lease", new { p_owner_id = MachineId });

        public static Task<bool> CompleteInventorySyncAsync() =>
            RpcAsync<bool>("complete_inventory_sync", new { p_owner_id = MachineId });

        public static Task<bool> UpdateInventorySyncHeartbeatAsync(string ownerId) =>
            RpcAsync<bool>("refresh_inventory_lease", new { p_owner_id = ownerId, p_lease_seconds = 1800 });

        public static async Task<int> UpsertNewWaybillsOnlyAsync(IEnumerable<string> fetchedWaybills)
        {
            if (_client == null) await InitializeAsync();
            var arr = fetchedWaybills?.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToArray() ?? Array.Empty<string>();
            if (arr.Length == 0) return 0;
            return await RpcAsync<int>("upsert_new_waybills", new { p_waybills = arr });
        }

        public static async Task<List<WaybillDbModel>> GetActiveWaybillsAsync(int pageSize = 1000)
        {
            if (_client == null) await InitializeAsync();
            var collected = new List<WaybillDbModel>();
            int page = 1;

            while (true)
            {
                var response = await _client.From<WaybillDbModel>()
                    .Range((page - 1) * pageSize, page * pageSize - 1)
                    .Get();

                var chunk = response.Models?.ToList() ?? new List<WaybillDbModel>();
                collected.AddRange(chunk);
                if (chunk.Count < pageSize) break;
                page++;
            }

            return collected;
        }

        public static async Task<List<string>> GetWaybillsDueForTrackingAsync(int pageSize = 1000)
        {
            if (_client == null) await InitializeAsync();
            var collected = new List<string>();
            int page = 1;

            while (true)
            {
                var response = await _client.From<WaybillDbModel>()
                    .Select("waybill_no")
                    .Range((page - 1) * pageSize, page * pageSize - 1)
                    .Get();

                var chunk = response.Models?.Select(x => x.WaybillNo).Where(x => !string.IsNullOrWhiteSpace(x)).ToList() ?? new List<string>();
                collected.AddRange(chunk);
                if (chunk.Count < pageSize) break;
                page++;
            }

            return collected.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }

        public static async Task UpsertManyWaybillsAsync(List<WaybillDbModel> rows)
        {
            if (_client == null) await InitializeAsync();
            if (rows == null || rows.Count == 0) return;

            var payload = rows
                .Where(r => !string.IsNullOrWhiteSpace(r?.WaybillNo))
                .Select(r => new
                {
                    waybill_no = r.WaybillNo.Trim(),
                    trang_thai_hien_tai = Normalize(r.TrangThaiHienTai),
                    thao_tac_cuoi = Normalize(r.ThaoTacCuoi),
                    thoi_gian_thao_tac = Normalize(r.ThoiGianThaoTac),
                    thoi_gian_yeu_cau_phat_lai = Normalize(r.ThoiGianYeuCauPhatLai),
                    nhan_vien_kien_van_de = Normalize(r.NhanVienKienVanDe),
                    nguyen_nhan_kien_van_de = Normalize(r.NguyenNhanKienVanDe),
                    buu_cuc_thao_tac = Normalize(r.BuuCucThaoTac),
                    nguoi_thao_tac = Normalize(r.NguoiThaoTac),
                    dau_chuyen_hoan = Normalize(r.DauChuyenHoan),
                    dia_chi_nhan_hang = Normalize(r.DiaChiNhanHang),
                    phuong = Normalize(r.Phuong),
                    noi_dung_hang_hoa = Normalize(r.NoiDungHangHoa),
                    cod_thuc_te = Normalize(r.CODThucTe),
                    pttt = Normalize(r.PTTT),
                    nhan_vien_nhan_hang = Normalize(r.NhanVienNhanHang),
                    dia_chi_lay_hang = Normalize(r.DiaChiLayHang),
                    thoi_gian_nhan_hang = Normalize(r.ThoiGianNhanHang),
                    ten_nguoi_gui = Normalize(r.TenNguoiGui),
                    trong_luong = Normalize(r.TrongLuong),
                    ma_doan_full = Normalize(r.MaDoanFull),
                    ma_doan_1 = Normalize(r.MaDoan1),
                    ma_doan_2 = Normalize(r.MaDoan2),
                    ma_doan_3 = Normalize(r.MaDoan3),
                    print_count = r.PrintCount,
                    is_active = r.IsActive,
                    tracking_interval_mins = r.TrackingIntervalMins,
                    last_tracked_at = r.LastTrackedAt,
                    next_track_at = r.NextTrackAt,
                    updated_at = DateTime.UtcNow
                })
                .ToList();

            await _client.Rpc("merge_waybill_tracking_rows", new { p_rows = payload });
        }

        private static string Normalize(string value) => string.IsNullOrWhiteSpace(value) ? "empty" : value;

        private static string ResolveUrl()
        {
            lock (_configLock)
            {
                string value = FirstNonEmpty(
                    _configuredUrl,
                    Environment.GetEnvironmentVariable("AUTOJMS_SUPABASE_PROJECT_URL"),
                    Environment.GetEnvironmentVariable("AUTOJMS_SUPABASE_URL"),
                    SUPABASE_URL);

                return NormalizeUrl(value);
            }
        }

        private static string ResolveKey()
        {
            lock (_configLock)
            {
                return FirstNonEmpty(
                    _configuredKey,
                    Environment.GetEnvironmentVariable("AUTOJMS_SUPABASE_ANON_KEY"),
                    Environment.GetEnvironmentVariable("SUPABASE_ANON_KEY"));
            }
        }

        private static string FirstNonEmpty(params string[] values)
        {
            foreach (var value in values)
            {
                if (!string.IsNullOrWhiteSpace(value))
                    return value.Trim();
            }

            return "";
        }

        private static string NormalizeUrl(string value)
            => string.IsNullOrWhiteSpace(value) ? "" : value.Trim().TrimEnd('/');

        private static string MaskUrl(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "<missing>";

            return value.Length <= 48 ? value : value[..48] + "...";
        }
    }
}

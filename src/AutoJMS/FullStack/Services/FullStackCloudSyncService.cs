using AutoJMS.FullStack.LocalDb;
using Microsoft.Data.Sqlite;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace AutoJMS.FullStack.Services
{
    /// <summary>
    /// Hybrid local-first + Supabase sync orchestrator (docs/hybrid-supabase-sync-plan.md).
    ///
    /// Principles:
    ///  * SQLite stays the authoritative read store — this service only mirrors data.
    ///  * Local writes are queued in fs_outbox and flushed in the background (offline-safe).
    ///  * One machine per site holds the inventory lease and pushes Group A rows after each
    ///    JMS sync; every machine delta-pulls (updated_at &gt; cursor, newest-wins).
    ///  * Supabase Realtime is only a "doorbell": any site change wakes the pull cycle.
    /// </summary>
    public sealed class FullStackCloudSyncService : IDisposable
    {
        private static readonly Lazy<FullStackCloudSyncService> _instance = new(() => new FullStackCloudSyncService());
        public static FullStackCloudSyncService Instance => _instance.Value;

        private readonly FullStackDbConnectionFactory _connectionFactory = new();
        private readonly FullStackDbInitializer _initializer;
        private readonly SemaphoreSlim _cycleGate = new(1, 1);
        private System.Threading.Timer _timer;
        private volatile bool _started;
        private volatile bool _hasLease;
        private volatile bool _stopping;
        private int _consecutiveFailures;
        private string _clientId;
        private int _pendingOutboxCount;

        private const int MaxConsecutiveFailures = 5;
        private const string LeaseSecondsKey = "cloud_lease_seconds";
        private const int DefaultLeaseSeconds = 1800;

        /// <summary>Fired after remote rows were merged into SQLite (UI should refresh).</summary>
        public event Action DataMerged;
        /// <summary>Human-readable sync status for the UI banner.</summary>
        public event Action<string> StatusChanged;

        private FullStackCloudSyncService()
        {
            _initializer = new FullStackDbInitializer(_connectionFactory);
        }

        public bool HasLease => _hasLease;
        public bool IsRunning => _started;
        public int PendingOutboxCount => _pendingOutboxCount;

        public static string ResolveSiteCode()
        {
            var site = (AppConfig.Current.ActionSiteCode ?? "").Trim().ToUpperInvariant();
            if (string.IsNullOrWhiteSpace(site) || site == "0000") return "";
            return site;
        }

        public static bool IsEnabled
        {
            get
            {
                try
                {
                    if (!SettingsManager.Load().CloudSyncEnabled) return false;
                    if (string.IsNullOrEmpty(ResolveSiteCode())) return false;
                    return SupabaseDbService.HasCredentials;
                }
                catch (Exception ex)
                {
                    AppLogger.Warning("[HybridSync] IsEnabled check failed: " + ex.Message);
                    return false;
                }
            }
        }

        /// <summary>Notify from any local write path — flushes the outbox soon (debounced).</summary>
        public static void NotifyLocalWrite()
        {
            var self = _instance.IsValueCreated ? _instance.Value : null;
            if (self is { _started: true })
                self.RequestCycleSoon();
        }

        // ------------------------------------------------------------------
        // Lifecycle
        // ------------------------------------------------------------------
        public async Task StartAsync(CancellationToken ct = default)
        {
            if (_started || !IsEnabled) return;
            _started = true;
            _stopping = false;
            _consecutiveFailures = 0;

            try
            {
                await _initializer.InitializeAsync(ct).ConfigureAwait(false);
                _clientId = await GetOrCreateClientIdAsync(ct).ConfigureAwait(false);

                var site = ResolveSiteCode();
                await SupabaseDbService.InitializeAsync().ConfigureAwait(false);

                // Realtime doorbell — failure is non-fatal, the periodic pull still runs.
                try
                {
                    await SupabaseDbService.SubscribeSiteChangesAsync(site, RequestCycleSoon).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    AppLogger.Warning("[HybridSync] realtime unavailable, falling back to polling: " + ex.Message);
                }

                int interval = Math.Max(15, SettingsManager.Load().CloudSyncIntervalSeconds) * 1000;
                _timer = new System.Threading.Timer(async _ => await RunCycleSafeAsync().ConfigureAwait(false), null, 1500, interval);
                AppLogger.Info($"[HybridSync] started site={site} clientId={_clientId} interval={interval}ms");
                RaiseStatus("Cloud sync: đang khởi động…");
            }
            catch (Exception ex)
            {
                _started = false;
                AppLogger.Error("[HybridSync] start failed", ex);
                RaiseStatus("Cloud sync: lỗi khởi động (chạy local-only)");
            }
        }

        public async Task StopAsync()
        {
            if (!_started) return;
            _stopping = true;
            _started = false;
            try { _timer?.Dispose(); } catch { /* ignore */ }
            _timer = null;
            SupabaseDbService.UnsubscribeSiteChanges();

            if (_hasLease)
            {
                try
                {
                    await SupabaseDbService.ReleaseSiteLeaseAsync(ResolveSiteCode()).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    AppLogger.Warning("[HybridSync] lease release failed: " + ex.Message);
                }
                _hasLease = false;
            }

            AppLogger.Info("[HybridSync] stopped");
        }

        public void Dispose()
        {
            try { StopAsync().GetAwaiter().GetResult(); } catch { /* ignore */ }
        }

        private void RequestCycleSoon()
        {
            if (!_started || _stopping) return;
            try
            {
                int interval = Math.Max(15, SettingsManager.Load().CloudSyncIntervalSeconds) * 1000;
                _timer?.Change(750, interval); // debounce bursts of realtime events
            }
            catch (ObjectDisposedException) { /* raced with StopAsync */ }
        }

        // ------------------------------------------------------------------
        // Lease (who pulls from JMS)
        // ------------------------------------------------------------------
        public async Task<bool> TryBecomeLeaderAsync(CancellationToken ct = default)
        {
            if (!IsEnabled) return true; // cloud off => behave like before (always pull JMS)
            try
            {
                var site = ResolveSiteCode();
                _hasLease = await SupabaseDbService.TryAcquireSiteLeaseAsync(site, DefaultLeaseSeconds).ConfigureAwait(false);
                AppLogger.Info($"[HybridSync] lease acquire site={site} granted={_hasLease}");
                return _hasLease;
            }
            catch (Exception ex)
            {
                // Cloud unreachable — degrade to local-only behaviour (machine pulls JMS itself).
                AppLogger.Warning("[HybridSync] lease acquire failed, fallback to JMS pull: " + ex.Message);
                _hasLease = false;
                return true;
            }
        }

        // ------------------------------------------------------------------
        // Periodic cycle: heartbeat lease → flush outbox → pull deltas → push (leader)
        // ------------------------------------------------------------------
        private async Task RunCycleSafeAsync()
        {
            if (!_started || _stopping) return;
            if (!await _cycleGate.WaitAsync(0).ConfigureAwait(false)) return;

            try
            {
                var site = ResolveSiteCode();
                if (string.IsNullOrEmpty(site)) return;

                if (_hasLease)
                {
                    try { _hasLease = await SupabaseDbService.RefreshSiteLeaseAsync(site, DefaultLeaseSeconds).ConfigureAwait(false); }
                    catch { _hasLease = false; }
                }

                int flushed = await FlushOutboxAsync(CancellationToken.None).ConfigureAwait(false);
                int merged = await PullAllAsync(CancellationToken.None).ConfigureAwait(false);

                if (_hasLease)
                    await PushDashboardRowsAsync(CancellationToken.None).ConfigureAwait(false);

                _consecutiveFailures = 0;
                if (merged > 0)
                    RaiseDataMerged();

                RaiseStatus(_pendingOutboxCount > 0
                    ? $"Cloud sync: {_pendingOutboxCount} thao tác chờ đồng bộ"
                    : $"Cloud sync OK{(_hasLease ? " (máy chủ lease)" : "")} — {DateTime.Now:HH:mm:ss}");

                if (flushed > 0 || merged > 0)
                    AppLogger.Info($"[HybridSync] cycle done flushed={flushed} merged={merged} lease={_hasLease}");
            }
            catch (Exception ex)
            {
                _consecutiveFailures++;
                AppLogger.Warning($"[HybridSync] cycle failed ({_consecutiveFailures}/{MaxConsecutiveFailures}): {ex.Message}");
                RaiseStatus("Cloud sync: mất kết nối — dữ liệu vẫn lưu local");
                if (_consecutiveFailures >= MaxConsecutiveFailures)
                {
                    AppLogger.Warning("[HybridSync] too many failures, pausing cloud sync (local-first still fully functional)");
                    await StopAsync().ConfigureAwait(false);
                }
            }
            finally
            {
                _cycleGate.Release();
            }
        }

        private void RaiseDataMerged()
        {
            try { DataMerged?.Invoke(); }
            catch (Exception ex) { AppLogger.Warning("[HybridSync] DataMerged handler error: " + ex.Message); }
        }

        private void RaiseStatus(string message)
        {
            try { StatusChanged?.Invoke(message); }
            catch { /* ignore */ }
        }

        // ------------------------------------------------------------------
        // Group A push (leader only): fs_waybills rows changed since last push
        // ------------------------------------------------------------------
        public async Task PushDashboardRowsAsync(CancellationToken ct)
        {
            if (!IsEnabled) return;
            var site = ResolveSiteCode();
            string cursor = await GetSyncStateAsync("cloud_push_waybills_at", ct).ConfigureAwait(false) ?? "";

            var rows = new List<object>();
            string maxUpdatedAt = cursor;

            await using (var connection = await _connectionFactory.OpenAsync(ct).ConfigureAwait(false))
            await using (var command = connection.CreateCommand())
            {
                command.CommandText = @"
SELECT waybill_no, is_in_current_inventory, left_inventory_at, first_seen_at, last_seen_at,
       current_state, current_status, last_action, last_action_time, last_site_code,
       last_site_name, employee_code, employee_name, receiver_name, receiver_phone_masked,
       age_hours, days_in_inventory, risk_score, risk_level, risk_reasons, sla_status,
       sla_deadline, trang_thai_hien_tai, thao_tac_cuoi, thoi_gian_thao_tac,
       thoi_gian_yeu_cau_phat_lai, nhan_vien_kien_van_de, nguyen_nhan_kien_van_de,
       buu_cuc_thao_tac, nguoi_thao_tac, dau_chuyen_hoan, dia_chi_nhan_hang, phuong,
       noi_dung_hang_hoa, cod_thuc_te, pttt, nhan_vien_nhan_hang, dia_chi_lay_hang,
       thoi_gian_nhan_hang, ten_nguoi_gui, trong_luong, ma_doan_full, ma_doan_1,
       ma_doan_2, ma_doan_3, reback_status, in_hoan_scan_time, print_count, updated_at
FROM fs_waybills
WHERE ($cursor = '' OR updated_at > $cursor)
ORDER BY updated_at ASC
LIMIT 2000;";
                command.Parameters.AddWithValue("$cursor", cursor);

                await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
                while (await reader.ReadAsync(ct).ConfigureAwait(false))
                {
                    string updatedAt = S(reader, 48);
                    if (string.CompareOrdinal(updatedAt, maxUpdatedAt) > 0) maxUpdatedAt = updatedAt;
                    rows.Add(new
                    {
                        waybill_no = S(reader, 0),
                        is_in_current_inventory = I(reader, 1) != 0,
                        left_inventory_at = S(reader, 2),
                        first_seen_at = S(reader, 3),
                        last_seen_at = S(reader, 4),
                        current_state = S(reader, 5),
                        current_status = S(reader, 6),
                        last_action = S(reader, 7),
                        last_action_time = S(reader, 8),
                        last_site_code = S(reader, 9),
                        last_site_name = S(reader, 10),
                        employee_code = S(reader, 11),
                        employee_name = S(reader, 12),
                        receiver_name = S(reader, 13),
                        receiver_phone_masked = S(reader, 14),
                        age_hours = D(reader, 15),
                        days_in_inventory = D(reader, 16),
                        risk_score = I(reader, 17),
                        risk_level = S(reader, 18),
                        risk_reasons = S(reader, 19),
                        sla_status = S(reader, 20),
                        sla_deadline = S(reader, 21),
                        trang_thai_hien_tai = S(reader, 22),
                        thao_tac_cuoi = S(reader, 23),
                        thoi_gian_thao_tac = S(reader, 24),
                        thoi_gian_yeu_cau_phat_lai = S(reader, 25),
                        nhan_vien_kien_van_de = S(reader, 26),
                        nguyen_nhan_kien_van_de = S(reader, 27),
                        buu_cuc_thao_tac = S(reader, 28),
                        nguoi_thao_tac = S(reader, 29),
                        dau_chuyen_hoan = S(reader, 30),
                        dia_chi_nhan_hang = S(reader, 31),
                        phuong = S(reader, 32),
                        noi_dung_hang_hoa = S(reader, 33),
                        cod_thuc_te = S(reader, 34),
                        pttt = S(reader, 35),
                        nhan_vien_nhan_hang = S(reader, 36),
                        dia_chi_lay_hang = S(reader, 37),
                        thoi_gian_nhan_hang = S(reader, 38),
                        ten_nguoi_gui = S(reader, 39),
                        trong_luong = S(reader, 40),
                        ma_doan_full = S(reader, 41),
                        ma_doan_1 = S(reader, 42),
                        ma_doan_2 = S(reader, 43),
                        ma_doan_3 = S(reader, 44),
                        reback_status = S(reader, 45),
                        in_hoan_scan_time = S(reader, 46),
                        print_count = I(reader, 47),
                        updated_at = ParseIsoUtc(updatedAt)
                    });
                }
            }

            if (rows.Count == 0) return;

            int pushed = await SupabaseDbService.MergeWaybillRowsV2Async(site, rows).ConfigureAwait(false);
            await SetSyncStateAsync("cloud_push_waybills_at", maxUpdatedAt, ct).ConfigureAwait(false);
            AppLogger.Info($"[HybridSync] pushed waybills={rows.Count} merged={pushed} site={site}");
        }

        // ------------------------------------------------------------------
        // Delta pull (all machines): waybills + notes + checks + tasks
        // ------------------------------------------------------------------
        public async Task<int> PullAllAsync(CancellationToken ct)
        {
            if (!IsEnabled) return 0;
            int merged = 0;
            merged += await PullWaybillsAsync(ct).ConfigureAwait(false);
            merged += await PullNotesAsync(ct).ConfigureAwait(false);
            merged += await PullChecksAsync(ct).ConfigureAwait(false);
            merged += await PullTasksAsync(ct).ConfigureAwait(false);
            return merged;
        }

        private async Task<int> PullWaybillsAsync(CancellationToken ct)
        {
            var site = ResolveSiteCode();
            var since = await GetCursorAsync("cloud_pull_waybills_at", ct).ConfigureAwait(false);
            var rows = await SupabaseDbService.PullWaybillDeltaAsync(site, since).ConfigureAwait(false);
            if (rows.Count == 0) return 0;

            // Leader pushed these rows itself — skip merging our own echo when we hold the lease.
            int applied = 0;
            DateTime maxUpdated = since;

            await using var connection = await _connectionFactory.OpenAsync(ct).ConfigureAwait(false);
            await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(ct).ConfigureAwait(false);

            foreach (var row in rows)
            {
                ct.ThrowIfCancellationRequested();
                var updatedAt = row.Value<DateTime?>("updated_at")?.ToUniversalTime() ?? DateTime.UtcNow;
                if (updatedAt > maxUpdated) maxUpdated = updatedAt;
                if (_hasLease) continue;

                await using var command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = @"
INSERT INTO fs_waybills(
    waybill_no, is_in_current_inventory, left_inventory_at, first_seen_at, last_seen_at,
    current_state, current_status, last_action, last_action_time, last_site_code,
    last_site_name, employee_code, employee_name, receiver_name, receiver_phone_masked,
    age_hours, days_in_inventory, risk_score, risk_level, risk_reasons, sla_status,
    sla_deadline, trang_thai_hien_tai, thao_tac_cuoi, thoi_gian_thao_tac,
    thoi_gian_yeu_cau_phat_lai, nhan_vien_kien_van_de, nguyen_nhan_kien_van_de,
    buu_cuc_thao_tac, nguoi_thao_tac, dau_chuyen_hoan, dia_chi_nhan_hang, phuong,
    noi_dung_hang_hoa, cod_thuc_te, pttt, nhan_vien_nhan_hang, dia_chi_lay_hang,
    thoi_gian_nhan_hang, ten_nguoi_gui, trong_luong, ma_doan_full, ma_doan_1,
    ma_doan_2, ma_doan_3, reback_status, in_hoan_scan_time, print_count,
    created_at, updated_at)
VALUES (
    $waybill_no, $is_in_current_inventory, $left_inventory_at, $first_seen_at, $last_seen_at,
    $current_state, $current_status, $last_action, $last_action_time, $last_site_code,
    $last_site_name, $employee_code, $employee_name, $receiver_name, $receiver_phone_masked,
    $age_hours, $days_in_inventory, $risk_score, $risk_level, $risk_reasons, $sla_status,
    $sla_deadline, $trang_thai_hien_tai, $thao_tac_cuoi, $thoi_gian_thao_tac,
    $thoi_gian_yeu_cau_phat_lai, $nhan_vien_kien_van_de, $nguyen_nhan_kien_van_de,
    $buu_cuc_thao_tac, $nguoi_thao_tac, $dau_chuyen_hoan, $dia_chi_nhan_hang, $phuong,
    $noi_dung_hang_hoa, $cod_thuc_te, $pttt, $nhan_vien_nhan_hang, $dia_chi_lay_hang,
    $thoi_gian_nhan_hang, $ten_nguoi_gui, $trong_luong, $ma_doan_full, $ma_doan_1,
    $ma_doan_2, $ma_doan_3, $reback_status, $in_hoan_scan_time, $print_count,
    $created_at, $updated_at)
ON CONFLICT(waybill_no) DO UPDATE SET
    is_in_current_inventory = excluded.is_in_current_inventory,
    left_inventory_at = excluded.left_inventory_at,
    first_seen_at = COALESCE(NULLIF(fs_waybills.first_seen_at, ''), excluded.first_seen_at),
    last_seen_at = excluded.last_seen_at,
    current_state = excluded.current_state,
    current_status = excluded.current_status,
    last_action = excluded.last_action,
    last_action_time = excluded.last_action_time,
    last_site_code = excluded.last_site_code,
    last_site_name = excluded.last_site_name,
    employee_code = excluded.employee_code,
    employee_name = excluded.employee_name,
    receiver_name = excluded.receiver_name,
    receiver_phone_masked = excluded.receiver_phone_masked,
    age_hours = excluded.age_hours,
    days_in_inventory = excluded.days_in_inventory,
    risk_score = excluded.risk_score,
    risk_level = excluded.risk_level,
    risk_reasons = excluded.risk_reasons,
    sla_status = excluded.sla_status,
    sla_deadline = excluded.sla_deadline,
    trang_thai_hien_tai = excluded.trang_thai_hien_tai,
    thao_tac_cuoi = excluded.thao_tac_cuoi,
    thoi_gian_thao_tac = excluded.thoi_gian_thao_tac,
    thoi_gian_yeu_cau_phat_lai = excluded.thoi_gian_yeu_cau_phat_lai,
    nhan_vien_kien_van_de = excluded.nhan_vien_kien_van_de,
    nguyen_nhan_kien_van_de = excluded.nguyen_nhan_kien_van_de,
    buu_cuc_thao_tac = excluded.buu_cuc_thao_tac,
    nguoi_thao_tac = excluded.nguoi_thao_tac,
    dau_chuyen_hoan = excluded.dau_chuyen_hoan,
    dia_chi_nhan_hang = excluded.dia_chi_nhan_hang,
    phuong = excluded.phuong,
    noi_dung_hang_hoa = excluded.noi_dung_hang_hoa,
    cod_thuc_te = excluded.cod_thuc_te,
    pttt = excluded.pttt,
    nhan_vien_nhan_hang = excluded.nhan_vien_nhan_hang,
    dia_chi_lay_hang = excluded.dia_chi_lay_hang,
    thoi_gian_nhan_hang = excluded.thoi_gian_nhan_hang,
    ten_nguoi_gui = excluded.ten_nguoi_gui,
    trong_luong = excluded.trong_luong,
    ma_doan_full = excluded.ma_doan_full,
    ma_doan_1 = excluded.ma_doan_1,
    ma_doan_2 = excluded.ma_doan_2,
    ma_doan_3 = excluded.ma_doan_3,
    reback_status = excluded.reback_status,
    in_hoan_scan_time = excluded.in_hoan_scan_time,
    print_count = excluded.print_count,
    updated_at = excluded.updated_at
WHERE excluded.updated_at > fs_waybills.updated_at;"; // newest-wins

                string iso = updatedAt.ToString("O");
                AddJ(command, "$waybill_no", (row.Value<string>("waybill_no") ?? "").Trim().ToUpperInvariant());
                AddJ(command, "$is_in_current_inventory", row.Value<bool?>("is_in_current_inventory") == false ? 0 : 1);
                AddJ(command, "$left_inventory_at", row.Value<string>("left_inventory_at"));
                AddJ(command, "$first_seen_at", row.Value<string>("first_seen_at"));
                AddJ(command, "$last_seen_at", row.Value<string>("last_seen_at"));
                AddJ(command, "$current_state", row.Value<string>("current_state"));
                AddJ(command, "$current_status", row.Value<string>("current_status"));
                AddJ(command, "$last_action", row.Value<string>("last_action"));
                AddJ(command, "$last_action_time", row.Value<string>("last_action_time"));
                AddJ(command, "$last_site_code", row.Value<string>("last_site_code"));
                AddJ(command, "$last_site_name", row.Value<string>("last_site_name"));
                AddJ(command, "$employee_code", row.Value<string>("employee_code"));
                AddJ(command, "$employee_name", row.Value<string>("employee_name"));
                AddJ(command, "$receiver_name", row.Value<string>("receiver_name"));
                AddJ(command, "$receiver_phone_masked", row.Value<string>("receiver_phone_masked"));
                AddJ(command, "$age_hours", row.Value<double?>("age_hours") ?? 0);
                AddJ(command, "$days_in_inventory", row.Value<double?>("days_in_inventory") ?? 0);
                AddJ(command, "$risk_score", row.Value<int?>("risk_score") ?? 0);
                AddJ(command, "$risk_level", row.Value<string>("risk_level"));
                AddJ(command, "$risk_reasons", row.Value<string>("risk_reasons"));
                AddJ(command, "$sla_status", row.Value<string>("sla_status"));
                AddJ(command, "$sla_deadline", row.Value<string>("sla_deadline"));
                AddJ(command, "$trang_thai_hien_tai", row.Value<string>("trang_thai_hien_tai"));
                AddJ(command, "$thao_tac_cuoi", row.Value<string>("thao_tac_cuoi"));
                AddJ(command, "$thoi_gian_thao_tac", row.Value<string>("thoi_gian_thao_tac"));
                AddJ(command, "$thoi_gian_yeu_cau_phat_lai", row.Value<string>("thoi_gian_yeu_cau_phat_lai"));
                AddJ(command, "$nhan_vien_kien_van_de", row.Value<string>("nhan_vien_kien_van_de"));
                AddJ(command, "$nguyen_nhan_kien_van_de", row.Value<string>("nguyen_nhan_kien_van_de"));
                AddJ(command, "$buu_cuc_thao_tac", row.Value<string>("buu_cuc_thao_tac"));
                AddJ(command, "$nguoi_thao_tac", row.Value<string>("nguoi_thao_tac"));
                AddJ(command, "$dau_chuyen_hoan", row.Value<string>("dau_chuyen_hoan"));
                AddJ(command, "$dia_chi_nhan_hang", row.Value<string>("dia_chi_nhan_hang"));
                AddJ(command, "$phuong", row.Value<string>("phuong"));
                AddJ(command, "$noi_dung_hang_hoa", row.Value<string>("noi_dung_hang_hoa"));
                AddJ(command, "$cod_thuc_te", row.Value<string>("cod_thuc_te"));
                AddJ(command, "$pttt", row.Value<string>("pttt"));
                AddJ(command, "$nhan_vien_nhan_hang", row.Value<string>("nhan_vien_nhan_hang"));
                AddJ(command, "$dia_chi_lay_hang", row.Value<string>("dia_chi_lay_hang"));
                AddJ(command, "$thoi_gian_nhan_hang", row.Value<string>("thoi_gian_nhan_hang"));
                AddJ(command, "$ten_nguoi_gui", row.Value<string>("ten_nguoi_gui"));
                AddJ(command, "$trong_luong", row.Value<string>("trong_luong"));
                AddJ(command, "$ma_doan_full", row.Value<string>("ma_doan_full"));
                AddJ(command, "$ma_doan_1", row.Value<string>("ma_doan_1"));
                AddJ(command, "$ma_doan_2", row.Value<string>("ma_doan_2"));
                AddJ(command, "$ma_doan_3", row.Value<string>("ma_doan_3"));
                AddJ(command, "$reback_status", row.Value<string>("reback_status"));
                AddJ(command, "$in_hoan_scan_time", row.Value<string>("in_hoan_scan_time"));
                AddJ(command, "$print_count", row.Value<int?>("print_count") ?? 0);
                AddJ(command, "$created_at", iso);
                AddJ(command, "$updated_at", iso);

                applied += await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            }

            await transaction.CommitAsync(ct).ConfigureAwait(false);
            await SetSyncStateAsync("cloud_pull_waybills_at", maxUpdated.ToString("O"), ct).ConfigureAwait(false);
            if (applied > 0) AppLogger.Info($"[HybridSync] pulled waybills rows={rows.Count} applied={applied}");
            return applied;
        }

        private async Task<int> PullNotesAsync(CancellationToken ct)
        {
            var site = ResolveSiteCode();
            var since = await GetCursorAsync("cloud_pull_notes_at", ct).ConfigureAwait(false);
            var rows = await SupabaseDbService.PullOrderNotesAsync(site, since).ConfigureAwait(false);
            if (rows.Count == 0) return 0;

            int applied = 0;
            DateTime maxUpdated = since;

            await using var connection = await _connectionFactory.OpenAsync(ct).ConfigureAwait(false);
            await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(ct).ConfigureAwait(false);

            foreach (var row in rows)
            {
                var updatedAt = row.Value<DateTime?>("updated_at")?.ToUniversalTime() ?? DateTime.UtcNow;
                if (updatedAt > maxUpdated) maxUpdated = updatedAt;

                var clientId = row.Value<string>("client_id") ?? "";
                if (IsOwnClientId(clientId)) continue; // our own echo

                await using var command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = @"
INSERT OR IGNORE INTO fs_order_notes(waybill_no, note, created_by, created_at, client_id, origin)
VALUES ($waybillNo, $note, $createdBy, $createdAt, $clientId, 'cloud');";
                AddJ(command, "$waybillNo", (row.Value<string>("waybill_no") ?? "").Trim().ToUpperInvariant());
                AddJ(command, "$note", row.Value<string>("note") ?? "");
                AddJ(command, "$createdBy", row.Value<string>("created_by") ?? "");
                AddJ(command, "$createdAt", (row.Value<DateTime?>("created_at")?.ToUniversalTime() ?? updatedAt).ToString("O"));
                AddJ(command, "$clientId", string.IsNullOrWhiteSpace(clientId) ? row.Value<string>("id") : clientId);
                applied += await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            }

            await transaction.CommitAsync(ct).ConfigureAwait(false);
            await SetSyncStateAsync("cloud_pull_notes_at", maxUpdated.ToString("O"), ct).ConfigureAwait(false);
            if (applied > 0) AppLogger.Info($"[HybridSync] pulled notes rows={rows.Count} applied={applied}");
            return applied;
        }

        private async Task<int> PullChecksAsync(CancellationToken ct)
        {
            var site = ResolveSiteCode();
            var since = await GetCursorAsync("cloud_pull_checks_at", ct).ConfigureAwait(false);
            var rows = await SupabaseDbService.PullOrderChecksAsync(site, since).ConfigureAwait(false);
            if (rows.Count == 0) return 0;

            int applied = 0;
            DateTime maxUpdated = since;

            await using var connection = await _connectionFactory.OpenAsync(ct).ConfigureAwait(false);
            await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(ct).ConfigureAwait(false);

            foreach (var row in rows)
            {
                var updatedAt = row.Value<DateTime?>("updated_at")?.ToUniversalTime() ?? DateTime.UtcNow;
                if (updatedAt > maxUpdated) maxUpdated = updatedAt;

                // Last-write-wins on fs_waybills check columns; only newer remote wins.
                await using var command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = @"
UPDATE fs_waybills
SET is_checked = $isChecked,
    checked_at = $checkedAt,
    checked_by = $checkedBy
WHERE waybill_no = $waybillNo
  AND (checked_at IS NULL OR checked_at = '' OR checked_at < $checkedAt);";
                AddJ(command, "$isChecked", row.Value<bool?>("is_checked") == false ? 0 : 1);
                AddJ(command, "$checkedAt", NormalizeIso(row.Value<string>("checked_at")) ?? updatedAt.ToString("O"));
                AddJ(command, "$checkedBy", row.Value<string>("checked_by") ?? "");
                AddJ(command, "$waybillNo", (row.Value<string>("waybill_no") ?? "").Trim().ToUpperInvariant());
                applied += await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            }

            await transaction.CommitAsync(ct).ConfigureAwait(false);
            await SetSyncStateAsync("cloud_pull_checks_at", maxUpdated.ToString("O"), ct).ConfigureAwait(false);
            if (applied > 0) AppLogger.Info($"[HybridSync] pulled checks rows={rows.Count} applied={applied}");
            return applied;
        }

        private async Task<int> PullTasksAsync(CancellationToken ct)
        {
            var site = ResolveSiteCode();
            var since = await GetCursorAsync("cloud_pull_tasks_at", ct).ConfigureAwait(false);
            var rows = await SupabaseDbService.PullDispatchTasksAsync(site, since).ConfigureAwait(false);
            if (rows.Count == 0) return 0;

            int applied = 0;
            DateTime maxUpdated = since;

            await using var connection = await _connectionFactory.OpenAsync(ct).ConfigureAwait(false);
            await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(ct).ConfigureAwait(false);

            foreach (var row in rows)
            {
                var updatedAt = row.Value<DateTime?>("updated_at")?.ToUniversalTime() ?? DateTime.UtcNow;
                if (updatedAt > maxUpdated) maxUpdated = updatedAt;

                var clientId = row.Value<string>("client_id") ?? "";
                if (IsOwnClientId(clientId)) continue;
                if (string.IsNullOrWhiteSpace(clientId)) clientId = row.Value<string>("id") ?? Guid.NewGuid().ToString("N");

                await using var command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = @"
INSERT INTO fs_dispatch_tasks(waybill_no, task_type, priority, status, assigned_to, due_at, created_at, completed_at, client_id, origin)
VALUES ($waybillNo, $taskType, $priority, $status, $assignedTo, $dueAt, $createdAt, $completedAt, $clientId, 'cloud')
ON CONFLICT(client_id) WHERE client_id IS NOT NULL DO UPDATE SET
    task_type = excluded.task_type,
    priority = excluded.priority,
    status = excluded.status,
    assigned_to = excluded.assigned_to,
    due_at = excluded.due_at,
    completed_at = excluded.completed_at;";
                AddJ(command, "$waybillNo", (row.Value<string>("waybill_no") ?? "").Trim().ToUpperInvariant());
                AddJ(command, "$taskType", row.Value<string>("task_type") ?? "CHECK_PHYSICAL_STOCK");
                AddJ(command, "$priority", row.Value<int?>("priority") ?? 0);
                AddJ(command, "$status", row.Value<string>("status") ?? "OPEN");
                AddJ(command, "$assignedTo", row.Value<string>("assigned_to") ?? "");
                AddJ(command, "$dueAt", NormalizeIso(row.Value<string>("due_at")));
                AddJ(command, "$createdAt", (row.Value<DateTime?>("created_at")?.ToUniversalTime() ?? updatedAt).ToString("O"));
                AddJ(command, "$completedAt", row.Value<DateTime?>("completed_at")?.ToUniversalTime().ToString("O"));
                AddJ(command, "$clientId", clientId);
                applied += await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            }

            await transaction.CommitAsync(ct).ConfigureAwait(false);
            await SetSyncStateAsync("cloud_pull_tasks_at", maxUpdated.ToString("O"), ct).ConfigureAwait(false);
            if (applied > 0) AppLogger.Info($"[HybridSync] pulled tasks rows={rows.Count} applied={applied}");
            return applied;
        }

        // ------------------------------------------------------------------
        // Outbox flush (Phase 3 — offline-safe local writes)
        // ------------------------------------------------------------------
        public async Task<int> FlushOutboxAsync(CancellationToken ct)
        {
            if (!IsEnabled) return 0;
            var site = ResolveSiteCode();

            var pending = new List<(long Id, string Kind, string Payload)>();
            await using (var connection = await _connectionFactory.OpenAsync(ct).ConfigureAwait(false))
            await using (var command = connection.CreateCommand())
            {
                command.CommandText = @"
SELECT id, kind, payload FROM fs_outbox
WHERE synced_at IS NULL
ORDER BY id ASC
LIMIT 300;";
                await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
                while (await reader.ReadAsync(ct).ConfigureAwait(false))
                    pending.Add((reader.GetInt64(0), reader.GetString(1), reader.GetString(2)));
            }

            _pendingOutboxCount = pending.Count;
            if (pending.Count == 0) return 0;

            var doneIds = new List<long>();
            foreach (var group in pending.GroupBy(x => x.Kind))
            {
                var payloads = new List<object>();
                var ids = new List<long>();
                foreach (var item in group)
                {
                    try
                    {
                        payloads.Add(JObject.Parse(item.Payload));
                        ids.Add(item.Id);
                    }
                    catch (Exception ex)
                    {
                        AppLogger.Warning($"[HybridSync] outbox payload corrupt id={item.Id}: {ex.Message}");
                        await MarkOutboxErrorAsync(item.Id, "corrupt payload: " + ex.Message, ct).ConfigureAwait(false);
                    }
                }

                if (payloads.Count == 0) continue;

                try
                {
                    switch (group.Key)
                    {
                        case "NOTE":
                            await SupabaseDbService.PushOrderNotesAsync(site, payloads).ConfigureAwait(false);
                            break;
                        case "CHECK":
                            await SupabaseDbService.MergeOrderChecksAsync(site, payloads).ConfigureAwait(false);
                            break;
                        case "TASK":
                            await SupabaseDbService.MergeDispatchTasksAsync(site, payloads).ConfigureAwait(false);
                            break;
                        default:
                            AppLogger.Warning($"[HybridSync] unknown outbox kind={group.Key}, skipping");
                            continue;
                    }
                    doneIds.AddRange(ids);
                }
                catch (Exception ex)
                {
                    AppLogger.Warning($"[HybridSync] outbox flush failed kind={group.Key}: {ex.Message}");
                    foreach (var id in ids)
                        await MarkOutboxErrorAsync(id, ex.Message, ct).ConfigureAwait(false);
                }
            }

            if (doneIds.Count > 0)
            {
                await using var connection = await _connectionFactory.OpenAsync(ct).ConfigureAwait(false);
                await using var command = connection.CreateCommand();
                command.CommandText =
                    $"UPDATE fs_outbox SET synced_at = $syncedAt WHERE id IN ({string.Join(",", doneIds)});";
                command.Parameters.AddWithValue("$syncedAt", DateTime.UtcNow.ToString("O"));
                await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);

                // Housekeeping: drop synced entries older than 7 days.
                await using var cleanup = connection.CreateCommand();
                cleanup.CommandText = "DELETE FROM fs_outbox WHERE synced_at IS NOT NULL AND synced_at < $cutoff;";
                cleanup.Parameters.AddWithValue("$cutoff", DateTime.UtcNow.AddDays(-7).ToString("O"));
                await cleanup.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            }

            _pendingOutboxCount = Math.Max(0, pending.Count - doneIds.Count);
            return doneIds.Count;
        }

        private async Task MarkOutboxErrorAsync(long id, string error, CancellationToken ct)
        {
            try
            {
                await using var connection = await _connectionFactory.OpenAsync(ct).ConfigureAwait(false);
                await using var command = connection.CreateCommand();
                command.CommandText = @"
UPDATE fs_outbox SET attempts = attempts + 1, last_error = $error WHERE id = $id;";
                command.Parameters.AddWithValue("$error", error ?? "");
                command.Parameters.AddWithValue("$id", id);
                await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                AppLogger.Warning("[HybridSync] outbox error-mark failed: " + ex.Message);
            }
        }

        // ------------------------------------------------------------------
        // Client identity + cursors
        // ------------------------------------------------------------------
        public async Task<string> GetOrCreateClientIdAsync(CancellationToken ct = default)
        {
            if (!string.IsNullOrEmpty(_clientId)) return _clientId;

            await _initializer.InitializeAsync(ct).ConfigureAwait(false);
            await using var connection = await _connectionFactory.OpenAsync(ct).ConfigureAwait(false);

            await using (var select = connection.CreateCommand())
            {
                select.CommandText = "SELECT value FROM fs_settings WHERE key = 'cloud_client_id';";
                var existing = (await select.ExecuteScalarAsync(ct).ConfigureAwait(false))?.ToString();
                if (!string.IsNullOrWhiteSpace(existing))
                {
                    _clientId = existing;
                    return _clientId;
                }
            }

            var created = Environment.MachineName + "-" + Guid.NewGuid().ToString("N")[..12];
            await using (var insert = connection.CreateCommand())
            {
                insert.CommandText = @"
INSERT INTO fs_settings(key, value, updated_at) VALUES ('cloud_client_id', $value, $updatedAt)
ON CONFLICT(key) DO NOTHING;";
                insert.Parameters.AddWithValue("$value", created);
                insert.Parameters.AddWithValue("$updatedAt", DateTime.UtcNow.ToString("O"));
                await insert.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            }

            _clientId = created;
            return _clientId;
        }

        private bool IsOwnClientId(string clientId) =>
            !string.IsNullOrEmpty(_clientId) &&
            !string.IsNullOrEmpty(clientId) &&
            clientId.StartsWith(_clientId + ":", StringComparison.Ordinal);

        private async Task<DateTime> GetCursorAsync(string key, CancellationToken ct)
        {
            var value = await GetSyncStateAsync(key, ct).ConfigureAwait(false);
            return DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dt)
                ? dt.ToUniversalTime()
                : DateTime.UnixEpoch;
        }

        private async Task<string> GetSyncStateAsync(string key, CancellationToken ct)
        {
            await using var connection = await _connectionFactory.OpenAsync(ct).ConfigureAwait(false);
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT value FROM fs_sync_state WHERE key = $key;";
            command.Parameters.AddWithValue("$key", key);
            return (await command.ExecuteScalarAsync(ct).ConfigureAwait(false))?.ToString();
        }

        private async Task SetSyncStateAsync(string key, string value, CancellationToken ct)
        {
            await using var connection = await _connectionFactory.OpenAsync(ct).ConfigureAwait(false);
            await using var command = connection.CreateCommand();
            command.CommandText = @"
INSERT INTO fs_sync_state(key, value, updated_at) VALUES ($key, $value, $updatedAt)
ON CONFLICT(key) DO UPDATE SET value = excluded.value, updated_at = excluded.updated_at;";
            command.Parameters.AddWithValue("$key", key);
            command.Parameters.AddWithValue("$value", value ?? "");
            command.Parameters.AddWithValue("$updatedAt", DateTime.UtcNow.ToString("O"));
            await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        // ------------------------------------------------------------------
        // Helpers
        // ------------------------------------------------------------------
        private static string S(SqliteDataReader reader, int i) => reader.IsDBNull(i) ? "" : reader.GetValue(i)?.ToString() ?? "";
        private static int I(SqliteDataReader reader, int i) => reader.IsDBNull(i) ? 0 : Convert.ToInt32(reader.GetValue(i), CultureInfo.InvariantCulture);
        private static double D(SqliteDataReader reader, int i) => reader.IsDBNull(i) ? 0 : Convert.ToDouble(reader.GetValue(i), CultureInfo.InvariantCulture);

        private static void AddJ(SqliteCommand command, string name, object value) =>
            command.Parameters.AddWithValue(name, value ?? (object)DBNull.Value);

        private static DateTime ParseIsoUtc(string value) =>
            DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dt)
                ? dt.ToUniversalTime()
                : DateTime.UtcNow;

        private static string NormalizeIso(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return null;
            return DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dt)
                ? dt.ToUniversalTime().ToString("O")
                : value;
        }
    }
}

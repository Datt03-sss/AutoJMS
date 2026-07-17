using AutoJMS.Data;
using AutoJMS.FullStack.LocalDb;
using AutoJMS.FullStack.Models;
using AutoJMS.FullStack.Services;
using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace AutoJMS.FullStack.Repositories
{
    public sealed class FullStackWaybillRepository : IFullStackWaybillRepository
    {
        private readonly FullStackDbConnectionFactory _connectionFactory;
        private readonly FullStackOrderStateEngine _stateEngine = new();
        private readonly FullStackRiskEngine _riskEngine = new();
        private readonly FullStackSlaEngine _slaEngine = new();
        // Serializes all write transactions so streamed per-page enrichment writes and the final
        // inventory-apply never run concurrently (WAL + busy_timeout would otherwise queue/stall).
        private readonly SemaphoreSlim _writeGate = new(1, 1);

        public FullStackWaybillRepository(FullStackDbConnectionFactory connectionFactory)
        {
            _connectionFactory = connectionFactory;
        }

        public async Task<List<WaybillDbModel>> GetDashboardRowsAsync(CancellationToken ct = default)
        {
            var rows = new List<WaybillDbModel>();
            await using var connection = await _connectionFactory.OpenAsync(ct).ConfigureAwait(false);
            await using var command = connection.CreateCommand();
            command.CommandText = @"
SELECT waybill_no, trang_thai_hien_tai, thao_tac_cuoi, thoi_gian_thao_tac,
       thoi_gian_yeu_cau_phat_lai, nhan_vien_kien_van_de, nguyen_nhan_kien_van_de,
       buu_cuc_thao_tac, nguoi_thao_tac, dau_chuyen_hoan, dia_chi_nhan_hang,
       phuong, noi_dung_hang_hoa, cod_thuc_te, pttt, nhan_vien_nhan_hang,
       dia_chi_lay_hang, thoi_gian_nhan_hang, ten_nguoi_gui, trong_luong,
       ma_doan_full, ma_doan_1, ma_doan_2, ma_doan_3, reback_status,
       in_hoan_scan_time, print_count, is_active, tracking_interval_mins,
       last_track_at, next_track_at
FROM fs_waybills
WHERE is_in_current_inventory = 1
ORDER BY COALESCE(last_seen_at, first_seen_at, updated_at) DESC, waybill_no ASC;";

            await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                rows.Add(ReadWaybillDbModel(reader));
            }

            return rows;
        }

        public async Task<FullStackSyncResult> ApplyInventoryRunAsync(InventoryRun run, IReadOnlyList<InventoryFetchItem> items, CancellationToken ct = default)
        {
            await _writeGate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
            var now = DateTime.UtcNow;
            var uniqueItems = items?
                .Where(x => !string.IsNullOrWhiteSpace(x?.WaybillNo))
                .GroupBy(x => x.WaybillNo.Trim().ToUpperInvariant(), StringComparer.OrdinalIgnoreCase)
                .Select(g => new InventoryFetchItem { WaybillNo = g.Key, PageNo = g.Min(x => x.PageNo) })
                .ToList() ?? new List<InventoryFetchItem>();

            await using var connection = await _connectionFactory.OpenAsync(ct).ConfigureAwait(false);
            await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(ct).ConfigureAwait(false);

            long runId = await InsertRunAsync(connection, transaction, run, uniqueItems.Count, ct).ConfigureAwait(false);
            int newCount = 0;
            int stillCount = 0;

            foreach (var item in uniqueItems)
            {
                ct.ThrowIfCancellationRequested();
                bool exists = await ExistsAsync(connection, transaction, item.WaybillNo, ct).ConfigureAwait(false);
                var current = exists ? await GetRowForMutationAsync(connection, transaction, item.WaybillNo, ct).ConfigureAwait(false) : CreateEmptyRow(item.WaybillNo);
                ApplyInventoryPresence(current, runId, exists, now);
                EnrichStateRiskSla(current, isInCurrentInventory: true);
                await UpsertRowAsync(connection, transaction, current, ct).ConfigureAwait(false);
                await InsertRunItemAsync(connection, transaction, runId, item, now, ct).ConfigureAwait(false);

                if (exists) stillCount++;
                else newCount++;
            }

            int leftCount = await MarkLeftInventoryAsync(connection, transaction, runId, now, ct).ConfigureAwait(false);

            await SetSyncStateAsync(connection, transaction, "last_inventory_sync_at", now.ToString("O"), ct).ConfigureAwait(false);
            await SetSyncStateAsync(connection, transaction, "last_inventory_run_id", runId.ToString(CultureInfo.InvariantCulture), ct).ConfigureAwait(false);

            await transaction.CommitAsync(ct).ConfigureAwait(false);

            return new FullStackSyncResult
            {
                RunId = runId,
                TotalFetched = uniqueItems.Count,
                TotalPages = run.TotalPages,
                NewWaybills = newCount,
                StillInInventory = stillCount,
                LeftInventory = leftCount,
                StartedAt = run.StartedAt,
                FinishedAt = now
            };
            }
            finally
            {
                _writeGate.Release();
            }
        }

        public async Task UpsertTrackingRowsAsync(IReadOnlyList<WaybillDbModel> rows, CancellationToken ct = default)
        {
            if (rows == null || rows.Count == 0) return;

            await _writeGate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
            await using var connection = await _connectionFactory.OpenAsync(ct).ConfigureAwait(false);
            await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(ct).ConfigureAwait(false);

            foreach (var source in rows.Where(x => !string.IsNullOrWhiteSpace(x?.WaybillNo)))
            {
                ct.ThrowIfCancellationRequested();
                var existing = await GetRowForMutationAsync(connection, transaction, source.WaybillNo.Trim().ToUpperInvariant(), ct).ConfigureAwait(false)
                    ?? CreateEmptyRow(source.WaybillNo.Trim().ToUpperInvariant());

                MergeTracking(existing, source);
                EnrichStateRiskSla(existing, existing.IsActive);
                await UpsertRowAsync(connection, transaction, existing, ct).ConfigureAwait(false);
            }

            await transaction.CommitAsync(ct).ConfigureAwait(false);
            AppLogger.Info($"[FullStackLocalDb] tracking rows upserted count={rows.Count}");
            }
            finally
            {
                _writeGate.Release();
            }
        }

        public async Task UpsertTrackingEventsAsync(IReadOnlyList<TrackingEvent> events, CancellationToken ct = default)
        {
            if (events == null || events.Count == 0) return;

            await _writeGate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
            await using var connection = await _connectionFactory.OpenAsync(ct).ConfigureAwait(false);
            await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(ct).ConfigureAwait(false);
            foreach (var item in events.Where(x => !string.IsNullOrWhiteSpace(x?.WaybillNo)))
            {
                await using var command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = @"
INSERT OR IGNORE INTO fs_tracking_events(
    waybill_no, event_time, action, status, site_code, site_name, operator_code, operator_name, raw_json, created_at)
VALUES (
    $waybillNo, $eventTime, $action, $status, $siteCode, $siteName, $operatorCode, $operatorName, $rawJson, $createdAt);";
                Add(command, "$waybillNo", item.WaybillNo.Trim().ToUpperInvariant());
                Add(command, "$eventTime", item.EventTime?.ToString("O"));
                Add(command, "$action", item.Action);
                Add(command, "$status", item.Status);
                Add(command, "$siteCode", item.SiteCode);
                Add(command, "$siteName", item.SiteName);
                Add(command, "$operatorCode", item.OperatorCode);
                Add(command, "$operatorName", item.OperatorName);
                Add(command, "$rawJson", item.RawJson);
                Add(command, "$createdAt", item.CreatedAt == default ? DateTime.UtcNow.ToString("O") : item.CreatedAt.ToString("O"));
                await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            }

            await transaction.CommitAsync(ct).ConfigureAwait(false);
            AppLogger.Info($"[FullStackLocalDb] tracking events upserted count={events.Count}");
            }
            finally
            {
                _writeGate.Release();
            }
        }

        public async Task MarkEnrichedAsync(IEnumerable<string> waybillNos, CancellationToken ct = default)
        {
            var list = waybillNos?
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim().ToUpperInvariant())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList() ?? new List<string>();
            if (list.Count == 0) return;

            await _writeGate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
            var now = DateTime.UtcNow;
            await using var connection = await _connectionFactory.OpenAsync(ct).ConfigureAwait(false);
            await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(ct).ConfigureAwait(false);
            foreach (var waybill in list)
            {
                await using var command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = @"
UPDATE fs_waybills
SET is_enriched = 1,
    enriched_at = $enrichedAt,
    last_track_at = $lastTrackAt,
    updated_at = $updatedAt
WHERE waybill_no = $waybillNo;";
                Add(command, "$enrichedAt", now.ToString("O"));
                Add(command, "$lastTrackAt", now.ToString("O"));
                Add(command, "$updatedAt", now.ToString("O"));
                Add(command, "$waybillNo", waybill);
                await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            }

            await transaction.CommitAsync(ct).ConfigureAwait(false);
            }
            finally
            {
                _writeGate.Release();
            }
        }

        public async Task<HashSet<string>> GetWaybillsWithDetailAsync(IReadOnlyCollection<string> waybillNos, CancellationToken ct = default)
        {
            var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var list = waybillNos?
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim().ToUpperInvariant())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList() ?? new List<string>();
            if (list.Count == 0) return result;

            await using var connection = await _connectionFactory.OpenAsync(ct).ConfigureAwait(false);
            foreach (var chunk in list.Chunk(300))
            {
                await using var command = connection.CreateCommand();
                var names = new List<string>(chunk.Length);
                for (int i = 0; i < chunk.Length; i++)
                {
                    var p = "$w" + i;
                    names.Add(p);
                    command.Parameters.AddWithValue(p, chunk[i]);
                }
                // "Has detail" = any getOrderDetail-only field is populated (tracking never sets these).
                command.CommandText =
                    "SELECT waybill_no FROM fs_waybills WHERE waybill_no IN (" + string.Join(",", names) + ") " +
                    "AND (ten_nguoi_gui <> 'empty' OR dia_chi_nhan_hang <> 'empty' OR noi_dung_hang_hoa <> 'empty' OR ma_doan_full <> 'empty');";
                await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
                while (await reader.ReadAsync(ct).ConfigureAwait(false))
                {
                    var wb = reader.IsDBNull(0) ? null : reader.GetValue(0)?.ToString();
                    if (!string.IsNullOrWhiteSpace(wb)) result.Add(wb.Trim());
                }
            }
            return result;
        }

        public async Task IncrementReminderCountAsync(IEnumerable<string> waybillNos, CancellationToken ct = default)
        {
            var list = waybillNos?
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim().ToUpperInvariant())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList() ?? new List<string>();
            if (list.Count == 0) return;

            await using var connection = await _connectionFactory.OpenAsync(ct).ConfigureAwait(false);
            await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(ct).ConfigureAwait(false);
            foreach (var waybill in list)
            {
                await using var command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = @"
UPDATE fs_waybills
SET print_count = print_count + 1,
    updated_at = $updatedAt
WHERE waybill_no = $waybillNo;";
                command.Parameters.AddWithValue("$updatedAt", DateTime.UtcNow.ToString("O"));
                command.Parameters.AddWithValue("$waybillNo", waybill);
                await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            }
            await transaction.CommitAsync(ct).ConfigureAwait(false);
        }

        public async Task<string> GetSyncStateAsync(string key, CancellationToken ct = default)
        {
            await using var connection = await _connectionFactory.OpenAsync(ct).ConfigureAwait(false);
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT value FROM fs_sync_state WHERE key = $key;";
            command.Parameters.AddWithValue("$key", key);
            var result = await command.ExecuteScalarAsync(ct).ConfigureAwait(false);
            return result?.ToString();
        }

        public async Task SetSyncStateAsync(string key, string value, CancellationToken ct = default)
        {
            await using var connection = await _connectionFactory.OpenAsync(ct).ConfigureAwait(false);
            await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(ct).ConfigureAwait(false);
            await SetSyncStateAsync(connection, transaction, key, value, ct).ConfigureAwait(false);
            await transaction.CommitAsync(ct).ConfigureAwait(false);
        }

        public async Task<string> GetWaybillLastActionTimeAsync(string waybillNo, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(waybillNo)) return string.Empty;
            await using var connection = await _connectionFactory.OpenAsync(ct).ConfigureAwait(false);
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT last_action_time FROM fs_waybills WHERE waybill_no = $w LIMIT 1;";
            command.Parameters.AddWithValue("$w", waybillNo.Trim().ToUpperInvariant());
            var result = await command.ExecuteScalarAsync(ct).ConfigureAwait(false);
            return result?.ToString() ?? string.Empty;
        }

        private static async Task<long> InsertRunAsync(SqliteConnection connection, SqliteTransaction transaction, InventoryRun run, int totalRecords, CancellationToken ct)
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = @"
INSERT INTO fs_inventory_runs(action_site_code, start_date, end_date, started_at, finished_at,
                              total_records, total_pages, status, error_message, source, created_at)
VALUES ($actionSiteCode, $startDate, $endDate, $startedAt, $finishedAt,
        $totalRecords, $totalPages, $status, $errorMessage, $source, $createdAt);
SELECT last_insert_rowid();";
            Add(command, "$actionSiteCode", run.ActionSiteCode);
            Add(command, "$startDate", run.StartDate.ToString("O"));
            Add(command, "$endDate", run.EndDate.ToString("O"));
            Add(command, "$startedAt", run.StartedAt.ToString("O"));
            Add(command, "$finishedAt", run.FinishedAt?.ToString("O"));
            Add(command, "$totalRecords", totalRecords);
            Add(command, "$totalPages", run.TotalPages);
            Add(command, "$status", run.Status ?? "SUCCESS");
            Add(command, "$errorMessage", run.ErrorMessage);
            Add(command, "$source", run.Source ?? "JMS");
            Add(command, "$createdAt", DateTime.UtcNow.ToString("O"));
            var result = await command.ExecuteScalarAsync(ct).ConfigureAwait(false);
            return Convert.ToInt64(result, CultureInfo.InvariantCulture);
        }

        private static async Task InsertRunItemAsync(SqliteConnection connection, SqliteTransaction transaction, long runId, InventoryFetchItem item, DateTime now, CancellationToken ct)
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = @"
INSERT OR IGNORE INTO fs_inventory_run_items(run_id, waybill_no, page_no, seen_at)
VALUES ($runId, $waybillNo, $pageNo, $seenAt);";
            Add(command, "$runId", runId);
            Add(command, "$waybillNo", item.WaybillNo);
            Add(command, "$pageNo", item.PageNo);
            Add(command, "$seenAt", now.ToString("O"));
            await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        private static async Task<bool> ExistsAsync(SqliteConnection connection, SqliteTransaction transaction, string waybillNo, CancellationToken ct)
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "SELECT 1 FROM fs_waybills WHERE waybill_no = $waybillNo LIMIT 1;";
            command.Parameters.AddWithValue("$waybillNo", waybillNo);
            var result = await command.ExecuteScalarAsync(ct).ConfigureAwait(false);
            return result != null;
        }

        private static async Task<FullStackWaybill> GetRowForMutationAsync(SqliteConnection connection, SqliteTransaction transaction, string waybillNo, CancellationToken ct)
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = @"
SELECT * FROM fs_waybills WHERE waybill_no = $waybillNo LIMIT 1;";
            command.Parameters.AddWithValue("$waybillNo", waybillNo);
            await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
            return await reader.ReadAsync(ct).ConfigureAwait(false) ? ReadFullStackWaybill(reader) : null;
        }

        private async Task<int> MarkLeftInventoryAsync(SqliteConnection connection, SqliteTransaction transaction, long currentRunId, DateTime now, CancellationToken ct)
        {
            var leftRows = new List<FullStackWaybill>();
            await using (var select = connection.CreateCommand())
            {
                select.Transaction = transaction;
                select.CommandText = @"
SELECT * FROM fs_waybills
WHERE is_in_current_inventory = 1
  AND (last_inventory_run_id IS NULL OR last_inventory_run_id <> $runId);";
                select.Parameters.AddWithValue("$runId", currentRunId);
                await using var reader = await select.ExecuteReaderAsync(ct).ConfigureAwait(false);
                while (await reader.ReadAsync(ct).ConfigureAwait(false))
                    leftRows.Add(ReadFullStackWaybill(reader));
            }

            foreach (var row in leftRows)
            {
                row.IsInCurrentInventory = false;
                row.LeftInventoryAt ??= now;
                row.UpdatedAt = now;
                EnrichStateRiskSla(row, isInCurrentInventory: false);
                await UpsertRowAsync(connection, transaction, row, ct).ConfigureAwait(false);
            }

            return leftRows.Count;
        }

        private void ApplyInventoryPresence(FullStackWaybill row, long runId, bool exists, DateTime now)
        {
            if (!exists)
            {
                row.FirstSeenAt = now;
                row.FirstInventoryRunId = runId;
                row.CreatedAt = now;
                row.CurrentState = "NewArrival";
                row.TrangThaiHienTai = Empty(row.TrangThaiHienTai);
                row.ThaoTacCuoi = Empty(row.ThaoTacCuoi);
            }

            row.LastSeenAt = now;
            row.LastInventoryRunId = runId;
            row.IsInCurrentInventory = true;
            row.LeftInventoryAt = null;
            row.UpdatedAt = now;
        }

        private void EnrichStateRiskSla(FullStackWaybill row, bool isInCurrentInventory)
        {
            var dto = ToDto(row);
            row.CurrentState = _stateEngine.DeriveState(dto, isInCurrentInventory);
            row.CurrentStatus = dto.TrangThaiHienTai;
            row.LastAction = dto.ThaoTacCuoi;
            row.LastActionTime = TryParse(dto.ThoiGianThaoTac);
            row.LastSiteName = dto.BuuCucThaoTac;
            row.EmployeeName = dto.NguoiThaoTac;
            row.ReceiverName = dto.NhanVienNhanHang;

            var now = DateTime.Now;
            row.AgeHours = row.FirstSeenAt.HasValue ? Math.Max(0, (now - row.FirstSeenAt.Value.ToLocalTime()).TotalHours) : 0;
            row.DaysInInventory = row.AgeHours / 24.0;

            var risk = _riskEngine.Evaluate(dto, row.CurrentState, row.FirstSeenAt?.ToLocalTime(), isInCurrentInventory);
            row.RiskScore = risk.Score;
            row.RiskLevel = risk.Level;
            row.RiskReasons = risk.Reasons;

            var sla = _slaEngine.Evaluate(dto);
            row.SlaStatus = sla.Status;
            row.SlaDeadline = sla.Deadline;
        }

        private static async Task UpsertRowAsync(SqliteConnection connection, SqliteTransaction transaction, FullStackWaybill row, CancellationToken ct)
        {
            row.WaybillNo = row.WaybillNo?.Trim().ToUpperInvariant();
            row.CreatedAt = row.CreatedAt == default ? DateTime.UtcNow : row.CreatedAt;
            row.UpdatedAt = row.UpdatedAt == default ? DateTime.UtcNow : row.UpdatedAt;

            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = @"
INSERT INTO fs_waybills(
    waybill_no, first_seen_at, last_seen_at, first_inventory_run_id, last_inventory_run_id,
    is_in_current_inventory, left_inventory_at, current_state, current_status, last_action,
    last_action_time, last_site_code, last_site_name, employee_code, employee_name,
    receiver_name, receiver_phone_masked, age_hours, days_in_inventory, risk_score,
    risk_level, risk_reasons, sla_status, sla_deadline, last_track_at, next_track_at,
    created_at, updated_at, trang_thai_hien_tai, thao_tac_cuoi, thoi_gian_thao_tac,
    thoi_gian_yeu_cau_phat_lai, nhan_vien_kien_van_de, nguyen_nhan_kien_van_de,
    buu_cuc_thao_tac, nguoi_thao_tac, dau_chuyen_hoan, dia_chi_nhan_hang,
    phuong, noi_dung_hang_hoa, cod_thuc_te, pttt, nhan_vien_nhan_hang,
    dia_chi_lay_hang, thoi_gian_nhan_hang, ten_nguoi_gui, trong_luong,
    ma_doan_full, ma_doan_1, ma_doan_2, ma_doan_3, reback_status,
    in_hoan_scan_time, print_count, is_active, tracking_interval_mins)
VALUES (
    $waybill_no, $first_seen_at, $last_seen_at, $first_inventory_run_id, $last_inventory_run_id,
    $is_in_current_inventory, $left_inventory_at, $current_state, $current_status, $last_action,
    $last_action_time, $last_site_code, $last_site_name, $employee_code, $employee_name,
    $receiver_name, $receiver_phone_masked, $age_hours, $days_in_inventory, $risk_score,
    $risk_level, $risk_reasons, $sla_status, $sla_deadline, $last_track_at, $next_track_at,
    $created_at, $updated_at, $trang_thai_hien_tai, $thao_tac_cuoi, $thoi_gian_thao_tac,
    $thoi_gian_yeu_cau_phat_lai, $nhan_vien_kien_van_de, $nguyen_nhan_kien_van_de,
    $buu_cuc_thao_tac, $nguoi_thao_tac, $dau_chuyen_hoan, $dia_chi_nhan_hang,
    $phuong, $noi_dung_hang_hoa, $cod_thuc_te, $pttt, $nhan_vien_nhan_hang,
    $dia_chi_lay_hang, $thoi_gian_nhan_hang, $ten_nguoi_gui, $trong_luong,
    $ma_doan_full, $ma_doan_1, $ma_doan_2, $ma_doan_3, $reback_status,
    $in_hoan_scan_time, $print_count, $is_active, $tracking_interval_mins)
ON CONFLICT(waybill_no) DO UPDATE SET
    last_seen_at = excluded.last_seen_at,
    first_inventory_run_id = COALESCE(fs_waybills.first_inventory_run_id, excluded.first_inventory_run_id),
    last_inventory_run_id = excluded.last_inventory_run_id,
    is_in_current_inventory = excluded.is_in_current_inventory,
    left_inventory_at = excluded.left_inventory_at,
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
    last_track_at = excluded.last_track_at,
    next_track_at = excluded.next_track_at,
    updated_at = excluded.updated_at,
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
    is_active = excluded.is_active,
    tracking_interval_mins = excluded.tracking_interval_mins;";

            AddRowParameters(command, row);
            await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        // When source has no value ("empty"), keep the previously cached value. Lets a tracking-only
        // re-sync (getOrderDetail skipped for already-cached orders) avoid wiping cached detail.
        private static string Keep(string incoming, string existing)
        {
            var s = Empty(incoming);
            return s == "empty" ? Empty(existing) : s;
        }

        private static void MergeTracking(FullStackWaybill target, WaybillDbModel source)
        {
            target.WaybillNo = source.WaybillNo?.Trim().ToUpperInvariant();
            // Tracking fields — refreshed from every sync.
            target.TrangThaiHienTai = Empty(source.TrangThaiHienTai);
            target.ThaoTacCuoi = Empty(source.ThaoTacCuoi);
            target.ThoiGianThaoTac = Empty(source.ThoiGianThaoTac);
            target.ThoiGianYeuCauPhatLai = Empty(source.ThoiGianYeuCauPhatLai);
            target.NhanVienKienVanDe = Empty(source.NhanVienKienVanDe);
            target.NguyenNhanKienVanDe = Empty(source.NguyenNhanKienVanDe);
            target.BuuCucThaoTac = Empty(source.BuuCucThaoTac);
            target.NguoiThaoTac = Empty(source.NguoiThaoTac);
            target.DauChuyenHoan = Empty(source.DauChuyenHoan);
            // Detail fields (getOrderDetail) — preserve cached value when this sync skipped detail.
            target.NhanVienNhanHang = Keep(source.NhanVienNhanHang, target.NhanVienNhanHang);
            target.DiaChiNhanHang = Keep(source.DiaChiNhanHang, target.DiaChiNhanHang);
            target.Phuong = Keep(source.Phuong, target.Phuong);
            target.NoiDungHangHoa = Keep(source.NoiDungHangHoa, target.NoiDungHangHoa);
            target.CODThucTe = Keep(source.CODThucTe, target.CODThucTe);
            target.PTTT = Keep(source.PTTT, target.PTTT);
            target.DiaChiLayHang = Keep(source.DiaChiLayHang, target.DiaChiLayHang);
            target.ThoiGianNhanHang = Keep(source.ThoiGianNhanHang, target.ThoiGianNhanHang);
            target.TenNguoiGui = Keep(source.TenNguoiGui, target.TenNguoiGui);
            target.TrongLuong = Keep(source.TrongLuong, target.TrongLuong);
            target.MaDoanFull = Keep(source.MaDoanFull, target.MaDoanFull);
            target.MaDoan1 = Keep(source.MaDoan1, target.MaDoan1);
            target.MaDoan2 = Keep(source.MaDoan2, target.MaDoan2);
            target.MaDoan3 = Keep(source.MaDoan3, target.MaDoan3);
            target.RebackStatus = Empty(source.RebackStatus);
            target.InHoanScanTime = Empty(source.InHoanScanTime);
            target.PrintCount = source.PrintCount;
            target.IsActive = source.IsActive;
            target.TrackingIntervalMins = source.TrackingIntervalMins <= 0 ? 30 : source.TrackingIntervalMins;
            target.LastTrackAt = source.LastTrackedAt == default ? DateTime.UtcNow : source.LastTrackedAt;
            target.NextTrackAt = source.NextTrackAt == default ? DateTime.UtcNow.AddMinutes(target.TrackingIntervalMins) : source.NextTrackAt;
            target.UpdatedAt = DateTime.UtcNow;
        }

        private static WaybillDbModel ToDto(FullStackWaybill row) => new()
        {
            WaybillNo = row.WaybillNo,
            TrangThaiHienTai = Empty(row.TrangThaiHienTai),
            ThaoTacCuoi = Empty(row.ThaoTacCuoi),
            ThoiGianThaoTac = Empty(row.ThoiGianThaoTac),
            ThoiGianYeuCauPhatLai = Empty(row.ThoiGianYeuCauPhatLai),
            NhanVienKienVanDe = Empty(row.NhanVienKienVanDe),
            NguyenNhanKienVanDe = Empty(row.NguyenNhanKienVanDe),
            BuuCucThaoTac = Empty(row.BuuCucThaoTac),
            NguoiThaoTac = Empty(row.NguoiThaoTac),
            DauChuyenHoan = Empty(row.DauChuyenHoan),
            DiaChiNhanHang = Empty(row.DiaChiNhanHang),
            Phuong = Empty(row.Phuong),
            NoiDungHangHoa = Empty(row.NoiDungHangHoa),
            CODThucTe = Empty(row.CODThucTe),
            PTTT = Empty(row.PTTT),
            NhanVienNhanHang = Empty(row.NhanVienNhanHang),
            DiaChiLayHang = Empty(row.DiaChiLayHang),
            ThoiGianNhanHang = Empty(row.ThoiGianNhanHang),
            TenNguoiGui = Empty(row.TenNguoiGui),
            TrongLuong = Empty(row.TrongLuong),
            MaDoanFull = Empty(row.MaDoanFull),
            MaDoan1 = Empty(row.MaDoan1),
            MaDoan2 = Empty(row.MaDoan2),
            MaDoan3 = Empty(row.MaDoan3),
            RebackStatus = Empty(row.RebackStatus),
            InHoanScanTime = Empty(row.InHoanScanTime),
            PrintCount = row.PrintCount,
            IsActive = row.IsInCurrentInventory,
            TrackingIntervalMins = row.TrackingIntervalMins <= 0 ? 30 : row.TrackingIntervalMins,
            LastTrackedAt = row.LastTrackAt ?? DateTime.MinValue,
            NextTrackAt = row.NextTrackAt ?? DateTime.MinValue
        };

        private static FullStackWaybill CreateEmptyRow(string waybillNo)
        {
            var now = DateTime.UtcNow;
            return new FullStackWaybill
            {
                WaybillNo = waybillNo,
                CreatedAt = now,
                UpdatedAt = now,
                IsInCurrentInventory = true,
                IsActive = true,
                TrackingIntervalMins = 30,
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
                RebackStatus = "empty",
                InHoanScanTime = "empty"
            };
        }

        private static WaybillDbModel ReadWaybillDbModel(IDataRecord reader) => new()
        {
            WaybillNo = GetString(reader, "waybill_no"),
            TrangThaiHienTai = Empty(GetString(reader, "trang_thai_hien_tai")),
            ThaoTacCuoi = Empty(GetString(reader, "thao_tac_cuoi")),
            ThoiGianThaoTac = Empty(GetString(reader, "thoi_gian_thao_tac")),
            ThoiGianYeuCauPhatLai = Empty(GetString(reader, "thoi_gian_yeu_cau_phat_lai")),
            NhanVienKienVanDe = Empty(GetString(reader, "nhan_vien_kien_van_de")),
            NguyenNhanKienVanDe = Empty(GetString(reader, "nguyen_nhan_kien_van_de")),
            BuuCucThaoTac = Empty(GetString(reader, "buu_cuc_thao_tac")),
            NguoiThaoTac = Empty(GetString(reader, "nguoi_thao_tac")),
            DauChuyenHoan = Empty(GetString(reader, "dau_chuyen_hoan")),
            DiaChiNhanHang = Empty(GetString(reader, "dia_chi_nhan_hang")),
            Phuong = Empty(GetString(reader, "phuong")),
            NoiDungHangHoa = Empty(GetString(reader, "noi_dung_hang_hoa")),
            CODThucTe = Empty(GetString(reader, "cod_thuc_te")),
            PTTT = Empty(GetString(reader, "pttt")),
            NhanVienNhanHang = Empty(GetString(reader, "nhan_vien_nhan_hang")),
            DiaChiLayHang = Empty(GetString(reader, "dia_chi_lay_hang")),
            ThoiGianNhanHang = Empty(GetString(reader, "thoi_gian_nhan_hang")),
            TenNguoiGui = Empty(GetString(reader, "ten_nguoi_gui")),
            TrongLuong = Empty(GetString(reader, "trong_luong")),
            MaDoanFull = Empty(GetString(reader, "ma_doan_full")),
            MaDoan1 = Empty(GetString(reader, "ma_doan_1")),
            MaDoan2 = Empty(GetString(reader, "ma_doan_2")),
            MaDoan3 = Empty(GetString(reader, "ma_doan_3")),
            RebackStatus = Empty(GetString(reader, "reback_status")),
            InHoanScanTime = Empty(GetString(reader, "in_hoan_scan_time")),
            PrintCount = GetInt(reader, "print_count"),
            IsActive = GetBool(reader, "is_active"),
            TrackingIntervalMins = GetInt(reader, "tracking_interval_mins", 30),
            LastTrackedAt = GetDate(reader, "last_track_at") ?? DateTime.MinValue,
            NextTrackAt = GetDate(reader, "next_track_at") ?? DateTime.MinValue
        };

        private static FullStackWaybill ReadFullStackWaybill(IDataRecord reader) => new()
        {
            WaybillNo = GetString(reader, "waybill_no"),
            FirstSeenAt = GetDate(reader, "first_seen_at"),
            LastSeenAt = GetDate(reader, "last_seen_at"),
            FirstInventoryRunId = GetLongNullable(reader, "first_inventory_run_id"),
            LastInventoryRunId = GetLongNullable(reader, "last_inventory_run_id"),
            IsInCurrentInventory = GetBool(reader, "is_in_current_inventory"),
            LeftInventoryAt = GetDate(reader, "left_inventory_at"),
            CurrentState = GetString(reader, "current_state"),
            CurrentStatus = GetString(reader, "current_status"),
            LastAction = GetString(reader, "last_action"),
            LastActionTime = GetDate(reader, "last_action_time"),
            LastSiteCode = GetString(reader, "last_site_code"),
            LastSiteName = GetString(reader, "last_site_name"),
            EmployeeCode = GetString(reader, "employee_code"),
            EmployeeName = GetString(reader, "employee_name"),
            ReceiverName = GetString(reader, "receiver_name"),
            ReceiverPhoneMasked = GetString(reader, "receiver_phone_masked"),
            AgeHours = GetDouble(reader, "age_hours"),
            DaysInInventory = GetDouble(reader, "days_in_inventory"),
            RiskScore = GetInt(reader, "risk_score"),
            RiskLevel = GetString(reader, "risk_level"),
            RiskReasons = GetString(reader, "risk_reasons"),
            SlaStatus = GetString(reader, "sla_status"),
            SlaDeadline = GetDate(reader, "sla_deadline"),
            LastTrackAt = GetDate(reader, "last_track_at"),
            NextTrackAt = GetDate(reader, "next_track_at"),
            CreatedAt = GetDate(reader, "created_at") ?? DateTime.UtcNow,
            UpdatedAt = GetDate(reader, "updated_at") ?? DateTime.UtcNow,
            TrangThaiHienTai = Empty(GetString(reader, "trang_thai_hien_tai")),
            ThaoTacCuoi = Empty(GetString(reader, "thao_tac_cuoi")),
            ThoiGianThaoTac = Empty(GetString(reader, "thoi_gian_thao_tac")),
            ThoiGianYeuCauPhatLai = Empty(GetString(reader, "thoi_gian_yeu_cau_phat_lai")),
            NhanVienKienVanDe = Empty(GetString(reader, "nhan_vien_kien_van_de")),
            NguyenNhanKienVanDe = Empty(GetString(reader, "nguyen_nhan_kien_van_de")),
            BuuCucThaoTac = Empty(GetString(reader, "buu_cuc_thao_tac")),
            NguoiThaoTac = Empty(GetString(reader, "nguoi_thao_tac")),
            DauChuyenHoan = Empty(GetString(reader, "dau_chuyen_hoan")),
            DiaChiNhanHang = Empty(GetString(reader, "dia_chi_nhan_hang")),
            Phuong = Empty(GetString(reader, "phuong")),
            NoiDungHangHoa = Empty(GetString(reader, "noi_dung_hang_hoa")),
            CODThucTe = Empty(GetString(reader, "cod_thuc_te")),
            PTTT = Empty(GetString(reader, "pttt")),
            NhanVienNhanHang = Empty(GetString(reader, "nhan_vien_nhan_hang")),
            DiaChiLayHang = Empty(GetString(reader, "dia_chi_lay_hang")),
            ThoiGianNhanHang = Empty(GetString(reader, "thoi_gian_nhan_hang")),
            TenNguoiGui = Empty(GetString(reader, "ten_nguoi_gui")),
            TrongLuong = Empty(GetString(reader, "trong_luong")),
            MaDoanFull = Empty(GetString(reader, "ma_doan_full")),
            MaDoan1 = Empty(GetString(reader, "ma_doan_1")),
            MaDoan2 = Empty(GetString(reader, "ma_doan_2")),
            MaDoan3 = Empty(GetString(reader, "ma_doan_3")),
            RebackStatus = Empty(GetString(reader, "reback_status")),
            InHoanScanTime = Empty(GetString(reader, "in_hoan_scan_time")),
            PrintCount = GetInt(reader, "print_count"),
            IsActive = GetBool(reader, "is_active"),
            TrackingIntervalMins = GetInt(reader, "tracking_interval_mins", 30)
        };

        private static void AddRowParameters(SqliteCommand command, FullStackWaybill row)
        {
            Add(command, "$waybill_no", row.WaybillNo);
            Add(command, "$first_seen_at", row.FirstSeenAt?.ToString("O"));
            Add(command, "$last_seen_at", row.LastSeenAt?.ToString("O"));
            Add(command, "$first_inventory_run_id", row.FirstInventoryRunId);
            Add(command, "$last_inventory_run_id", row.LastInventoryRunId);
            Add(command, "$is_in_current_inventory", row.IsInCurrentInventory ? 1 : 0);
            Add(command, "$left_inventory_at", row.LeftInventoryAt?.ToString("O"));
            Add(command, "$current_state", row.CurrentState);
            Add(command, "$current_status", row.CurrentStatus);
            Add(command, "$last_action", row.LastAction);
            Add(command, "$last_action_time", row.LastActionTime?.ToString("O"));
            Add(command, "$last_site_code", row.LastSiteCode);
            Add(command, "$last_site_name", row.LastSiteName);
            Add(command, "$employee_code", row.EmployeeCode);
            Add(command, "$employee_name", row.EmployeeName);
            Add(command, "$receiver_name", row.ReceiverName);
            Add(command, "$receiver_phone_masked", row.ReceiverPhoneMasked);
            Add(command, "$age_hours", row.AgeHours);
            Add(command, "$days_in_inventory", row.DaysInInventory);
            Add(command, "$risk_score", row.RiskScore);
            Add(command, "$risk_level", row.RiskLevel);
            Add(command, "$risk_reasons", row.RiskReasons);
            Add(command, "$sla_status", row.SlaStatus);
            Add(command, "$sla_deadline", row.SlaDeadline?.ToString("O"));
            Add(command, "$last_track_at", row.LastTrackAt?.ToString("O"));
            Add(command, "$next_track_at", row.NextTrackAt?.ToString("O"));
            Add(command, "$created_at", row.CreatedAt.ToString("O"));
            Add(command, "$updated_at", row.UpdatedAt.ToString("O"));
            Add(command, "$trang_thai_hien_tai", Empty(row.TrangThaiHienTai));
            Add(command, "$thao_tac_cuoi", Empty(row.ThaoTacCuoi));
            Add(command, "$thoi_gian_thao_tac", Empty(row.ThoiGianThaoTac));
            Add(command, "$thoi_gian_yeu_cau_phat_lai", Empty(row.ThoiGianYeuCauPhatLai));
            Add(command, "$nhan_vien_kien_van_de", Empty(row.NhanVienKienVanDe));
            Add(command, "$nguyen_nhan_kien_van_de", Empty(row.NguyenNhanKienVanDe));
            Add(command, "$buu_cuc_thao_tac", Empty(row.BuuCucThaoTac));
            Add(command, "$nguoi_thao_tac", Empty(row.NguoiThaoTac));
            Add(command, "$dau_chuyen_hoan", Empty(row.DauChuyenHoan));
            Add(command, "$dia_chi_nhan_hang", Empty(row.DiaChiNhanHang));
            Add(command, "$phuong", Empty(row.Phuong));
            Add(command, "$noi_dung_hang_hoa", Empty(row.NoiDungHangHoa));
            Add(command, "$cod_thuc_te", Empty(row.CODThucTe));
            Add(command, "$pttt", Empty(row.PTTT));
            Add(command, "$nhan_vien_nhan_hang", Empty(row.NhanVienNhanHang));
            Add(command, "$dia_chi_lay_hang", Empty(row.DiaChiLayHang));
            Add(command, "$thoi_gian_nhan_hang", Empty(row.ThoiGianNhanHang));
            Add(command, "$ten_nguoi_gui", Empty(row.TenNguoiGui));
            Add(command, "$trong_luong", Empty(row.TrongLuong));
            Add(command, "$ma_doan_full", Empty(row.MaDoanFull));
            Add(command, "$ma_doan_1", Empty(row.MaDoan1));
            Add(command, "$ma_doan_2", Empty(row.MaDoan2));
            Add(command, "$ma_doan_3", Empty(row.MaDoan3));
            Add(command, "$reback_status", Empty(row.RebackStatus));
            Add(command, "$in_hoan_scan_time", Empty(row.InHoanScanTime));
            Add(command, "$print_count", row.PrintCount);
            Add(command, "$is_active", row.IsActive ? 1 : 0);
            Add(command, "$tracking_interval_mins", row.TrackingIntervalMins <= 0 ? 30 : row.TrackingIntervalMins);
        }

        private static async Task SetSyncStateAsync(SqliteConnection connection, SqliteTransaction transaction, string key, string value, CancellationToken ct)
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = @"
INSERT INTO fs_sync_state(key, value, updated_at)
VALUES ($key, $value, $updatedAt)
ON CONFLICT(key) DO UPDATE SET value = excluded.value, updated_at = excluded.updated_at;";
            Add(command, "$key", key);
            Add(command, "$value", value);
            Add(command, "$updatedAt", DateTime.UtcNow.ToString("O"));
            await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        private static void Add(SqliteCommand command, string name, object value) =>
            command.Parameters.AddWithValue(name, value ?? DBNull.Value);

        private static string Empty(string value) => string.IsNullOrWhiteSpace(value) ? "empty" : value.Trim();

        private static string GetString(IDataRecord reader, string name)
        {
            int index = reader.GetOrdinal(name);
            return reader.IsDBNull(index) ? null : reader.GetValue(index)?.ToString();
        }

        private static int GetInt(IDataRecord reader, string name, int defaultValue = 0)
        {
            int index = reader.GetOrdinal(name);
            if (reader.IsDBNull(index)) return defaultValue;
            return Convert.ToInt32(reader.GetValue(index), CultureInfo.InvariantCulture);
        }

        private static long? GetLongNullable(IDataRecord reader, string name)
        {
            int index = reader.GetOrdinal(name);
            if (reader.IsDBNull(index)) return null;
            return Convert.ToInt64(reader.GetValue(index), CultureInfo.InvariantCulture);
        }

        private static double GetDouble(IDataRecord reader, string name)
        {
            int index = reader.GetOrdinal(name);
            if (reader.IsDBNull(index)) return 0;
            return Convert.ToDouble(reader.GetValue(index), CultureInfo.InvariantCulture);
        }

        private static bool GetBool(IDataRecord reader, string name)
        {
            int index = reader.GetOrdinal(name);
            if (reader.IsDBNull(index)) return false;
            return Convert.ToInt32(reader.GetValue(index), CultureInfo.InvariantCulture) != 0;
        }

        private static DateTime? GetDate(IDataRecord reader, string name)
        {
            var value = GetString(reader, name);
            if (DateTime.TryParse(value, null, DateTimeStyles.RoundtripKind, out var parsed)) return parsed;
            if (DateTime.TryParse(value, out parsed)) return parsed;
            return null;
        }

        private static DateTime? TryParse(string value) =>
            DateTime.TryParse(value, out var dt) ? dt : null;
    }
}

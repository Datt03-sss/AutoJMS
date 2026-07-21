using AutoJMS.FullStack.LocalDb;
using Microsoft.Data.Sqlite;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace AutoJMS.FullStack.Events
{
    /// <summary>
    /// Append-only local event log (fs_events) with fingerprint dedupe, plus the
    /// projection fold that derives latest waybill state from events ordered by
    /// event_time (docs/roadmap: event-sourcing-lite).
    ///
    /// Runs additively next to the existing row-sync: appending events never
    /// changes dashboard behavior on its own. The fold is available for the
    /// scope-C cutover (projection derived from events as source of truth).
    /// </summary>
    public sealed class FullStackEventLog
    {
        private readonly FullStackDbConnectionFactory _connectionFactory;
        private readonly FullStackDbInitializer _initializer;

        public FullStackEventLog() : this(new FullStackDbConnectionFactory()) { }

        public FullStackEventLog(FullStackDbConnectionFactory connectionFactory)
        {
            _connectionFactory = connectionFactory;
            _initializer = new FullStackDbInitializer(_connectionFactory);
        }

        /// <summary>
        /// Append locally-produced events (dedupe by fingerprint) and, when a
        /// connection/transaction is supplied, enqueue them onto the outbox for
        /// cloud push. Returns number of NEW events inserted.
        /// </summary>
        public async Task<int> AppendLocalAsync(IReadOnlyList<FullStackEvent> events, bool enqueueOutbox, CancellationToken ct = default)
        {
            if (events == null || events.Count == 0) return 0;
            await _initializer.InitializeAsync(ct).ConfigureAwait(false);

            await using var connection = await _connectionFactory.OpenAsync(ct).ConfigureAwait(false);
            await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(ct).ConfigureAwait(false);

            int inserted = 0;
            foreach (var ev in events)
            {
                if (ev == null || string.IsNullOrWhiteSpace(ev.WaybillNo) || string.IsNullOrWhiteSpace(ev.Fingerprint))
                    continue;

                int rows = await InsertEventAsync(connection, transaction, ev, origin: "local", ct).ConfigureAwait(false);
                if (rows > 0)
                {
                    inserted++;
                    if (enqueueOutbox)
                        await EnqueueEventOutboxAsync(connection, transaction, ev, ct).ConfigureAwait(false);
                }
            }

            await transaction.CommitAsync(ct).ConfigureAwait(false);
            if (inserted > 0) AppLogger.Info($"[EventLog] appended local events new={inserted}/{events.Count}");
            return inserted;
        }

        /// <summary>Merge events pulled from the cloud (carry server seq). Returns affected waybills.</summary>
        public async Task<HashSet<string>> MergeRemoteAsync(IReadOnlyList<FullStackEvent> events, CancellationToken ct = default)
        {
            var affected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (events == null || events.Count == 0) return affected;
            await _initializer.InitializeAsync(ct).ConfigureAwait(false);

            await using var connection = await _connectionFactory.OpenAsync(ct).ConfigureAwait(false);
            await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(ct).ConfigureAwait(false);

            foreach (var ev in events)
            {
                if (ev == null || string.IsNullOrWhiteSpace(ev.WaybillNo) || string.IsNullOrWhiteSpace(ev.Fingerprint))
                    continue;
                int rows = await InsertEventAsync(connection, transaction, ev, origin: "cloud", ct).ConfigureAwait(false);
                if (rows > 0) affected.Add(ev.WaybillNo.Trim().ToUpperInvariant());
            }

            await transaction.CommitAsync(ct).ConfigureAwait(false);
            if (affected.Count > 0) AppLogger.Info($"[EventLog] merged remote events waybills={affected.Count}");
            return affected;
        }

        /// <summary>Highest remote seq stored locally (delta-pull cursor).</summary>
        public async Task<long> GetMaxRemoteSeqAsync(CancellationToken ct = default)
        {
            await _initializer.InitializeAsync(ct).ConfigureAwait(false);
            await using var connection = await _connectionFactory.OpenAsync(ct).ConfigureAwait(false);
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT COALESCE(MAX(remote_seq), 0) FROM fs_events WHERE remote_seq IS NOT NULL;";
            var value = await command.ExecuteScalarAsync(ct).ConfigureAwait(false);
            return value == null || value is DBNull ? 0 : Convert.ToInt64(value, CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Fold the events of one waybill into its latest projected tracking state,
        /// ordered by event_time (newest wins; ties broken by observed_at). Returns
        /// null when the waybill has no TrackingObserved/OrderDetailObserved events.
        /// This is the ordering-correct projection the roadmap requires — it uses
        /// event_time (observation time), NOT updated_at (write time).
        /// </summary>
        public async Task<FullStackProjection> FoldProjectionAsync(string waybillNo, CancellationToken ct = default)
        {
            waybillNo = (waybillNo ?? "").Trim().ToUpperInvariant();
            if (string.IsNullOrWhiteSpace(waybillNo)) return null;
            await _initializer.InitializeAsync(ct).ConfigureAwait(false);

            await using var connection = await _connectionFactory.OpenAsync(ct).ConfigureAwait(false);
            await using var command = connection.CreateCommand();
            command.CommandText = @"
SELECT event_type, event_time, payload
FROM fs_events
WHERE waybill_no = $wb
ORDER BY event_time ASC, observed_at ASC;";
            command.Parameters.AddWithValue("$wb", waybillNo);

            var projection = new FullStackProjection { WaybillNo = waybillNo };
            bool any = false;
            await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                any = true;
                string type = reader.IsDBNull(0) ? "" : reader.GetString(0);
                DateTime evTime = reader.IsDBNull(1) ? DateTime.MinValue
                    : (DateTime.TryParse(reader.GetString(1), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dt) ? dt : DateTime.MinValue);
                JObject p;
                try { p = JObject.Parse(reader.IsDBNull(2) ? "{}" : reader.GetString(2)); }
                catch { p = new JObject(); }

                switch (type)
                {
                    case FullStackEventTypes.TrackingObserved:
                        if (evTime >= projection.LastActionTime)
                        {
                            projection.LastActionTime = evTime;
                            projection.TrangThaiHienTai = Str(p, "trang_thai_hien_tai", projection.TrangThaiHienTai);
                            projection.ThaoTacCuoi = Str(p, "thao_tac_cuoi", projection.ThaoTacCuoi);
                            projection.ThoiGianThaoTac = Str(p, "thoi_gian_thao_tac", projection.ThoiGianThaoTac);
                            projection.BuuCucThaoTac = Str(p, "buu_cuc_thao_tac", projection.BuuCucThaoTac);
                            projection.NguoiThaoTac = Str(p, "nguoi_thao_tac", projection.NguoiThaoTac);
                        }
                        break;
                    case FullStackEventTypes.OrderDetailObserved:
                        projection.TenNguoiGui = Str(p, "ten_nguoi_gui", projection.TenNguoiGui);
                        projection.DiaChiNhanHang = Str(p, "dia_chi_nhan_hang", projection.DiaChiNhanHang);
                        projection.CODThucTe = Str(p, "cod_thuc_te", projection.CODThucTe);
                        projection.NoiDungHangHoa = Str(p, "noi_dung_hang_hoa", projection.NoiDungHangHoa);
                        break;
                    case FullStackEventTypes.InventoryLeft:
                        projection.IsInCurrentInventory = false;
                        break;
                    case FullStackEventTypes.InventorySeen:
                        projection.IsInCurrentInventory = true;
                        break;
                }
            }

            return any ? projection : null;
        }

        // ------------------------------------------------------------------
        private static async Task<int> InsertEventAsync(SqliteConnection connection, SqliteTransaction transaction, FullStackEvent ev, string origin, CancellationToken ct)
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = @"
INSERT OR IGNORE INTO fs_events(
    event_id, waybill_no, event_type, event_time, source, source_client,
    fingerprint, payload, observed_at, schema_version, origin, remote_seq)
VALUES (
    $eventId, $waybillNo, $eventType, $eventTime, $source, $sourceClient,
    $fingerprint, $payload, $observedAt, $schemaVersion, $origin, $remoteSeq);";
            Add(command, "$eventId", ev.EventId);
            Add(command, "$waybillNo", ev.WaybillNo.Trim().ToUpperInvariant());
            Add(command, "$eventType", ev.EventType);
            Add(command, "$eventTime", ev.EventTime.ToUniversalTime().ToString("O"));
            Add(command, "$source", ev.Source);
            Add(command, "$sourceClient", ev.SourceClient);
            Add(command, "$fingerprint", ev.Fingerprint);
            Add(command, "$payload", string.IsNullOrWhiteSpace(ev.Payload) ? "{}" : ev.Payload);
            Add(command, "$observedAt", ev.ObservedAt.ToUniversalTime().ToString("O"));
            Add(command, "$schemaVersion", ev.SchemaVersion);
            Add(command, "$origin", origin);
            Add(command, "$remoteSeq", ev.Seq.HasValue ? (object)ev.Seq.Value : DBNull.Value);
            return await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        private static async Task EnqueueEventOutboxAsync(SqliteConnection connection, SqliteTransaction transaction, FullStackEvent ev, CancellationToken ct)
        {
            var payload = new JObject
            {
                ["event_id"] = ev.EventId,
                ["waybill_no"] = ev.WaybillNo.Trim().ToUpperInvariant(),
                ["event_type"] = ev.EventType,
                ["event_time"] = ev.EventTime.ToUniversalTime().ToString("O"),
                ["source"] = ev.Source ?? "",
                ["source_client"] = ev.SourceClient ?? "",
                ["fingerprint"] = ev.Fingerprint,
                ["payload"] = ev.Payload ?? "{}",
                ["observed_at"] = ev.ObservedAt.ToUniversalTime().ToString("O"),
                ["schema_version"] = ev.SchemaVersion
            };

            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = @"
INSERT INTO fs_outbox(kind, ref_key, payload, created_at)
VALUES ('EVENT', $refKey, $payload, $createdAt);";
            command.Parameters.AddWithValue("$refKey", ev.Fingerprint);
            command.Parameters.AddWithValue("$payload", payload.ToString(Newtonsoft.Json.Formatting.None));
            command.Parameters.AddWithValue("$createdAt", DateTime.UtcNow.ToString("O"));
            await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        private static string Str(JObject o, string key, string fallback)
        {
            var token = o[key];
            if (token == null || token.Type == JTokenType.Null) return fallback;
            var s = token.ToString();
            return string.IsNullOrWhiteSpace(s) ? fallback : s;
        }

        private static void Add(SqliteCommand command, string name, object value) =>
            command.Parameters.AddWithValue(name, value ?? DBNull.Value);
    }

    /// <summary>Latest projected state derived from folding a waybill's events.</summary>
    public sealed class FullStackProjection
    {
        public string WaybillNo { get; set; }
        public DateTime LastActionTime { get; set; } = DateTime.MinValue;
        public bool IsInCurrentInventory { get; set; } = true;
        public string TrangThaiHienTai { get; set; }
        public string ThaoTacCuoi { get; set; }
        public string ThoiGianThaoTac { get; set; }
        public string BuuCucThaoTac { get; set; }
        public string NguoiThaoTac { get; set; }
        public string TenNguoiGui { get; set; }
        public string DiaChiNhanHang { get; set; }
        public string CODThucTe { get; set; }
        public string NoiDungHangHoa { get; set; }
    }
}

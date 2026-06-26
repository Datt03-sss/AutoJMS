using AutoJMS.FullStack.LocalDb;
using AutoJMS.FullStack.Models;
using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace AutoJMS.FullStack.Repositories
{
    /// <summary>
    /// Durable storage for shipping-journey tracking events in
    /// FullStack/journey_history.db. SaveAsync replaces the stored snapshot for
    /// a waybill with the freshly fetched events.
    /// </summary>
    public sealed class JourneyHistoryRepository
    {
        private readonly JourneyHistoryDbConnectionFactory _connectionFactory;
        private readonly JourneyHistoryDbInitializer _initializer;

        public JourneyHistoryRepository()
            : this(new JourneyHistoryDbConnectionFactory())
        {
        }

        public JourneyHistoryRepository(JourneyHistoryDbConnectionFactory connectionFactory)
        {
            _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
            _initializer = new JourneyHistoryDbInitializer(_connectionFactory);
        }

        public async Task SaveAsync(
            string waybillNo,
            IReadOnlyList<JourneyHistoryEvent> events,
            CancellationToken cancellationToken = default)
        {
            waybillNo = (waybillNo ?? string.Empty).Trim();
            if (waybillNo.Length == 0 || events == null || events.Count == 0)
                return;

            await _initializer.InitializeAsync(cancellationToken).ConfigureAwait(false);
            await using var connection = await _connectionFactory.OpenAsync(cancellationToken).ConfigureAwait(false);
            await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

            await using (var delete = connection.CreateCommand())
            {
                delete.Transaction = transaction;
                delete.CommandText = "DELETE FROM journey_history WHERE waybill_no = $waybillNo;";
                delete.Parameters.AddWithValue("$waybillNo", waybillNo);
                await delete.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            foreach (var ev in events)
            {
                await using var insert = connection.CreateCommand();
                insert.Transaction = transaction;
                insert.CommandText = @"
INSERT OR IGNORE INTO journey_history(
    waybill_no, stt, scan_time, upload_time, scan_type_name, description, source, weight, raw_json, fetched_at)
VALUES (
    $waybillNo, $stt, $scanTime, $uploadTime, $scanTypeName, $description, $source, $weight, $rawJson, $fetchedAt);";
                insert.Parameters.AddWithValue("$waybillNo", waybillNo);
                insert.Parameters.AddWithValue("$stt", ev.Stt);
                insert.Parameters.AddWithValue("$scanTime", Val(ev.ScanTime));
                insert.Parameters.AddWithValue("$uploadTime", Val(ev.UploadTime));
                insert.Parameters.AddWithValue("$scanTypeName", Val(ev.ScanTypeName));
                insert.Parameters.AddWithValue("$description", Val(ev.Description));
                insert.Parameters.AddWithValue("$source", Val(ev.Source));
                insert.Parameters.AddWithValue("$weight", Val(ev.Weight));
                insert.Parameters.AddWithValue("$rawJson", Val(ev.RawJson));
                insert.Parameters.AddWithValue("$fetchedAt", ev.FetchedAt.ToString("yyyy-MM-dd HH:mm:ss"));
                await insert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }

        public async Task<List<JourneyHistoryEvent>> GetByWaybillAsync(
            string waybillNo,
            CancellationToken cancellationToken = default)
        {
            var list = new List<JourneyHistoryEvent>();
            waybillNo = (waybillNo ?? string.Empty).Trim();
            if (waybillNo.Length == 0)
                return list;

            await _initializer.InitializeAsync(cancellationToken).ConfigureAwait(false);
            await using var connection = await _connectionFactory.OpenAsync(cancellationToken).ConfigureAwait(false);
            await using var command = connection.CreateCommand();
            command.CommandText = @"
SELECT stt, scan_time, upload_time, scan_type_name, description, source, weight, raw_json, fetched_at
FROM journey_history
WHERE waybill_no = $waybillNo
ORDER BY stt;";
            command.Parameters.AddWithValue("$waybillNo", waybillNo);

            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                list.Add(new JourneyHistoryEvent
                {
                    WaybillNo = waybillNo,
                    Stt = reader.IsDBNull(0) ? 0 : reader.GetInt32(0),
                    ScanTime = reader.IsDBNull(1) ? string.Empty : reader.GetString(1),
                    UploadTime = reader.IsDBNull(2) ? string.Empty : reader.GetString(2),
                    ScanTypeName = reader.IsDBNull(3) ? string.Empty : reader.GetString(3),
                    Description = reader.IsDBNull(4) ? string.Empty : reader.GetString(4),
                    Source = reader.IsDBNull(5) ? string.Empty : reader.GetString(5),
                    Weight = reader.IsDBNull(6) ? string.Empty : reader.GetString(6),
                    RawJson = reader.IsDBNull(7) ? string.Empty : reader.GetString(7),
                    FetchedAt = reader.IsDBNull(8)
                        ? default
                        : (DateTime.TryParse(reader.GetString(8), out var fetched) ? fetched : default)
                });
            }

            return list;
        }

        /// <summary>
        /// Bulk-insert events in one transaction (INSERT OR IGNORE — de-duplicated
        /// by the UNIQUE constraint). Used by the enrichment bulk path; keeps history.
        /// </summary>
        public async Task InsertManyAsync(
            IReadOnlyList<JourneyHistoryEvent> events,
            CancellationToken cancellationToken = default)
        {
            if (events == null || events.Count == 0)
                return;

            await _initializer.InitializeAsync(cancellationToken).ConfigureAwait(false);
            await using var connection = await _connectionFactory.OpenAsync(cancellationToken).ConfigureAwait(false);
            await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

            foreach (var ev in events)
            {
                if (ev == null || string.IsNullOrWhiteSpace(ev.WaybillNo)) continue;
                await using var insert = connection.CreateCommand();
                insert.Transaction = transaction;
                insert.CommandText = @"
INSERT OR IGNORE INTO journey_history(
    waybill_no, stt, scan_time, upload_time, scan_type_name, description, source, weight, raw_json, fetched_at)
VALUES (
    $waybillNo, $stt, $scanTime, $uploadTime, $scanTypeName, $description, $source, $weight, $rawJson, $fetchedAt);";
                insert.Parameters.AddWithValue("$waybillNo", ev.WaybillNo.Trim());
                insert.Parameters.AddWithValue("$stt", ev.Stt);
                insert.Parameters.AddWithValue("$scanTime", Val(ev.ScanTime));
                insert.Parameters.AddWithValue("$uploadTime", Val(ev.UploadTime));
                insert.Parameters.AddWithValue("$scanTypeName", Val(ev.ScanTypeName));
                insert.Parameters.AddWithValue("$description", Val(ev.Description));
                insert.Parameters.AddWithValue("$source", Val(ev.Source));
                insert.Parameters.AddWithValue("$weight", Val(ev.Weight));
                insert.Parameters.AddWithValue("$rawJson", Val(ev.RawJson));
                insert.Parameters.AddWithValue("$fetchedAt", ev.FetchedAt.ToString("yyyy-MM-dd HH:mm:ss"));
                await insert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }

        private static object Val(string value) =>
            string.IsNullOrEmpty(value) ? (object)DBNull.Value : value;
    }
}

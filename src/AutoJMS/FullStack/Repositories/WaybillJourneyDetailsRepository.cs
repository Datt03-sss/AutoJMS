using AutoJMS.FullStack.LocalDb;
using AutoJMS.FullStack.Models;
using AutoJMS.FullStack.Services;
using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace AutoJMS.FullStack.Repositories
{
    public sealed class WaybillJourneyDetailsRepository : IWaybillJourneyDetailsRepository
    {
        private readonly WaybillJourneyDetailsDbConnectionFactory _connectionFactory;
        private readonly WaybillJourneyDetailsDbInitializer _initializer;

        public string DatabasePath => _connectionFactory.DatabasePath;

        public WaybillJourneyDetailsRepository()
            : this(new WaybillJourneyDetailsDbConnectionFactory())
        {
        }

        public WaybillJourneyDetailsRepository(WaybillJourneyDetailsDbConnectionFactory connectionFactory)
        {
            _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
            _initializer = new WaybillJourneyDetailsDbInitializer(_connectionFactory);
        }

        public async Task<WaybillJourneyViewModel> GetLocalJourneyAsync(
            string waybillNo,
            CancellationToken cancellationToken)
        {
            waybillNo = NormalizeWaybill(waybillNo);
            if (string.IsNullOrWhiteSpace(waybillNo))
                return new WaybillJourneyViewModel();

            await _initializer.InitializeAsync(cancellationToken).ConfigureAwait(false);

            var rows = new List<WaybillJourneyRow>();
            await using var connection = await _connectionFactory.OpenAsync(cancellationToken).ConfigureAwait(false);
            await using var command = connection.CreateCommand();
            command.CommandText = @"
SELECT COALESCE(seq, event_index) AS seq,
       COALESCE(event_time, scan_time) AS event_time,
       COALESCE(upload_time, uploaded_at) AS upload_time,
       scan_type_name, action_type, description,
       scan_network_name, scan_network_code, COALESCE(scan_site, site_info) AS site_info,
       scan_by_code, scan_by_name,
       COALESCE(bag_no, package_number, task_code) AS bag_no,
       package_number, task_code, scan_source, weight, COALESCE(volume_weight, volume, converted_weight) AS volume,
       attachment_text, img_type, severity, raw_event_json
FROM waybill_journey_events
WHERE waybill_no = $waybillNo
ORDER BY COALESCE(event_time, scan_time) DESC, COALESCE(seq, event_index) ASC;";
            command.Parameters.AddWithValue("$waybillNo", waybillNo);

            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                rows.Add(new WaybillJourneyRow
                {
                    Stt = GetInt(reader, 0),
                    ActionTime = ParseNullableDate(GetString(reader, 1)),
                    UploadedAt = ParseNullableDate(GetString(reader, 2)),
                    ActionType = Clean(GetString(reader, 4), fallback: Clean(GetString(reader, 3))),
                    Description = Clean(GetString(reader, 5)),
                    SiteName = Clean(GetString(reader, 6)),
                    SiteCode = Clean(GetString(reader, 7)),
                    SiteInfo = Clean(GetString(reader, 8)),
                    OperatorCode = Clean(GetString(reader, 9)),
                    OperatorName = Clean(GetString(reader, 10)),
                    ContainerCode = FirstNonEmpty(GetString(reader, 11), GetString(reader, 12), GetString(reader, 13)),
                    PackageNumber = Clean(GetString(reader, 12)),
                    TaskCode = Clean(GetString(reader, 13)),
                    ScanSource = Clean(GetString(reader, 14)),
                    Weight = Clean(GetString(reader, 15)),
                    ConvertedWeight = Clean(GetString(reader, 16)),
                    AttachmentText = Clean(GetString(reader, 17)),
                    ImgType = GetNullableInt(reader, 18),
                    Severity = Clean(GetString(reader, 19), fallback: "Scan"),
                    RawJson = GetString(reader, 20) ?? string.Empty
                });
            }

            NormalizeRows(rows);
            return new WaybillJourneyViewModel
            {
                WaybillNo = waybillNo,
                Rows = rows
            };
        }

        public async Task<bool> HasLocalJourneyAsync(
            string waybillNo,
            CancellationToken cancellationToken)
        {
            waybillNo = NormalizeWaybill(waybillNo);
            if (string.IsNullOrWhiteSpace(waybillNo))
                return false;

            await _initializer.InitializeAsync(cancellationToken).ConfigureAwait(false);

            await using var connection = await _connectionFactory.OpenAsync(cancellationToken).ConfigureAwait(false);
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT EXISTS(SELECT 1 FROM waybill_journey_events WHERE waybill_no = $waybillNo LIMIT 1);";
            command.Parameters.AddWithValue("$waybillNo", waybillNo);
            var result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
            return Convert.ToInt32(result, CultureInfo.InvariantCulture) == 1;
        }

        public async Task<WaybillJourneyRawCache> GetLatestRawJsonAsync(
            string waybillNo,
            CancellationToken cancellationToken)
        {
            waybillNo = NormalizeWaybill(waybillNo);
            if (string.IsNullOrWhiteSpace(waybillNo))
                return new WaybillJourneyRawCache { WaybillNo = waybillNo };

            await _initializer.InitializeAsync(cancellationToken).ConfigureAwait(false);
            await using var connection = await _connectionFactory.OpenAsync(cancellationToken).ConfigureAwait(false);

            var cache = await ReadRawCacheAsync(
                connection,
                @"SELECT waybill_no, raw_json, event_count, fetched_at, expires_at, source_hash, last_error
                  FROM waybill_journey_cache
                  WHERE waybill_no = $waybillNo;",
                waybillNo,
                cancellationToken).ConfigureAwait(false);
            if (cache.HasValue)
                return cache;

            return await ReadRawCacheAsync(
                connection,
                @"SELECT waybill_no, raw_json, event_count, fetched_at, expires_at, '' AS source_hash, last_error
                  FROM waybill_journey_json_cache
                  WHERE waybill_no = $waybillNo;",
                waybillNo,
                cancellationToken).ConfigureAwait(false);
        }

        public async Task SaveRawJsonAsync(
            string waybillNo,
            string rawJson,
            int? responseCode,
            string responseMessage,
            int eventCount,
            DateTime fetchedAt,
            DateTime expiresAt,
            string lastError,
            CancellationToken cancellationToken)
        {
            waybillNo = NormalizeWaybill(waybillNo);
            if (string.IsNullOrWhiteSpace(waybillNo) || string.IsNullOrWhiteSpace(rawJson))
                return;

            await _initializer.InitializeAsync(cancellationToken).ConfigureAwait(false);
            await using var connection = await _connectionFactory.OpenAsync(cancellationToken).ConfigureAwait(false);
            await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
            await SaveRawJsonCoreAsync(
                connection,
                transaction,
                waybillNo,
                rawJson,
                responseCode,
                responseMessage,
                eventCount,
                fetchedAt,
                expiresAt,
                lastError,
                cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }

        public async Task ReplaceJourneyEventsAsync(
            string waybillNo,
            IReadOnlyList<WaybillJourneyRow> rows,
            DateTime fetchedAt,
            CancellationToken cancellationToken)
        {
            waybillNo = NormalizeWaybill(waybillNo);
            if (string.IsNullOrWhiteSpace(waybillNo))
                return;

            await _initializer.InitializeAsync(cancellationToken).ConfigureAwait(false);
            await using var connection = await _connectionFactory.OpenAsync(cancellationToken).ConfigureAwait(false);
            await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
            await ReplaceJourneyEventsCoreAsync(connection, transaction, waybillNo, rows, fetchedAt, cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }

        public async Task SaveJourneySnapshotAsync(
            string waybillNo,
            string rawJson,
            int? responseCode,
            string responseMessage,
            IReadOnlyList<WaybillJourneyRow> rows,
            DateTime fetchedAt,
            DateTime expiresAt,
            string lastError,
            CancellationToken cancellationToken)
        {
            waybillNo = NormalizeWaybill(waybillNo);
            if (string.IsNullOrWhiteSpace(waybillNo) || string.IsNullOrWhiteSpace(rawJson))
                return;

            await _initializer.InitializeAsync(cancellationToken).ConfigureAwait(false);
            await using var connection = await _connectionFactory.OpenAsync(cancellationToken).ConfigureAwait(false);
            await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

            await SaveRawJsonCoreAsync(
                connection,
                transaction,
                waybillNo,
                rawJson,
                responseCode,
                responseMessage,
                rows?.Count ?? 0,
                fetchedAt,
                expiresAt,
                lastError,
                cancellationToken).ConfigureAwait(false);

            await ReplaceJourneyEventsCoreAsync(connection, transaction, waybillNo, rows, fetchedAt, cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }

        private static async Task SaveRawJsonCoreAsync(
            SqliteConnection connection,
            SqliteTransaction transaction,
            string waybillNo,
            string rawJson,
            int? responseCode,
            string responseMessage,
            int eventCount,
            DateTime fetchedAt,
            DateTime expiresAt,
            string lastError,
            CancellationToken cancellationToken)
        {
            var sourceHash = ComputeSourceHash(rawJson);

            await using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = @"
INSERT INTO waybill_journey_cache(
    waybill_no, raw_json, event_count, fetched_at, expires_at, source_hash, last_error)
VALUES (
    $waybillNo, $rawJson, $eventCount, $fetchedAt, $expiresAt, $sourceHash, $lastError)
ON CONFLICT(waybill_no) DO UPDATE SET
    raw_json = excluded.raw_json,
    event_count = excluded.event_count,
    fetched_at = excluded.fetched_at,
    expires_at = excluded.expires_at,
    source_hash = excluded.source_hash,
    last_error = excluded.last_error;";
                Add(command, "$waybillNo", waybillNo);
                Add(command, "$rawJson", rawJson);
                Add(command, "$eventCount", eventCount);
                Add(command, "$fetchedAt", fetchedAt.ToString("O"));
                Add(command, "$expiresAt", expiresAt.ToString("O"));
                Add(command, "$sourceHash", sourceHash);
                Add(command, "$lastError", lastError);
                await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            await using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = @"
INSERT INTO waybill_journey_json_cache(
    waybill_no, raw_json, fetched_at, expires_at, response_code, response_message, event_count, last_error)
VALUES (
    $waybillNo, $rawJson, $fetchedAt, $expiresAt, $responseCode, $responseMessage, $eventCount, $lastError)
ON CONFLICT(waybill_no) DO UPDATE SET
    raw_json = excluded.raw_json,
    fetched_at = excluded.fetched_at,
    expires_at = excluded.expires_at,
    response_code = excluded.response_code,
    response_message = excluded.response_message,
    event_count = excluded.event_count,
    last_error = excluded.last_error;";
                Add(command, "$waybillNo", waybillNo);
                Add(command, "$rawJson", rawJson);
                Add(command, "$fetchedAt", fetchedAt.ToString("O"));
                Add(command, "$expiresAt", expiresAt.ToString("O"));
                Add(command, "$responseCode", responseCode);
                Add(command, "$responseMessage", responseMessage);
                Add(command, "$eventCount", eventCount);
                Add(command, "$lastError", lastError);
                await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }
        }

        private static async Task ReplaceJourneyEventsCoreAsync(
            SqliteConnection connection,
            SqliteTransaction transaction,
            string waybillNo,
            IReadOnlyList<WaybillJourneyRow> rows,
            DateTime fetchedAt,
            CancellationToken cancellationToken)
        {
            await using (var delete = connection.CreateCommand())
            {
                delete.Transaction = transaction;
                delete.CommandText = "DELETE FROM waybill_journey_events WHERE waybill_no = $waybillNo;";
                Add(delete, "$waybillNo", waybillNo);
                await delete.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            if (rows == null || rows.Count == 0)
                return;

            for (var i = 0; i < rows.Count; i++)
            {
                var row = rows[i];
                await using var insert = connection.CreateCommand();
                insert.Transaction = transaction;
                insert.CommandText = @"
INSERT OR REPLACE INTO waybill_journey_events(
    waybill_no, event_index, seq, scan_time, event_time, upload_time, uploaded_at, scan_type_name, action_type, description,
    scan_network_name, scan_network_code, site_info, scan_by_code, scan_by_name,
    package_number, task_code, bag_no, scan_source, weight, converted_weight, volume, volume_weight, attachment_text,
    img_type, severity, raw_event_json, fetched_at, created_at, scan_site)
VALUES (
    $waybillNo, $eventIndex, $seq, $scanTime, $eventTime, $uploadTime, $uploadedAt, $scanTypeName, $actionType, $description,
    $scanNetworkName, $scanNetworkCode, $siteInfo, $scanByCode, $scanByName,
    $packageNumber, $taskCode, $bagNo, $scanSource, $weight, $convertedWeight, $volume, $volumeWeight, $attachmentText,
    $imgType, $severity, $rawEventJson, $fetchedAt, $createdAt, $scanSite);";
                Add(insert, "$waybillNo", waybillNo);
                Add(insert, "$eventIndex", i + 1);
                Add(insert, "$seq", i + 1);
                Add(insert, "$scanTime", row.ActionTime?.ToString("O"));
                Add(insert, "$eventTime", row.EventTime?.ToString("O"));
                Add(insert, "$uploadTime", row.UploadedAt?.ToString("O"));
                Add(insert, "$uploadedAt", row.UploadedAt?.ToString("O"));
                Add(insert, "$scanTypeName", ToDb(row.ActionType));
                Add(insert, "$actionType", ToDb(row.ActionType));
                Add(insert, "$description", ToDb(row.Description));
                Add(insert, "$scanNetworkName", ToDb(row.SiteName));
                Add(insert, "$scanNetworkCode", ToDb(row.SiteCode));
                Add(insert, "$siteInfo", ToDb(row.SiteInfo));
                Add(insert, "$scanByCode", ToDb(row.OperatorCode));
                Add(insert, "$scanByName", ToDb(row.OperatorName));
                Add(insert, "$packageNumber", ToDb(row.PackageNumber));
                Add(insert, "$taskCode", ToDb(row.TaskCode));
                Add(insert, "$bagNo", ToDb(row.BagNo));
                Add(insert, "$scanSource", ToDb(row.ScanSource));
                Add(insert, "$weight", ToDb(row.Weight));
                Add(insert, "$convertedWeight", ToDb(row.ConvertedWeight));
                Add(insert, "$volume", ToDb(row.Volume));
                Add(insert, "$volumeWeight", ToDb(row.VolumeWeight));
                Add(insert, "$attachmentText", ToDb(row.AttachmentText));
                Add(insert, "$imgType", row.ImgType);
                Add(insert, "$severity", ToDb(row.Severity));
                Add(insert, "$rawEventJson", row.RawJson ?? string.Empty);
                Add(insert, "$fetchedAt", fetchedAt.ToString("O"));
                Add(insert, "$createdAt", fetchedAt.ToString("O"));
                Add(insert, "$scanSite", ToDb(row.ScanSite));
                await insert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }
        }

        private static async Task<WaybillJourneyRawCache> ReadRawCacheAsync(
            SqliteConnection connection,
            string sql,
            string waybillNo,
            CancellationToken cancellationToken)
        {
            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            command.Parameters.AddWithValue("$waybillNo", waybillNo);

            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                return new WaybillJourneyRawCache { WaybillNo = waybillNo };

            return new WaybillJourneyRawCache
            {
                WaybillNo = Clean(GetString(reader, 0), fallback: waybillNo),
                RawJson = GetString(reader, 1) ?? string.Empty,
                EventCount = GetInt(reader, 2),
                FetchedAt = ParseNullableDate(GetString(reader, 3)),
                ExpiresAt = ParseNullableDate(GetString(reader, 4)),
                SourceHash = Clean(GetString(reader, 5), fallback: string.Empty),
                LastError = Clean(GetString(reader, 6), fallback: string.Empty)
            };
        }

        private static void NormalizeRows(List<WaybillJourneyRow> rows)
        {
            rows.Sort((a, b) => Nullable.Compare(b.ActionTime, a.ActionTime));
            for (var i = 0; i < rows.Count; i++)
                rows[i].Stt = i + 1;
        }

        private static string NormalizeWaybill(string value) =>
            string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim().ToUpperInvariant();

        private static DateTime? ParseNullableDate(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return null;
            if (DateTime.TryParse(value, null, DateTimeStyles.RoundtripKind, out var roundTrip)) return roundTrip;
            if (DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var invariant)) return invariant;
            if (DateTime.TryParse(value, CultureInfo.CurrentCulture, DateTimeStyles.AssumeLocal, out var local)) return local;
            return null;
        }

        private static string FirstNonEmpty(params string[] values)
        {
            foreach (var value in values ?? Array.Empty<string>())
            {
                var clean = Clean(value);
                if (clean != "--") return clean;
            }

            return "--";
        }

        private static string Clean(string value, string fallback = "--")
        {
            if (string.IsNullOrWhiteSpace(value) || string.Equals(value, "empty", StringComparison.OrdinalIgnoreCase))
                return fallback;
            return value.Trim();
        }

        private static string ToDb(string value)
        {
            var clean = Clean(value);
            return clean == "--" ? string.Empty : clean;
        }

        private static string ComputeSourceHash(string rawJson)
        {
            if (string.IsNullOrEmpty(rawJson))
                return string.Empty;

            using var sha = SHA256.Create();
            var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(rawJson));
            return Convert.ToHexString(bytes).ToLowerInvariant();
        }

        private static string GetString(IDataRecord reader, int index) =>
            reader.IsDBNull(index) ? null : reader.GetValue(index)?.ToString();

        private static int GetInt(IDataRecord reader, int index)
        {
            if (reader.IsDBNull(index)) return 0;
            return Convert.ToInt32(reader.GetValue(index), CultureInfo.InvariantCulture);
        }

        private static int? GetNullableInt(IDataRecord reader, int index)
        {
            if (reader.IsDBNull(index)) return null;
            return Convert.ToInt32(reader.GetValue(index), CultureInfo.InvariantCulture);
        }

        private static void Add(SqliteCommand command, string name, object value)
        {
            command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        }
    }
}

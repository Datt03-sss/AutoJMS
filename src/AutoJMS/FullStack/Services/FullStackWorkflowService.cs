using AutoJMS.FullStack.LocalDb;
using AutoJMS.FullStack.Models;
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
    public sealed class FullStackWorkflowService
    {
        private readonly FullStackDbConnectionFactory _connectionFactory;
        private readonly FullStackDbInitializer _initializer;

        public FullStackWorkflowService()
            : this(new FullStackDbConnectionFactory())
        {
        }

        public FullStackWorkflowService(FullStackDbConnectionFactory connectionFactory)
        {
            _connectionFactory = connectionFactory;
            _initializer = new FullStackDbInitializer(_connectionFactory);
        }

        public async Task AddNoteAsync(string waybillNo, string note, string createdBy = "operator", CancellationToken ct = default)
        {
            waybillNo = NormalizeWaybill(waybillNo);
            if (string.IsNullOrWhiteSpace(waybillNo) || string.IsNullOrWhiteSpace(note)) return;

            await _initializer.InitializeAsync(ct).ConfigureAwait(false);
            string clientId = await FullStackCloudSyncService.Instance.GetOrCreateClientIdAsync(ct).ConfigureAwait(false);
            var createdAt = DateTime.UtcNow;
            string createdByValue = string.IsNullOrWhiteSpace(createdBy) ? "operator" : createdBy.Trim();

            await using var connection = await _connectionFactory.OpenAsync(ct).ConfigureAwait(false);
            await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(ct).ConfigureAwait(false);

            long noteId;
            await using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = @"
INSERT INTO fs_order_notes(waybill_no, note, created_by, created_at, client_id, origin)
VALUES ($waybillNo, $note, $createdBy, $createdAt, NULL, 'local');
SELECT last_insert_rowid();";
                command.Parameters.AddWithValue("$waybillNo", waybillNo);
                command.Parameters.AddWithValue("$note", note.Trim());
                command.Parameters.AddWithValue("$createdBy", createdByValue);
                command.Parameters.AddWithValue("$createdAt", createdAt.ToString("O"));
                noteId = Convert.ToInt64(await command.ExecuteScalarAsync(ct).ConfigureAwait(false), CultureInfo.InvariantCulture);
            }

            await StampNoteClientIdAsync(connection, transaction, noteId, $"{clientId}:note:{noteId}", ct).ConfigureAwait(false);
            await EnqueueOutboxAsync(connection, transaction, "NOTE", waybillNo, new JObject
            {
                ["waybill_no"] = waybillNo,
                ["note"] = note.Trim(),
                ["created_by"] = createdByValue,
                ["created_at"] = createdAt.ToString("O"),
                ["client_id"] = $"{clientId}:note:{noteId}"
            }, ct).ConfigureAwait(false);

            await transaction.CommitAsync(ct).ConfigureAwait(false);
            FullStackCloudSyncService.NotifyLocalWrite();
        }

        public async Task CreateTaskAsync(
            string waybillNo,
            string taskType,
            int priority,
            string assignedTo,
            string note,
            CancellationToken ct = default)
        {
            waybillNo = NormalizeWaybill(waybillNo);
            if (string.IsNullOrWhiteSpace(waybillNo)) return;

            await _initializer.InitializeAsync(ct).ConfigureAwait(false);
            string clientId = await FullStackCloudSyncService.Instance.GetOrCreateClientIdAsync(ct).ConfigureAwait(false);
            var now = DateTime.UtcNow;
            string taskTypeValue = string.IsNullOrWhiteSpace(taskType) ? "CHECK_PHYSICAL_STOCK" : taskType;
            string dueAt = now.AddHours(4).ToString("O");

            await using var connection = await _connectionFactory.OpenAsync(ct).ConfigureAwait(false);
            await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(ct).ConfigureAwait(false);

            long taskId;
            await using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = @"
INSERT INTO fs_dispatch_tasks(waybill_no, task_type, priority, status, assigned_to, due_at, created_at, origin)
VALUES ($waybillNo, $taskType, $priority, 'OPEN', $assignedTo, $dueAt, $createdAt, 'local');
SELECT last_insert_rowid();";
                command.Parameters.AddWithValue("$waybillNo", waybillNo);
                command.Parameters.AddWithValue("$taskType", taskTypeValue);
                command.Parameters.AddWithValue("$priority", priority);
                command.Parameters.AddWithValue("$assignedTo", string.IsNullOrWhiteSpace(assignedTo) ? DBNull.Value : assignedTo.Trim());
                command.Parameters.AddWithValue("$dueAt", dueAt);
                command.Parameters.AddWithValue("$createdAt", now.ToString("O"));
                taskId = Convert.ToInt64(await command.ExecuteScalarAsync(ct).ConfigureAwait(false), CultureInfo.InvariantCulture);
            }

            await using (var stamp = connection.CreateCommand())
            {
                stamp.Transaction = transaction;
                stamp.CommandText = "UPDATE fs_dispatch_tasks SET client_id = $clientId WHERE id = $id;";
                stamp.Parameters.AddWithValue("$clientId", $"{clientId}:task:{taskId}");
                stamp.Parameters.AddWithValue("$id", taskId);
                await stamp.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            }

            await EnqueueOutboxAsync(connection, transaction, "TASK", waybillNo, new JObject
            {
                ["waybill_no"] = waybillNo,
                ["task_type"] = taskTypeValue,
                ["priority"] = priority,
                ["status"] = "OPEN",
                ["assigned_to"] = string.IsNullOrWhiteSpace(assignedTo) ? "" : assignedTo.Trim(),
                ["due_at"] = dueAt,
                ["created_at"] = now.ToString("O"),
                ["updated_at"] = now.ToString("O"),
                ["client_id"] = $"{clientId}:task:{taskId}"
            }, ct).ConfigureAwait(false);

            if (!string.IsNullOrWhiteSpace(note))
            {
                long noteId;
                await using (var noteCommand = connection.CreateCommand())
                {
                    noteCommand.Transaction = transaction;
                    noteCommand.CommandText = @"
INSERT INTO fs_order_notes(waybill_no, note, created_by, created_at, client_id, origin)
VALUES ($waybillNo, $note, 'task', $createdAt, NULL, 'local');
SELECT last_insert_rowid();";
                    noteCommand.Parameters.AddWithValue("$waybillNo", waybillNo);
                    noteCommand.Parameters.AddWithValue("$note", note.Trim());
                    noteCommand.Parameters.AddWithValue("$createdAt", now.ToString("O"));
                    noteId = Convert.ToInt64(await noteCommand.ExecuteScalarAsync(ct).ConfigureAwait(false), CultureInfo.InvariantCulture);
                }

                await StampNoteClientIdAsync(connection, transaction, noteId, $"{clientId}:note:{noteId}", ct).ConfigureAwait(false);
                await EnqueueOutboxAsync(connection, transaction, "NOTE", waybillNo, new JObject
                {
                    ["waybill_no"] = waybillNo,
                    ["note"] = note.Trim(),
                    ["created_by"] = "task",
                    ["created_at"] = now.ToString("O"),
                    ["client_id"] = $"{clientId}:note:{noteId}"
                }, ct).ConfigureAwait(false);
            }

            await transaction.CommitAsync(ct).ConfigureAwait(false);
            FullStackCloudSyncService.NotifyLocalWrite();
        }

        public async Task MarkCheckedAsync(string waybillNo, CancellationToken ct = default)
        {
            await MarkCheckedAsync(waybillNo, "operator", "Đã kiểm tra tồn thực tế", ct).ConfigureAwait(false);
        }

        public async Task MarkCheckedAsync(string waybillNo, string checkedBy, string note, CancellationToken ct = default)
        {
            waybillNo = NormalizeWaybill(waybillNo);
            if (string.IsNullOrWhiteSpace(waybillNo)) return;

            checkedBy = string.IsNullOrWhiteSpace(checkedBy) ? "operator" : checkedBy.Trim();
            note = string.IsNullOrWhiteSpace(note) ? "Đã kiểm tra tồn thực tế" : note.Trim();
            var now = DateTime.UtcNow;

            await _initializer.InitializeAsync(ct).ConfigureAwait(false);
            string clientId = await FullStackCloudSyncService.Instance.GetOrCreateClientIdAsync(ct).ConfigureAwait(false);
            await using var connection = await _connectionFactory.OpenAsync(ct).ConfigureAwait(false);
            await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(ct).ConfigureAwait(false);

            await using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = @"
INSERT INTO fs_order_checks(waybill_no, checked_at, checked_by, note, created_at)
VALUES ($waybillNo, $checkedAt, $checkedBy, $note, $createdAt);";
                command.Parameters.AddWithValue("$waybillNo", waybillNo);
                command.Parameters.AddWithValue("$checkedAt", now.ToString("O"));
                command.Parameters.AddWithValue("$checkedBy", checkedBy);
                command.Parameters.AddWithValue("$note", note);
                command.Parameters.AddWithValue("$createdAt", now.ToString("O"));
                await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            }

            await using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = @"
UPDATE fs_waybills
SET is_checked = 1,
    checked_at = $checkedAt,
    checked_by = $checkedBy,
    updated_at = $updatedAt
WHERE waybill_no = $waybillNo;";
                command.Parameters.AddWithValue("$waybillNo", waybillNo);
                command.Parameters.AddWithValue("$checkedAt", now.ToString("O"));
                command.Parameters.AddWithValue("$checkedBy", checkedBy);
                command.Parameters.AddWithValue("$updatedAt", now.ToString("O"));
                await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            }

            long checkNoteId;
            await using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = @"
INSERT INTO fs_order_notes(waybill_no, note, created_by, created_at, client_id, origin)
VALUES ($waybillNo, $note, $createdBy, $createdAt, NULL, 'local');
SELECT last_insert_rowid();";
                command.Parameters.AddWithValue("$waybillNo", waybillNo);
                command.Parameters.AddWithValue("$note", $"[{checkedBy}] {note}");
                command.Parameters.AddWithValue("$createdBy", checkedBy);
                command.Parameters.AddWithValue("$createdAt", now.ToString("O"));
                checkNoteId = Convert.ToInt64(await command.ExecuteScalarAsync(ct).ConfigureAwait(false), CultureInfo.InvariantCulture);
            }

            await StampNoteClientIdAsync(connection, transaction, checkNoteId, $"{clientId}:note:{checkNoteId}", ct).ConfigureAwait(false);

            await EnqueueOutboxAsync(connection, transaction, "CHECK", waybillNo, new JObject
            {
                ["waybill_no"] = waybillNo,
                ["is_checked"] = true,
                ["checked_at"] = now.ToString("O"),
                ["checked_by"] = checkedBy,
                ["note"] = note,
                ["updated_at"] = now.ToString("O")
            }, ct).ConfigureAwait(false);

            await EnqueueOutboxAsync(connection, transaction, "NOTE", waybillNo, new JObject
            {
                ["waybill_no"] = waybillNo,
                ["note"] = $"[{checkedBy}] {note}",
                ["created_by"] = checkedBy,
                ["created_at"] = now.ToString("O"),
                ["client_id"] = $"{clientId}:note:{checkNoteId}"
            }, ct).ConfigureAwait(false);

            await transaction.CommitAsync(ct).ConfigureAwait(false);
            FullStackCloudSyncService.NotifyLocalWrite();
        }

        private static async Task StampNoteClientIdAsync(SqliteConnection connection, SqliteTransaction transaction, long noteId, string clientId, CancellationToken ct)
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "UPDATE fs_order_notes SET client_id = $clientId WHERE id = $id;";
            command.Parameters.AddWithValue("$clientId", clientId);
            command.Parameters.AddWithValue("$id", noteId);
            await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        private static async Task EnqueueOutboxAsync(SqliteConnection connection, SqliteTransaction transaction, string kind, string refKey, JObject payload, CancellationToken ct)
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = @"
INSERT INTO fs_outbox(kind, ref_key, payload, created_at)
VALUES ($kind, $refKey, $payload, $createdAt);";
            command.Parameters.AddWithValue("$kind", kind);
            command.Parameters.AddWithValue("$refKey", refKey ?? "");
            command.Parameters.AddWithValue("$payload", payload.ToString(Newtonsoft.Json.Formatting.None));
            command.Parameters.AddWithValue("$createdAt", DateTime.UtcNow.ToString("O"));
            await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        public async Task<FullStackOperationMetadataSnapshot> LoadOperationMetadataAsync(IEnumerable<string> waybillNos, CancellationToken ct = default)
        {
            var list = waybillNos?
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(NormalizeWaybill)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList() ?? new List<string>();

            if (list.Count == 0)
                return new FullStackOperationMetadataSnapshot();

            await _initializer.InitializeAsync(ct).ConfigureAwait(false);
            await using var connection = await _connectionFactory.OpenAsync(ct).ConfigureAwait(false);
            var result = new Dictionary<string, FullStackOperationMetadata>(StringComparer.OrdinalIgnoreCase);

            foreach (var batch in list.Chunk(300))
            {
                var names = batch.Select((_, index) => $"$wb{index}").ToArray();
                await using var command = connection.CreateCommand();
                command.CommandText = $@"
SELECT w.waybill_no,
       COALESCE(w.is_checked, 0) AS is_checked,
       w.checked_at,
       w.checked_by,
       COALESCE(w.is_enriched, 0) AS is_enriched,
       w.enriched_at,
       EXISTS(SELECT 1 FROM fs_dispatch_tasks t WHERE t.waybill_no = w.waybill_no) AS has_task,
       EXISTS(SELECT 1 FROM fs_dispatch_tasks t WHERE t.waybill_no = w.waybill_no AND t.status = 'OPEN') AS has_open_task,
       (SELECT COUNT(1) FROM fs_tracking_events e WHERE e.waybill_no = w.waybill_no) AS tracking_event_count
FROM fs_waybills w
WHERE w.waybill_no IN ({string.Join(",", names)});";
                for (int i = 0; i < batch.Length; i++)
                    command.Parameters.AddWithValue(names[i], batch[i]);

                await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
                while (await reader.ReadAsync(ct).ConfigureAwait(false))
                {
                    var item = new FullStackOperationMetadata
                    {
                        WaybillNo = reader.GetString(0),
                        IsChecked = reader.GetInt32(1) != 0,
                        CheckedAt = ParseNullableDate(reader.IsDBNull(2) ? null : reader.GetString(2)),
                        CheckedBy = reader.IsDBNull(3) ? string.Empty : reader.GetString(3),
                        IsEnriched = reader.GetInt32(4) != 0,
                        EnrichedAt = ParseNullableDate(reader.IsDBNull(5) ? null : reader.GetString(5)),
                        HasTask = Convert.ToInt32(reader.GetValue(6), CultureInfo.InvariantCulture) != 0,
                        HasOpenTask = Convert.ToInt32(reader.GetValue(7), CultureInfo.InvariantCulture) != 0,
                        TrackingEventCount = Convert.ToInt32(reader.GetValue(8), CultureInfo.InvariantCulture)
                    };
                    result[item.WaybillNo] = item;
                }
            }

            return new FullStackOperationMetadataSnapshot { Items = result };
        }

        public async Task<IReadOnlyList<TrackingEvent>> LoadTrackingEventsAsync(string waybillNo, CancellationToken ct = default)
        {
            waybillNo = NormalizeWaybill(waybillNo);
            if (string.IsNullOrWhiteSpace(waybillNo)) return Array.Empty<TrackingEvent>();

            await _initializer.InitializeAsync(ct).ConfigureAwait(false);
            await using var connection = await _connectionFactory.OpenAsync(ct).ConfigureAwait(false);
            await using var command = connection.CreateCommand();
            command.CommandText = @"
SELECT id, waybill_no, event_time, action, status, site_code, site_name, operator_code, operator_name, raw_json, created_at
FROM fs_tracking_events
WHERE waybill_no = $waybillNo
ORDER BY COALESCE(event_time, created_at) DESC, id DESC
LIMIT 80;";
            command.Parameters.AddWithValue("$waybillNo", waybillNo);
            var events = new List<TrackingEvent>();
            await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                events.Add(new TrackingEvent
                {
                    Id = reader.GetInt64(0),
                    WaybillNo = reader.GetString(1),
                    EventTime = ParseNullableDate(reader.IsDBNull(2) ? null : reader.GetString(2)),
                    Action = reader.IsDBNull(3) ? string.Empty : reader.GetString(3),
                    Status = reader.IsDBNull(4) ? string.Empty : reader.GetString(4),
                    SiteCode = reader.IsDBNull(5) ? string.Empty : reader.GetString(5),
                    SiteName = reader.IsDBNull(6) ? string.Empty : reader.GetString(6),
                    OperatorCode = reader.IsDBNull(7) ? string.Empty : reader.GetString(7),
                    OperatorName = reader.IsDBNull(8) ? string.Empty : reader.GetString(8),
                    RawJson = reader.IsDBNull(9) ? string.Empty : reader.GetString(9),
                    CreatedAt = ParseDate(reader.IsDBNull(10) ? null : reader.GetString(10))
                });
            }

            return events;
        }

        public async Task<FullStackWorkflowSnapshot> LoadWorkflowAsync(string waybillNo, CancellationToken ct = default)
        {
            waybillNo = NormalizeWaybill(waybillNo);
            if (string.IsNullOrWhiteSpace(waybillNo))
                return new FullStackWorkflowSnapshot();

            await _initializer.InitializeAsync(ct).ConfigureAwait(false);
            await using var connection = await _connectionFactory.OpenAsync(ct).ConfigureAwait(false);
            var notes = new List<FullStackNote>();
            var tasks = new List<FullStackDispatchTask>();

            await using (var command = connection.CreateCommand())
            {
                command.CommandText = @"
SELECT id, waybill_no, note, created_by, created_at
FROM fs_order_notes
WHERE waybill_no = $waybillNo
ORDER BY id DESC
LIMIT 20;";
                command.Parameters.AddWithValue("$waybillNo", waybillNo);
                await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
                while (await reader.ReadAsync(ct).ConfigureAwait(false))
                {
                    notes.Add(new FullStackNote
                    {
                        Id = reader.GetInt64(0),
                        WaybillNo = reader.GetString(1),
                        Note = reader.GetString(2),
                        CreatedBy = reader.IsDBNull(3) ? string.Empty : reader.GetString(3),
                        CreatedAt = ParseDate(reader.IsDBNull(4) ? null : reader.GetString(4))
                    });
                }
            }

            await using (var command = connection.CreateCommand())
            {
                command.CommandText = @"
SELECT id, waybill_no, task_type, priority, status, assigned_to, due_at, created_at, completed_at
FROM fs_dispatch_tasks
WHERE waybill_no = $waybillNo
ORDER BY CASE status WHEN 'OPEN' THEN 0 ELSE 1 END, priority DESC, id DESC
LIMIT 20;";
                command.Parameters.AddWithValue("$waybillNo", waybillNo);
                await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
                while (await reader.ReadAsync(ct).ConfigureAwait(false))
                {
                    tasks.Add(new FullStackDispatchTask
                    {
                        Id = reader.GetInt64(0),
                        WaybillNo = reader.GetString(1),
                        TaskType = reader.GetString(2),
                        Priority = reader.GetInt32(3),
                        Status = reader.GetString(4),
                        AssignedTo = reader.IsDBNull(5) ? string.Empty : reader.GetString(5),
                        DueAt = ParseNullableDate(reader.IsDBNull(6) ? null : reader.GetString(6)),
                        CreatedAt = ParseDate(reader.IsDBNull(7) ? null : reader.GetString(7)),
                        CompletedAt = ParseNullableDate(reader.IsDBNull(8) ? null : reader.GetString(8))
                    });
                }
            }

            return new FullStackWorkflowSnapshot { Notes = notes, Tasks = tasks };
        }

        private static string NormalizeWaybill(string waybillNo) =>
            string.IsNullOrWhiteSpace(waybillNo) ? string.Empty : waybillNo.Trim().ToUpperInvariant();

        private static DateTime ParseDate(string value) =>
            DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var dt)
                ? dt
                : DateTime.UtcNow;

        private static DateTime? ParseNullableDate(string value) =>
            DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var dt)
                ? dt
                : null;
    }
}

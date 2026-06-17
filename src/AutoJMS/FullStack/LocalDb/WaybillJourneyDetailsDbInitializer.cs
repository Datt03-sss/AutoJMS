using Microsoft.Data.Sqlite;
using System.Threading;
using System.Threading.Tasks;

namespace AutoJMS.FullStack.LocalDb
{
    public sealed class WaybillJourneyDetailsDbInitializer
    {
        private readonly WaybillJourneyDetailsDbConnectionFactory _connectionFactory;
        private readonly SemaphoreSlim _initGate = new(1, 1);
        private bool _initialized;

        public WaybillJourneyDetailsDbInitializer(WaybillJourneyDetailsDbConnectionFactory connectionFactory)
        {
            _connectionFactory = connectionFactory;
        }

        public async Task InitializeAsync(CancellationToken cancellationToken = default)
        {
            if (_initialized) return;

            await _initGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (_initialized) return;

                await using var connection = await _connectionFactory.OpenAsync(cancellationToken).ConfigureAwait(false);
                await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
                await using var command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = SchemaSql;
                await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

                await EnsureColumnAsync(connection, transaction, "waybill_journey_events", "seq", "seq INTEGER", cancellationToken).ConfigureAwait(false);
                await EnsureColumnAsync(connection, transaction, "waybill_journey_events", "event_time", "event_time TEXT", cancellationToken).ConfigureAwait(false);
                await EnsureColumnAsync(connection, transaction, "waybill_journey_events", "upload_time", "upload_time TEXT", cancellationToken).ConfigureAwait(false);
                await EnsureColumnAsync(connection, transaction, "waybill_journey_events", "uploaded_at", "uploaded_at TEXT", cancellationToken).ConfigureAwait(false);
                await EnsureColumnAsync(connection, transaction, "waybill_journey_events", "scan_site", "scan_site TEXT", cancellationToken).ConfigureAwait(false);
                await EnsureColumnAsync(connection, transaction, "waybill_journey_events", "bag_no", "bag_no TEXT", cancellationToken).ConfigureAwait(false);
                await EnsureColumnAsync(connection, transaction, "waybill_journey_events", "volume", "volume TEXT", cancellationToken).ConfigureAwait(false);
                await EnsureColumnAsync(connection, transaction, "waybill_journey_events", "volume_weight", "volume_weight TEXT", cancellationToken).ConfigureAwait(false);
                await EnsureColumnAsync(connection, transaction, "waybill_journey_events", "created_at", "created_at TEXT", cancellationToken).ConfigureAwait(false);

                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

                _initialized = true;
                AppLogger.Info($"[FullStackJourneyDetailsDb] initialized path={_connectionFactory.DatabasePath}");
            }
            finally
            {
                _initGate.Release();
            }
        }

        private static async Task EnsureColumnAsync(
            SqliteConnection connection,
            SqliteTransaction transaction,
            string tableName,
            string columnName,
            string columnDefinition,
            CancellationToken cancellationToken)
        {
            if (await ColumnExistsAsync(connection, transaction, tableName, columnName, cancellationToken).ConfigureAwait(false))
                return;

            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = $"ALTER TABLE {tableName} ADD COLUMN {columnDefinition};";
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        private static async Task<bool> ColumnExistsAsync(
            SqliteConnection connection,
            SqliteTransaction transaction,
            string tableName,
            string columnName,
            CancellationToken cancellationToken)
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = $"PRAGMA table_info({tableName});";

            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                var name = reader["name"]?.ToString();
                if (string.Equals(name, columnName, System.StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        private const string SchemaSql = @"
CREATE TABLE IF NOT EXISTS waybill_journey_cache (
    waybill_no TEXT PRIMARY KEY,
    raw_json TEXT NOT NULL,
    event_count INTEGER DEFAULT 0,
    fetched_at TEXT NOT NULL,
    expires_at TEXT NOT NULL,
    source_hash TEXT,
    last_error TEXT
);

CREATE TABLE IF NOT EXISTS waybill_journey_json_cache (
    waybill_no TEXT PRIMARY KEY,
    raw_json TEXT NOT NULL,
    fetched_at TEXT NOT NULL,
    expires_at TEXT NOT NULL,
    response_code INTEGER,
    response_message TEXT,
    event_count INTEGER DEFAULT 0,
    last_error TEXT
);

CREATE TABLE IF NOT EXISTS waybill_journey_events (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    waybill_no TEXT NOT NULL,
    event_index INTEGER NOT NULL,
    scan_time TEXT,
    scan_type_name TEXT,
    action_type TEXT,
    description TEXT,
    scan_network_name TEXT,
    scan_network_code TEXT,
    site_info TEXT,
    scan_by_code TEXT,
    scan_by_name TEXT,
    package_number TEXT,
    task_code TEXT,
    scan_source TEXT,
    weight TEXT,
    converted_weight TEXT,
    attachment_text TEXT,
    img_type INTEGER,
    severity TEXT,
    raw_event_json TEXT,
    fetched_at TEXT NOT NULL,
    UNIQUE(waybill_no, scan_time, scan_type_name, description)
);

CREATE INDEX IF NOT EXISTS idx_journey_events_waybill_time
ON waybill_journey_events(waybill_no, scan_time DESC);

CREATE INDEX IF NOT EXISTS idx_journey_cache_expires
ON waybill_journey_json_cache(expires_at);

CREATE INDEX IF NOT EXISTS idx_journey_cache_new_expires
ON waybill_journey_cache(expires_at);

CREATE INDEX IF NOT EXISTS idx_journey_events_fetched
ON waybill_journey_events(fetched_at);

CREATE INDEX IF NOT EXISTS idx_journey_events_waybill
ON waybill_journey_events(waybill_no, seq);
";
    }
}

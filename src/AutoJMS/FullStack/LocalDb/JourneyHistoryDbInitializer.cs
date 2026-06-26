using Microsoft.Data.Sqlite;
using System.Threading;
using System.Threading.Tasks;

namespace AutoJMS.FullStack.LocalDb
{
    /// <summary>
    /// Creates the schema for the standalone journey-history database. The
    /// journey_history table keeps one row per tracking event with exactly the
    /// columns shown in "Hành trình vận chuyển" plus the raw event JSON.
    /// </summary>
    public sealed class JourneyHistoryDbInitializer
    {
        private readonly JourneyHistoryDbConnectionFactory _connectionFactory;
        private readonly SemaphoreSlim _initGate = new(1, 1);
        private bool _initialized;

        public JourneyHistoryDbInitializer(JourneyHistoryDbConnectionFactory connectionFactory)
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
                await using var command = connection.CreateCommand();
                command.CommandText = SchemaSql;
                await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

                _initialized = true;
                AppLogger.Info($"[JourneyHistoryDb] initialized path={_connectionFactory.DatabasePath}");
            }
            finally
            {
                _initGate.Release();
            }
        }

        private const string SchemaSql = @"
CREATE TABLE IF NOT EXISTS journey_history (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    waybill_no TEXT NOT NULL,
    stt INTEGER NOT NULL,
    scan_time TEXT,
    upload_time TEXT,
    scan_type_name TEXT,
    description TEXT,
    source TEXT,
    weight TEXT,
    raw_json TEXT,
    fetched_at TEXT NOT NULL,
    UNIQUE(waybill_no, scan_time, scan_type_name, description)
);

CREATE INDEX IF NOT EXISTS idx_journey_history_waybill
ON journey_history(waybill_no, stt);

CREATE INDEX IF NOT EXISTS idx_journey_history_scan_time
ON journey_history(waybill_no, scan_time DESC);
";
    }
}

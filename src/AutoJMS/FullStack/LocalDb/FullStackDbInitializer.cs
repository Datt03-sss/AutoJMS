using Microsoft.Data.Sqlite;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace AutoJMS.FullStack.LocalDb
{
    public sealed class FullStackDbInitializer
    {
        private readonly FullStackDbConnectionFactory _connectionFactory;
        private readonly SemaphoreSlim _initGate = new(1, 1);
        private bool _initialized;

        public FullStackDbInitializer(FullStackDbConnectionFactory connectionFactory)
        {
            _connectionFactory = connectionFactory;
        }

        public async Task InitializeAsync(CancellationToken ct = default)
        {
            if (_initialized) return;

            await _initGate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                if (_initialized) return;

                await using var connection = await _connectionFactory.OpenAsync(ct).ConfigureAwait(false);
                await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(ct).ConfigureAwait(false);

                await ExecuteAsync(connection, transaction, FullStackMigrations.SchemaV1, ct).ConfigureAwait(false);
                await EnsureWaybillColumnsAsync(connection, transaction, ct).ConfigureAwait(false);
                await ExecuteAsync(connection, transaction, FullStackMigrations.SchemaV1PostColumnIndexes, ct).ConfigureAwait(false);
                await ExecuteAsync(connection, transaction, FullStackMigrations.SchemaV2, ct).ConfigureAwait(false);
                await EnsureSyncColumnsAsync(connection, transaction, ct).ConfigureAwait(false);
                await ExecuteAsync(connection, transaction, FullStackMigrations.SchemaV2PostColumnIndexes, ct).ConfigureAwait(false);
                await ExecuteAsync(
                    connection,
                    transaction,
                    "INSERT OR IGNORE INTO fs_schema_version(version, applied_at) VALUES ($version, $appliedAt);",
                    ct,
                    ("$version", FullStackMigrations.CurrentVersion),
                    ("$appliedAt", DateTime.UtcNow.ToString("O"))).ConfigureAwait(false);

                await transaction.CommitAsync(ct).ConfigureAwait(false);
                _initialized = true;
                AppLogger.Info($"[FullStackLocalDb] DB initialized path={_connectionFactory.DatabasePath}");
                AppLogger.Info($"[FullStackLocalDb] migration applied version={FullStackMigrations.CurrentVersion}");
            }
            finally
            {
                _initGate.Release();
            }
        }

        private static async Task ExecuteAsync(
            SqliteConnection connection,
            SqliteTransaction transaction,
            string sql,
            CancellationToken ct,
            params (string Name, object Value)[] parameters)
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = sql;
            foreach (var (name, value) in parameters)
                command.Parameters.AddWithValue(name, value ?? DBNull.Value);
            await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        private static async Task EnsureWaybillColumnsAsync(SqliteConnection connection, SqliteTransaction transaction, CancellationToken ct)
        {
            foreach (var (columnName, sql) in FullStackMigrations.WaybillColumnGuards)
            {
                if (await HasColumnAsync(connection, transaction, "fs_waybills", columnName, ct).ConfigureAwait(false))
                    continue;

                await ExecuteAsync(connection, transaction, sql, ct).ConfigureAwait(false);
            }
        }

        private static async Task EnsureSyncColumnsAsync(SqliteConnection connection, SqliteTransaction transaction, CancellationToken ct)
        {
            foreach (var (tableName, columnName, sql) in FullStackMigrations.SyncColumnGuards)
            {
                if (await HasColumnAsync(connection, transaction, tableName, columnName, ct).ConfigureAwait(false))
                    continue;

                await ExecuteAsync(connection, transaction, sql, ct).ConfigureAwait(false);
            }
        }

        private static async Task<bool> HasColumnAsync(SqliteConnection connection, SqliteTransaction transaction, string tableName, string columnName, CancellationToken ct)
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = $"PRAGMA table_info({tableName});";
            await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                if (string.Equals(reader["name"]?.ToString(), columnName, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }
    }
}

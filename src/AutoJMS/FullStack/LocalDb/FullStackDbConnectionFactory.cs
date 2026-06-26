using Microsoft.Data.Sqlite;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace AutoJMS.FullStack.LocalDb
{
    public sealed class FullStackDbConnectionFactory
    {
        public string DatabasePath { get; }

        public FullStackDbConnectionFactory()
        {
            DatabasePath = Path.Combine(AppPaths.UserDataDir, "FullStack", "journey_history.db");
        }

        public async Task<SqliteConnection> OpenAsync(CancellationToken ct = default)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(DatabasePath)!);

            var connection = new SqliteConnection($"Data Source={DatabasePath};Cache=Shared");
            await connection.OpenAsync(ct).ConfigureAwait(false);
            await ApplyPragmasAsync(connection, ct).ConfigureAwait(false);
            return connection;
        }

        private static async Task ApplyPragmasAsync(SqliteConnection connection, CancellationToken ct)
        {
            string[] pragmas =
            {
                "PRAGMA journal_mode=WAL;",
                "PRAGMA synchronous=NORMAL;",
                "PRAGMA busy_timeout=5000;",
                "PRAGMA foreign_keys=ON;"
            };

            foreach (var sql in pragmas)
            {
                await using var command = connection.CreateCommand();
                command.CommandText = sql;
                await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            }
        }
    }
}

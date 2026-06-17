using Microsoft.Data.Sqlite;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace AutoJMS.FullStack.LocalDb
{
    public sealed class WaybillJourneyDetailsDbConnectionFactory
    {
        public string DatabasePath { get; }

        public WaybillJourneyDetailsDbConnectionFactory()
        {
            DatabasePath = Path.Combine(AppPaths.UserDataDir, "FullStack", "details.db");
        }

        public async Task<SqliteConnection> OpenAsync(CancellationToken cancellationToken = default)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(DatabasePath)!);
            var connection = new SqliteConnection($"Data Source={DatabasePath};Cache=Shared");
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            await ApplyPragmasAsync(connection, cancellationToken).ConfigureAwait(false);
            return connection;
        }

        private static async Task ApplyPragmasAsync(SqliteConnection connection, CancellationToken cancellationToken)
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
                await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }
        }
    }
}

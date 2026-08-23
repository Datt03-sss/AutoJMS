using AutoJMS.FullStack.LocalDb;
using Microsoft.Data.Sqlite;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace AutoJMS.Tests
{
    /// <summary>
    /// Covers the SQLCipher-at-rest layer for the FullStack databases. These tests are the only
    /// automated proof that an existing plaintext database survives the one-time migration —
    /// on a real station it runs exactly once, against data that cannot be re-created.
    /// </summary>
    public sealed class LocalDbEncryptionTests
    {
        [Fact]
        public async Task PrepareDatabase_EncryptsExistingPlaintextFileWithoutLosingRows()
        {
            if (!LocalDbEncryption.IsEnabled) return;   // DPAPI unavailable on this host

            var path = NewDatabasePath();
            try
            {
                using (var plain = new SqliteConnection("Data Source=" + path + ";Pooling=False"))
                {
                    plain.Open();
                    Execute(plain, "CREATE TABLE fs_probe(id INTEGER PRIMARY KEY, waybill_no TEXT);");
                    Execute(plain, "INSERT INTO fs_probe(waybill_no) VALUES ('JMS0001'),('JMS0002'),('JMS0003');");
                }
                SqliteConnection.ClearAllPools();

                LocalDbEncryption.PrepareDatabase(path);

                // The rows survived...
                using (var encrypted = new SqliteConnection("Data Source=" + path + ";Pooling=False"))
                {
                    encrypted.Open();
                    await LocalDbEncryption.ApplyKeyAsync(encrypted, CancellationToken.None);
                    Assert.Equal(3L, Scalar(encrypted, "SELECT COUNT(*) FROM fs_probe;"));
                    Assert.Equal("JMS0002", Scalar(encrypted, "SELECT waybill_no FROM fs_probe WHERE id = 2;"));
                }
                SqliteConnection.ClearAllPools();

                // ...and the file is genuinely no longer readable without the key.
                Assert.Throws<SqliteException>(() =>
                {
                    using var unkeyed = new SqliteConnection("Data Source=" + path + ";Pooling=False");
                    unkeyed.Open();
                    Scalar(unkeyed, "SELECT COUNT(*) FROM fs_probe;");
                });
                SqliteConnection.ClearAllPools();

                // No plaintext copy is left behind next to it.
                Assert.False(File.Exists(path + ".plaintext-bak"));
                Assert.False(File.Exists(path + ".sqlcipher-tmp"));
            }
            finally
            {
                Cleanup(path);
            }
        }

        [Fact]
        public async Task PrepareDatabase_IsIdempotentOnAnAlreadyEncryptedFile()
        {
            if (!LocalDbEncryption.IsEnabled) return;

            var path = NewDatabasePath();
            try
            {
                // First open creates the file already keyed — the normal path for a new install.
                using (var connection = new SqliteConnection("Data Source=" + path + ";Pooling=False"))
                {
                    LocalDbEncryption.PrepareDatabase(path);
                    connection.Open();
                    await LocalDbEncryption.ApplyKeyAsync(connection, CancellationToken.None);
                    Execute(connection, "CREATE TABLE fs_probe(id INTEGER PRIMARY KEY);");
                    Execute(connection, "INSERT INTO fs_probe(id) VALUES (7);");
                }
                SqliteConnection.ClearAllPools();

                // A second process-lifetime worth of Prepare calls must not re-encrypt or wipe it.
                LocalDbEncryption.PrepareDatabase(path);

                using var reopened = new SqliteConnection("Data Source=" + path + ";Pooling=False");
                reopened.Open();
                await LocalDbEncryption.ApplyKeyAsync(reopened, CancellationToken.None);
                Assert.Equal(7L, Scalar(reopened, "SELECT id FROM fs_probe;"));
            }
            finally
            {
                Cleanup(path);
            }
        }

        private static string NewDatabasePath() =>
            Path.Combine(Path.GetTempPath(), "autojms-dbenc-" + Guid.NewGuid().ToString("N") + ".db");

        private static void Execute(SqliteConnection connection, string sql)
        {
            using var command = connection.CreateCommand();
            command.CommandText = sql;
            command.ExecuteNonQuery();
        }

        private static object Scalar(SqliteConnection connection, string sql)
        {
            using var command = connection.CreateCommand();
            command.CommandText = sql;
            return command.ExecuteScalar();
        }

        private static void Cleanup(string path)
        {
            SqliteConnection.ClearAllPools();
            foreach (var suffix in new[] { "", "-wal", "-shm", ".plaintext-bak", ".sqlcipher-tmp" })
            {
                try { if (File.Exists(path + suffix)) File.Delete(path + suffix); }
                catch { /* temp files, best effort */ }
            }
        }
    }
}

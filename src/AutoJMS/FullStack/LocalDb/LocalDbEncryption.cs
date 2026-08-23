#nullable enable
using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace AutoJMS.FullStack.LocalDb
{
    /// <summary>
    /// SQLCipher-at-rest for the FullStack SQLite files.
    ///
    /// There are three connection factories but only two physical databases:
    ///   * FullStack\journey_history.db — shared by <see cref="FullStackDbConnectionFactory"/>
    ///     and <see cref="JourneyHistoryDbConnectionFactory"/>
    ///   * FullStack\details.db        — <see cref="WaybillJourneyDetailsDbConnectionFactory"/>
    ///
    /// Key handling:
    ///   * A 32-byte random key is generated once and stored DPAPI-protected under
    ///     AppData\secure\fullstack-db.key. The key never exists in plaintext on disk.
    ///   * <see cref="DataProtectionScope.LocalMachine"/> is deliberate: AppData lives inside the
    ///     install root, which any Windows account on the station may run the app from, and
    ///     CurrentUser scope would make the history unreadable for a second operator. The threat
    ///     this closes is a database file copied off the machine — not a local administrator.
    ///   * The key is passed as a raw hex literal (PRAGMA key = "x'…'"), so SQLCipher skips the
    ///     256k-iteration KDF. Connections are opened per operation here, and a passphrase key
    ///     would add that derivation cost to every single one.
    ///
    /// Availability rules — encryption must never be able to take the ULTRA window offline:
    ///   * No key (DPAPI unavailable, unwritable folder) ⇒ databases stay plaintext, log a warning.
    ///   * A failed migration leaves the original plaintext file untouched and keeps running
    ///     unencrypted; the next launch retries.
    ///   * Set AUTOJMS_DB_ENCRYPTION=0 to disable this layer entirely (support escape hatch for a
    ///     station whose key was lost — the databases are caches of JMS + DataHub data).
    /// </summary>
    internal static class LocalDbEncryption
    {
        private const string DisableEnvVar = "AUTOJMS_DB_ENCRYPTION";
        private const string KeyFileName = "fullstack-db.key";
        private const int KeySizeBytes = 32;

        /// <summary>DPAPI optional entropy — binds the blob to this application.</summary>
        private static readonly byte[] Entropy =
            System.Text.Encoding.UTF8.GetBytes("AutoJMS.FullStack.LocalDb.v1");

        private static readonly object Gate = new();
        private static readonly HashSet<string> PreparedPaths =
            new(StringComparer.OrdinalIgnoreCase);

        private static string? _keyLiteral;
        private static bool _keyResolved;

        private static string KeyFilePath => Path.Combine(AppPaths.SecureDir, KeyFileName);

        /// <summary>True when a usable key exists and the databases are being kept encrypted.</summary>
        public static bool IsEnabled => ResolveKeyLiteral() != null;

        /// <summary>
        /// Applies the cipher key to a freshly opened connection. Must be the first statement
        /// executed on that connection — SQLCipher reads the header on the first page access.
        /// A no-op when encryption is unavailable, which is what keeps plaintext installs working.
        /// </summary>
        public static async Task ApplyKeyAsync(SqliteConnection connection, CancellationToken ct)
        {
            var keyLiteral = ResolveKeyLiteral();
            if (keyLiteral == null) return;

            await using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA key = \"" + keyLiteral + "\";";
            await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        /// <summary>
        /// Runs once per database path, before its first connection: creates the key if needed and
        /// converts an existing plaintext file in place. Cheap on every call after the first.
        /// </summary>
        public static void PrepareDatabase(string databasePath)
        {
            if (string.IsNullOrWhiteSpace(databasePath)) return;

            lock (Gate)
            {
                if (!PreparedPaths.Add(databasePath)) return;

                var keyLiteral = ResolveKeyLiteral();
                if (keyLiteral == null) return;

                try
                {
                    if (!File.Exists(databasePath)) return;   // created keyed on first open
                    if (CanOpen(databasePath, keyLiteral)) return;

                    if (!CanOpen(databasePath, null))
                    {
                        // Neither the key nor "no key" opens it: the file is corrupt, or it was
                        // encrypted with a key this machine no longer has. Do not touch it —
                        // surface the real SQLite error to the caller instead of losing history.
                        AppLogger.Error("[LocalDb] " + Path.GetFileName(databasePath) +
                            " cannot be opened with the current key. Restore AppData\\secure\\" + KeyFileName +
                            ", or set " + DisableEnvVar + "=0 and remove the file to start a fresh cache.");
                        return;
                    }

                    EncryptInPlace(databasePath, keyLiteral);
                }
                catch (Exception ex)
                {
                    // Availability first: keep the plaintext database and carry on unencrypted.
                    AppLogger.Error("[LocalDb] encryption migration failed for " +
                        Path.GetFileName(databasePath) + ": " + ex.Message);
                }
            }
        }

        // ── key material ────────────────────────────────────────

        private static string? ResolveKeyLiteral()
        {
            lock (Gate)
            {
                if (_keyResolved) return _keyLiteral;
                _keyResolved = true;

                if (string.Equals(Environment.GetEnvironmentVariable(DisableEnvVar), "0", StringComparison.Ordinal))
                {
                    AppLogger.Warning("[LocalDb] " + DisableEnvVar + "=0 — FullStack databases stay unencrypted.");
                    return _keyLiteral = null;
                }

                try
                {
                    _keyLiteral = "x'" + Convert.ToHexString(LoadOrCreateKey()) + "'";
                }
                catch (Exception ex)
                {
                    AppLogger.Error("[LocalDb] cannot obtain the database key, falling back to " +
                        "unencrypted storage: " + ex.Message);
                    _keyLiteral = null;
                }

                return _keyLiteral;
            }
        }

        private static byte[] LoadOrCreateKey()
        {
            Directory.CreateDirectory(AppPaths.SecureDir);
            var path = KeyFilePath;

            if (File.Exists(path))
            {
                var protectedBlob = File.ReadAllBytes(path);
                if (protectedBlob.Length > 0)
                {
                    var key = ProtectedData.Unprotect(protectedBlob, Entropy, DataProtectionScope.LocalMachine);
                    if (key.Length == KeySizeBytes) return key;
                    throw new CryptographicException("Khóa cơ sở dữ liệu có độ dài không hợp lệ.");
                }
            }

            var fresh = RandomNumberGenerator.GetBytes(KeySizeBytes);
            var blob = ProtectedData.Protect(fresh, Entropy, DataProtectionScope.LocalMachine);
            // Write via a temp file so a crash mid-write cannot leave a truncated key behind —
            // a half-written key is indistinguishable from a wrong key once a database uses it.
            var tempPath = path + ".tmp";
            File.WriteAllBytes(tempPath, blob);
            if (File.Exists(path)) File.Delete(path);
            File.Move(tempPath, path);
            AppLogger.Info("[LocalDb] generated a new SQLCipher key for the FullStack databases.");
            return fresh;
        }

        // ── migration ───────────────────────────────────────────

        private static void EncryptInPlace(string databasePath, string keyLiteral)
        {
            var tempPath = databasePath + ".sqlcipher-tmp";
            DeleteDatabaseFiles(tempPath);

            Dictionary<string, long> before;
            using (var plain = new SqliteConnection(BuildConnectionString(databasePath)))
            {
                plain.Open();
                // Anything still in -wal has to land in the main file: sqlcipher_export copies
                // the database it can see, and the stale -wal is dropped below.
                Execute(plain, "PRAGMA wal_checkpoint(TRUNCATE);");
                before = ReadTableCounts(plain);

                Execute(plain, "ATTACH DATABASE '" + EscapeLiteral(tempPath) +
                    "' AS encrypted KEY \"" + keyLiteral + "\";");
                Execute(plain, "SELECT sqlcipher_export('encrypted');");
                Execute(plain, "DETACH DATABASE encrypted;");
            }
            SqliteConnection.ClearAllPools();

            VerifyCopy(tempPath, keyLiteral, before);

            // Atomic swap, then drop the plaintext original and its journal siblings.
            var backupPath = databasePath + ".plaintext-bak";
            if (File.Exists(backupPath)) File.Delete(backupPath);
            File.Replace(tempPath, databasePath, backupPath);
            File.Delete(backupPath);
            TryDelete(databasePath + "-wal");
            TryDelete(databasePath + "-shm");
            TryDelete(tempPath + "-wal");
            TryDelete(tempPath + "-shm");

            long rows = 0;
            foreach (var pair in before) rows += pair.Value;
            AppLogger.Info("[LocalDb] encrypted " + Path.GetFileName(databasePath) +
                " with SQLCipher (tables=" + before.Count.ToString(CultureInfo.InvariantCulture) +
                " rows=" + rows.ToString(CultureInfo.InvariantCulture) + ").");
        }

        /// <summary>
        /// Refuses the swap unless the encrypted copy opens with the key, passes integrity_check
        /// and holds exactly the same row count in every table.
        /// </summary>
        private static void VerifyCopy(string tempPath, string keyLiteral, Dictionary<string, long> before)
        {
            using (var encrypted = new SqliteConnection(BuildConnectionString(tempPath)))
            {
                encrypted.Open();
                Execute(encrypted, "PRAGMA key = \"" + keyLiteral + "\";");

                var integrity = Scalar(encrypted, "PRAGMA integrity_check;");
                if (!string.Equals(integrity, "ok", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("integrity_check = " + (integrity ?? "null"));

                var after = ReadTableCounts(encrypted);
                foreach (var pair in before)
                {
                    if (!after.TryGetValue(pair.Key, out var count) || count != pair.Value)
                        throw new InvalidDataException("row count mismatch on table " + pair.Key +
                            " (" + pair.Value.ToString(CultureInfo.InvariantCulture) + " → " +
                            count.ToString(CultureInfo.InvariantCulture) + ")");
                }
            }
            SqliteConnection.ClearAllPools();
        }

        private static bool CanOpen(string databasePath, string? keyLiteral)
        {
            try
            {
                using var connection = new SqliteConnection(BuildConnectionString(databasePath));
                connection.Open();
                if (keyLiteral != null) Execute(connection, "PRAGMA key = \"" + keyLiteral + "\";");
                // Touches page 1, which is what actually proves the key: PRAGMA key alone never fails.
                Execute(connection, "SELECT count(*) FROM sqlite_master;");
                return true;
            }
            catch (SqliteException)
            {
                return false;
            }
            finally
            {
                SqliteConnection.ClearAllPools();
            }
        }

        private static Dictionary<string, long> ReadTableCounts(SqliteConnection connection)
        {
            var names = new List<string>();
            using (var command = connection.CreateCommand())
            {
                command.CommandText =
                    "SELECT name FROM sqlite_master WHERE type = 'table' AND name NOT LIKE 'sqlite_%' ORDER BY name;";
                using var reader = command.ExecuteReader();
                while (reader.Read()) names.Add(reader.GetString(0));
            }

            var counts = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
            foreach (var name in names)
            {
                using var command = connection.CreateCommand();
                // Table names come from sqlite_master, so quoting is enough.
                command.CommandText = "SELECT COUNT(*) FROM \"" + name.Replace("\"", "\"\"") + "\";";
                counts[name] = Convert.ToInt64(command.ExecuteScalar(), CultureInfo.InvariantCulture);
            }
            return counts;
        }

        private static string BuildConnectionString(string databasePath) =>
            "Data Source=" + databasePath + ";Pooling=False";

        private static void Execute(SqliteConnection connection, string sql)
        {
            using var command = connection.CreateCommand();
            command.CommandText = sql;
            command.ExecuteNonQuery();
        }

        private static string? Scalar(SqliteConnection connection, string sql)
        {
            using var command = connection.CreateCommand();
            command.CommandText = sql;
            return command.ExecuteScalar()?.ToString();
        }

        private static string EscapeLiteral(string value) => value.Replace("'", "''");

        private static void DeleteDatabaseFiles(string path)
        {
            TryDelete(path);
            TryDelete(path + "-wal");
            TryDelete(path + "-shm");
        }

        private static void TryDelete(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); }
            catch (Exception ex) { AppLogger.Warning("[LocalDb] cannot delete " + Path.GetFileName(path) + ": " + ex.Message); }
        }
    }
}

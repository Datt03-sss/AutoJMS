using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace AutoJMS.FullStack.LocalDb
{
    public static class FullStackLocalDbPaths
    {
        private static readonly object MigrateGate = new();
        // Danh sach duong dan da kiem tra migration trong phien nay — cung pattern voi
        // LocalDbEncryption.PreparedPaths de dam bao chi chay 1 lan moi duong dan,
        // kể ca khi FullStackDbConnectionFactory va JourneyHistoryDbConnectionFactory
        // mo cung 1 file dong thoi.
        private static readonly HashSet<string> MigratedPaths =
            new(StringComparer.OrdinalIgnoreCase);

        public static string GetDatabasePath(string dbFileName)
        {
            string siteCode = SiteContextProvider.Get();
            string folder = string.IsNullOrWhiteSpace(siteCode) ? "default" : SanitizeFolderName(siteCode);
            return Path.Combine(AppPaths.UserDataDir, "FullStack", folder, dbFileName);
        }

        /// <summary>
        /// Di chuyen file database cu (AppData/FullStack/&lt;fileName&gt;) sang duong dan
        /// theo ma buu cuc neu can. Goi truoc Directory.CreateDirectory trong OpenAsync.
        /// </summary>
        public static void MigrateLegacyIfNeeded(string targetPath)
            => MigrateLegacyIfNeeded(targetPath, AppPaths.UserDataDir);

        // Overload noi bo cho test: nhan root thu muc de khong phu thuoc AppPaths.UserDataDir.
        internal static void MigrateLegacyIfNeeded(string targetPath, string userDataRoot)
        {
            // Bo qua khi siteCode rong: neu chuyen file vao thu muc "default", file se bi
            // giam o day khi siteCode that su den, vi factory luc do se mo thu muc khac.
            // De nguyen: lan dau tien co siteCode se tim thay file cu tai legacyPath va nhan no.
            if (string.IsNullOrWhiteSpace(SiteContextProvider.Get())) return;

            lock (MigrateGate)
            {
                // Chi chay 1 lan moi duong dan trong phien — hai factory co the goi dong thoi
                // cho cung journey_history.db, khoa nay ngan race condition.
                if (!MigratedPaths.Add(targetPath)) return;

                string fileName = Path.GetFileName(targetPath);
                // Duong dan legacy: AppData/FullStack/<fileName> (khong co thu muc buu cuc)
                string legacyPath = Path.Combine(userDataRoot, "FullStack", fileName);

                if (!File.Exists(legacyPath)) return;
                // Neu file dich da ton tai, giu nguyen file cu — khong ghi de du lieu moi
                if (File.Exists(targetPath)) return;

                try
                {
                    // Chuyen WAL va SHM truoc, DB sau: neu crash giua chung, DB chinh van con
                    // o legacyPath va factory van mo duoc. Mat WAL toi hon mat DB chinh.
                    TryMoveFile(legacyPath + "-wal", targetPath + "-wal");
                    TryMoveFile(legacyPath + "-shm", targetPath + "-shm");
                    Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
                    File.Move(legacyPath, targetPath);
                    AppLogger.Info("[LocalDb] doi database cu: " + legacyPath + " -> " + targetPath);
                }
                catch (Exception ex)
                {
                    AppLogger.Error("[LocalDb] doi database cu that bai: " +
                        legacyPath + " -> " + targetPath + ": " + ex.Message);
                    // Khong nuot loi: neu nuot, OpenAsync se tiep tuc tao file rong tai targetPath,
                    // File.Exists(targetPath) se tra ve true va biet lap migration mai mai —
                    // du lieu legacy bi mat vinh vien. Nem lai de giu legacy nguyen va cho lan
                    // khoi dong ke tiep thu lai.
                    throw;
                }
            }
        }

        private static void TryMoveFile(string src, string dest)
        {
            if (!File.Exists(src)) return;
            // Neu dich da ton tai (tu lan chay truoc bi gian doan), giu nguyen — khong ghi de
            if (File.Exists(dest)) return;
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            File.Move(src, dest);
        }

        private static string SanitizeFolderName(string name)
        {
            var invalid = Path.GetInvalidFileNameChars();
            // Path.GetInvalidFileNameChars() khong co '.'; can Trim('.') de "." khoi ve
            // thu muc chia se va ".." khoi thoat khoi FullStack\.
            var clean = new string(name.Where(c => !invalid.Contains(c)).ToArray()).Trim().Trim('.');
            return string.IsNullOrWhiteSpace(clean) ? "default" : clean;
        }
    }
}

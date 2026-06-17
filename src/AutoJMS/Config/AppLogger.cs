using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace AutoJMS
{
    /// <summary>
    /// Real-time logger that records every operation, warning, and error to
    /// <see cref="AppPaths.DebugLogFile"/> ({InstallRoot}\AppData\logs\debug.log).
    /// </summary>
    public static class AppLogger
    {
        private static readonly object _lockObj = new();
        private static readonly string LogFilePath = AppPaths.DebugLogFile;
        private const long MaxLogBytes = 5 * 1024 * 1024;   // rotate at 5 MB
        private const int KeepRotated = 5;                   // keep 5 rotated files

        static AppLogger()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(LogFilePath)!);
            }
            catch { }
        }

        public static string LogFile => LogFilePath;

        public static void Info(string message)    => WriteLog("INFO",  message);
        public static void Warning(string message) => WriteLog("WARN",  message);
        public static void Error(string message, Exception ex = null) => WriteLog("ERROR", Compose(message, ex));
        public static void Fatal(string message, Exception ex = null) => WriteLog("FATAL", Compose(message, ex));
        public static void Debug(string message)   => WriteLog("DEBUG", message);
        public static void Action(string message)  => WriteLog("ACTION", message);
        public static void WriteRaw(string message) => WriteLog("RAW", message);

        private static string Compose(string message, Exception ex)
        {
            if (ex == null) return message;
            return $"{message}{Environment.NewLine}Exception: {ex.GetType().FullName}: {ex.Message}{Environment.NewLine}StackTrace: {ex.StackTrace}";
        }

        private static void WriteLog(string level, string message)
        {
            try
            {
                lock (_lockObj)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(LogFilePath)!);
                    RotateIfNeeded();
                    string entry = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [{level}] {message}{Environment.NewLine}";
                    File.AppendAllText(LogFilePath, entry, Encoding.UTF8);
                }
            }
            catch
            {
                // Last-resort fallback so a logger crash never crashes the app.
                try
                {
                    string fallback = Path.Combine(AppPaths.InstallRoot, "AutoJMS_fallback.log");
                    File.AppendAllText(fallback,
                        $"[{DateTime.Now:HH:mm:ss}] [{level}] {message}{Environment.NewLine}",
                        Encoding.UTF8);
                }
                catch { }
            }
        }

        private static void RotateIfNeeded()
        {
            try
            {
                if (!File.Exists(LogFilePath)) return;
                var info = new FileInfo(LogFilePath);
                if (info.Length < MaxLogBytes) return;

                // debug.log -> debug.1.log -> debug.2.log -> ... -> drop oldest
                string dir = Path.GetDirectoryName(LogFilePath)!;
                string baseName = Path.GetFileNameWithoutExtension(LogFilePath); // "debug"
                string ext = Path.GetExtension(LogFilePath);                     // ".log"

                string oldest = Path.Combine(dir, $"{baseName}.{KeepRotated}{ext}");
                if (File.Exists(oldest)) File.Delete(oldest);

                for (int i = KeepRotated - 1; i >= 1; i--)
                {
                    string src = Path.Combine(dir, $"{baseName}.{i}{ext}");
                    string dst = Path.Combine(dir, $"{baseName}.{i + 1}{ext}");
                    if (File.Exists(src)) File.Move(src, dst);
                }

                File.Move(LogFilePath, Path.Combine(dir, $"{baseName}.1{ext}"));
            }
            catch { }
        }
    }
}

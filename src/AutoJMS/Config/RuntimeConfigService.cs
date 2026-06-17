using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace AutoJMS
{
    public class RuntimeConfigService
    {
        private static readonly string CacheDir = AppPaths.CacheDir;
        private static readonly string CacheFilePath = AppPaths.RuntimeConfigCache;
        private static readonly string CacheSecret = $"{Environment.MachineName}|{Environment.UserName}|RuntimeConfig";
        private static readonly JsonSerializerOptions JsonOpts = new()
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        };

        private RuntimeConfig _current = new();
        private readonly object _lock = new();

        public RuntimeConfig Current
        {
            get { lock (_lock) return _current; }
        }

        public event Action<RuntimeConfig> OnConfigChanged;

        public bool ApplyDecryptedConfig(byte[] configBytes)
        {
            if (configBytes == null || configBytes.Length == 0) return false;
            try
            {
                var json = Encoding.UTF8.GetString(configBytes);
                var parsed = JsonSerializer.Deserialize<RuntimeConfig>(json, JsonOpts);
                if (parsed == null) return false;

                lock (_lock)
                {
                    _current = parsed;
                }

                SaveCache(json);
                OnConfigChanged?.Invoke(_current);
                AppLogger.Info("RuntimeConfig applied successfully");
                return true;
            }
            catch (Exception ex)
            {
                AppLogger.Error("RuntimeConfigService: apply failed", ex);
                return false;
            }
        }

        public RuntimeConfig LoadCachedOrDefault()
        {
            try
            {
                if (!File.Exists(CacheFilePath)) return _current;
                var encrypted = File.ReadAllText(CacheFilePath);
                var json = SecureConfigCrypto.UnprotectString(encrypted, CacheSecret);
                var parsed = JsonSerializer.Deserialize<RuntimeConfig>(json, JsonOpts);
                if (parsed != null)
                {
                    lock (_lock) { _current = parsed; }
                    AppLogger.Info("RuntimeConfig loaded from cache");
                }
            }
            catch (Exception ex)
            {
                AppLogger.Warning($"RuntimeConfigService: cache load failed, using defaults. {ex.Message}");
            }
            return _current;
        }

        private void SaveCache(string json)
        {
            try
            {
                Directory.CreateDirectory(CacheDir);
                var encrypted = SecureConfigCrypto.ProtectString(json, CacheSecret);
                var tmp = CacheFilePath + ".tmp";
                File.WriteAllText(tmp, encrypted);
                if (File.Exists(CacheFilePath)) File.Delete(CacheFilePath);
                File.Move(tmp, CacheFilePath);
            }
            catch (Exception ex)
            {
                AppLogger.Warning($"RuntimeConfigService: cache save failed. {ex.Message}");
            }
        }

        public T GetValue<T>(string section, string key, T defaultValue = default)
        {
            var cfg = Current;
            if (cfg.TimeoutRetry != null)
            {
                var dict = new System.Collections.Generic.Dictionary<string, object>
                {
                    ["tracking.intervalMinutes"] = cfg.Tracking?.IntervalMinutes ?? 30,
                    ["tracking.slaWarningHours"] = cfg.Tracking?.SlaWarningHours ?? 2.0,
                    ["tracking.slaCriticalHours"] = cfg.Tracking?.SlaCriticalHours ?? 0.5,
                    ["tracking.autoRefreshIntervalMs"] = cfg.Tracking?.AutoRefreshIntervalMs ?? 60000,
                    ["print.keepRecentPdfCount"] = cfg.Print?.KeepRecentPdfCount ?? 500,
                    ["print.printLogRetentionDays"] = cfg.Print?.PrintLogRetentionDays ?? 3,
                    ["workflow.dkchTimeoutMs"] = cfg.Workflow?.DkchTimeoutMs ?? 30000,
                    ["workflow.searchTimeoutMs"] = cfg.Workflow?.SearchTimeoutMs ?? 15000,
                    ["timeoutRetry.defaultTimeoutMs"] = cfg.TimeoutRetry?.DefaultTimeoutMs ?? 30000,
                    ["timeoutRetry.maxRetries"] = cfg.TimeoutRetry?.MaxRetries ?? 3,
                    ["timeoutRetry.retryDelayMs"] = cfg.TimeoutRetry?.RetryDelayMs ?? 2000,
                };
            }
            return defaultValue;
        }
    }
}

using AutoJMS.Abstractions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace AutoJMS.ModuleSystem
{
    public class BuiltInRetryPolicy : IRetryPolicy
    {
        public string Name => "BuiltIn.Retry";
        public string Version => "1.0.0";

        public Task<bool> InitializeAsync(CancellationToken ct = default) => Task.FromResult(true);

        public async Task<T> RetryAsync<T>(Func<Task<T>> action, int maxRetries, int delayMs, CancellationToken ct = default)
        {
            for (int attempt = 1; attempt <= maxRetries; attempt++)
            {
                try
                {
                    ct.ThrowIfCancellationRequested();
                    return await action();
                }
                catch (Exception ex) when (attempt < maxRetries && !ct.IsCancellationRequested)
                {
                    AppLogger.Warning($"Retry {attempt}/{maxRetries} failed: {ex.Message}");
                    await Task.Delay(delayMs * attempt, ct);
                }
            }
            return await action();
        }
    }

    public class BuiltInWorkflowProvider : IWorkflowProvider
    {
        public string Name => "BuiltIn.Workflow";
        public string Version => "1.0.0";

        public Task<bool> InitializeAsync(CancellationToken ct = default) => Task.FromResult(true);

        public Task<bool> ExecuteAsync(string action, string parameter, CancellationToken ct = default)
        {
            AppLogger.Warning($"BuiltInWorkflowProvider: ExecuteAsync({action}) not implemented");
            return Task.FromResult(false);
        }

        public async Task<string> GetTrackingDataAsync(string waybillNo, int timeoutMs = 10000)
        {
            await Task.CompletedTask;
            return null;
        }

        public bool NeedSwitchToDkch2(string pageSource)
        {
            if (string.IsNullOrWhiteSpace(pageSource)) return false;
            return pageSource.Contains("DKCH2", StringComparison.OrdinalIgnoreCase) ||
                   pageSource.Contains("needSwitchToDkch2", StringComparison.OrdinalIgnoreCase);
        }
    }

    public class BuiltInSelectorProvider : ISelectorProvider
    {
        public string Name => "BuiltIn.Selectors";
        public string Version => "1.0.0";

        private static readonly Dictionary<string, Dictionary<string, string>> DefaultSelectors = new()
        {
            ["tracking"] = new Dictionary<string, string>
            {
                ["searchInput"] = "#waybillSearch",
                ["searchButton"] = ".btn-search",
                ["resultTable"] = "#trackingResult",
                ["detailRow"] = ".detail-row",
                ["statusCell"] = ".status-cell",
                ["timeCell"] = ".time-cell"
            },
            ["login"] = new Dictionary<string, string>
            {
                ["usernameInput"] = "#username",
                ["passwordInput"] = "#password",
                ["loginButton"] = ".btn-login"
            }
        };

        public Task<bool> InitializeAsync(CancellationToken ct = default) => Task.FromResult(true);

        public Dictionary<string, string> GetSelectors(string scope)
        {
            return DefaultSelectors.TryGetValue(scope, out var selectors)
                ? new Dictionary<string, string>(selectors)
                : new Dictionary<string, string>();
        }

        public string GetSelector(string scope, string key)
        {
            return DefaultSelectors.TryGetValue(scope, out var scopeSelectors)
                ? scopeSelectors.TryGetValue(key, out var val) ? val : null
                : null;
        }

        public string GetSelector(string key)
        {
            return DefaultSelectors.Values
                .SelectMany(d => d)
                .Where(kv => string.Equals(kv.Key, key, StringComparison.OrdinalIgnoreCase))
                .Select(kv => kv.Value)
                .FirstOrDefault();
        }
    }

    public class BuiltInConfigProvider : IConfigProvider
    {
        public string Name => "BuiltIn.Config";
        public string Version => "1.0.0";

        private readonly Dictionary<string, object> _config = new()
        {
            ["trackingIntervalMins"] = 30,
            ["retryMaxAttempts"] = 3,
            ["retryDelayMs"] = 2000,
            ["slaWarningHours"] = 2.0,
            ["slaCriticalHours"] = 0.5,
            ["autoRefreshIntervalMs"] = 60000
        };

        public Task<bool> InitializeAsync(CancellationToken ct = default) => Task.FromResult(true);

        public string GetConfig(string section, string key, string defaultValue = "")
        {
            return GetValue($"{section}.{key}", defaultValue);
        }

        public T GetConfig<T>(string section, string key, T defaultValue = default)
        {
            return GetValue($"{section}.{key}", defaultValue);
        }

        public T GetValue<T>(string key, T defaultValue = default)
        {
            if (_config.TryGetValue(key, out var val) && val is T t)
                return t;
            return defaultValue;
        }

        public Dictionary<string, object> GetSection(string section)
        {
            return _config
                .Where(kv => kv.Key.StartsWith(section + ".", StringComparison.OrdinalIgnoreCase))
                .ToDictionary(kv => kv.Key.Substring(section.Length + 1), kv => kv.Value);
        }
    }
}

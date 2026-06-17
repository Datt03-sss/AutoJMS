using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AutoJMS.Abstractions;

namespace AutoJMS.Selectors
{
    public class SelectorsModule : IModule, ISelectorProvider
    {
        public string Name => "Selectors";
        public string Version => "1.0.0";

        private Dictionary<string, Dictionary<string, string>> _selectors = new();
        private static readonly string SelectorsPath = Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory, "modules", "selectors.json");
        private static readonly JsonSerializerOptions JsonOpts = new()
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        };

        public Task<bool> InitializeAsync(CancellationToken ct = default)
        {
            try
            {
                if (File.Exists(SelectorsPath))
                {
                    var json = File.ReadAllText(SelectorsPath);
                    _selectors = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, string>>>(json, JsonOpts)
                        ?? new Dictionary<string, Dictionary<string, string>>();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"SelectorsModule init: {ex.Message}");
            }
            return Task.FromResult(true);
        }

        public Dictionary<string, string> GetSelectors(string scope)
        {
            return _selectors.TryGetValue(scope, out var selectors)
                ? new Dictionary<string, string>(selectors)
                : new Dictionary<string, string>();
        }

        public string GetSelector(string scope, string key)
        {
            return _selectors.TryGetValue(scope, out var scopeSelectors)
                ? scopeSelectors.TryGetValue(key, out var val) ? val : null
                : null;
        }

        public string GetSelector(string key)
        {
            foreach (var scope in _selectors.Values)
            {
                if (scope.TryGetValue(key, out var value))
                    return value;
            }
            return null;
        }
    }
}

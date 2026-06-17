#nullable enable
using AutoJMS.Diagnostics;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace AutoJMS.Automation.DevTools
{
    public sealed class WebViewDevToolsInspector : IDisposable
    {
        private readonly object _consoleSync = new();
        private readonly List<ConsoleMessageCapture> _consoleMessages = new();
        private WebView2? _webView;
        private CoreWebView2? _core;
        private CoreWebView2DevToolsProtocolEventReceiver? _consoleReceiver;
        private CoreWebView2DevToolsProtocolEventReceiver? _logReceiver;
        private bool _attached;

        public WebViewDevToolsInspector(string surfaceName)
        {
            SurfaceName = string.IsNullOrWhiteSpace(surfaceName) ? "WebView" : surfaceName.Trim();
            Network = new NetworkCaptureService();
            Dom = new DomSnapshotService();
            SelectorDiscovery = new SelectorDiscoveryService();
            RouteDetector = new WebRouteDetector();
        }

        public string SurfaceName { get; }
        public NetworkCaptureService Network { get; }
        public DomSnapshotService Dom { get; }
        public SelectorDiscoveryService SelectorDiscovery { get; }
        public WebRouteDetector RouteDetector { get; }
        public bool IsAttached => _attached;

        public async Task AttachAsync(WebView2 webView, CancellationToken token)
        {
            if (webView == null) throw new ArgumentNullException(nameof(webView));
            token.ThrowIfCancellationRequested();

            await UiThread.InvokeOnUiAsync(webView, async () =>
            {
                token.ThrowIfCancellationRequested();
                if (_attached) return true;

                if (webView.CoreWebView2 == null)
                    await webView.EnsureCoreWebView2Async(null);

                _webView = webView;
                _core = webView.CoreWebView2 ?? throw new InvalidOperationException("WebView2 CoreWebView2 is not ready.");

                await Network.AttachAsync(webView, token);
                await AttachConsoleCaptureAsync(_core);
                _attached = true;
                AppLogger.Info($"[WebDebugInspector] attached surface={SurfaceName}");
                return true;
            });
        }

        public async Task<string> EvaluateScriptAsync(string script, CancellationToken token)
        {
            if (string.IsNullOrWhiteSpace(script)) return "";
            var webView = RequireWebView();
            token.ThrowIfCancellationRequested();

            return await UiThread.InvokeOnUiAsync(webView, async () =>
            {
                token.ThrowIfCancellationRequested();
                return await webView.ExecuteScriptAsync(script);
            });
        }

        public async Task<DomSnapshot> CaptureDomSnapshotAsync(CancellationToken token)
        {
            return await Dom.CaptureAsync(RequireWebView(), SurfaceName, token);
        }

        public SelectorDiscoveryResult DiscoverSelectors(DomSnapshot snapshot)
        {
            return SelectorDiscovery.Discover(snapshot);
        }

        public async Task<WebDebugRouteState> DetectRouteAsync(CancellationToken token)
        {
            var webView = RequireWebView();
            if (SurfaceName.Equals("DKCH", StringComparison.OrdinalIgnoreCase))
                return await RouteDetector.DetectDkchAsync(webView, SurfaceName, token);

            return await RouteDetector.DetectGenericAsync(webView, SurfaceName, token);
        }

        public async Task<LocalStorageKeysSnapshot> CaptureLocalStorageKeysAsync(CancellationToken token)
        {
            var webView = RequireWebView();
            token.ThrowIfCancellationRequested();

            return await UiThread.InvokeOnUiAsync(webView, async () =>
            {
                token.ThrowIfCancellationRequested();
                string raw = await webView.ExecuteScriptAsync(LocalStorageKeysScript);
                string json = UnwrapWebViewJsonString(raw);
                var snapshot = JsonSerializer.Deserialize<LocalStorageKeysSnapshot>(json, AppConfig.CreateJsonOptions())
                    ?? new LocalStorageKeysSnapshot();
                snapshot.SurfaceName = SurfaceName;
                snapshot.CapturedAtUtc = DateTime.UtcNow;
                return snapshot;
            });
        }

        public IReadOnlyList<ConsoleMessageCapture> GetConsoleErrorsSnapshot()
        {
            lock (_consoleSync)
            {
                return _consoleMessages
                    .Where(x => x.Level.Equals("error", StringComparison.OrdinalIgnoreCase)
                        || x.Level.Equals("warning", StringComparison.OrdinalIgnoreCase))
                    .Select(CloneConsoleMessage)
                    .ToList();
            }
        }

        private async Task AttachConsoleCaptureAsync(CoreWebView2 core)
        {
            await core.CallDevToolsProtocolMethodAsync("Runtime.enable", "{}");
            await core.CallDevToolsProtocolMethodAsync("Log.enable", "{}");

            _consoleReceiver = core.GetDevToolsProtocolEventReceiver("Runtime.consoleAPICalled");
            _logReceiver = core.GetDevToolsProtocolEventReceiver("Log.entryAdded");
            _consoleReceiver.DevToolsProtocolEventReceived += OnConsoleApiCalled;
            _logReceiver.DevToolsProtocolEventReceived += OnLogEntryAdded;
        }

        private void OnConsoleApiCalled(object? sender, CoreWebView2DevToolsProtocolEventReceivedEventArgs e)
        {
            try
            {
                using var doc = JsonDocument.Parse(e.ParameterObjectAsJson);
                var root = doc.RootElement;
                string level = GetString(root, "type");
                string text = "";
                if (root.TryGetProperty("args", out var args) && args.ValueKind == JsonValueKind.Array)
                {
                    text = string.Join(" ", args.EnumerateArray().Select(ReadConsoleArg));
                }

                AddConsoleMessage(new ConsoleMessageCapture
                {
                    CapturedAtUtc = DateTime.UtcNow,
                    Source = "Runtime.consoleAPICalled",
                    Level = level,
                    Text = TokenRedactor.RedactText(text)
                });
            }
            catch { }
        }

        private void OnLogEntryAdded(object? sender, CoreWebView2DevToolsProtocolEventReceivedEventArgs e)
        {
            try
            {
                using var doc = JsonDocument.Parse(e.ParameterObjectAsJson);
                var root = doc.RootElement.TryGetProperty("entry", out var entry) ? entry : doc.RootElement;
                AddConsoleMessage(new ConsoleMessageCapture
                {
                    CapturedAtUtc = DateTime.UtcNow,
                    Source = GetString(root, "source"),
                    Level = GetString(root, "level"),
                    Text = TokenRedactor.RedactText(GetString(root, "text")),
                    Url = TokenRedactor.RedactText(GetString(root, "url")),
                    LineNumber = GetInt(root, "lineNumber")
                });
            }
            catch { }
        }

        private void AddConsoleMessage(ConsoleMessageCapture message)
        {
            lock (_consoleSync)
            {
                _consoleMessages.Add(message);
                while (_consoleMessages.Count > 200)
                    _consoleMessages.RemoveAt(0);
            }
        }

        private WebView2 RequireWebView()
        {
            if (_webView == null) throw new InvalidOperationException("WebViewDevToolsInspector is not attached.");
            return _webView;
        }

        private static ConsoleMessageCapture CloneConsoleMessage(ConsoleMessageCapture source)
        {
            return new ConsoleMessageCapture
            {
                CapturedAtUtc = source.CapturedAtUtc,
                Source = source.Source,
                Level = source.Level,
                Text = TokenRedactor.RedactText(source.Text),
                Url = TokenRedactor.RedactText(source.Url),
                LineNumber = source.LineNumber
            };
        }

        private static string ReadConsoleArg(JsonElement arg)
        {
            if (arg.TryGetProperty("value", out var value)) return value.ToString();
            if (arg.TryGetProperty("description", out var desc)) return desc.ToString();
            return arg.ToString();
        }

        private static string GetString(JsonElement element, string propertyName)
        {
            if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(propertyName, out var value))
                return "";
            return value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : value.ToString();
        }

        private static int GetInt(JsonElement element, string propertyName)
        {
            if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(propertyName, out var value))
                return 0;
            if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out int number)) return number;
            return int.TryParse(value.ToString(), out number) ? number : 0;
        }

        private static string UnwrapWebViewJsonString(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw) || raw == "null") return "{}";
            try { return JsonSerializer.Deserialize<string>(raw) ?? "{}"; }
            catch { return raw.Trim('"'); }
        }

        private const string LocalStorageKeysScript = @"
(() => {
  const keys = store => {
    const result = [];
    if (!store) return result;
    for (let i = 0; i < store.length; i++) result.push(String(store.key(i) || ''));
    return result.sort();
  };
  return JSON.stringify({
    capturedAtUtc: new Date().toISOString(),
    url: String(location.href || ''),
    localStorageKeys: keys(window.localStorage),
    sessionStorageKeys: keys(window.sessionStorage)
  });
})();";

        public void Dispose()
        {
            if (_consoleReceiver != null) _consoleReceiver.DevToolsProtocolEventReceived -= OnConsoleApiCalled;
            if (_logReceiver != null) _logReceiver.DevToolsProtocolEventReceived -= OnLogEntryAdded;
            Network.Dispose();
            _attached = false;
        }
    }
}

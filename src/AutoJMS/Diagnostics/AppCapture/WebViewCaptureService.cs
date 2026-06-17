#nullable enable
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace AutoJMS.Diagnostics.AppCapture
{
    public sealed class WebViewCaptureService : IDisposable
    {
        private readonly IAppCaptureManager _capture;
        private readonly Dictionary<string, DateTime> _lastDomCaptureBySource = new(StringComparer.OrdinalIgnoreCase);
        private WebView2? _webView;
        private string _source = "";
        private CoreWebView2DevToolsProtocolEventReceiver? _consoleReceiver;
        private CoreWebView2DevToolsProtocolEventReceiver? _logReceiver;
        private CoreWebView2DevToolsProtocolEventReceiver? _requestReceiver;
        private CoreWebView2DevToolsProtocolEventReceiver? _responseReceiver;
        private CoreWebView2DevToolsProtocolEventReceiver? _loadingFinishedReceiver;
        private CoreWebView2DevToolsProtocolEventReceiver? _loadingFailedReceiver;
        private readonly Dictionary<string, WebRequestState> _requests = new(StringComparer.Ordinal);

        public WebViewCaptureService(IAppCaptureManager capture)
        {
            _capture = capture ?? AppCaptureManager.Instance;
        }

        public async Task AttachAsync(WebView2 webView, string source, CancellationToken token)
        {
            if (!_capture.IsEnabled || webView == null) return;
            await UiThread.InvokeOnUiAsync(webView, async () =>
            {
                token.ThrowIfCancellationRequested();
                if (webView.CoreWebView2 == null)
                    await webView.EnsureCoreWebView2Async(null);

                _webView = webView;
                _source = source;
                var core = webView.CoreWebView2;
                if (core == null) return;

                await core.CallDevToolsProtocolMethodAsync("Runtime.enable", "{}");
                await core.CallDevToolsProtocolMethodAsync("Log.enable", "{}");
                await core.CallDevToolsProtocolMethodAsync("Page.enable", "{}");

                if (AppCaptureManager.Instance.Options.CaptureWebViewNetwork)
                {
                    await core.CallDevToolsProtocolMethodAsync(
                        "Network.enable",
                        "{\"maxTotalBufferSize\":2097152,\"maxResourceBufferSize\":524288,\"maxPostDataSize\":65536}");
                    _requestReceiver = core.GetDevToolsProtocolEventReceiver("Network.requestWillBeSent");
                    _responseReceiver = core.GetDevToolsProtocolEventReceiver("Network.responseReceived");
                    _loadingFinishedReceiver = core.GetDevToolsProtocolEventReceiver("Network.loadingFinished");
                    _loadingFailedReceiver = core.GetDevToolsProtocolEventReceiver("Network.loadingFailed");
                    _requestReceiver.DevToolsProtocolEventReceived += OnRequestWillBeSent;
                    _responseReceiver.DevToolsProtocolEventReceived += OnResponseReceived;
                    _loadingFinishedReceiver.DevToolsProtocolEventReceived += OnLoadingFinished;
                    _loadingFailedReceiver.DevToolsProtocolEventReceived += OnLoadingFailed;
                }

                if (AppCaptureManager.Instance.Options.CaptureConsole)
                {
                    _consoleReceiver = core.GetDevToolsProtocolEventReceiver("Runtime.consoleAPICalled");
                    _logReceiver = core.GetDevToolsProtocolEventReceiver("Log.entryAdded");
                    _consoleReceiver.DevToolsProtocolEventReceived += OnConsoleApiCalled;
                    _logReceiver.DevToolsProtocolEventReceived += OnLogEntryAdded;
                }

                webView.NavigationStarting += OnNavigationStarting;
                webView.NavigationCompleted += OnNavigationCompleted;
                _capture.RecordEvent(AppCaptureEvent.Create("webview.route", source, "WebViewCapture.Attached", new { url = SafeSource(webView) }));
                AppLogger.Info($"[AppCapture] WebView {source} attached");
            });
        }

        public async Task CaptureDomNowAsync(string reason, CancellationToken token)
        {
            if (!_capture.IsEnabled || _webView == null) return;
            await CaptureDomAsync(reason, token).ConfigureAwait(false);
        }

        private void OnNavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs e)
        {
            _capture.RecordEvent(new AppCaptureEvent
            {
                Category = "webview.route",
                Source = _source,
                EventName = "NavigationStarting",
                Route = e.Uri,
                Data = new Dictionary<string, object?> { ["url"] = AppCaptureRedactor.RedactText(e.Uri) }
            });
        }

        private void OnNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
        {
            string url = SafeSource(_webView);
            _capture.RecordEvent(new AppCaptureEvent
            {
                Category = "webview.route",
                Source = _source,
                EventName = "NavigationCompleted",
                Route = url,
                Data = new Dictionary<string, object?>
                {
                    ["isSuccess"] = e.IsSuccess,
                    ["webErrorStatus"] = e.WebErrorStatus.ToString(),
                    ["url"] = AppCaptureRedactor.RedactText(url)
                }
            });

            if (AppCaptureManager.Instance.Options.CaptureDomOnRouteChange)
                _ = CaptureDomAsync("RouteChanged", CancellationToken.None);
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
                    var parts = new List<string>();
                    foreach (var arg in args.EnumerateArray())
                        parts.Add(arg.TryGetProperty("value", out var value) ? value.ToString() : arg.ToString());
                    text = string.Join(" ", parts);
                }
                RecordConsole(level, text, "Runtime.consoleAPICalled", "");
            }
            catch { }
        }

        private void OnLogEntryAdded(object? sender, CoreWebView2DevToolsProtocolEventReceivedEventArgs e)
        {
            try
            {
                using var doc = JsonDocument.Parse(e.ParameterObjectAsJson);
                var root = doc.RootElement.TryGetProperty("entry", out var entry) ? entry : doc.RootElement;
                RecordConsole(GetString(root, "level"), GetString(root, "text"), GetString(root, "source"), GetString(root, "url"));
            }
            catch { }
        }

        private void RecordConsole(string level, string text, string source, string url)
        {
            _capture.RecordEvent(new AppCaptureEvent
            {
                Category = "webview.console",
                Source = _source,
                EventName = string.IsNullOrWhiteSpace(level) ? "console" : level,
                Route = url,
                Data = new Dictionary<string, object?>
                {
                    ["level"] = level,
                    ["consoleSource"] = source,
                    ["text"] = AppCaptureRedactor.RedactText(text),
                    ["url"] = AppCaptureRedactor.RedactText(url)
                }
            });

            if (AppCaptureManager.Instance.Options.CaptureDomOnError
                && (level.Equals("error", StringComparison.OrdinalIgnoreCase)
                    || level.Equals("warning", StringComparison.OrdinalIgnoreCase)))
            {
                _ = CaptureDomAsync("ConsoleError", CancellationToken.None);
            }
        }

        private void OnRequestWillBeSent(object? sender, CoreWebView2DevToolsProtocolEventReceivedEventArgs e)
        {
            try
            {
                using var doc = JsonDocument.Parse(e.ParameterObjectAsJson);
                var root = doc.RootElement;
                string requestId = GetString(root, "requestId");
                if (string.IsNullOrWhiteSpace(requestId)) return;
                var request = root.TryGetProperty("request", out var req) ? req : default;
                string url = GetString(request, "url");
                var state = new WebRequestState
                {
                    RequestId = requestId,
                    StartedAt = DateTimeOffset.Now,
                    Method = GetString(request, "method"),
                    Url = url,
                    RequestBody = Truncate(AppCaptureRedactor.RedactText(GetString(request, "postData")), AppCaptureManager.Instance.Options.MaxRequestBodyBytes)
                };
                lock (_requests) _requests[requestId] = state;
                _capture.RecordEvent(new AppCaptureEvent
                {
                    Category = "webview.network",
                    Source = _source,
                    EventName = "Network.RequestWillBeSent",
                    CorrelationId = requestId,
                    Route = url,
                    Data = new Dictionary<string, object?>
                    {
                        ["method"] = state.Method,
                        ["url"] = AppCaptureRedactor.RedactText(url),
                        ["requestBody"] = state.RequestBody
                    }
                });
            }
            catch { }
        }

        private void OnResponseReceived(object? sender, CoreWebView2DevToolsProtocolEventReceivedEventArgs e)
        {
            try
            {
                using var doc = JsonDocument.Parse(e.ParameterObjectAsJson);
                var root = doc.RootElement;
                string requestId = GetString(root, "requestId");
                var response = root.TryGetProperty("response", out var res) ? res : default;
                lock (_requests)
                {
                    if (_requests.TryGetValue(requestId, out var state))
                    {
                        state.Status = TryGetInt(response, "status");
                        state.MimeType = GetString(response, "mimeType");
                    }
                }
            }
            catch { }
        }

        private void OnLoadingFinished(object? sender, CoreWebView2DevToolsProtocolEventReceivedEventArgs e)
        {
            try
            {
                using var doc = JsonDocument.Parse(e.ParameterObjectAsJson);
                string requestId = GetString(doc.RootElement, "requestId");
                WebRequestState? state;
                lock (_requests) _requests.TryGetValue(requestId, out state);
                if (state == null) return;
                long durationMs = (long)(DateTimeOffset.Now - state.StartedAt).TotalMilliseconds;
                _capture.RecordEvent(new AppCaptureEvent
                {
                    Category = "webview.network",
                    Source = _source,
                    EventName = "Network.LoadingFinished",
                    CorrelationId = requestId,
                    Route = state.Url,
                    DurationMs = durationMs,
                    Data = new Dictionary<string, object?>
                    {
                        ["method"] = state.Method,
                        ["url"] = AppCaptureRedactor.RedactText(state.Url),
                        ["status"] = state.Status,
                        ["mimeType"] = state.MimeType,
                        ["durationMs"] = durationMs
                    }
                });
                if (durationMs >= AppCaptureManager.Instance.Options.SlowApiThresholdMs)
                    _capture.RecordPerformance("webview.network", state.Url, durationMs, new { state.Method, state.Status });
            }
            catch { }
        }

        private void OnLoadingFailed(object? sender, CoreWebView2DevToolsProtocolEventReceivedEventArgs e)
        {
            try
            {
                using var doc = JsonDocument.Parse(e.ParameterObjectAsJson);
                var root = doc.RootElement;
                _capture.RecordEvent(new AppCaptureEvent
                {
                    Category = "webview.network",
                    Source = _source,
                    EventName = "Network.LoadingFailed",
                    CorrelationId = GetString(root, "requestId"),
                    Data = new Dictionary<string, object?>
                    {
                        ["errorText"] = GetString(root, "errorText"),
                        ["canceled"] = GetBool(root, "canceled")
                    }
                });
            }
            catch { }
        }

        private async Task CaptureDomAsync(string reason, CancellationToken token)
        {
            if (_webView == null) return;
            var options = AppCaptureManager.Instance.Options;
            if (!options.CaptureDomOnDemand && !options.CaptureDomOnRouteChange && !options.CaptureDomOnError) return;
            lock (_lastDomCaptureBySource)
            {
                if (_lastDomCaptureBySource.TryGetValue(_source, out var last)
                    && DateTime.Now - last < TimeSpan.FromSeconds(Math.Max(1, options.DomMinIntervalSeconds)))
                    return;
                _lastDomCaptureBySource[_source] = DateTime.Now;
            }

            try
            {
                await UiThread.InvokeOnUiAsync(_webView, async () =>
                {
                    if (_webView.CoreWebView2 == null) return true;
                    string raw = await _webView.ExecuteScriptAsync("document.documentElement ? document.documentElement.outerHTML : ''");
                    string html = UnwrapWebViewJsonString(raw);
                    string file = AppCaptureManager.Instance.SaveDomSnapshot(_source, html);
                    _capture.RecordEvent(new AppCaptureEvent
                    {
                        Category = "webview.dom",
                        Source = _source,
                        EventName = "DomSnapshot",
                        Route = SafeSource(_webView),
                        Data = new Dictionary<string, object?>
                        {
                            ["reason"] = reason,
                            ["file"] = file,
                            ["sizeBytes"] = html.Length
                        }
                    });
                    return true;
                });
            }
            catch (Exception ex)
            {
                _capture.RecordError(ex, $"WebViewCapture.{_source}.DomSnapshot", new { reason });
            }
        }

        private static string SafeSource(WebView2? webView)
        {
            try { return webView?.CoreWebView2?.Source ?? webView?.Source?.ToString() ?? ""; }
            catch { return ""; }
        }

        private static string UnwrapWebViewJsonString(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw) || raw == "null") return "";
            try { return JsonSerializer.Deserialize<string>(raw) ?? ""; }
            catch { return raw.Trim('"'); }
        }

        private static string Truncate(string value, int maxBytes)
        {
            if (string.IsNullOrEmpty(value)) return "";
            if (maxBytes <= 0 || value.Length <= maxBytes) return value;
            return value.Substring(0, maxBytes) + "...[truncated]";
        }

        private static string GetString(JsonElement element, string propertyName)
        {
            if (element.ValueKind == JsonValueKind.Undefined || !element.TryGetProperty(propertyName, out var value))
                return "";
            return value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : value.ToString();
        }

        private static int? TryGetInt(JsonElement element, string propertyName)
        {
            if (element.ValueKind == JsonValueKind.Undefined || !element.TryGetProperty(propertyName, out var value))
                return null;
            if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number)) return number;
            return int.TryParse(value.ToString(), out number) ? number : null;
        }

        private static bool GetBool(JsonElement element, string propertyName)
        {
            return element.ValueKind == JsonValueKind.Object
                && element.TryGetProperty(propertyName, out var value)
                && value.ValueKind == JsonValueKind.True;
        }

        public void Dispose()
        {
            if (_consoleReceiver != null) _consoleReceiver.DevToolsProtocolEventReceived -= OnConsoleApiCalled;
            if (_logReceiver != null) _logReceiver.DevToolsProtocolEventReceived -= OnLogEntryAdded;
            if (_requestReceiver != null) _requestReceiver.DevToolsProtocolEventReceived -= OnRequestWillBeSent;
            if (_responseReceiver != null) _responseReceiver.DevToolsProtocolEventReceived -= OnResponseReceived;
            if (_loadingFinishedReceiver != null) _loadingFinishedReceiver.DevToolsProtocolEventReceived -= OnLoadingFinished;
            if (_loadingFailedReceiver != null) _loadingFailedReceiver.DevToolsProtocolEventReceived -= OnLoadingFailed;
            if (_webView != null)
            {
                _webView.NavigationStarting -= OnNavigationStarting;
                _webView.NavigationCompleted -= OnNavigationCompleted;
            }
        }

        private sealed class WebRequestState
        {
            public string RequestId { get; set; } = "";
            public DateTimeOffset StartedAt { get; set; }
            public string Method { get; set; } = "";
            public string Url { get; set; } = "";
            public string RequestBody { get; set; } = "";
            public int? Status { get; set; }
            public string MimeType { get; set; } = "";
        }
    }
}

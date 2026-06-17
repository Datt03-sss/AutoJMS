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
    public sealed class NetworkCaptureService : IDisposable
    {
        private const int MaxEntries = 200;
        private const int MaxBodyChars = 64 * 1024;

        private readonly object _sync = new();
        private readonly List<NetworkCaptureEntry> _entries = new();
        private readonly Dictionary<string, NetworkCaptureEntry> _byRequestId = new(StringComparer.Ordinal);

        private WebView2? _owner;
        private CoreWebView2? _core;
        private bool _attached;
        private CoreWebView2DevToolsProtocolEventReceiver? _requestWillBeSent;
        private CoreWebView2DevToolsProtocolEventReceiver? _responseReceived;
        private CoreWebView2DevToolsProtocolEventReceiver? _loadingFinished;
        private CoreWebView2DevToolsProtocolEventReceiver? _loadingFailed;

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

                _owner = webView;
                _core = webView.CoreWebView2 ?? throw new InvalidOperationException("WebView2 CoreWebView2 is not ready.");

                await _core.CallDevToolsProtocolMethodAsync(
                    "Network.enable",
                    "{\"maxTotalBufferSize\":2097152,\"maxResourceBufferSize\":524288,\"maxPostDataSize\":65536}");

                _requestWillBeSent = _core.GetDevToolsProtocolEventReceiver("Network.requestWillBeSent");
                _responseReceived = _core.GetDevToolsProtocolEventReceiver("Network.responseReceived");
                _loadingFinished = _core.GetDevToolsProtocolEventReceiver("Network.loadingFinished");
                _loadingFailed = _core.GetDevToolsProtocolEventReceiver("Network.loadingFailed");

                _requestWillBeSent.DevToolsProtocolEventReceived += OnRequestWillBeSent;
                _responseReceived.DevToolsProtocolEventReceived += OnResponseReceived;
                _loadingFinished.DevToolsProtocolEventReceived += OnLoadingFinished;
                _loadingFailed.DevToolsProtocolEventReceived += OnLoadingFailed;
                _attached = true;
                return true;
            });
        }

        public IReadOnlyList<NetworkCaptureEntry> GetSnapshot()
        {
            lock (_sync)
            {
                return _entries.Select(CloneRedacted).ToList();
            }
        }

        public void Clear()
        {
            lock (_sync)
            {
                _entries.Clear();
                _byRequestId.Clear();
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

                JsonElement request = root.TryGetProperty("request", out var req) ? req : default;
                string url = GetString(request, "url");
                var entry = new NetworkCaptureEntry
                {
                    RequestId = requestId,
                    StartedAtUtc = DateTime.UtcNow,
                    Method = GetString(request, "method"),
                    Url = url,
                    Host = GetHost(url),
                    EndpointKind = ClassifyEndpoint(url),
                    ResourceType = GetString(root, "type"),
                    RequestHeaders = ReadHeaders(request, "headers"),
                    RequestBody = Truncate(TokenRedactor.RedactText(GetString(request, "postData")), out bool requestTruncated),
                    RequestBodyTruncated = requestTruncated
                };

                lock (_sync)
                {
                    _byRequestId[requestId] = entry;
                    _entries.Add(entry);
                    TrimLocked();
                }
            }
            catch (Exception ex)
            {
                AppLogger.Warning($"[WebDebugInspector] request capture failed: {ex.Message}");
            }
        }

        private void OnResponseReceived(object? sender, CoreWebView2DevToolsProtocolEventReceivedEventArgs e)
        {
            try
            {
                using var doc = JsonDocument.Parse(e.ParameterObjectAsJson);
                var root = doc.RootElement;
                string requestId = GetString(root, "requestId");
                if (string.IsNullOrWhiteSpace(requestId)) return;

                JsonElement response = root.TryGetProperty("response", out var res) ? res : default;
                lock (_sync)
                {
                    var entry = GetOrCreateLocked(requestId);
                    entry.ResponseStatus = TryGetInt(response, "status");
                    entry.ResponseMimeType = GetString(response, "mimeType");
                    entry.ResponseHeaders = ReadHeaders(response, "headers");
                    string url = GetString(response, "url");
                    if (!string.IsNullOrWhiteSpace(url))
                    {
                        entry.Url = url;
                        entry.Host = GetHost(url);
                        entry.EndpointKind = ClassifyEndpoint(url);
                    }
                }
            }
            catch (Exception ex)
            {
                AppLogger.Warning($"[WebDebugInspector] response capture failed: {ex.Message}");
            }
        }

        private void OnLoadingFinished(object? sender, CoreWebView2DevToolsProtocolEventReceivedEventArgs e)
        {
            try
            {
                using var doc = JsonDocument.Parse(e.ParameterObjectAsJson);
                string requestId = GetString(doc.RootElement, "requestId");
                if (string.IsNullOrWhiteSpace(requestId)) return;

                lock (_sync)
                {
                    if (_byRequestId.TryGetValue(requestId, out var entry))
                        entry.FinishedAtUtc = DateTime.UtcNow;
                }

                _ = CaptureResponseBodyAsync(requestId);
            }
            catch (Exception ex)
            {
                AppLogger.Warning($"[WebDebugInspector] loadingFinished capture failed: {ex.Message}");
            }
        }

        private void OnLoadingFailed(object? sender, CoreWebView2DevToolsProtocolEventReceivedEventArgs e)
        {
            try
            {
                using var doc = JsonDocument.Parse(e.ParameterObjectAsJson);
                var root = doc.RootElement;
                string requestId = GetString(root, "requestId");
                if (string.IsNullOrWhiteSpace(requestId)) return;

                lock (_sync)
                {
                    var entry = GetOrCreateLocked(requestId);
                    entry.FinishedAtUtc = DateTime.UtcNow;
                    entry.FailureText = GetString(root, "errorText");
                }
            }
            catch (Exception ex)
            {
                AppLogger.Warning($"[WebDebugInspector] loadingFailed capture failed: {ex.Message}");
            }
        }

        private async Task CaptureResponseBodyAsync(string requestId)
        {
            try
            {
                WebView2? owner = _owner;
                CoreWebView2? core = _core;
                if (owner == null || core == null) return;

                NetworkCaptureEntry? snapshot;
                lock (_sync)
                {
                    _byRequestId.TryGetValue(requestId, out snapshot);
                }

                if (snapshot == null || !IsTextualResponse(snapshot.ResponseMimeType, snapshot.ResponseHeaders))
                    return;

                string arg = JsonSerializer.Serialize(new { requestId });
                string raw = await UiThread.InvokeOnUiAsync(owner, async () =>
                    await core.CallDevToolsProtocolMethodAsync("Network.getResponseBody", arg));

                using var doc = JsonDocument.Parse(raw);
                var root = doc.RootElement;
                bool base64Encoded = root.TryGetProperty("base64Encoded", out var b64) && b64.GetBoolean();
                string body = GetString(root, "body");
                if (base64Encoded)
                    body = $"[base64 response body omitted; length={body.Length}]";

                body = Truncate(TokenRedactor.RedactText(body), out bool truncated);
                lock (_sync)
                {
                    if (_byRequestId.TryGetValue(requestId, out var entry))
                    {
                        entry.ResponseBody = body;
                        entry.ResponseBodyTruncated = truncated;
                    }
                }
            }
            catch (Exception ex)
            {
                lock (_sync)
                {
                    if (_byRequestId.TryGetValue(requestId, out var entry))
                        entry.BodyFetchError = ex.Message;
                }
            }
        }

        private NetworkCaptureEntry GetOrCreateLocked(string requestId)
        {
            if (_byRequestId.TryGetValue(requestId, out var existing)) return existing;

            var entry = new NetworkCaptureEntry { RequestId = requestId, StartedAtUtc = DateTime.UtcNow };
            _byRequestId[requestId] = entry;
            _entries.Add(entry);
            TrimLocked();
            return entry;
        }

        private void TrimLocked()
        {
            while (_entries.Count > MaxEntries)
            {
                var first = _entries[0];
                _entries.RemoveAt(0);
                _byRequestId.Remove(first.RequestId);
            }
        }

        private static NetworkCaptureEntry CloneRedacted(NetworkCaptureEntry source)
        {
            return new NetworkCaptureEntry
            {
                RequestId = source.RequestId,
                StartedAtUtc = source.StartedAtUtc,
                FinishedAtUtc = source.FinishedAtUtc,
                Method = source.Method,
                Url = TokenRedactor.RedactText(source.Url),
                Host = source.Host,
                EndpointKind = source.EndpointKind,
                ResourceType = source.ResourceType,
                RequestHeaders = TokenRedactor.RedactHeaders(source.RequestHeaders),
                RequestBody = TokenRedactor.RedactText(source.RequestBody),
                RequestBodyTruncated = source.RequestBodyTruncated,
                ResponseStatus = source.ResponseStatus,
                ResponseMimeType = source.ResponseMimeType,
                ResponseHeaders = TokenRedactor.RedactHeaders(source.ResponseHeaders),
                ResponseBody = TokenRedactor.RedactText(source.ResponseBody),
                ResponseBodyTruncated = source.ResponseBodyTruncated,
                BodyFetchError = source.BodyFetchError,
                FailureText = source.FailureText
            };
        }

        private static string GetHost(string url)
        {
            return Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri.Host : "";
        }

        private static string ClassifyEndpoint(string url)
        {
            string host = GetHost(url);
            if (host.Equals("jmsgw.jtexpress.vn", StringComparison.OrdinalIgnoreCase)) return "JmsGateway";
            if (host.Equals("jms.jtexpress.vn", StringComparison.OrdinalIgnoreCase)) return "JmsFrontend";
            if (host.EndsWith(".jtexpress.vn", StringComparison.OrdinalIgnoreCase)) return "JmsRelated";
            return "Other";
        }

        private static Dictionary<string, string> ReadHeaders(JsonElement owner, string propertyName)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (owner.ValueKind == JsonValueKind.Undefined || !owner.TryGetProperty(propertyName, out var headers))
                return result;

            if (headers.ValueKind != JsonValueKind.Object) return result;
            foreach (var header in headers.EnumerateObject())
                result[header.Name] = header.Value.ToString();
            return result;
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
            if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out int number)) return number;
            return int.TryParse(value.ToString(), out number) ? number : null;
        }

        private static bool IsTextualResponse(string mimeType, IReadOnlyDictionary<string, string> headers)
        {
            string contentType = mimeType ?? "";
            if (string.IsNullOrWhiteSpace(contentType) && headers.TryGetValue("content-type", out var headerType))
                contentType = headerType;

            return contentType.Contains("json", StringComparison.OrdinalIgnoreCase)
                || contentType.Contains("text", StringComparison.OrdinalIgnoreCase)
                || contentType.Contains("javascript", StringComparison.OrdinalIgnoreCase)
                || contentType.Contains("xml", StringComparison.OrdinalIgnoreCase)
                || contentType.Contains("html", StringComparison.OrdinalIgnoreCase);
        }

        private static string Truncate(string value, out bool truncated)
        {
            truncated = false;
            if (string.IsNullOrEmpty(value)) return "";
            if (value.Length <= MaxBodyChars) return value;
            truncated = true;
            return value[..MaxBodyChars] + "\n[truncated]";
        }

        public void Dispose()
        {
            if (_requestWillBeSent != null) _requestWillBeSent.DevToolsProtocolEventReceived -= OnRequestWillBeSent;
            if (_responseReceived != null) _responseReceived.DevToolsProtocolEventReceived -= OnResponseReceived;
            if (_loadingFinished != null) _loadingFinished.DevToolsProtocolEventReceived -= OnLoadingFinished;
            if (_loadingFailed != null) _loadingFailed.DevToolsProtocolEventReceived -= OnLoadingFailed;
            _attached = false;
        }
    }
}

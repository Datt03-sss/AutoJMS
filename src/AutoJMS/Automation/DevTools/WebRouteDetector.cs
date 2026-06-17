#nullable enable
using Microsoft.Web.WebView2.WinForms;
using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace AutoJMS.Automation.DevTools
{
    public sealed class WebRouteDetector
    {
        private const string DkchRouteFragment = "returnandforwardmaintainaddsite";

        public Task<WebDebugRouteState> DetectDkchAsync(WebView2 webView, string surfaceName, CancellationToken token)
        {
            return DetectAsync(webView, surfaceName, "DKCH", DkchRouteFragment, token);
        }

        public async Task<WebDebugRouteState> DetectGenericAsync(WebView2 webView, string surfaceName, CancellationToken token)
        {
            if (webView == null) throw new ArgumentNullException(nameof(webView));
            token.ThrowIfCancellationRequested();

            return await UiThread.InvokeOnUiAsync(webView, async () =>
            {
                token.ThrowIfCancellationRequested();
                string currentUrl = GetCurrentUrl(webView);
                var state = CreateBase(surfaceName, "CurrentPage", "", currentUrl);

                if (webView.CoreWebView2 == null)
                {
                    state.State = WebDebugRouteStateKind.Loading;
                    state.Detail = "CoreWebView2=null";
                    return state;
                }

                if (!IsJmsUrl(currentUrl))
                {
                    state.State = string.IsNullOrWhiteSpace(currentUrl) ? WebDebugRouteStateKind.Loading : WebDebugRouteStateKind.WrongPage;
                    state.Detail = string.IsNullOrWhiteSpace(currentUrl) ? "blank-url" : "non-jms-host";
                    return state;
                }

                string raw = await webView.ExecuteScriptAsync(LoginProbeScript);
                var probe = ParseProbe(raw);
                bool hasLoginMarker = GetBool(probe, "hasPasswordInput") || GetBool(probe, "hasLoginText");
                state.Signals["hasPasswordInput"] = GetBool(probe, "hasPasswordInput").ToString();
                state.Signals["hasLoginText"] = GetBool(probe, "hasLoginText").ToString();
                state.Signals["bodyTextSample"] = GetString(probe, "bodyTextSample");
                state.State = hasLoginMarker ? WebDebugRouteStateKind.NotLoggedIn : WebDebugRouteStateKind.Ready;
                state.Detail = hasLoginMarker ? "login-marker" : "jms-origin";
                return state;
            });
        }

        public async Task<WebDebugRouteState> DetectAsync(
            WebView2 webView,
            string surfaceName,
            string routeName,
            string expectedRouteFragment,
            CancellationToken token)
        {
            if (webView == null) throw new ArgumentNullException(nameof(webView));
            token.ThrowIfCancellationRequested();

            return await UiThread.InvokeOnUiAsync(webView, async () =>
            {
                token.ThrowIfCancellationRequested();
                string currentUrl = GetCurrentUrl(webView);
                var state = CreateBase(surfaceName, routeName, expectedRouteFragment, currentUrl);

                if (webView.CoreWebView2 == null)
                {
                    state.State = WebDebugRouteStateKind.Loading;
                    state.Detail = "CoreWebView2=null";
                    return state;
                }

                if (string.IsNullOrWhiteSpace(currentUrl) || currentUrl == "about:blank")
                {
                    state.State = WebDebugRouteStateKind.Loading;
                    state.Detail = "blank-url";
                    return state;
                }

                if (!IsJmsUrl(currentUrl))
                {
                    state.State = WebDebugRouteStateKind.WrongPage;
                    state.Detail = "non-jms-host";
                    return state;
                }

                try
                {
                    string raw = await webView.ExecuteScriptAsync(BuildRouteProbeScript(expectedRouteFragment));
                    var probe = ParseProbe(raw);

                    bool hasRoute = GetBool(probe, "hasRoute");
                    bool hasPassword = GetBool(probe, "hasPasswordInput");
                    bool hasLoginText = GetBool(probe, "hasLoginText");
                    bool hasDkchText = GetBool(probe, "hasDkchText");
                    bool hasWaybillInput = GetBool(probe, "hasWaybillInput");
                    bool hasDropdown = GetBool(probe, "hasDropdown");
                    bool hasSearchButton = GetBool(probe, "hasSearchButton");

                    state.Signals["hasRoute"] = hasRoute.ToString();
                    state.Signals["hasPasswordInput"] = hasPassword.ToString();
                    state.Signals["hasLoginText"] = hasLoginText.ToString();
                    state.Signals["hasDkchText"] = hasDkchText.ToString();
                    state.Signals["hasWaybillInput"] = hasWaybillInput.ToString();
                    state.Signals["hasDropdown"] = hasDropdown.ToString();
                    state.Signals["hasSearchButton"] = hasSearchButton.ToString();

                    if (!hasRoute && (hasPassword || hasLoginText))
                    {
                        state.State = WebDebugRouteStateKind.NotLoggedIn;
                        state.Detail = "login-marker";
                    }
                    else if (!hasRoute)
                    {
                        state.State = WebDebugRouteStateKind.WrongPage;
                        state.Detail = "route-mismatch";
                    }
                    else if (hasWaybillInput && hasDropdown && hasSearchButton && hasDkchText)
                    {
                        state.State = WebDebugRouteStateKind.Ready;
                        state.Detail = "route+form-marker";
                    }
                    else if (hasPassword || hasLoginText)
                    {
                        state.State = WebDebugRouteStateKind.NotLoggedIn;
                        state.Detail = "route-login-marker";
                    }
                    else
                    {
                        state.State = WebDebugRouteStateKind.Loading;
                        state.Detail = "route-found-form-not-ready";
                    }
                }
                catch (Exception ex)
                {
                    state.State = WebDebugRouteStateKind.Error;
                    state.Detail = ex.Message;
                }

                return state;
            });
        }

        private static WebDebugRouteState CreateBase(string surfaceName, string routeName, string expectedRouteFragment, string currentUrl)
        {
            return new WebDebugRouteState
            {
                CapturedAtUtc = DateTime.UtcNow,
                SurfaceName = surfaceName,
                RouteName = routeName,
                CurrentUrl = currentUrl,
                ExpectedRouteFragment = expectedRouteFragment
            };
        }

        private static bool IsJmsUrl(string currentUrl)
        {
            return Uri.TryCreate(currentUrl, UriKind.Absolute, out var uri)
                && uri.Host.EndsWith("jtexpress.vn", StringComparison.OrdinalIgnoreCase);
        }

        private static string GetCurrentUrl(WebView2 webView)
        {
            try
            {
                return webView.CoreWebView2?.Source ?? webView.Source?.ToString() ?? "";
            }
            catch { return ""; }
        }

        private static string BuildRouteProbeScript(string expectedRouteFragment)
        {
            string route = JsonSerializer.Serialize(expectedRouteFragment ?? "");
            return @"
(() => {
  const expectedRoute = " + route + @";
  const href = String(location.href || '');
  const lowerHref = href.toLowerCase();
  const bodyText = String((document.body && document.body.innerText) || '').toLowerCase();
  const hasRoute = expectedRoute ? lowerHref.includes(String(expectedRoute).toLowerCase()) : true;
  const hasPasswordInput = !!document.querySelector('input[type=""password""]');
  const hasLoginText = bodyText.includes('login') || bodyText.includes('đăng nhập') || bodyText.includes('dang nhap') || bodyText.includes('登录');
  // TODO(runtime-config): move DKCH route markers to runtime config when inspector output is adopted for route guards.
  const container = document.querySelector('div[id^=""el-collapse-content-""]');
  const waybillInput = container ? container.querySelector('input') : null;
  const dropdown = document.querySelector('.el-select .el-input__inner');
  const searchButton = container ? container.querySelector('button.el-button--primary') : document.querySelector('button.el-button--primary');
  const hasDkchText = bodyText.includes('chuyển hoàn') || bodyText.includes('chuyen hoan') || bodyText.includes('đăng ký chuyển hoàn') || bodyText.includes('dang ky chuyen hoan');
  return JSON.stringify({
    href,
    hasRoute,
    hasPasswordInput,
    hasLoginText,
    hasDkchText,
    hasWaybillInput: !!waybillInput,
    hasDropdown: !!dropdown,
    hasSearchButton: !!searchButton
  });
})();";
        }

        private const string LoginProbeScript = @"
(() => {
  const bodyText = String((document.body && document.body.innerText) || '').toLowerCase();
  return JSON.stringify({
    hasPasswordInput: !!document.querySelector('input[type=""password""]'),
    hasLoginText: bodyText.includes('login') || bodyText.includes('đăng nhập') || bodyText.includes('dang nhap') || bodyText.includes('登录'),
    bodyTextSample: bodyText.replace(/\s+/g, ' ').slice(0, 500)
  });
})();";

        private static JsonElement ParseProbe(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw) || raw == "null")
                return JsonDocument.Parse("{}").RootElement.Clone();

            string json;
            try { json = JsonSerializer.Deserialize<string>(raw) ?? "{}"; }
            catch { json = raw.Trim('"'); }
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.Clone();
        }

        private static bool GetBool(JsonElement element, string propertyName)
        {
            return element.ValueKind == JsonValueKind.Object
                && element.TryGetProperty(propertyName, out var value)
                && value.ValueKind == JsonValueKind.True;
        }

        private static string GetString(JsonElement element, string propertyName)
        {
            return element.ValueKind == JsonValueKind.Object
                && element.TryGetProperty(propertyName, out var value)
                ? value.ToString()
                : "";
        }
    }
}

using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace AutoJMS
{
    public static class WebViewHost
    {
        private static readonly SemaphoreSlim _initLock = new(1, 1);
        public static WebView2 WebView { get; private set; }
        public static string UserDataFolder { get; private set; } = AppPaths.BrowserDataDir;

        public static async Task InitAsync(WebView2 webView, string userDataFolder = null, CancellationToken ct = default)
        {
            if (webView == null) throw new ArgumentNullException(nameof(webView));
            await _initLock.WaitAsync(ct);
            try
            {
                WebView = webView;
                if (!string.IsNullOrWhiteSpace(userDataFolder))
                    UserDataFolder = userDataFolder;

                Directory.CreateDirectory(UserDataFolder);
                webView.CreationProperties ??= new Microsoft.Web.WebView2.WinForms.CoreWebView2CreationProperties { UserDataFolder = UserDataFolder };
                webView.CreationProperties.UserDataFolder = UserDataFolder;

                await webView.EnsureCoreWebView2Async(null);
            }
            catch (Exception ex)
            {
                AppLogger.Error("WebView init failed", ex);
                throw;
            }
            finally
            {
                _initLock.Release();
            }
        }

        public static Task NavigateAsync(string url)
        {
            try
            {
                if (WebView?.CoreWebView2 != null && !string.IsNullOrWhiteSpace(url))
                    WebView.CoreWebView2.Navigate(url);
            }
            catch (Exception ex)
            {
                AppLogger.Warning($"WebView navigate failed: {ex.Message}");
            }
            return Task.CompletedTask;
        }

        public static async Task<string> ExecJsAsync(string script)
        {
            try
            {
                if (WebView?.CoreWebView2 != null)
                    return await WebView.ExecuteScriptAsync(script);
            }
            catch (Exception ex)
            {
                AppLogger.Warning($"WebView JS failed: {ex.Message}");
            }
            return string.Empty;
        }
    }
}
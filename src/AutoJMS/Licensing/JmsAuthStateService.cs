using System;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace AutoJMS
{
    public static class JmsAuthStateService
    {
        private static string _token = string.Empty;
        private static readonly object _lock = new();

        public static string CurrentToken
        {
            get { lock (_lock) return _token; }
            private set { lock (_lock) _token = value ?? string.Empty; }
        }

        public static bool HasToken
        {
            get { lock (_lock) return !string.IsNullOrEmpty(_token) && _token.Length > 20; }
        }

        /// <summary>Raised when JMS auth token is detected as expired/revoked.</summary>
        public static event Action AuthExpired;

        /// <summary>Raised when a new valid JMS auth token is captured or set.</summary>
        public static event Action<string> TokenUpdated;

        /// <summary>
        /// Hook injected by Main: pulls a fresh authToken from the WebView2 localStorage.
        /// Returns the new token (string with length > 20) on success, or null/empty on failure.
        /// Set this from Main on form load. JmsAuthStateService uses it before declaring
        /// a token expired, because the WebView2 session may still be valid even when
        /// our cached HTTP token isn't.
        /// </summary>
        public static Func<Task<string>> WebViewTokenRefresher { get; set; }

        public static void SetToken(string token)
        {
            if (string.IsNullOrEmpty(token) || token.Length <= 20) return;
            lock (_lock)
            {
                if (string.Equals(_token, token, StringComparison.Ordinal)) return;
                _token = token;
            }
            TokenUpdated?.Invoke(token);
        }

        /// <summary>
        /// Delegates to <see cref="JmsResponseClassifier"/> so the success-wins
        /// rule applies everywhere: a 200 + code=1 success is never "expired".
        /// </summary>
        public static bool IsExpiredResponse(string responseBody, int statusCode)
            => JmsResponseClassifier.IsAuthExpired(statusCode, responseBody);

        public static void HandleExpired()
        {
            string oldToken;
            lock (_lock)
            {
                oldToken = _token;
                if (string.IsNullOrEmpty(_token)) return;
                _token = string.Empty;
            }

            AppLogger.Warning("JMS auth token expired — clearing token and notifying subscribers.");

            // Update AuthStateService singleton
            AuthStateService.Instance.ClearToken();

            // Fire event so subscribers (including Main) can stop background loops
            AuthExpired?.Invoke();
        }

        /// <summary>
        /// Validates token by doing a lightweight API call.
        /// Returns true if token is still valid, false if expired, null if network error.
        /// </summary>
        public static async Task<bool?> ValidateTokenAsync(string token)
        {
            if (string.IsNullOrEmpty(token) || token.Length <= 20) return false;
            try
            {
                string url = AppConfig.Current.BuildJmsApiUrl("operatingplatform/podTracking/inner/query/keywordList");
                using var http = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(10) };
                using var req = new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Post, url)
                {
                    Content = new System.Net.Http.StringContent(
                        System.Text.Json.JsonSerializer.Serialize(
                            new { keywordList = new[] { "VALIDATE_CHECK" }, trackingTypeEnum = "WAYBILL", countryId = "1" }),
                        System.Text.Encoding.UTF8, "application/json")
                };
                req.Headers.Add("authToken", token);
                req.Headers.Add("lang", "VN");
                req.Headers.Add("langType", "VN");
                req.Headers.Add("routeName", "trackingExpress");

                using var res = await http.SendAsync(req);
                var body = await res.Content.ReadAsStringAsync();

                if (IsExpiredResponse(body, (int)res.StatusCode))
                {
                    AppLogger.Warning("Token validation failed — auth expired.");
                    return false;
                }

                AppLogger.Info("Token validation passed.");
                return true;
            }
            catch
            {
                // Network error — assume token might still be valid
                return null;
            }
        }

        /// <summary>
        /// Checks an HTTP response for JMS auth expiry.
        ///
        /// On expiry:
        ///   1. Try <see cref="WebViewTokenRefresher"/> to pull a fresh token from
        ///      the WebView2 localStorage (the WebView2 session may still be valid
        ///      even when our cached HTTP token isn't).
        ///   2. If a fresh, DIFFERENT token is found, accept it via <see cref="SetToken"/>
        ///      and return TRUE-WITH-REFRESH semantics: the caller should retry the
        ///      operation using the new token. (Returns <c>true</c> to signal "abort
        ///      current request"; the new token is now active for the next call.)
        ///   3. If refresh fails (no WebView2, no token in localStorage, or the same
        ///      stale token), call <see cref="HandleExpired"/> and return <c>true</c>.
        /// </summary>
        public static async Task<bool> CheckResponseAndHandleExpiredAsync(System.Net.Http.HttpResponseMessage response)
        {
            var body = await response.Content.ReadAsStringAsync();
            if (!IsExpiredResponse(body, (int)response.StatusCode)) return false;

            AppLogger.Warning($"JMS auth expired detected (HTTP {(int)response.StatusCode}, body={Truncate(body, 100)}). Attempting WebView2 refresh before clearing.");

            // Capture old token to detect "actually changed" vs "still the same stale value".
            string oldToken;
            lock (_lock) oldToken = _token;

            string fresh = null;
            try
            {
                if (WebViewTokenRefresher != null)
                    fresh = await WebViewTokenRefresher().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                AppLogger.Warning($"WebViewTokenRefresher threw: {ex.Message}");
            }

            if (!string.IsNullOrEmpty(fresh) && fresh.Length > 20 &&
                !string.Equals(fresh, oldToken, StringComparison.Ordinal))
            {
                SetToken(fresh);
                AppLogger.Info("JMS auth refreshed from WebView2 localStorage — caller should retry.");
                return true;   // current request is still aborted; next call uses the new token
            }

            // No fresh token available — token really is expired.
            HandleExpired();
            return true;
        }

        private static string Truncate(string value, int maxLen)
        {
            if (string.IsNullOrEmpty(value) || value.Length <= maxLen) return value;
            return value.Substring(0, maxLen) + "...";
        }
    }
}

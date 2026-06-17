using System;
using System.Threading;
using System.Threading.Tasks;
using AutoJMS.Diagnostics;

namespace AutoJMS
{
    /// <summary>
    /// High-level orchestration for the JMS <c>authToken</c> (the short hex
    /// session token used by JMS API calls — NOT the license JWT).
    ///
    /// Token priority (resolved by <see cref="ResolveTokenAsync"/>):
    ///   1. in-memory  (<see cref="JmsAuthStateService.CurrentToken"/>)
    ///   2. WebView2 localStorage  (via <see cref="WebViewTokenReader"/>, only
    ///      executed on a WebView currently at the jms.jtexpress.vn origin)
    ///   3. LastAuthToken from AutoJMS.json  (via <see cref="ConfigTokenProvider"/>)
    ///
    /// Storage stays in <see cref="JmsAuthStateService"/> (single source of
    /// truth). This class only orchestrates resolution / refresh / notify so
    /// existing callers of JmsAuthStateService keep working unchanged.
    /// </summary>
    [System.Reflection.Obfuscation(Exclude = true, ApplyToMembers = true)]
    public static class JmsAuthTokenService
    {
        /// <summary>Reads a fresh token from the WebView2 localStorage (jms origin only). Set by Main.</summary>
        public static Func<Task<string>> WebViewTokenReader { get; set; }

        /// <summary>Returns LastAuthToken persisted in AutoJMS.json. Set by Main.</summary>
        public static Func<string> ConfigTokenProvider { get; set; }

        /// <summary>Invoked when the token is truly expired after a forced refresh + one retry. Set by Main.</summary>
        public static Action ReallyExpiredCallback { get; set; }

        // Serialise force-refresh so a burst of concurrent 401s (e.g. 40 parallel
        // tracking batches) triggers exactly one WebView read, not 40.
        private static readonly SemaphoreSlim _refreshGate = new(1, 1);
        private static DateTime _lastRefreshUtc = DateTime.MinValue;
        private static string _lastRefreshToken = string.Empty;

        // Guards the "confirmed expired" heavy path against a 401 storm.
        private static readonly object _expiredGate = new();
        private static DateTime _lastConfirmedExpiredUtc = DateTime.MinValue;

        public static string CurrentToken => JmsAuthStateService.CurrentToken;
        public static bool HasToken => JmsAuthStateService.HasToken;

        // A valid JMS authToken is a 32-char hex string (observed: len=32 hex).
        private static readonly System.Text.RegularExpressions.Regex HexToken32 =
            new("^[a-fA-F0-9]{32}$", System.Text.RegularExpressions.RegexOptions.Compiled);

        /// <summary>
        /// The canonical shape test for a JMS authToken: exactly 32 hex chars.
        /// Anything else (yl_ce_sdk_user_id=25, JWT, GUIDs with dashes, …) is
        /// rejected so we never capture the wrong storage value.
        /// </summary>
        public static bool IsValidJmsToken(string t)
            => !string.IsNullOrEmpty(t) && HexToken32.IsMatch(t);

        private static bool IsUsable(string t) => IsValidJmsToken(t);

        /// <summary>
        /// Detects a JSON Web Token (license token). The JMS authToken is a
        /// short hex string with no dots, so anything that looks like a JWT
        /// must never be accepted as a JMS authToken.
        /// </summary>
        public static bool LooksLikeJwt(string t)
        {
            if (string.IsNullOrEmpty(t)) return false;
            if (!t.StartsWith("eyJ", StringComparison.Ordinal)) return false;
            int dots = 0;
            foreach (var c in t) if (c == '.') dots++;
            return dots == 2;
        }

        public static string LogToken(string t) => string.IsNullOrEmpty(t) ? "<none>" : TokenRedactor.MaskToken(t);

        /// <summary>
        /// Apply a discovered token to the shared state, but only if it is a
        /// valid 32-hex JMS authToken AND actually changed. Returns true if
        /// changed. Logs the source, length and whether the token changed.
        /// </summary>
        public static bool ApplyToken(string token, string source)
        {
            if (LooksLikeJwt(token))
            {
                AppLogger.Warning($"[JmsAuth] Rejected JWT-looking token from {source} — license JWT must not be used as JMS authToken.");
                return false;
            }

            if (!IsValidJmsToken(token))
            {
                AppLogger.Warning($"[JmsAuth] Rejected token from {source}: not 32-hex (len={token?.Length ?? 0}).");
                return false;
            }

            bool changed = !string.Equals(JmsAuthStateService.CurrentToken, token, StringComparison.Ordinal);

            // SetToken dedupes internally and fires TokenUpdated (Main persists
            // LastAuthToken) only when the value really changed.
            JmsAuthStateService.SetToken(token);

            // Keep the realtime/FullStack singleton in sync, but only fire its
            // TokenAcquired event when the token actually changed.
            if (changed) AuthStateService.Instance.SetToken(token);

            AppLogger.Info($"[JmsAuth] token source={source}, len={token.Length}, changed={(changed ? "yes" : "no")}, authToken={LogToken(token)}");
            return changed;
        }

        /// <summary>
        /// Resolve the best available token using the documented priority.
        /// Logs the resolved source.
        /// </summary>
        public static async Task<string> ResolveTokenAsync(CancellationToken ct = default)
        {
            // 1) in-memory
            var mem = JmsAuthStateService.CurrentToken;
            if (IsUsable(mem))
            {
                AppLogger.Info($"[JmsAuth] token source=memory, authToken={LogToken(mem)}");
                return mem;
            }

            // 2) WebView2 localStorage (jms origin only)
            if (WebViewTokenReader != null)
            {
                string wv = null;
                try { wv = await WebViewTokenReader().ConfigureAwait(false); }
                catch (Exception ex) { AppLogger.Warning($"[JmsAuth] WebView token read failed: {ex.Message}"); }
                if (IsUsable(wv))
                {
                    ApplyToken(wv, "webview");
                    return wv;
                }
            }

            // 3) LastAuthToken from AutoJMS.json
            var cfg = ConfigTokenProvider?.Invoke();
            if (IsUsable(cfg))
            {
                ApplyToken(cfg, "local config");
                return cfg;
            }

            AppLogger.Warning("[JmsAuth] No usable token from memory / webview / local config.");
            return mem ?? string.Empty;
        }

        /// <summary>
        /// Force a fresh token read from the WebView2 localStorage and apply it.
        /// Returns the newest token (may equal the old one if unchanged).
        /// Concurrent callers within a short window share a single WebView read.
        /// </summary>
        public static async Task<string> ForceRefreshFromWebViewAsync()
        {
            if (WebViewTokenReader == null)
            {
                AppLogger.Warning("[JmsAuth] ForceRefresh: no WebView reader registered.");
                return JmsAuthStateService.CurrentToken;
            }

            await _refreshGate.WaitAsync().ConfigureAwait(false);
            try
            {
                // Coalesce: if we just refreshed (< 3s ago), reuse that result so
                // a burst of 401s doesn't hammer the WebView with reads.
                if ((DateTime.UtcNow - _lastRefreshUtc).TotalSeconds < 3 && IsUsable(_lastRefreshToken))
                {
                    AppLogger.Info("[JmsAuth] ForceRefresh: reusing recent refresh result.");
                    return _lastRefreshToken;
                }

                string wv = null;
                try { wv = await WebViewTokenReader().ConfigureAwait(false); }
                catch (Exception ex) { AppLogger.Warning($"[JmsAuth] ForceRefresh read failed: {ex.Message}"); }

                if (IsUsable(wv))
                {
                    ApplyToken(wv, "webview (force refresh)");
                    _lastRefreshUtc = DateTime.UtcNow;
                    _lastRefreshToken = wv;
                    return wv;
                }

                AppLogger.Warning("[JmsAuth] ForceRefresh: WebView returned no usable token.");
                return JmsAuthStateService.CurrentToken;
            }
            finally
            {
                _refreshGate.Release();
            }
        }

        /// <summary>
        /// The token is genuinely expired (WebView session is gone too).
        /// Notify the user (throttled, non-blocking) and clear local state.
        /// Guarded so a burst of failed retries fires the heavy path once.
        /// </summary>
        public static void NotifyReallyExpired()
        {
            lock (_expiredGate)
            {
                if ((DateTime.UtcNow - _lastConfirmedExpiredUtc).TotalSeconds < 10)
                    return; // already confirmed expired very recently — ignore the storm
                _lastConfirmedExpiredUtc = DateTime.UtcNow;
            }

            AppLogger.Warning("[JmsAuth] confirmed expired: token invalid after refresh + retry. User must re-login to JMS.");
            try { ReallyExpiredCallback?.Invoke(); } catch (Exception ex) { AppLogger.Warning($"[JmsAuth] ReallyExpiredCallback threw: {ex.Message}"); }
            JmsAuthStateService.HandleExpired();
        }
    }
}

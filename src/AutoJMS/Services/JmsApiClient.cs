using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AutoJMS.Diagnostics.AppCapture;

namespace AutoJMS
{
    /// <summary>
    /// Centralised JMS API caller. Builds requests with the exact headers a
    /// valid browser request uses, attaches the current JMS <c>authToken</c>,
    /// and on a 401 (or JMS "session expired" body) force-refreshes the token
    /// from the WebView2 session and retries the request EXACTLY ONCE.
    ///
    /// Only if the retry also fails do we declare the session truly expired
    /// (which asks the user to log in again).
    ///
    /// NOTE: The license JWT is never used here — only the JMS authToken from
    /// <see cref="JmsAuthTokenService"/>.
    /// </summary>
    [System.Reflection.Obfuscation(Exclude = true, ApplyToMembers = true)]
    public class JmsApiClient : IJmsApiClient
    {
        public static IJmsApiClient Instance { get; set; } = new JmsApiClient();

        private static readonly HttpClient _http = CreateClient();

        private static HttpClient CreateClient()
        {
            var handler = new HttpClientHandler
            {
                // Server replies gzip/deflate/br — decompress transparently so
                // callers always read plain JSON.
                AutomaticDecompression = DecompressionMethods.GZip
                                       | DecompressionMethods.Deflate
                                       | DecompressionMethods.Brotli
            };
            var captureHandler = new AppHttpCaptureHandler(handler, "JmsApiClient");
            return new HttpClient(captureHandler) { Timeout = TimeSpan.FromSeconds(60) };
        }

        private const string DefaultUserAgent =
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36";

        /// <summary>
        /// POST a JSON body to a JMS API endpoint with browser-equivalent
        /// headers, automatic token attach, and one transparent retry after a
        /// forced token refresh on 401.
        /// </summary>
        /// <param name="url">Absolute JMS gateway URL.</param>
        /// <param name="jsonBody">Serialized JSON payload.</param>
        /// <param name="routeName">e.g. "trackingExpress".</param>
        /// <param name="routerNameList">URL-encoded breadcrumb header, or null.</param>
        /// <param name="origin">jms web origin used as Origin/Referer.</param>
        /// <returns>
        /// The successful <see cref="HttpResponseMessage"/>, or the final 401
        /// response if even the retry failed. Caller still checks
        /// <c>IsSuccessStatusCode</c>. Returns null only on transport error.
        /// </returns>
        public static Task<HttpResponseMessage> PostJsonAsync(
            string url,
            string jsonBody,
            string routeName = "trackingExpress",
            string routerNameList = null,
            string origin = "https://jms.jtexpress.vn",
            CancellationToken ct = default)
        {
            return Instance.PostJsonAsync(url, jsonBody, routeName, routerNameList, origin, ct);
        }

        Task<HttpResponseMessage> IJmsApiClient.PostJsonAsync(
            string url,
            string jsonBody,
            string routeName,
            string routerNameList,
            string origin,
            CancellationToken ct)
        {
            return PostJsonInternalAsync(url, jsonBody, routeName, routerNameList, origin, ct);
        }

        Task<byte[]> IJmsApiClient.GetByteArrayAsync(string url, CancellationToken ct)
        {
            return GetByteArrayInternalAsync(url, ct);
        }

        private static async Task<HttpResponseMessage> PostJsonInternalAsync(
            string url,
            string jsonBody,
            string routeName,
            string routerNameList,
            string origin,
            CancellationToken ct)
        {
            // Ensure we start from the best token we can find.
            string token = await JmsAuthTokenService.ResolveTokenAsync(ct).ConfigureAwait(false);

            // ---- Attempt 1 -------------------------------------------------
            var resp = await SendOnceAsync(url, jsonBody, token, routeName, routerNameList, origin, ct)
                            .ConfigureAwait(false);

            var (expired1, body1) = await ClassifyAsync(resp).ConfigureAwait(false);
            if (!expired1)
            {
                // success or non-auth error — return as-is, do NOT touch token.
                return resp;
            }

            AppLogger.Warning($"[JmsApi] first401 endpoint={Shorten(url)} status={(int)resp.StatusCode} body={BodyPreview(body1)}");
            resp.Dispose();

            // ---- Force refresh from WebView2 (UI thread, single-flight) ----
            string oldToken = token;
            string refreshed = await JmsAuthTokenService.ForceRefreshFromWebViewAsync().ConfigureAwait(false);
            bool tokenChanged = !string.Equals(oldToken, refreshed, StringComparison.Ordinal);
            AppLogger.Info($"[JmsApi] refresh result: changed={(tokenChanged ? "yes" : "no")}, len={(refreshed?.Length ?? 0)} " +
                           $"(unchanged token is fine — JMS authToken can stay constant for a whole session).");

            // ---- Retry EXACTLY ONCE, even if the token is unchanged --------
            var resp2 = await SendOnceAsync(url, jsonBody, refreshed, routeName, routerNameList, origin, ct)
                             .ConfigureAwait(false);

            var (expired2, body2) = await ClassifyAsync(resp2).ConfigureAwait(false);
            if (expired2)
            {
                AppLogger.Warning($"[JmsApi] retry result=still-expired endpoint={Shorten(url)} status={(int)resp2.StatusCode} body={BodyPreview(body2)}. Confirmed expired.");
                JmsAuthTokenService.NotifyReallyExpired();
                return resp2; // caller sees the 401 and bails gracefully
            }

            AppLogger.Info($"[JmsApi] retry result=success endpoint={Shorten(url)} (no login prompt needed).");
            return resp2;
        }

        private static async Task<byte[]> GetByteArrayInternalAsync(string url, CancellationToken ct)
        {
            return await _http.GetByteArrayAsync(url, ct).ConfigureAwait(false);
        }

        private static async Task<HttpResponseMessage> SendOnceAsync(
            string url, string jsonBody, string token,
            string routeName, string routerNameList, string origin,
            CancellationToken ct)
        {
            var req = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(jsonBody ?? "{}", Encoding.UTF8, "application/json")
            };

            // Custom JMS headers — use TryAddWithoutValidation so values that
            // contain reserved chars (percent-encoded breadcrumb, GMT+0700, …)
            // are not rejected by HttpClient's strict header validation.
            req.Headers.TryAddWithoutValidation("authToken", token ?? string.Empty);
            req.Headers.TryAddWithoutValidation("lang", "VN");
            req.Headers.TryAddWithoutValidation("langType", "VN");
            req.Headers.TryAddWithoutValidation("routeName", routeName ?? "trackingExpress");
            if (!string.IsNullOrEmpty(routerNameList))
                req.Headers.TryAddWithoutValidation("routerNameList", routerNameList);
            req.Headers.TryAddWithoutValidation("timezone", "GMT+0700");
            req.Headers.TryAddWithoutValidation("Accept", "application/json, text/plain, */*");
            req.Headers.TryAddWithoutValidation("Origin", origin);
            req.Headers.TryAddWithoutValidation("Referer", origin + "/");
            req.Headers.TryAddWithoutValidation("User-Agent", DefaultUserAgent);

            return await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        }

        /// <summary>
        /// Buffer the response body (so the caller can still read it) and decide
        /// whether it indicates an auth failure. Returns (isExpired, body).
        /// A confirmed success (200 + code=1) is never expired.
        /// </summary>
        private static async Task<(bool expired, string body)> ClassifyAsync(HttpResponseMessage resp)
        {
            if (resp == null) return (false, null);

            string body = null;
            try
            {
                // Buffer so the body can be read again by the caller.
                await resp.Content.LoadIntoBufferAsync().ConfigureAwait(false);
                body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
            }
            catch
            {
                // Couldn't read body — fall back to status-only classification.
                return ((int)resp.StatusCode == 401, null);
            }

            bool expired = JmsResponseClassifier.IsAuthExpired((int)resp.StatusCode, body);
            return (expired, body);
        }

        private static string BodyPreview(string body)
        {
            if (string.IsNullOrEmpty(body)) return "<empty>";
            const int max = 160;
            return body.Length <= max ? body : body.Substring(0, max) + "...";
        }

        private static string Shorten(string url)
        {
            if (string.IsNullOrEmpty(url)) return url;
            int i = url.IndexOf("operatingplatform", StringComparison.OrdinalIgnoreCase);
            if (i < 0) i = url.IndexOf("businessindicator", StringComparison.OrdinalIgnoreCase);
            return i >= 0 ? url.Substring(i) : url;
        }
    }
}

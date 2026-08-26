using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.IdentityModel.Tokens.Jwt;
using AutoJMS.Diagnostics;
using AutoJMS.Diagnostics.AppCapture;
using Microsoft.IdentityModel.Tokens;

namespace AutoJMS
{
    public enum HeartbeatOutcome { Continue, ServerKill, TransientFailure, Fatal }
    public enum VerifyFailureKind { None, Transient, Denied, InvalidResponse }

    public class VerifyResult
    {
        public bool Success { get; set; }
        public string Message { get; set; }
        public VerifyFailureKind FailureKind { get; set; } = VerifyFailureKind.None;
        public bool AllowOfflineCacheFallback =>
            FailureKind == VerifyFailureKind.Transient ||
            FailureKind == VerifyFailureKind.InvalidResponse;
        public string Token { get; set; }
        public string Tier { get; set; } = "BASE";
        public string MiddleCode { get; set; } = "";
        public bool AutoUpdate { get; set; } = false;
        public bool SilentUpdate { get; set; } = false;
        public bool ApplyOnNextStartup { get; set; } = true;
        public bool SkipHashCheck { get; set; } = false;
        public string IntegrityMode { get; set; } = "HASH_ONLY";
        public string DataHubBaseUrl { get; set; }
        public string DataHubDeviceToken { get; set; }
        public string DataHubSiteId { get; set; }
        /// <summary>
        /// Site code the license is scoped to, uppercased. This is what /api/v1/devices/enroll
        /// matches on — <see cref="DataHubSiteId"/> is the GUID the enrollment hands back.
        /// </summary>
        public string DataHubSiteCode { get; set; }
        /// <summary>
        /// Short-lived signed assertion (v1rs256.…) that buys exactly one device token. It is a
        /// credential: never log it, never persist it.
        /// </summary>
        public string DataHubLicenseAssertion { get; set; }
        /// <summary>Unix seconds; 0 when the license server issued no assertion.</summary>
        public long DataHubAssertionExpiresAt { get; set; }
        /// <summary>When the device token stops working. Null when enrollment did not happen.</summary>
        public DateTimeOffset? DataHubDeviceTokenExpiresAt { get; set; }
        public DataHubManifestUrls Manifests { get; set; }
        public VpsReleasesConfig Releases { get; set; }
        public string UpdateChannel { get; set; } = "stable";
        public string DataSpreadsheetId { get; set; }
        public string SessionId { get; set; }
    }

    public class HeartbeatResult
    {
        public HeartbeatOutcome Outcome { get; }
        public string NewToken { get; }
        public string ErrorMessage { get; }

        public HeartbeatResult(HeartbeatOutcome outcome, string newToken, string errorMessage)
        {
            Outcome = outcome;
            NewToken = newToken;
            ErrorMessage = errorMessage;
        }
    }

    public static class LicenseApiService
    {
        private const string DEFAULT_API_BASE = "https://autojms-api.onrender.com";

        private const string JWT_PUBLIC_KEY = @"-----BEGIN PUBLIC KEY-----
MIIBIjANBgkqhkiG9w0BAQEFAAOCAQ8AMIIBCgKCAQEAtaK8L4eH5kvH9UQRVRsU
rJh3qoizfSmBgLLSc8dnLfICa/uVH6K9d6pxAc+iYkgqcB8LxOr7oRDnVeBKwnZm
O59Wnf/dWIYHG7bx/RZ4qa/RjU/qhTzxz4sxAnzEgH5zD2kkpXZPwisglx1naMLc
bRKz/Rmd/KYHDTgEcNDXB9QlB0vehTalCTFiwMHZCnZKHgFysIBju/4/iLmpE/7Y
ztn/m+C4k0KX03gdTbQIeqwyOX5NxDZ74TTtNiHDiMNGrOuB68+TF6SGBDbHUfc/
II8JJiIgzjDJgzNjOXB5nkyaJ6Twf0Y2TeZqX4sxdZdEWacr/RwuWRccN/NsDZI3
eQIDAQAB
-----END PUBLIC KEY-----";

        private static readonly HttpClient Http = CreateHttpClient();
        public static string CurrentSessionId { get; private set; } = string.Empty;
        public static string CurrentAccessToken { get; private set; } = string.Empty;
        private static string ApiBase =>
            (Environment.GetEnvironmentVariable("AUTOJMS_LICENSE_API_BASE_URL") ?? DEFAULT_API_BASE)
                .Trim()
                .TrimEnd('/');

        private static string ApiVerify => ApiBase + "/api/verify-license";
        private static string ApiHeartbeat => ApiBase + "/api/heartbeat";
        private static string ApiDataHubAssertion => ApiBase + "/api/datahub/license-assertion";

        public static string ApiBaseUrl => ApiBase;

        private static HttpClient CreateHttpClient()
        {
            var handler = new HttpClientHandler();
            var captureHandler = new AppHttpCaptureHandler(handler, "LicenseApiService");
            return new HttpClient(captureHandler) { Timeout = TimeSpan.FromSeconds(60) };
        }

        public static async Task<VerifyResult> VerifyLicenseSecureAsync(
            string licenseKey, string hwid, CancellationToken ct = default)
        {
            try
            {
                var payload = new { licenseKey = licenseKey, hwid = hwid, exeHash = Program.ExecutableHash };
                string json = JsonSerializer.Serialize(payload);

                using var req = new HttpRequestMessage(HttpMethod.Post, ApiVerify);
                req.Content = new StringContent(json, Encoding.UTF8, "application/json");

                using var res = await Http.SendAsync(req, ct);
                string body = await res.Content.ReadAsStringAsync(ct);

                string safeBody = RedactVerifyResponseForLog(body);
                string truncatedBody = safeBody?.Length > 3000 ? safeBody.Substring(0, 3000) + "..." : safeBody ?? "(null)";
                AppLogger.Info($"SERVER RESPONSE (len={body?.Length ?? 0}): {truncatedBody}");

                if (string.IsNullOrWhiteSpace(body))
                {
                    return new VerifyResult
                    {
                        Success = false,
                        Message = "Phản hồi máy chủ rỗng.",
                        FailureKind = VerifyFailureKind.InvalidResponse
                    };
                }

                JsonDocument doc;
                try { doc = JsonDocument.Parse(body); }
                catch
                {
                    return new VerifyResult
                    {
                        Success = false,
                        Message = "Dữ liệu máy chủ không hợp lệ.",
                        FailureKind = VerifyFailureKind.InvalidResponse
                    };
                }

                using (doc)
                {
                    var root = doc.RootElement;

                    if (!res.IsSuccessStatusCode)
                    {
                        string errMsg = root.TryGetProperty("error", out var err) ? err.GetString() : "Bị từ chối";
                        return new VerifyResult
                        {
                            Success = false,
                            Message = errMsg,
                            FailureKind = IsTransientHttpStatus((int)res.StatusCode)
                                ? VerifyFailureKind.Transient
                                : VerifyFailureKind.Denied
                        };
                    }

                    if (!root.TryGetProperty("payload", out var tokenProp))
                    {
                        return new VerifyResult
                        {
                            Success = false,
                            Message = "Dữ liệu máy chủ không hợp lệ.",
                            FailureKind = VerifyFailureKind.InvalidResponse
                        };
                    }

                    string token = tokenProp.GetString();
                    if (!ValidateJwtToken(token))
                    {
                        return new VerifyResult
                        {
                            Success = false,
                            Message = "Token không hợp lệ.",
                            FailureKind = VerifyFailureKind.InvalidResponse
                        };
                    }
                    CurrentAccessToken = token?.Trim() ?? string.Empty;

                    string sid = root.TryGetProperty("sid", out var sidProp) ? sidProp.GetString() : string.Empty;
                    CurrentSessionId = sid ?? string.Empty;

                    // Parse tier from root
                    string tier = "BASE";
                    if (root.TryGetProperty("tier", out var tierProp))
                        tier = tierProp.GetString() ?? "BASE";
                    else if (root.TryGetProperty("license", out var licProp) && licProp.TryGetProperty("tier", out var licTier))
                        tier = licTier.GetString() ?? "BASE";
                    AppLogger.Info($"Parsed tier from server response: {tier}");

                    // Parse middleCode from root
                    string middleCode = "";
                    if (root.TryGetProperty("middleCode", out var mcProp))
                        middleCode = mcProp.GetString() ?? "";
                    else if (root.TryGetProperty("license", out var licMiddleProp) &&
                             licMiddleProp.ValueKind == JsonValueKind.Object &&
                             licMiddleProp.TryGetProperty("middleCode", out var nestedMcProp))
                        middleCode = nestedMcProp.GetString() ?? "";
                    AppLogger.Info($"Parsed middleCode from server response: {(string.IsNullOrWhiteSpace(middleCode) ? "<empty>" : middleCode)}");

                    // Parse skipHashCheck from root or integrity sub-object
                    bool skipHashCheck = false;
                    string integrityMode = "HASH_ONLY";
                    if (root.TryGetProperty("skipHashCheck", out var skipProp))
                        skipHashCheck = skipProp.ValueKind == JsonValueKind.True;
                    if (root.TryGetProperty("integrity", out var intProp) && intProp.ValueKind == JsonValueKind.Object)
                    {
                        if (intProp.TryGetProperty("skipHashCheck", out var intSkip))
                            skipHashCheck = intSkip.ValueKind == JsonValueKind.True;
                        if (intProp.TryGetProperty("mode", out var modeProp))
                            integrityMode = modeProp.GetString() ?? "HASH_ONLY";
                    }

                    // Parse modulePolicy from root
                    bool autoUpdate = false, silentUpdate = false, applyOnNextStartup = true;
                    if (root.TryGetProperty("modulePolicy", out var mpProp) && mpProp.ValueKind == JsonValueKind.Object)
                    {
                        if (mpProp.TryGetProperty("autoUpdate", out var auProp))
                            autoUpdate = auProp.ValueKind == JsonValueKind.True;
                        if (mpProp.TryGetProperty("silentUpdate", out var suProp))
                            silentUpdate = suProp.ValueKind == JsonValueKind.True;
                        if (mpProp.TryGetProperty("applyOnNextStartup", out var anProp))
                            applyOnNextStartup = anProp.ValueKind != JsonValueKind.False;
                    }

                    // Parse cfg sub-object
                    string dataSpreadsheetId = "";
                    string updateChannel = "stable";
                    string datahubBaseUrl = null;
                    string datahubSiteId = null;
                    string datahubSiteCode = null;
                    string datahubDeviceToken = null;
                    string datahubAssertion = null;
                    long datahubAssertionExpiresAt = 0;
                    DataHubManifestUrls manifests = null;
                    VpsReleasesConfig releases = null;

                    if (root.TryGetProperty("cfg", out var cfgProp) && cfgProp.ValueKind == JsonValueKind.Object)
                    {
                        if (cfgProp.TryGetProperty("dataSpreadsheetId", out var sheetProp))
                            dataSpreadsheetId = sheetProp.GetString() ?? "";
                        if (cfgProp.TryGetProperty("updateChannel", out var chProp))
                            updateChannel = chProp.GetString() ?? "stable";

                        if (cfgProp.TryGetProperty("datahub", out var datahubProp) && datahubProp.ValueKind == JsonValueKind.Object)
                        {
                            if (datahubProp.TryGetProperty("apiBaseUrl", out var baseProp))
                                datahubBaseUrl = baseProp.GetString();
                            if (datahubProp.TryGetProperty("siteId", out var siteProp))
                                datahubSiteId = siteProp.GetString();
                            if (datahubProp.TryGetProperty("deviceToken", out var deviceProp))
                                datahubDeviceToken = deviceProp.GetString();
                            if (datahubProp.TryGetProperty("siteCode", out var siteCodeProp))
                                datahubSiteCode = siteCodeProp.GetString();
                            if (datahubProp.TryGetProperty("licenseAssertion", out var assertProp))
                                datahubAssertion = assertProp.GetString();
                            if (datahubProp.TryGetProperty("assertionExpiresAt", out var assertExpProp) &&
                                assertExpProp.ValueKind == JsonValueKind.Number)
                                datahubAssertionExpiresAt = assertExpProp.GetInt64();
                            if (datahubProp.TryGetProperty("manifests", out var manProp) && manProp.ValueKind == JsonValueKind.Object)
                                manifests = JsonSerializer.Deserialize<DataHubManifestUrls>(manProp.GetRawText());
                            if (datahubProp.TryGetProperty("releases", out var relProp) && relProp.ValueKind == JsonValueKind.Object)
                                releases = JsonSerializer.Deserialize<VpsReleasesConfig>(relProp.GetRawText());
                        }
                    }

                    // Fallback: parse datahub from root level (old format compat)
                    if (datahubBaseUrl == null && root.TryGetProperty("datahub", out var rootDataHub) && rootDataHub.ValueKind == JsonValueKind.Object)
                    {
                        if (rootDataHub.TryGetProperty("apiBaseUrl", out var baseProp2))
                            datahubBaseUrl = baseProp2.GetString();
                        if (datahubSiteId == null && rootDataHub.TryGetProperty("siteId", out var siteProp2))
                            datahubSiteId = siteProp2.GetString();
                        if (datahubDeviceToken == null && rootDataHub.TryGetProperty("deviceToken", out var deviceProp2))
                            datahubDeviceToken = deviceProp2.GetString();
                        if (datahubSiteCode == null && rootDataHub.TryGetProperty("siteCode", out var siteCodeProp2))
                            datahubSiteCode = siteCodeProp2.GetString();
                        if (datahubAssertion == null && rootDataHub.TryGetProperty("licenseAssertion", out var assertProp2))
                            datahubAssertion = assertProp2.GetString();
                        if (datahubAssertionExpiresAt == 0 && rootDataHub.TryGetProperty("assertionExpiresAt", out var assertExpProp2) &&
                            assertExpProp2.ValueKind == JsonValueKind.Number)
                            datahubAssertionExpiresAt = assertExpProp2.GetInt64();
                        if (manifests == null && rootDataHub.TryGetProperty("manifests", out var manProp2) && manProp2.ValueKind == JsonValueKind.Object)
                            manifests = JsonSerializer.Deserialize<DataHubManifestUrls>(manProp2.GetRawText());
                        if (releases == null && rootDataHub.TryGetProperty("releases", out var relProp2) && relProp2.ValueKind == JsonValueKind.Object)
                            releases = JsonSerializer.Deserialize<VpsReleasesConfig>(relProp2.GetRawText());
                    }

                    AppLogger.Info($"DataHub config: baseUrl={datahubBaseUrl?.Substring(0, Math.Min(40, datahubBaseUrl?.Length ?? 0))}, channel={updateChannel}");

                    // The license server never hands out a device token — it only proves the
                    // license is real. Trade the assertion for a token here so Program.cs keeps
                    // calling DataHubClient.Configure with exactly the same three arguments.
                    if (string.IsNullOrWhiteSpace(datahubSiteCode))
                        datahubSiteCode = (middleCode ?? "").Trim().ToUpperInvariant();

                    DateTimeOffset? deviceTokenExpiresAt = null;
                    if (string.IsNullOrWhiteSpace(datahubDeviceToken) &&
                        !string.IsNullOrWhiteSpace(datahubBaseUrl) &&
                        !string.IsNullOrWhiteSpace(datahubAssertion) &&
                        !string.IsNullOrWhiteSpace(datahubSiteCode))
                    {
                        var enrollResult = await EnrollDataHubDeviceAsync(
                            datahubBaseUrl, datahubAssertion, datahubSiteCode, BuildDeviceName(hwid), ct);

                        if (enrollResult.Succeeded)
                        {
                            var enrollment = enrollResult.Enrollment;
                            datahubDeviceToken = enrollment.DeviceToken;
                            // The enroll response is authoritative for the site GUID: the license
                            // record's siteId is often just the middle code, which Guid.TryParse
                            // rejects, leaving every DataHub call unauthenticated.
                            datahubSiteId = enrollment.SiteId;
                            datahubSiteCode = enrollment.SiteCode;
                            deviceTokenExpiresAt = enrollment.ExpiresAt;
                        }
                        else
                        {
                            // Activation still succeeds and the app runs local-only. But a rejected
                            // licence and an exhausted seat pool both need a person, so they must
                            // not sit at Warning among the faults that clear themselves.
                            string reason = "DataHub enrollment failed at activation: "
                                + DescribeDataHubEnrollFailure()
                                + $" (site={datahubSiteCode}). Cloud sync stays off until this is resolved.";
                            if (enrollResult.Failure is DataHubEnrollFailure.LicenseRejected
                                or DataHubEnrollFailure.SeatLimitReached)
                                AppLogger.Error(reason);
                            else
                                AppLogger.Warning(reason);
                        }
                    }

                    // Save dataSpreadsheetId to AppConfig
                    if (!string.IsNullOrWhiteSpace(dataSpreadsheetId))
                        AppConfig.Current.DataSpreadsheetId = dataSpreadsheetId;
                    AppConfig.SaveCurrent();

                    return new VerifyResult
                    {
                        Success = true,
                        Message = "Kích hoạt thành công",
                        Token = token,
                        Tier = tier,
                        MiddleCode = middleCode,
                        AutoUpdate = autoUpdate,
                        SilentUpdate = silentUpdate,
                        ApplyOnNextStartup = applyOnNextStartup,
                        SkipHashCheck = skipHashCheck,
                        IntegrityMode = integrityMode,
                        DataHubBaseUrl = datahubBaseUrl,
                        DataHubSiteId = datahubSiteId,
                        DataHubDeviceToken = datahubDeviceToken,
                        DataHubSiteCode = datahubSiteCode,
                        DataHubLicenseAssertion = datahubAssertion,
                        DataHubAssertionExpiresAt = datahubAssertionExpiresAt,
                        DataHubDeviceTokenExpiresAt = deviceTokenExpiresAt,
                        Manifests = manifests,
                        Releases = releases,
                        UpdateChannel = updateChannel,
                        DataSpreadsheetId = dataSpreadsheetId,
                        SessionId = CurrentSessionId
                    };
                }
            }
            catch (HttpRequestException)
            {
                return new VerifyResult
                {
                    Success = false,
                    Message = "Mất kết nối máy chủ.",
                    FailureKind = VerifyFailureKind.Transient
                };
            }
            catch (TaskCanceledException)
            {
                return new VerifyResult
                {
                    Success = false,
                    Message = "Máy chủ phản hồi quá chậm.",
                    FailureKind = VerifyFailureKind.Transient
                };
            }
            catch (Exception ex)
            {
                return new VerifyResult
                {
                    Success = false,
                    Message = "Lỗi hệ thống: " + ex.Message,
                    FailureKind = VerifyFailureKind.Transient
                };
            }
        }

        private static bool IsTransientHttpStatus(int statusCode)
        {
            return statusCode == 408 ||
                   statusCode == 429 ||
                   statusCode >= 500;
        }

        private static string RedactVerifyResponseForLog(string body)
        {
            if (string.IsNullOrWhiteSpace(body))
                return body ?? string.Empty;

            return TokenRedactor.RedactText(Regex.Replace(
                body,
                "(\"(?:payload|accessToken|token|deviceToken|licenseAssertion|apikey|serviceKey)\"\\s*:\\s*\")([^\"]+)(\")",
                "$1<redacted>$3",
                RegexOptions.IgnoreCase));
        }

        // ─── DataHub device enrollment ────────────────────────────────────────
        //
        // Split of responsibilities: Render proves the license and signs a short-lived
        // assertion; the DataHub API trades that assertion for a device token. The desktop app
        // is the only place the two meet, so the exchange lives here rather than in
        // DataHubClient (which must stay usable with a token from an env var alone).

        private sealed class DataHubEnrollment
        {
            public string DeviceToken { get; set; }
            public string SiteId { get; set; }
            public string SiteCode { get; set; }
            public DateTimeOffset ExpiresAt { get; set; }
        }

        /// <summary>
        /// Why an enrollment attempt failed, in terms someone can act on. The API answers with
        /// problem+json carrying a machine-readable <c>code</c>, and collapsing every one of
        /// them into "enroll failed" hid the difference between re-activating the licence,
        /// freeing a device seat, and looking at whether the VPS is up.
        /// </summary>
        internal enum DataHubEnrollFailure
        {
            None,

            /// <summary>401 ASSERTION_INVALID or 403 CHANNEL_MISMATCH — the licence proof was refused.</summary>
            LicenseRejected,

            /// <summary>409 SEAT_LIMIT_REACHED — the site has no free device seat.</summary>
            SeatLimitReached,

            /// <summary>404, 5xx or a transport fault — the VPS did not answer, or does not know this site.</summary>
            HubUnreachable,

            /// <summary>Render would not issue a fresh assertion, so there was nothing to trade.</summary>
            AssertionUnavailable,

            /// <summary>A refusal with no specific advice attached to it.</summary>
            Rejected
        }

        private sealed class DataHubEnrollResult
        {
            public DataHubEnrollment Enrollment { get; init; }
            public DataHubEnrollFailure Failure { get; init; }
            public bool Succeeded => Enrollment != null;
        }

        /// <summary>Result of a device-token renewal attempt.</summary>
        public enum DataHubRenewOutcome
        {
            /// <summary>Not due yet, or DataHub is not configured on this machine.</summary>
            NotNeeded,

            Renewed,

            /// <summary>Renewal was attempted and failed; the current token will expire.</summary>
            Failed
        }

        private static readonly object DataHubEnrollLock = new object();
        private static string _datahubBaseUrl = string.Empty;
        private static string _datahubSiteCode = string.Empty;
        private static DateTimeOffset _datahubTokenExpiresAt = DateTimeOffset.MinValue;
        private static DataHubEnrollFailure _datahubLastFailure = DataHubEnrollFailure.None;

        /// <summary>Re-enroll this long before the device token actually dies.</summary>
        private static readonly TimeSpan DeviceTokenRenewLead = TimeSpan.FromMinutes(30);

        /// <summary>
        /// Attempts per enrollment, with 1 s then 2 s between them. Three is deliberate:
        /// enrollment runs on the activation path, so the budget is bounded by how long someone
        /// will watch a splash screen, and a fourth attempt buys nothing the heartbeat two
        /// minutes later does not also buy.
        /// </summary>
        private const int EnrollMaxAttempts = 3;

        /// <summary>
        /// A stable per-machine name. Enrollment is keyed on (site_id, name): a stable name
        /// rotates the existing device row and costs no seat, while a name that changes on every
        /// run burns one seat per launch until the site hits SEAT_LIMIT_REACHED.
        /// </summary>
        private static string BuildDeviceName(string hwid)
        {
            string machine = (Environment.MachineName ?? "PC").Trim();
            if (machine.Length == 0) machine = "PC";

            string suffix = new string((hwid ?? "")
                .Where(char.IsLetterOrDigit)
                .Take(8)
                .ToArray())
                .ToUpperInvariant();

            string name = suffix.Length > 0 ? machine + "-" + suffix : machine;
            return name.Length > 128 ? name.Substring(0, 128) : name;
        }

        /// <summary>
        /// Trades a signed license assertion for a DataHub device token, retrying the failures
        /// that are worth retrying. Never throws for a refusal: enrollment must not turn a valid
        /// license into a failed activation — the app stays local-only until the next attempt —
        /// but the result now says <em>why</em>, so the caller can tell a VPS that is briefly
        /// down from a site that has run out of device seats.
        /// </summary>
        private static async Task<DataHubEnrollResult> EnrollDataHubDeviceAsync(
            string baseUrl, string assertion, string siteCode, string deviceName, CancellationToken ct)
        {
            string endpoint = baseUrl.TrimEnd('/') + "/api/v1/devices/enroll";
            string json = JsonSerializer.Serialize(new { siteCode = siteCode, deviceName = deviceName, role = "operator" });
            var failure = DataHubEnrollFailure.HubUnreachable;

            for (int attempt = 1; attempt <= EnrollMaxAttempts; attempt++)
            {
                bool retryable = false;
                try
                {
                    // Rebuilt each attempt: an HttpRequestMessage cannot be sent twice.
                    using var req = new HttpRequestMessage(HttpMethod.Post, endpoint);
                    req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", assertion);
                    req.Content = new StringContent(json, Encoding.UTF8, "application/json");

                    using var res = await Http.SendAsync(req, ct);
                    string body = await res.Content.ReadAsStringAsync(ct);

                    if (!res.IsSuccessStatusCode)
                    {
                        string code = ReadProblemCode(body);
                        failure = ClassifyEnrollFailure(res.StatusCode, code);
                        retryable = IsRetryableEnrollStatus(res.StatusCode);
                        AppLogger.Warning($"DataHub enroll attempt {attempt}/{EnrollMaxAttempts} failed " +
                                          $"status={(int)res.StatusCode} code={code} cause={failure} site={siteCode}");
                    }
                    else
                    {
                        var enrollment = ParseEnrollment(body, siteCode);
                        if (enrollment != null)
                        {
                            RememberDataHubEnrollment(baseUrl, enrollment.SiteCode, enrollment.ExpiresAt);
                            AppLogger.Info($"DataHub enrolled device={deviceName} site={enrollment.SiteCode} " +
                                           $"token={TokenRedactor.MaskToken(enrollment.DeviceToken)} expiresAt={enrollment.ExpiresAt:u}");
                            return new DataHubEnrollResult { Enrollment = enrollment };
                        }

                        // A 200 with no token is the server contradicting itself, which is as
                        // likely to be a truncated response as a real refusal — worth one more ask.
                        failure = DataHubEnrollFailure.Rejected;
                        retryable = true;
                        AppLogger.Warning($"DataHub enroll attempt {attempt}/{EnrollMaxAttempts} returned no device token.");
                    }
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
                catch (Exception ex)
                {
                    // No status at all: the VPS is unreachable, DNS is failing, or the 30 s client
                    // timeout elapsed — the last of which lands here as TaskCanceledException and
                    // is emphatically worth another try, which is why the filter above tests the
                    // token rather than the exception type.
                    failure = DataHubEnrollFailure.HubUnreachable;
                    retryable = true;
                    AppLogger.Warning($"DataHub enroll attempt {attempt}/{EnrollMaxAttempts} error: " + ex.Message);
                }

                if (!retryable || attempt == EnrollMaxAttempts) break;
                // 1 s, then 2 s. Doubling from one second keeps three attempts inside three
                // seconds of the activation path rather than making it a visible stall.
                await Task.Delay(TimeSpan.FromSeconds(1 << (attempt - 1)), ct);
            }

            RememberDataHubEnrollFailure(failure);
            return new DataHubEnrollResult { Failure = failure };
        }

        /// <summary>
        /// Only transport faults and server-side stalls are retried. 401/403/404/409 are
        /// decisions, not glitches: the same assertion, site and seat count produce the same
        /// answer a second later, so retrying them would spend the whole backoff budget to reach
        /// the identical refusal with activation three seconds slower.
        /// </summary>
        private static bool IsRetryableEnrollStatus(HttpStatusCode status)
            => (int)status >= 500
                || status == HttpStatusCode.RequestTimeout
                || status == HttpStatusCode.TooManyRequests;

        private static string ReadProblemCode(string body)
        {
            try
            {
                using var problem = JsonDocument.Parse(body);
                return problem.RootElement.TryGetProperty("code", out var codeProp)
                    ? codeProp.GetString() ?? "UNKNOWN"
                    : "UNKNOWN";
            }
            catch { return "UNKNOWN"; }   // not problem+json
        }

        /// <summary>
        /// The problem code decides, and the status is only the fallback — a refusal can arrive
        /// without problem+json at all (a reverse-proxy 503 page, for instance), and in that
        /// case the status is the only thing left to read.
        /// </summary>
        private static DataHubEnrollFailure ClassifyEnrollFailure(HttpStatusCode status, string code)
        {
            if (string.Equals(code, "ASSERTION_INVALID", StringComparison.Ordinal)
                || string.Equals(code, "CHANNEL_MISMATCH", StringComparison.Ordinal))
                return DataHubEnrollFailure.LicenseRejected;
            if (string.Equals(code, "SEAT_LIMIT_REACHED", StringComparison.Ordinal))
                return DataHubEnrollFailure.SeatLimitReached;

            return status switch
            {
                HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => DataHubEnrollFailure.LicenseRejected,
                HttpStatusCode.Conflict => DataHubEnrollFailure.SeatLimitReached,
                HttpStatusCode.NotFound => DataHubEnrollFailure.HubUnreachable,
                _ => (int)status >= 500 ? DataHubEnrollFailure.HubUnreachable : DataHubEnrollFailure.Rejected
            };
        }

        private static DataHubEnrollment ParseEnrollment(string body, string requestedSiteCode)
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;

            string deviceToken = root.TryGetProperty("deviceToken", out var tokenProp) ? tokenProp.GetString() : null;
            if (string.IsNullOrWhiteSpace(deviceToken)) return null;

            return new DataHubEnrollment
            {
                DeviceToken = deviceToken,
                SiteId = root.TryGetProperty("siteId", out var siteIdProp) ? siteIdProp.GetString() : null,
                SiteCode = root.TryGetProperty("siteCode", out var siteCodeProp) ? siteCodeProp.GetString() : requestedSiteCode,
                ExpiresAt = root.TryGetProperty("expiresAt", out var expProp) && expProp.TryGetDateTimeOffset(out var exp)
                    ? exp
                    : DateTimeOffset.UtcNow.AddHours(24)
            };
        }

        private static void RememberDataHubEnrollment(string baseUrl, string siteCode, DateTimeOffset expiresAt)
        {
            lock (DataHubEnrollLock)
            {
                _datahubBaseUrl = baseUrl ?? string.Empty;
                _datahubSiteCode = siteCode ?? string.Empty;
                _datahubTokenExpiresAt = expiresAt;
                _datahubLastFailure = DataHubEnrollFailure.None;
            }
        }

        private static void RememberDataHubEnrollFailure(DataHubEnrollFailure failure)
        {
            lock (DataHubEnrollLock) _datahubLastFailure = failure;
        }

        /// <summary>
        /// The last enrollment failure phrased for whoever is reading the status bar. It lives
        /// here rather than at each call site so the activation path and the heartbeat renewal
        /// describe the same fault with the same words.
        /// </summary>
        public static string DescribeDataHubEnrollFailure()
        {
            DataHubEnrollFailure failure;
            lock (DataHubEnrollLock) failure = _datahubLastFailure;
            return failure switch
            {
                DataHubEnrollFailure.LicenseRejected => "giấy phép bị từ chối (assertion/kênh không hợp lệ) — cần kích hoạt lại",
                DataHubEnrollFailure.SeatLimitReached => "bưu cục đã hết chỗ thiết bị — cần thu hồi một máy cũ",
                DataHubEnrollFailure.HubUnreachable => "không liên lạc được máy chủ DataHub",
                DataHubEnrollFailure.AssertionUnavailable => "không lấy được chứng thực giấy phép từ máy chủ license",
                DataHubEnrollFailure.Rejected => "máy chủ DataHub từ chối đăng ký thiết bị",
                _ => "chưa rõ nguyên nhân"
            };
        }

        /// <summary>
        /// Asks Render for a fresh assertion using the access token the heartbeat keeps alive.
        /// Returns null when the deployment has no signing key (503) or the session is gone.
        /// </summary>
        private static async Task<string> FetchDataHubAssertionAsync(CancellationToken ct)
        {
            string accessToken = CurrentAccessToken;
            if (string.IsNullOrWhiteSpace(accessToken)) return null;

            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Post, ApiDataHubAssertion);
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
                req.Content = new StringContent("{}", Encoding.UTF8, "application/json");

                using var res = await Http.SendAsync(req, ct);
                string body = await res.Content.ReadAsStringAsync(ct);

                if (!res.IsSuccessStatusCode)
                {
                    AppLogger.Warning($"DataHub assertion refresh failed status={(int)res.StatusCode}");
                    return null;
                }

                using var doc = JsonDocument.Parse(body);
                var root = doc.RootElement;

                // apiBaseUrl / siteCode may have moved since activation (a site migrated to a new
                // VPS); trust the fresh answer over the remembered one.
                string baseUrl = root.TryGetProperty("apiBaseUrl", out var urlProp) ? urlProp.GetString() : null;
                string siteCode = root.TryGetProperty("siteCode", out var siteProp) ? siteProp.GetString() : null;
                lock (DataHubEnrollLock)
                {
                    if (!string.IsNullOrWhiteSpace(baseUrl)) _datahubBaseUrl = baseUrl;
                    if (!string.IsNullOrWhiteSpace(siteCode)) _datahubSiteCode = siteCode;
                }

                return root.TryGetProperty("licenseAssertion", out var assertProp) ? assertProp.GetString() : null;
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                AppLogger.Warning("DataHub assertion refresh error: " + ex.Message);
                return null;
            }
        }

        /// <summary>
        /// Renews the DataHub device token before it expires. Safe to call on every heartbeat:
        /// it returns immediately unless the token is inside the renew window. Called from the
        /// heartbeat loop because a station can stay open for days on a 24h device token, and a
        /// silently expired token turns every sync into a 401 with no visible symptom.
        ///
        /// The outcome distinguishes "nothing to do" from "tried and failed". It used to return
        /// false for both, so a machine whose renewal was failing every two minutes was
        /// indistinguishable from one whose token was simply still fresh — which is precisely
        /// the state the operator needs to be told about, since the sync dies at expiry.
        /// </summary>
        public static async Task<DataHubRenewOutcome> RenewDataHubDeviceTokenIfNeededAsync(string hwid, CancellationToken ct)
        {
            string baseUrl, siteCode;
            DateTimeOffset expiresAt;
            lock (DataHubEnrollLock)
            {
                baseUrl = _datahubBaseUrl;
                siteCode = _datahubSiteCode;
                expiresAt = _datahubTokenExpiresAt;
            }

            if (string.IsNullOrWhiteSpace(baseUrl) || string.IsNullOrWhiteSpace(siteCode)) return DataHubRenewOutcome.NotNeeded;
            if (expiresAt == DateTimeOffset.MinValue) return DataHubRenewOutcome.NotNeeded;
            if (DateTimeOffset.UtcNow < expiresAt - DeviceTokenRenewLead) return DataHubRenewOutcome.NotNeeded;

            // Past this point the token is inside its renew window, so every remaining exit is
            // either a renewal or a failure worth reporting.
            string assertion = await FetchDataHubAssertionAsync(ct);
            if (string.IsNullOrWhiteSpace(assertion))
            {
                RememberDataHubEnrollFailure(DataHubEnrollFailure.AssertionUnavailable);
                return DataHubRenewOutcome.Failed;
            }

            lock (DataHubEnrollLock)
            {
                baseUrl = _datahubBaseUrl;
                siteCode = _datahubSiteCode;
            }

            var enrollResult = await EnrollDataHubDeviceAsync(baseUrl, assertion, siteCode, BuildDeviceName(hwid), ct);
            if (!enrollResult.Succeeded) return DataHubRenewOutcome.Failed;

            // Configure tears down the realtime subscription, so the next sync cycle reconnects
            // the hub with the new token instead of retrying forever on a dead one.
            DataHubClient.Configure(baseUrl, enrollResult.Enrollment.DeviceToken, enrollResult.Enrollment.SiteId);
            AppLogger.Info("DataHub device token renewed.");
            return DataHubRenewOutcome.Renewed;
        }

        public static async Task<HeartbeatResult> SendHeartbeatOnceAsync(
            string tokenToUse, string hwid, CancellationToken ct)
        {
            try
            {
                var payload = new { clientHwid = hwid, exeHash = Program.ExecutableHash };
                string json = JsonSerializer.Serialize(payload);

                using var req = new HttpRequestMessage(HttpMethod.Post, ApiHeartbeat);
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokenToUse);
                req.Content = new StringContent(json, Encoding.UTF8, "application/json");

                using var res = await Http.SendAsync(req, ct);
                string body = await res.Content.ReadAsStringAsync(ct);

                if (string.IsNullOrWhiteSpace(body))
                    return new HeartbeatResult(HeartbeatOutcome.TransientFailure, null, "Empty response");

                JsonDocument doc;
                try { doc = JsonDocument.Parse(body); }
                catch { return new HeartbeatResult(HeartbeatOutcome.TransientFailure, null, "Malformed JSON"); }

                using (doc)
                {
                    var root = doc.RootElement;
                    string action = root.TryGetProperty("action", out var act) ? act.GetString() : "";

                    if (action == "kill")
                    {
                        return new HeartbeatResult(HeartbeatOutcome.ServerKill, null,
                            root.TryGetProperty("reason", out var r) ? r.GetString() : "Revoked");
                    }
                    else if (action == "continue")
                    {
                        string newToken = root.TryGetProperty("payload", out var tokenPayload) ? tokenPayload.GetString() : string.Empty;
                        if (!ValidateJwtToken(newToken))
                            return new HeartbeatResult(HeartbeatOutcome.Fatal, null, "Invalid JWT");
                        CurrentAccessToken = newToken?.Trim() ?? string.Empty;
                        return new HeartbeatResult(HeartbeatOutcome.Continue, newToken, null);
                    }

                    if (!res.IsSuccessStatusCode)
                        return new HeartbeatResult(HeartbeatOutcome.Fatal, null, "Token Expired");

                    return new HeartbeatResult(HeartbeatOutcome.TransientFailure, null, "Unknown action");
                }
            }
            catch (HttpRequestException) { return new HeartbeatResult(HeartbeatOutcome.TransientFailure, null, "Network error"); }
            catch (TaskCanceledException) { return new HeartbeatResult(HeartbeatOutcome.TransientFailure, null, "Timeout"); }
            catch { return new HeartbeatResult(HeartbeatOutcome.TransientFailure, null, "Unknown error"); }
        }

        private static bool ValidateJwtToken(string token)
        {
            try
            {
                string cleanToken = token.Trim().Replace("\"", "");
                RSA rsa = RSA.Create();
                rsa.ImportFromPem(JWT_PUBLIC_KEY.ToCharArray());

                var validationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = "autojms-license-server",
                    ValidateAudience = true,
                    ValidAudience = "autojms-desktop-client",
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.FromMinutes(2),
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new RsaSecurityKey(rsa) { KeyId = "accessKey" }
                };

                var handler = new JwtSecurityTokenHandler();
                handler.ValidateToken(cleanToken, validationParameters, out SecurityToken validatedToken);
                return true;
            }
            catch { return false; }
        }

        public sealed class HeartbeatSupervisor
        {
            private readonly string _licenseKey;
            private readonly string _deviceId;
            private string _currentToken;
            private readonly Action<string> _onTokenUpdate;
            private readonly Action<string> _onWarning;
            private readonly TimeSpan _interval = TimeSpan.FromMinutes(2);
            private int _fatalRetryCount = 0;
            private bool _datahubRenewalWarned;

            public HeartbeatSupervisor(string licenseKey, string deviceId, string initialToken, Action<string> onTokenUpdate, Action<string> onWarning)
            {
                _licenseKey = licenseKey;
                _deviceId = deviceId;
                _currentToken = initialToken;
                _onTokenUpdate = onTokenUpdate;
                _onWarning = onWarning;
            }

            /// <summary>
            /// Turns a renewal outcome into one report per fault. Latched, because the heartbeat
            /// retries every two minutes for as long as the token is inside its renew window: an
            /// unlatched message would repaint the status bar with the same warning thirty times
            /// an hour and fill the log with it. Re-armed on the renewal that finally succeeds,
            /// so a fault that comes back is announced again.
            /// </summary>
            private void ReportDataHubRenewal(DataHubRenewOutcome outcome)
            {
                if (outcome == DataHubRenewOutcome.Failed)
                {
                    if (_datahubRenewalWarned) return;
                    _datahubRenewalWarned = true;

                    // Error, not Warning: the device token is already inside its renew window, so
                    // this is not a transient blip — cloud sync stops for good at expiry unless
                    // somebody acts, and the app gives no other sign of it.
                    string cause = LicenseApiService.DescribeDataHubEnrollFailure();
                    AppLogger.Error("DataHub device token renewal FAILED (" + cause +
                        "). Cloud sync will stop when the current token expires; the site needs a re-activation.");
                    _onWarning?.Invoke("Không gia hạn được kết nối DataHub (" + cause +
                        "). Dữ liệu vẫn lưu local, nhưng sẽ ngừng đồng bộ đám mây.");
                    return;
                }

                if (outcome == DataHubRenewOutcome.Renewed && _datahubRenewalWarned)
                {
                    _datahubRenewalWarned = false;
                    AppLogger.Info("DataHub device token renewal recovered.");
                    _onWarning?.Invoke("Đã gia hạn kết nối DataHub — đồng bộ đám mây hoạt động lại.");
                }
            }

            public async Task StartAsync(CancellationToken ct = default)
            {
                if (string.IsNullOrEmpty(_currentToken))
                {
                    _onWarning?.Invoke("Đang thử kết nối đến máy chủ...");
                    var recoverResult = await LicenseApiService.VerifyLicenseSecureAsync(_licenseKey, _deviceId, ct);
                    if (recoverResult.Success && !string.IsNullOrEmpty(recoverResult.Token))
                    {
                        _currentToken = recoverResult.Token;
                        _onTokenUpdate(_currentToken);
                        _onWarning?.Invoke("Đã kết nối!");
                    }
                    else
                    {
                        _onWarning?.Invoke("Chưa có kết nối, sẽ thử lại sau.");
                    }
                }

                await Task.Delay(_interval, ct);

                while (!ct.IsCancellationRequested)
                {
                    if (string.IsNullOrEmpty(_currentToken))
                    {
                        var recoverResult = await LicenseApiService.VerifyLicenseSecureAsync(_licenseKey, _deviceId, ct);
                        if (recoverResult.Success && !string.IsNullOrEmpty(recoverResult.Token))
                        {
                            _currentToken = recoverResult.Token;
                            _onTokenUpdate(_currentToken);
                            _onWarning?.Invoke("Đã kết nối lại!");
                            _fatalRetryCount = 0;
                        }
                        else
                        {
                            _onWarning?.Invoke("Vẫn chưa có mạng, đang chờ...");
                            await Task.Delay(_interval, ct);
                            continue;
                        }
                    }

                    var result = await LicenseApiService.SendHeartbeatOnceAsync(_currentToken, _deviceId, ct);

                    switch (result.Outcome)
                    {
                        case HeartbeatOutcome.Continue:
                            _fatalRetryCount = 0;
                            if (!string.IsNullOrEmpty(result.NewToken))
                            {
                                _currentToken = result.NewToken;
                                _onTokenUpdate(result.NewToken);
                            }
                            // Piggyback on the heartbeat: this is the only loop guaranteed to be
                            // running while the app is open, and it already holds a fresh access
                            // token — exactly what the assertion endpoint asks for.
                            try
                            {
                                ReportDataHubRenewal(
                                    await LicenseApiService.RenewDataHubDeviceTokenIfNeededAsync(_deviceId, ct));
                            }
                            catch (OperationCanceledException) { throw; }
                            catch (Exception ex)
                            {
                                AppLogger.Error("DataHub token renewal threw: " + ex.Message);
                                ReportDataHubRenewal(DataHubRenewOutcome.Failed);
                            }
                            break;

                        case HeartbeatOutcome.ServerKill:
                            _onWarning?.Invoke("Phiên bản bị khóa từ máy chủ. Ứng dụng sẽ đóng.");
                            await Task.Delay(3000, ct);
                            System.Windows.Forms.Application.Exit();
                            return;

                        case HeartbeatOutcome.TransientFailure:
                            _onWarning?.Invoke("Mất kết nối tạm thời, đang chờ...");
                            break;

                        case HeartbeatOutcome.Fatal:
                            _fatalRetryCount++;
                            if (_fatalRetryCount >= 5)
                            {
                                _onWarning?.Invoke("Đứt kết nối quá lâu. Ứng dụng vẫn hoạt động nhưng chưa xác thực.");
                                _fatalRetryCount = 0;
                            }
                            _currentToken = null;
                            _onWarning?.Invoke($"Token hết hạn hoặc lỗi. Sẽ thử lại (lần {_fatalRetryCount})...");
                            break;
                    }

                    int jitterMs = new Random().Next(1000, 5000);
                    await Task.Delay(_interval + TimeSpan.FromMilliseconds(jitterMs), ct);
                }
            }
        }
    }
}

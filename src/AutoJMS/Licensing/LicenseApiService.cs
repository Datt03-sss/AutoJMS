using System;
using System.IO;
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
        public string SupabaseBaseUrl { get; set; }
        public string SupabaseProjectUrl { get; set; }
        public string SupabaseAnonKey { get; set; }
        public SupabaseManifestUrls Manifests { get; set; }
        public SupabaseReleasesConfig Releases { get; set; }
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
                    string supabaseBaseUrl = null;
                    string supabaseProjectUrl = null;
                    string supabaseAnonKey = null;
                    SupabaseManifestUrls manifests = null;
                    SupabaseReleasesConfig releases = null;

                    if (root.TryGetProperty("cfg", out var cfgProp) && cfgProp.ValueKind == JsonValueKind.Object)
                    {
                        if (cfgProp.TryGetProperty("dataSpreadsheetId", out var sheetProp))
                            dataSpreadsheetId = sheetProp.GetString() ?? "";
                        if (cfgProp.TryGetProperty("updateChannel", out var chProp))
                            updateChannel = chProp.GetString() ?? "stable";

                        if (cfgProp.TryGetProperty("supabase", out var supProp) && supProp.ValueKind == JsonValueKind.Object)
                        {
                            if (supProp.TryGetProperty("baseUrl", out var baseProp))
                                supabaseBaseUrl = baseProp.GetString();
                            if (supProp.TryGetProperty("projectUrl", out var projectProp))
                                supabaseProjectUrl = projectProp.GetString();
                            if (supProp.TryGetProperty("anonKey", out var anonProp))
                                supabaseAnonKey = anonProp.GetString();
                            if (supProp.TryGetProperty("manifests", out var manProp) && manProp.ValueKind == JsonValueKind.Object)
                                manifests = JsonSerializer.Deserialize<SupabaseManifestUrls>(manProp.GetRawText());
                            if (supProp.TryGetProperty("releases", out var relProp) && relProp.ValueKind == JsonValueKind.Object)
                                releases = JsonSerializer.Deserialize<SupabaseReleasesConfig>(relProp.GetRawText());
                        }
                    }

                    // Fallback: parse supabase from root level (old format compat)
                    if (supabaseBaseUrl == null && root.TryGetProperty("supabase", out var rootSup) && rootSup.ValueKind == JsonValueKind.Object)
                    {
                        if (rootSup.TryGetProperty("baseUrl", out var baseProp2))
                            supabaseBaseUrl = baseProp2.GetString();
                        if (supabaseProjectUrl == null && rootSup.TryGetProperty("projectUrl", out var projectProp2))
                            supabaseProjectUrl = projectProp2.GetString();
                        if (supabaseAnonKey == null && rootSup.TryGetProperty("anonKey", out var anonProp2))
                            supabaseAnonKey = anonProp2.GetString();
                        if (manifests == null && rootSup.TryGetProperty("manifests", out var manProp2) && manProp2.ValueKind == JsonValueKind.Object)
                            manifests = JsonSerializer.Deserialize<SupabaseManifestUrls>(manProp2.GetRawText());
                        if (releases == null && rootSup.TryGetProperty("releases", out var relProp2) && relProp2.ValueKind == JsonValueKind.Object)
                            releases = JsonSerializer.Deserialize<SupabaseReleasesConfig>(relProp2.GetRawText());
                    }

                    AppLogger.Info($"Supabase config: baseUrl={supabaseBaseUrl?.Substring(0, Math.Min(40, supabaseBaseUrl?.Length ?? 0))}, channel={updateChannel}");

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
                        SupabaseBaseUrl = supabaseBaseUrl,
                        SupabaseProjectUrl = supabaseProjectUrl,
                        SupabaseAnonKey = supabaseAnonKey,
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
                "(\"(?:payload|accessToken|token|anonKey|apikey|supabaseKey|serviceKey)\"\\s*:\\s*\")([^\"]+)(\")",
                "$1<redacted>$3",
                RegexOptions.IgnoreCase));
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

            public HeartbeatSupervisor(string licenseKey, string deviceId, string initialToken, Action<string> onTokenUpdate, Action<string> onWarning)
            {
                _licenseKey = licenseKey;
                _deviceId = deviceId;
                _currentToken = initialToken;
                _onTokenUpdate = onTokenUpdate;
                _onWarning = onWarning;
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

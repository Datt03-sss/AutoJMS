using System;
using System.Text.Json;

namespace AutoJMS
{
    /// <summary>
    /// Single source of truth for interpreting a JMS API response.
    ///
    /// JMS conventions observed:
    ///   • Success : HTTP 200, body { "code":1, "msg":"1:Thao tác thành công", "data":[...] }  (succ may be true)
    ///   • Auth exp: HTTP 401, body { "code":401, "fail":true, "succ":false,
    ///                                "msg":"页面长时间未操作，请重新登录系统" }
    ///
    /// The cardinal rule: a successful business response (code=1 / succ=true)
    /// is NEVER treated as auth-expired, even if some substring elsewhere in
    /// the body looks suspicious. This kills the false-positive 401 storm.
    /// </summary>
    [System.Reflection.Obfuscation(Exclude = true, ApplyToMembers = true)]
    public static class JmsResponseClassifier
    {
        /// <summary>True for a genuine successful JMS business response.</summary>
        public static bool IsSuccess(int statusCode, string body)
        {
            if (statusCode < 200 || statusCode > 299) return false;
            if (string.IsNullOrWhiteSpace(body)) return false;

            if (!TryParse(body, out var p)) return false;

            // succ:true is an explicit success flag.
            if (p.Succ == true) return true;

            // JMS uses code==1 as the success code. Treat 200 + code==1 as success.
            if (p.Code == 1)
            {
                // The fuller signal (code=1 + "thành công" + data) is the canonical
                // success shape, but code==1 alone on a 200 is already success.
                return true;
            }

            return false;
        }

        /// <summary>
        /// True when the response indicates the JMS auth session is expired
        /// or suspect. Guarded so a successful response is never expired.
        /// </summary>
        public static bool IsAuthExpired(int statusCode, string body)
        {
            // Never flag a confirmed success as expired.
            if (IsSuccess(statusCode, body)) return false;

            // HTTP 401 is the strongest signal.
            if (statusCode == 401) return true;

            if (string.IsNullOrWhiteSpace(body)) return false;
            if (!TryParse(body, out var p)) return false;

            // Business-level 401.
            if (p.Code == 401) return true;

            // "Please re-login / session expired" wording, but only when the
            // response also failed (fail=true or succ=false) to avoid matching
            // a legit payload that merely contains such words.
            if (HasReloginWording(p.Msg) && (p.Fail == true || p.Succ == false))
                return true;

            return false;
        }

        /// <summary>
        /// True when an auth failure is worth a single refresh+retry. For JMS
        /// this is the same set as <see cref="IsAuthExpired"/> (401 / suspect).
        /// </summary>
        public static bool IsRetryableAuthFailure(int statusCode, string body)
            => IsAuthExpired(statusCode, body);

        // ── internals ─────────────────────────────────────────────────────

        private static bool HasReloginWording(string msg)
        {
            if (string.IsNullOrEmpty(msg)) return false;
            // Chinese: 重新登录 / 重新登入 / 重新登陆 / 请重新登录 ; English: login / log in / expired / unauthorized
            return msg.Contains("重新登录")
                || msg.Contains("重新登入")
                || msg.Contains("重新登陆")
                || msg.Contains("登录系统")
                || msg.Contains("未登录")
                || msg.IndexOf("re-login", StringComparison.OrdinalIgnoreCase) >= 0
                || msg.IndexOf("relogin", StringComparison.OrdinalIgnoreCase) >= 0
                || msg.IndexOf("please login", StringComparison.OrdinalIgnoreCase) >= 0
                || msg.IndexOf("log in again", StringComparison.OrdinalIgnoreCase) >= 0
                || msg.IndexOf("expired", StringComparison.OrdinalIgnoreCase) >= 0
                || msg.IndexOf("unauthorized", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private struct Parsed
        {
            public int? Code;
            public bool? Succ;
            public bool? Fail;
            public string Msg;
            public bool DataExists;
        }

        private static bool TryParse(string body, out Parsed parsed)
        {
            parsed = default;
            try
            {
                using var doc = JsonDocument.Parse(body);
                if (doc.RootElement.ValueKind != JsonValueKind.Object) return false;
                var root = doc.RootElement;

                if (root.TryGetProperty("code", out var codeEl))
                    parsed.Code = ReadInt(codeEl);

                if (root.TryGetProperty("succ", out var succEl))
                    parsed.Succ = ReadBool(succEl);

                if (root.TryGetProperty("fail", out var failEl))
                    parsed.Fail = ReadBool(failEl);

                if (root.TryGetProperty("msg", out var msgEl) && msgEl.ValueKind == JsonValueKind.String)
                    parsed.Msg = msgEl.GetString();

                if (root.TryGetProperty("data", out var dataEl))
                    parsed.DataExists = dataEl.ValueKind != JsonValueKind.Null && dataEl.ValueKind != JsonValueKind.Undefined;

                return true;
            }
            catch
            {
                return false;
            }
        }

        private static int? ReadInt(JsonElement el)
        {
            switch (el.ValueKind)
            {
                case JsonValueKind.Number:
                    return el.TryGetInt32(out var n) ? n : (int?)null;
                case JsonValueKind.String:
                    return int.TryParse(el.GetString(), out var s) ? s : (int?)null;
                default:
                    return null;
            }
        }

        private static bool? ReadBool(JsonElement el)
        {
            switch (el.ValueKind)
            {
                case JsonValueKind.True: return true;
                case JsonValueKind.False: return false;
                case JsonValueKind.String:
                    var s = el.GetString();
                    if (bool.TryParse(s, out var b)) return b;
                    if (string.Equals(s, "true", StringComparison.OrdinalIgnoreCase)) return true;
                    if (string.Equals(s, "false", StringComparison.OrdinalIgnoreCase)) return false;
                    return null;
                default: return null;
            }
        }
    }
}

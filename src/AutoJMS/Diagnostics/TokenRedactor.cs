#nullable enable
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace AutoJMS.Diagnostics
{
    public static class TokenRedactor
    {
        private static readonly Regex JwtRegex = new(
            @"(?<![A-Za-z0-9_-])[A-Za-z0-9_-]{10,}\.[A-Za-z0-9_-]{10,}\.[A-Za-z0-9_-]{10,}(?![A-Za-z0-9_-])",
            RegexOptions.Compiled);

        private static readonly Regex Hex32Regex = new(
            @"(?<![A-Fa-f0-9])[A-Fa-f0-9]{32}(?![A-Fa-f0-9])",
            RegexOptions.Compiled);

        private static readonly Regex JsonSensitiveValueRegex = new(
            @"(?i)(""?(authToken|authorization|cookie|set-cookie|yl_token|accessToken|jms_token|token|licenseKey|license_key|sid|password|firebaseToken|supabaseKey|supabaseAnonKey|anonKey|apiKey|apikey|serviceKey|senderPhone|receiverPhone)""?\s*[:=]\s*"")([^""]+)("")",
            RegexOptions.Compiled);

        private static readonly Regex QuerySensitiveValueRegex = new(
            @"(?i)([?&](authToken|authorization|yl_token|accessToken|jms_token|token|licenseKey|license_key|sid|password|firebaseToken|supabaseKey|supabaseAnonKey|anonKey|apiKey|apikey|serviceKey|senderPhone|receiverPhone)=)([^&\s""]+)",
            RegexOptions.Compiled);

        private static readonly HashSet<string> SensitiveHeaderNames = new(StringComparer.OrdinalIgnoreCase)
        {
            "authorization",
            "cookie",
            "set-cookie",
            "authtoken",
            "auth-token",
            "licensekey",
            "license-key",
            "yl_token",
            "access-token",
            "accesstoken",
            "jms_token",
            "token",
            "sid",
            "password",
            "firebase-token",
            "supabase-key",
            "supabasekey",
            "anon-key",
            "anonkey",
            "apikey",
            "api-key",
            "service-key",
            "servicekey",
            "senderphone",
            "receiverphone"
        };

        public static string MaskToken(string value)
        {
            if (string.IsNullOrEmpty(value)) return "";
            string trimmed = value.Trim();
            if (trimmed.Length <= 10) return "******";
            return trimmed[..Math.Min(6, trimmed.Length)] + "******" + trimmed[^Math.Min(4, trimmed.Length)..];
        }

        public static bool IsSensitiveKey(string key)
        {
            if (string.IsNullOrWhiteSpace(key)) return false;
            if (SensitiveHeaderNames.Contains(key.Trim())) return true;
            string normalized = key.Replace("_", "", StringComparison.Ordinal).Replace("-", "", StringComparison.Ordinal);
            return normalized.Contains("auth", StringComparison.OrdinalIgnoreCase)
                || normalized.Contains("token", StringComparison.OrdinalIgnoreCase)
                || normalized.Contains("cookie", StringComparison.OrdinalIgnoreCase)
                || normalized.Contains("licensekey", StringComparison.OrdinalIgnoreCase)
                || normalized.Contains("password", StringComparison.OrdinalIgnoreCase)
                || normalized.Contains("phone", StringComparison.OrdinalIgnoreCase);
        }

        public static string RedactValue(string key, string value)
        {
            if (string.IsNullOrEmpty(value)) return "";
            if (IsSensitiveKey(key))
            {
                if (key.Contains("cookie", StringComparison.OrdinalIgnoreCase))
                    return $"[redacted-cookie; length={value.Length}]";
                return MaskToken(value);
            }
            return RedactText(value);
        }

        public static Dictionary<string, string> RedactHeaders(IReadOnlyDictionary<string, string> headers)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var kv in headers)
                result[kv.Key] = RedactValue(kv.Key, kv.Value);
            return result;
        }

        public static string RedactText(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";

            string redacted = JsonSensitiveValueRegex.Replace(
                text,
                m => m.Groups[1].Value + MaskToken(m.Groups[3].Value) + m.Groups[4].Value);

            redacted = QuerySensitiveValueRegex.Replace(
                redacted,
                m => m.Groups[1].Value + MaskToken(m.Groups[3].Value));

            redacted = JwtRegex.Replace(redacted, m => MaskToken(m.Value));
            redacted = Hex32Regex.Replace(redacted, m => MaskToken(m.Value));
            return redacted;
        }
    }
}

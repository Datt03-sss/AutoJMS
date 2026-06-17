#nullable enable
using AutoJMS.Diagnostics;
using System.Collections.Generic;

namespace AutoJMS.Diagnostics.AppCapture
{
    public static class AppCaptureRedactor
    {
        public static string RedactText(string? value)
            => TokenRedactor.RedactText(value ?? "");

        public static string RedactValue(string key, string? value)
            => TokenRedactor.RedactValue(key, value ?? "");

        public static Dictionary<string, string> RedactHeaders(IReadOnlyDictionary<string, string>? headers)
            => headers == null
                ? new Dictionary<string, string>()
                : TokenRedactor.RedactHeaders(headers);
    }
}


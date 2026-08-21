using System;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AutoJMS.Diagnostics.AppCapture;

namespace AutoJMS;

public static class HashVerifier
{
    public static string ComputeDllHash()
    {
        try
        {
            string dllPath = Path.Combine(AppPaths.InstallDir, "AutoJMS.dll");
            if (!File.Exists(dllPath))
                dllPath = Assembly.GetExecutingAssembly().Location;

            using var sha256 = SHA256.Create();
            using var stream = new FileStream(dllPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            byte[] hash = sha256.ComputeHash(stream);
            return BitConverter.ToString(hash).Replace("-", "").ToLower();
        }
        catch
        {
            return "HASH_ERROR";
        }
    }

    public static async Task<bool?> VerifyAgainstManifestAsync(CancellationToken ct = default)
    {
        try
        {
            string currentVersion = AppVersion.Current;
            string baseUrl = (Environment.GetEnvironmentVariable("AUTOJMS_DATAHUB_MODULES_BASE_URL")
                ?? Environment.GetEnvironmentVariable("AUTOJMS_DATAHUB_API_BASE_URL")
                ?? string.Empty).TrimEnd('/');
            if (string.IsNullOrWhiteSpace(baseUrl)) return null;
            string url = $"{baseUrl}/manifest/hash-manifest.json";

            using var http = new HttpClient(new AppHttpCaptureHandler(new HttpClientHandler(), "HashVerifier")) { Timeout = TimeSpan.FromSeconds(10) };
            string json = await http.GetStringAsync(url, ct);

            var manifest = JsonSerializer.Deserialize<HashManifest>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (manifest?.Versions == null) return null;

            string expectedHash = null;
            if (manifest.Versions.TryGetValue(currentVersion, out var versionEntry) &&
                versionEntry.Files != null &&
                versionEntry.Files.TryGetValue("AutoJMS.dll", out var hash))
            {
                expectedHash = hash;
            }

            if (string.IsNullOrEmpty(expectedHash))
            {
                AppLogger.Warning($"Hash manifest has no entry for version {currentVersion}");
                return null;
            }

            string localHash = ComputeDllHash();
            if (localHash == "HASH_ERROR") return null;

            bool match = string.Equals(localHash, expectedHash, StringComparison.OrdinalIgnoreCase);
            if (!match)
                AppLogger.Error($"INTEGRITY FAIL: expected {expectedHash}, got {localHash}");

            return match;
        }
        catch (HttpRequestException) { return null; }
        catch (TaskCanceledException) { return null; }
        catch (Exception ex)
        {
            AppLogger.Error("Hash verification error", ex);
            return null;
        }
    }
}

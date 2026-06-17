using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AutoJMS.Diagnostics.AppCapture;

namespace AutoJMS
{
    public class SupabaseManifestService
    {
        private readonly string _baseUrl;
        private readonly SupabaseManifestUrls _urls;
        private readonly HttpClient _http;
        private static readonly JsonSerializerOptions JsonOpts = new()
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        };

        public string BaseUrl => _baseUrl;
        public SupabaseManifestUrls Urls => _urls;

        public SupabaseManifestService(string baseUrl, SupabaseManifestUrls urls)
        {
            _baseUrl = baseUrl?.TrimEnd('/') ?? "";
            _urls = urls ?? new SupabaseManifestUrls();
            _http = new HttpClient(new AppHttpCaptureHandler(new HttpClientHandler(), "SupabaseManifestService")) { Timeout = TimeSpan.FromSeconds(15) };
        }

        private string ResolveUrl(string relativePath)
        {
            if (string.IsNullOrWhiteSpace(relativePath)) return null;
            if (relativePath.StartsWith("http")) return relativePath;
            return $"{_baseUrl}/{relativePath.TrimStart('/')}";
        }

        private async Task<T> FetchAsync<T>(string relativePath, CancellationToken ct = default) where T : new()
        {
            var url = ResolveUrl(relativePath);
            if (url == null) return new T();
            try
            {
                var json = await _http.GetStringAsync(url, ct);
                return JsonSerializer.Deserialize<T>(json, JsonOpts) ?? new T();
            }
            catch (Exception ex)
            {
                AppLogger.Warning($"SupabaseManifestService: fetch {relativePath} failed: {ex.Message}");
                return new T();
            }
        }

        public async Task<VersionLatest> FetchVersionLatestAsync(CancellationToken ct = default)
        {
            return await FetchAsync<VersionLatest>(_urls.VersionLatest, ct);
        }

        public async Task<HashManifest> FetchHashManifestAsync(CancellationToken ct = default)
        {
            return await FetchAsync<HashManifest>(_urls.HashManifest, ct);
        }

        public async Task<SelectorUpdateManifest> FetchSelectorUpdateManifestAsync(CancellationToken ct = default)
        {
            return await FetchAsync<SelectorUpdateManifest>(_urls.SelectorUpdateManifest, ct);
        }

        public async Task<TierDefinitions> FetchTierDefinitionsAsync(CancellationToken ct = default)
        {
            var url = ResolveUrl(_urls.TierDefinitions);
            if (url == null) return new TierDefinitions();
            try
            {
                var json = await _http.GetStringAsync(url, ct);
                return TierDefinitions.FromJson(json);
            }
            catch (Exception ex)
            {
                AppLogger.Warning($"SupabaseManifestService: fetch tier-definitions failed: {ex.Message}");
                return new TierDefinitions();
            }
        }

        public async Task<string> FetchStringAsync(string relativePath, CancellationToken ct = default)
        {
            var url = ResolveUrl(relativePath);
            if (url == null) return null;
            try
            {
                return await _http.GetStringAsync(url, ct);
            }
            catch (Exception ex)
            {
                AppLogger.Warning($"SupabaseManifestService: fetch string {relativePath} failed: {ex.Message}");
                return null;
            }
        }

        public async Task<byte[]> FetchBytesAsync(string relativePath, CancellationToken ct = default)
        {
            var url = ResolveUrl(relativePath);
            if (url == null) return null;
            try
            {
                return await _http.GetByteArrayAsync(url, ct);
            }
            catch (Exception ex)
            {
                AppLogger.Warning($"SupabaseManifestService: fetch bytes {relativePath} failed: {ex.Message}");
                return null;
            }
        }
    }
}

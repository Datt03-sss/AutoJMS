using AutoJMS.FullStack.Models;
using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using AutoJMS.Diagnostics.AppCapture;

namespace AutoJMS.FullStack.Services
{
    public sealed class FullStackPodTrackingApiClient : IFullStackPodTrackingApiClient
    {
        private const string Endpoint =
            "https://jmsgw.jtexpress.vn/servicequality/thirdService/ops/podTrackingList?isAllPod=2&waybillNos=";

        public const string FullStackUserAgent =
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36";

        private readonly HttpClient _httpClient;

        public FullStackPodTrackingApiClient()
            : this(CreateHttpClient())
        {
        }

        public FullStackPodTrackingApiClient(HttpClient httpClient)
        {
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        }

        public async Task<PodTrackingRawResponse> GetRawJourneyJsonAsync(
            string waybillNo,
            string authToken,
            CancellationToken cancellationToken)
        {
            waybillNo = NormalizeWaybill(waybillNo);
            if (string.IsNullOrWhiteSpace(waybillNo))
                return new PodTrackingRawResponse();
            if (string.IsNullOrWhiteSpace(authToken))
                throw new FullStackPodTrackingException("Missing JMS authToken.");

            using var request = new HttpRequestMessage(HttpMethod.Get, Endpoint + Uri.EscapeDataString(waybillNo));
            ApplyHeaders(request, authToken);

            using var response = await _httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken).ConfigureAwait(false);

            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            if (JmsResponseClassifier.IsAuthExpired((int)response.StatusCode, body))
            {
                throw new FullStackPodTrackingException("JMS authToken expired.", authExpired: true);
            }

            if (!response.IsSuccessStatusCode)
                throw new FullStackPodTrackingException($"JMS podTrackingList HTTP {(int)response.StatusCode}.");

            return new PodTrackingRawResponse
            {
                WaybillNo = waybillNo,
                RawJson = body ?? string.Empty,
                HttpStatusCode = (int)response.StatusCode,
                FetchedAt = DateTime.Now
            };
        }

        private static HttpClient CreateHttpClient()
        {
            var handler = new HttpClientHandler
            {
                AutomaticDecompression = DecompressionMethods.GZip
                    | DecompressionMethods.Deflate
                    | DecompressionMethods.Brotli
            };

            var captureHandler = new AppHttpCaptureHandler(handler, "FullStackPodTrackingApiClient");
            return new HttpClient(captureHandler)
            {
                Timeout = TimeSpan.FromSeconds(18)
            };
        }

        private static void ApplyHeaders(HttpRequestMessage request, string authToken)
        {
            request.Headers.TryAddWithoutValidation("Accept", "application/json, text/plain, */*");
            request.Headers.TryAddWithoutValidation("Content-Type", "application/json;charset=utf-8");
            request.Headers.TryAddWithoutValidation("Origin", "https://jms.jtexpress.vn");
            request.Headers.TryAddWithoutValidation("Referer", "https://jms.jtexpress.vn/");
            request.Headers.TryAddWithoutValidation("authToken", authToken);
            request.Headers.TryAddWithoutValidation("lang", "VN");
            request.Headers.TryAddWithoutValidation("langType", "VN");
            request.Headers.TryAddWithoutValidation("routeName", "integratedComprehensive");
            request.Headers.TryAddWithoutValidation("timezone", "GMT+0700");
            request.Headers.TryAddWithoutValidation("User-Agent", FullStackUserAgent);
        }

        private static string NormalizeWaybill(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim().ToUpperInvariant();
        }
    }

    public sealed class FullStackPodTrackingException : Exception
    {
        public bool AuthExpired { get; }

        public FullStackPodTrackingException(string message, bool authExpired = false) : base(message)
        {
            AuthExpired = authExpired;
        }

        public FullStackPodTrackingException(string message, Exception innerException, bool authExpired = false)
            : base(message, innerException)
        {
            AuthExpired = authExpired;
        }
    }
}

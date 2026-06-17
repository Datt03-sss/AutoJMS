#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace AutoJMS.Diagnostics.AppCapture
{
    public sealed class AppHttpCaptureHandler : DelegatingHandler
    {
        private readonly string _serviceName;

        public AppHttpCaptureHandler(HttpMessageHandler innerHandler, string serviceName)
            : base(innerHandler)
        {
            _serviceName = string.IsNullOrWhiteSpace(serviceName) ? "HttpClient" : serviceName;
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var capture = AppCaptureManager.Instance;
            if (!capture.IsEnabled || !capture.Options.CaptureAppHttpClient)
                return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);

            string correlationId = $"app-http-{Guid.NewGuid():N}";
            var watch = Stopwatch.StartNew();
            string requestBody = "";
            string requestBodyFile = "";
            try
            {
                if (request.Content != null)
                {
                    requestBody = await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                    request.Content = CloneStringContent(request.Content, requestBody);
                    if (requestBody.Length > capture.Options.MaxRequestBodyBytes)
                    {
                        requestBodyFile = capture.SaveBody("req", ".json", requestBody);
                        requestBody = requestBody.Substring(0, capture.Options.MaxRequestBodyBytes) + "...[truncated]";
                    }
                }

                capture.RecordEvent(new AppCaptureEvent
                {
                    Category = "app.http",
                    Source = _serviceName,
                    EventName = "Request",
                    CorrelationId = correlationId,
                    Route = request.RequestUri?.ToString() ?? "",
                    Data = new Dictionary<string, object?>
                    {
                        ["method"] = request.Method.Method,
                        ["url"] = AppCaptureRedactor.RedactText(request.RequestUri?.ToString() ?? ""),
                        ["headers"] = ReadHeaders(request),
                        ["requestBody"] = AppCaptureRedactor.RedactText(requestBody),
                        ["requestBodyFile"] = requestBodyFile
                    }
                });

                var response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
                watch.Stop();

                string responseBody = "";
                string responseBodyFile = "";
                if (response.Content != null && IsTextual(response.Content.Headers.ContentType?.MediaType))
                {
                    responseBody = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                    response.Content = CloneStringContent(response.Content, responseBody);
                    if (responseBody.Length > capture.Options.MaxResponseBodyBytes)
                    {
                        responseBodyFile = capture.SaveBody("res", ".json", responseBody);
                        responseBody = responseBody.Substring(0, capture.Options.MaxResponseBodyBytes) + "...[truncated]";
                    }
                }

                capture.RecordEvent(new AppCaptureEvent
                {
                    Category = "app.http",
                    Source = _serviceName,
                    EventName = "Response",
                    CorrelationId = correlationId,
                    Route = request.RequestUri?.ToString() ?? "",
                    DurationMs = watch.ElapsedMilliseconds,
                    Data = new Dictionary<string, object?>
                    {
                        ["method"] = request.Method.Method,
                        ["url"] = AppCaptureRedactor.RedactText(request.RequestUri?.ToString() ?? ""),
                        ["statusCode"] = (int)response.StatusCode,
                        ["reasonPhrase"] = response.ReasonPhrase,
                        ["headers"] = ReadHeaders(response),
                        ["responseBody"] = AppCaptureRedactor.RedactText(responseBody),
                        ["responseBodyFile"] = responseBodyFile,
                        ["durationMs"] = watch.ElapsedMilliseconds
                    }
                });

                if (watch.ElapsedMilliseconds >= capture.Options.SlowApiThresholdMs)
                    capture.RecordPerformance("app.http", request.RequestUri?.AbsolutePath ?? request.RequestUri?.ToString() ?? "", watch.ElapsedMilliseconds, new { _serviceName, status = (int)response.StatusCode });

                return response;
            }
            catch (Exception ex)
            {
                watch.Stop();
                capture.RecordError(ex, $"app.http.{_serviceName}", new
                {
                    url = AppCaptureRedactor.RedactText(request.RequestUri?.ToString() ?? ""),
                    method = request.Method.Method,
                    durationMs = watch.ElapsedMilliseconds
                });
                throw;
            }
        }

        private static HttpContent CloneStringContent(HttpContent original, string body)
        {
            var content = new StringContent(body ?? "", Encoding.UTF8, original.Headers.ContentType?.MediaType ?? "application/json");
            foreach (var header in original.Headers)
            {
                if (string.Equals(header.Key, "Content-Type", StringComparison.OrdinalIgnoreCase)) continue;
                content.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }
            return content;
        }

        private static Dictionary<string, string> ReadHeaders(HttpRequestMessage request)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var header in request.Headers)
                result[header.Key] = string.Join(",", header.Value);
            if (request.Content != null)
            {
                foreach (var header in request.Content.Headers)
                    result[header.Key] = string.Join(",", header.Value);
            }
            return AppCaptureRedactor.RedactHeaders(result);
        }

        private static Dictionary<string, string> ReadHeaders(HttpResponseMessage response)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var header in response.Headers)
                result[header.Key] = string.Join(",", header.Value);
            if (response.Content != null)
            {
                foreach (var header in response.Content.Headers)
                    result[header.Key] = string.Join(",", header.Value);
            }
            return AppCaptureRedactor.RedactHeaders(result);
        }

        private static bool IsTextual(string? mediaType)
        {
            if (string.IsNullOrWhiteSpace(mediaType)) return true;
            return mediaType.Contains("json", StringComparison.OrdinalIgnoreCase)
                || mediaType.Contains("text", StringComparison.OrdinalIgnoreCase)
                || mediaType.Contains("xml", StringComparison.OrdinalIgnoreCase)
                || mediaType.Contains("javascript", StringComparison.OrdinalIgnoreCase);
        }
    }
}


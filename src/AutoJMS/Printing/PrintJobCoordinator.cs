using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace AutoJMS;

public sealed class PrintJobCoordinator
{
    private readonly IJmsApiClient _jmsApiClient;
    private readonly IPrinterSpoolerSubmitter _printerSpoolerSubmitter;
    private readonly IPrintService _printService;
    private readonly SemaphoreSlim _semaphore = new(1, 1);
    private readonly Dictionary<string, PrintJobCacheEntry> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _cacheLock = new();

    public PrintJobCoordinator(
        IJmsApiClient jmsApiClient,
        IPrinterSpoolerSubmitter printerSpoolerSubmitter,
        IPrintService printService)
    {
        _jmsApiClient = jmsApiClient ?? throw new ArgumentNullException(nameof(jmsApiClient));
        _printerSpoolerSubmitter = printerSpoolerSubmitter ?? throw new ArgumentNullException(nameof(printerSpoolerSubmitter));
        _printService = printService ?? throw new ArgumentNullException(nameof(printService));
    }

    public async Task<PrintSubmitResult> PrintAsync(PrintJobRequest request, CancellationToken ct = default)
    {
        if (request == null) throw new ArgumentNullException(nameof(request));

        if (!await _semaphore.WaitAsync(0, ct).ConfigureAwait(false))
        {
            AppLogger.Warning("DUPLICATE_PRINT_REQUEST_IGNORED");
            return new PrintSubmitResult
            {
                CompletedBySpooler = false,
                Reason = "DUPLICATE_PRINT_REQUEST_IGNORED"
            };
        }

        try
        {
            // 1. Coordinate validation
            bool isValid = await _printService.ValidateSelectedBeforePrintAsync(request.Waybills, request.CurrentInputText)
                .ConfigureAwait(false);
            if (!isValid)
            {
                return new PrintSubmitResult
                {
                    CompletedBySpooler = false,
                    Reason = "Validation failed"
                };
            }

            // 2. API call
            var payload = new Dictionary<string, object>
            {
                { "waybillIds", request.Waybills },
                { "applyTypeCode", request.ApplyTypeCode },
                { "printType", request.PrintType },
                { "pringType", request.PrintType },
                { "countryId", "1" }
            };
            string jsonPayload = System.Text.Json.JsonSerializer.Serialize(payload);

            string pdfUrl;
            using (var response = await _jmsApiClient.PostJsonAsync(request.ApiUrl, jsonPayload, ct: ct).ConfigureAwait(false))
            {
                if (response == null || !response.IsSuccessStatusCode)
                {
                    return new PrintSubmitResult
                    {
                        CompletedBySpooler = false,
                        Reason = "API call failed"
                    };
                }

                string rawJson = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                using (var doc = System.Text.Json.JsonDocument.Parse(rawJson))
                {
                    var root = doc.RootElement;
                    if (root.TryGetProperty("code", out var codeElement))
                    {
                        string codeVal = codeElement.ToString();
                        if (codeVal == "200" || codeVal == "0" || codeVal == "1")
                        {
                            pdfUrl = null;
                            if (root.TryGetProperty("data", out var data))
                            {
                                if (data.ValueKind == System.Text.Json.JsonValueKind.String)
                                {
                                    pdfUrl = data.GetString();
                                }
                                else if (data.ValueKind == System.Text.Json.JsonValueKind.Array && data.GetArrayLength() > 0)
                                {
                                    pdfUrl = data[0].GetString();
                                }
                            }
                        }
                        else
                        {
                            return new PrintSubmitResult
                            {
                                CompletedBySpooler = false,
                                Reason = $"API returned error code: {codeVal}"
                            };
                        }
                    }
                    else
                    {
                        return new PrintSubmitResult
                        {
                            CompletedBySpooler = false,
                            Reason = "Invalid API response structure"
                        };
                    }
                }
            }

            if (string.IsNullOrEmpty(pdfUrl))
            {
                return new PrintSubmitResult
                {
                    CompletedBySpooler = false,
                    Reason = "No PDF URL found in response"
                };
            }

            // 3. Cache check (keep/reuse local cache for 60s)
            PrintJobCacheEntry entry;
            lock (_cacheLock)
            {
                if (_cache.TryGetValue(pdfUrl, out var existing) && !existing.IsExpired)
                {
                    entry = existing;
                }
                else
                {
                    entry = null;
                }
            }

            if (entry == null)
            {
                // Download PDF bytes
                byte[] pdfBytes = await _jmsApiClient.GetByteArrayAsync(pdfUrl, ct).ConfigureAwait(false);
                entry = new PrintJobCacheEntry
                {
                    CacheKey = pdfUrl,
                    WaybillNo = request.Waybills.Count > 0 ? request.Waybills[0] : "",
                    PdfBytes = pdfBytes,
                    CreatedAt = DateTime.Now,
                    ExpiresAt = DateTime.Now.AddSeconds(60)
                };

                lock (_cacheLock)
                {
                    _cache[pdfUrl] = entry;
                }
            }

            // 4. Spooler submit
            var firstWaybill = request.Waybills.Count > 0 ? request.Waybills[0] : "";
            var printResult = await _printerSpoolerSubmitter.SubmitPrintAsync(entry, firstWaybill).ConfigureAwait(false);

            // 5. Selection clearing on success spooler submission
            if (printResult != null && printResult.CompletedBySpooler)
            {
                _printService.SelectAll(false);
                _printService.ClearSelection();
            }

            return printResult;
        }
        finally
        {
            _semaphore.Release();
        }
    }
}

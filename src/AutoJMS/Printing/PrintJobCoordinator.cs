using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text.Json;
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
            AppLogger.Info("PRINT_JOB_START");

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

            // 2. API call (Enforce exactly one API request to JMS per print)
            var payload = new Dictionary<string, object>
            {
                { "waybillIds", request.Waybills },
                { "applyTypeCode", request.ApplyTypeCode },
                { "printType", request.PrintType },
                { "pringType", request.PrintType },
                { "countryId", "1" }
            };
            string jsonPayload = System.Text.Json.JsonSerializer.Serialize(payload);

            string rawJson;
            var apiWatch = Stopwatch.StartNew();
            AppLogger.Info("PRINT_STAGE_BEGIN stage=PrintWaybillApi");
            AppLogger.Info("PRINT_API_REQUEST_START");
            try
            {
                using var response = await _jmsApiClient.PostJsonAsync(request.ApiUrl, jsonPayload, ct: ct).ConfigureAwait(false);
                if (response == null)
                {
                    apiWatch.Stop();
                    AppLogger.Warning($"PRINT_API_RESPONSE http=0 bodyLength=0 success=False error=null-response");
                    AppLogger.Info($"PRINT_STAGE_END stage=PrintWaybillApi elapsedMs={apiWatch.ElapsedMilliseconds}");
                    return new PrintSubmitResult
                    {
                        CompletedBySpooler = false,
                        Reason = "API failed"
                    };
                }

                rawJson = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                AppLogger.Info($"PRINT_API_RESPONSE http={(int)response.StatusCode} bodyLength={rawJson?.Length ?? 0} success={response.IsSuccessStatusCode}");
                apiWatch.Stop();
                AppLogger.Info($"PRINT_STAGE_END stage=PrintWaybillApi elapsedMs={apiWatch.ElapsedMilliseconds}");
                if (!response.IsSuccessStatusCode)
                {
                    return new PrintSubmitResult
                    {
                        CompletedBySpooler = false,
                        Reason = "API failed"
                    };
                }
            }
            catch (Exception ex)
            {
                apiWatch.Stop();
                AppLogger.Warning($"PRINT_API_RESPONSE http=0 bodyLength=0 success=False error={ex.Message}");
                AppLogger.Error($"PRINT_STAGE_FAILED stage=PrintWaybillApi elapsedMs={apiWatch.ElapsedMilliseconds}", ex);
                return new PrintSubmitResult
                {
                    CompletedBySpooler = false,
                    Reason = "API failed"
                };
            }

            string pdfUrl = null;
            var parseWatch = Stopwatch.StartNew();
            AppLogger.Info("PRINT_STAGE_BEGIN stage=ParseResponse");
            try
            {
                using var doc = JsonDocument.Parse(rawJson);
                var root = doc.RootElement;
                if (root.TryGetProperty("code", out var codeElement))
                {
                    string codeVal = codeElement.ToString();
                    if (codeVal == "200" || codeVal == "0" || codeVal == "1")
                    {
                        if (root.TryGetProperty("data", out var data))
                        {
                            if (data.ValueKind == JsonValueKind.String)
                            {
                                pdfUrl = data.GetString();
                            }
                            else if (data.ValueKind == JsonValueKind.Array && data.GetArrayLength() > 0)
                            {
                                pdfUrl = data[0].GetString();
                            }
                        }
                    }
                    else
                    {
                        parseWatch.Stop();
                        AppLogger.Info($"PRINT_API_PARSE_OK code={codeVal} dataCount=0");
                        AppLogger.Info($"PRINT_STAGE_END stage=ParseResponse elapsedMs={parseWatch.ElapsedMilliseconds}");
                        return new PrintSubmitResult
                        {
                            CompletedBySpooler = false,
                            Reason = "API failed"
                        };
                    }
                }
                else
                {
                    throw new JsonException("JMS response is missing code.");
                }

                parseWatch.Stop();
                AppLogger.Info($"PRINT_API_PARSE_OK dataCount={(string.IsNullOrWhiteSpace(pdfUrl) ? 0 : 1)}");
                AppLogger.Info($"PRINT_STAGE_END stage=ParseResponse elapsedMs={parseWatch.ElapsedMilliseconds}");
            }
            catch (JsonException ex)
            {
                parseWatch.Stop();
                AppLogger.Error($"PRINT_STAGE_FAILED stage=ParseResponse elapsedMs={parseWatch.ElapsedMilliseconds}", ex);
                return new PrintSubmitResult
                {
                    CompletedBySpooler = false,
                    Reason = "JSON parse failed"
                };
            }
            catch (Exception ex)
            {
                parseWatch.Stop();
                AppLogger.Error($"PRINT_STAGE_FAILED stage=ParseResponse elapsedMs={parseWatch.ElapsedMilliseconds}", ex);
                return new PrintSubmitResult
                {
                    CompletedBySpooler = false,
                    Reason = "Unknown exception"
                };
            }

            var resolveWatch = Stopwatch.StartNew();
            AppLogger.Info("PRINT_STAGE_BEGIN stage=ResolvePdfUrl");
            if (string.IsNullOrEmpty(pdfUrl))
            {
                resolveWatch.Stop();
                AppLogger.Warning($"PRINT_STAGE_FAILED stage=ResolvePdfUrl elapsedMs={resolveWatch.ElapsedMilliseconds} reason=no-pdf-url");
                return new PrintSubmitResult
                {
                    CompletedBySpooler = false,
                    Reason = "PDF download failed"
                };
            }
            resolveWatch.Stop();
            AppLogger.Info($"PRINT_STAGE_END stage=ResolvePdfUrl elapsedMs={resolveWatch.ElapsedMilliseconds}");

            // 3. Cache check (keep/reuse local cache for 60s, but still hit the API once)
            var firstWaybill = request.Waybills.Count > 0 ? request.Waybills[0] : "";
            PrintJobCacheEntry entry = null;
            bool cacheHit = false;
            var downloadWatch = Stopwatch.StartNew();
            AppLogger.Info("PRINT_STAGE_BEGIN stage=DownloadOrCachePdf");
            try
            {
                lock (_cacheLock)
                {
                    if (_cache.TryGetValue(pdfUrl, out var existing) && !existing.IsExpired)
                    {
                        entry = existing;
                        cacheHit = true;
                    }
                }

                if (entry != null)
                {
                    AppLogger.Info("PRINT_CACHE_HIT");
                }
                else
                {
                    byte[] pdfBytes = await _jmsApiClient.GetByteArrayAsync(pdfUrl, ct).ConfigureAwait(false);
                    if (pdfBytes == null || pdfBytes.Length == 0)
                        throw new InvalidOperationException("Downloaded PDF bytes are empty.");

                    entry = new PrintJobCacheEntry
                    {
                        CacheKey = pdfUrl,
                        WaybillNo = firstWaybill,
                        PdfBytes = pdfBytes,
                        CreatedAt = DateTime.Now,
                        ExpiresAt = DateTime.Now.AddSeconds(60)
                    };

                    lock (_cacheLock)
                    {
                        _cache[pdfUrl] = entry;
                    }
                }

                downloadWatch.Stop();
                AppLogger.Info($"PDF_READY waybill={firstWaybill} cacheHit={cacheHit} bytes={entry.PdfBytes?.Length ?? 0}");
                AppLogger.Info($"PRINT_STAGE_END stage=DownloadOrCachePdf elapsedMs={downloadWatch.ElapsedMilliseconds}");
            }
            catch (Exception ex)
            {
                downloadWatch.Stop();
                AppLogger.Error($"PRINT_STAGE_FAILED stage=DownloadOrCachePdf elapsedMs={downloadWatch.ElapsedMilliseconds}", ex);
                return new PrintSubmitResult
                {
                    CompletedBySpooler = false,
                    Reason = "PDF download failed"
                };
            }

            // 4. Spooler submit
            PrintSubmitResult printResult;
            var submitWatch = Stopwatch.StartNew();
            AppLogger.Info("PRINT_STAGE_BEGIN stage=SubmitPrintJob");
            AppLogger.Info($"PRINT_SUBMIT_START waybill={firstWaybill}");
            try
            {
                printResult = await _printerSpoolerSubmitter.SubmitPrintAsync(entry, firstWaybill).ConfigureAwait(false);
                submitWatch.Stop();
                AppLogger.Info($"PRINT_STAGE_END stage=SubmitPrintJob elapsedMs={submitWatch.ElapsedMilliseconds}");
            }
            catch (Exception ex)
            {
                submitWatch.Stop();
                AppLogger.Warning($"PRINT_SUBMIT_FAILED waybill={firstWaybill} stage=SubmitPrintJob reason=Printer submit failed");
                AppLogger.Error($"PRINT_STAGE_FAILED stage=SubmitPrintJob elapsedMs={submitWatch.ElapsedMilliseconds}", ex);
                return new PrintSubmitResult
                {
                    CompletedBySpooler = false,
                    Reason = "Printer submit failed"
                };
            }

            if (printResult == null || !printResult.CompletedBySpooler)
            {
                AppLogger.Warning($"PRINT_SUBMIT_FAILED waybill={firstWaybill} stage=WaitSpoolerAccepted reason={printResult?.Reason ?? "spooler-not-accepted"}");
                return new PrintSubmitResult
                {
                    CompletedBySpooler = false,
                    Reason = printResult?.Reason ?? "Spooler rejected"
                };
            }

            AppLogger.Info($"PRINT_SUBMIT_SUCCESS waybill={firstWaybill} elapsedMs={printResult.ElapsedMs}");

            // 5. Selection clearing on success spooler submission
            var gridWatch = Stopwatch.StartNew();
            AppLogger.Info("PRINT_STAGE_BEGIN stage=GridDeselect");
            try
            {
                _printService.SelectAll(false);
                _printService.ClearSelection();
                gridWatch.Stop();
                AppLogger.Info($"GRID_DESELECT_DONE waybill={firstWaybill}");
                AppLogger.Info($"PRINT_STAGE_END stage=GridDeselect elapsedMs={gridWatch.ElapsedMilliseconds}");
            }
            catch (Exception ex)
            {
                gridWatch.Stop();
                AppLogger.Error($"PRINT_STAGE_FAILED stage=GridDeselect elapsedMs={gridWatch.ElapsedMilliseconds}", ex);
                return new PrintSubmitResult
                {
                    CompletedBySpooler = false,
                    Reason = "Unknown exception"
                };
            }

            return printResult;
        }
        finally
        {
            var finishWatch = Stopwatch.StartNew();
            AppLogger.Info("PRINT_STAGE_BEGIN stage=Finish");
            AppLogger.Info("PRINT_PIPELINE_FINISH source=PrintJobCoordinator");
            finishWatch.Stop();
            AppLogger.Info($"PRINT_STAGE_END stage=Finish elapsedMs={finishWatch.ElapsedMilliseconds}");
            _semaphore.Release();
        }
    }
}

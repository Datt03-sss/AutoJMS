using System;
using System.Collections.Generic;
using System.Drawing.Printing;
using System.Linq;
using System.Management;
using System.Threading;
using System.Threading.Tasks;

namespace AutoJMS;

public interface IPrinterPreflightService
{
    Task<PrinterPreflightResult> CheckAsync(
        string printerName,
        CancellationToken cancellationToken);
}

public interface IPrinterMaintenanceService
{
    Task<PrinterMaintenanceResult> ClearStuckJobsAsync(
        string printerName,
        CancellationToken cancellationToken);

    Task<PrinterPreflightResult> RefreshStatusAsync(
        string printerName,
        CancellationToken cancellationToken);
}

public sealed class PrinterPreflightResult
{
    public bool CanPrint { get; init; }
    public string PrinterName { get; init; } = "";
    public string StatusText { get; init; } = "";
    public string ReasonCode { get; init; } = "";
    public int QueueJobCount { get; init; }
    public int ErrorJobCount { get; init; }
    public bool IsOffline { get; init; }
    public bool IsPaused { get; init; }
    public bool HasError { get; init; }
    public bool IsPaperOut { get; init; }
    public bool QueueBusy { get; init; }
}

public sealed class PrinterMaintenanceResult
{
    public bool Success { get; init; }
    public string PrinterName { get; init; } = "";
    public int ClearedJobCount { get; init; }
    public int FailedJobCount { get; init; }
    public string Message { get; init; } = "";
}

public sealed class PrinterPreflightService : IPrinterPreflightService
{
    private readonly Func<AppSettings> _settingsProvider;
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(2);

    public PrinterPreflightService(Func<AppSettings> settingsProvider)
    {
        _settingsProvider = settingsProvider ?? (() => new AppSettings());
    }

    public async Task<PrinterPreflightResult> CheckAsync(
        string printerName,
        CancellationToken cancellationToken)
    {
        try
        {
            var checkTask = Task.Run(() => CheckCore(printerName, cancellationToken), CancellationToken.None);
            var timeoutTask = Task.Delay(DefaultTimeout, cancellationToken);
            var completed = await Task.WhenAny(checkTask, timeoutTask).ConfigureAwait(false);
            if (completed != checkTask)
                return Block(printerName, "PRINTER_CHECK_TIMEOUT", "Kiểm tra máy in quá thời gian.");

            return await checkTask.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return Block(printerName, "PRINTER_CHECK_TIMEOUT", "Kiểm tra máy in quá thời gian.");
        }
        catch (Exception ex)
        {
            return new PrinterPreflightResult { CanPrint = true, PrinterName = printerName, ReasonCode = "PRINTER_ERROR_IGNORED", StatusText = $"Không kiểm tra được máy in: {ex.Message} (Bypass)" };
        }
    }

    private PrinterPreflightResult CheckCore(string printerName, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();

        string resolvedPrinter = ResolvePrinterName(printerName);
        if (string.IsNullOrWhiteSpace(resolvedPrinter))
            return Block(resolvedPrinter, "PRINTER_NOT_FOUND", "Không tìm thấy máy in.");

        if (!PrinterExists(resolvedPrinter))
            return Block(resolvedPrinter, "PRINTER_NOT_FOUND", "Không tìm thấy máy in.");

        var settings = _settingsProvider() ?? new AppSettings();
        var printerStatus = TryReadPrinterStatus(resolvedPrinter);
        if (!TryReadQueueJobs(resolvedPrinter, out var jobs, out var queueError))
            return Build(true, resolvedPrinter, "PRINTER_ERROR_IGNORED", queueError, 0, 0, false, false, true, false, false);

        bool isOffline = printerStatus.IsOffline;
        bool isPaused = printerStatus.IsPaused;
        bool isPaperOut = printerStatus.IsPaperOut;
        bool hasError = printerStatus.HasError;
        int errorJobCount = jobs.Count(IsQueueJobError);
        int queueJobCount = jobs.Count;
        bool queueBusy = false;

        string statusText = BuildStatusText(printerStatus.StatusText, queueJobCount, errorJobCount);

        if (settings.BlockWhenPrinterOffline && isOffline)
            return Build(false, resolvedPrinter, "PRINTER_OFFLINE", statusText, queueJobCount, errorJobCount, isOffline, isPaused, hasError, isPaperOut, queueBusy);

        if (settings.BlockWhenPrinterPaused && isPaused)
            return Build(false, resolvedPrinter, "PRINTER_PAUSED", statusText, queueJobCount, errorJobCount, isOffline, isPaused, hasError, isPaperOut, queueBusy);

        if (isPaperOut)
            return Build(false, resolvedPrinter, "PRINTER_PAPER_OUT", statusText, queueJobCount, errorJobCount, isOffline, isPaused, hasError, isPaperOut, queueBusy);

        // if (hasError)
        //     return Build(false, resolvedPrinter, "PRINTER_ERROR", statusText, queueJobCount, errorJobCount, isOffline, isPaused, hasError, isPaperOut, queueBusy);

        if (settings.BlockWhenQueueHasErrorJob && errorJobCount > 0)
            return Build(false, resolvedPrinter, "PRINTER_QUEUE_HAS_ERROR_JOB", statusText, queueJobCount, errorJobCount, isOffline, isPaused, hasError, isPaperOut, queueBusy);

        return Build(true, resolvedPrinter, "PRINTER_OK", statusText, queueJobCount, errorJobCount, isOffline, isPaused, hasError, isPaperOut, queueBusy);
    }

    private static PrinterPreflightResult Build(
        bool canPrint,
        string printerName,
        string reasonCode,
        string statusText,
        int queueJobCount,
        int errorJobCount,
        bool isOffline,
        bool isPaused,
        bool hasError,
        bool isPaperOut,
        bool queueBusy)
    {
        return new PrinterPreflightResult
        {
            CanPrint = canPrint,
            PrinterName = printerName ?? "",
            StatusText = statusText ?? "",
            ReasonCode = reasonCode,
            QueueJobCount = queueJobCount,
            ErrorJobCount = errorJobCount,
            IsOffline = isOffline,
            IsPaused = isPaused,
            HasError = hasError,
            IsPaperOut = isPaperOut,
            QueueBusy = queueBusy
        };
    }

    private static PrinterPreflightResult Block(string printerName, string reasonCode, string statusText)
        => Build(false, printerName, reasonCode, statusText, 0, 0, false, false, true, false, false);

    private static string ResolvePrinterName(string printerName)
    {
        if (!string.IsNullOrWhiteSpace(printerName) && printerName.Trim() != "-1")
            return printerName.Trim();

        try
        {
            using var document = new PrintDocument();
            return document.PrinterSettings.PrinterName ?? "";
        }
        catch
        {
            return "";
        }
    }

    private static bool PrinterExists(string printerName)
    {
        foreach (string installed in PrinterSettings.InstalledPrinters)
        {
            if (string.Equals(installed, printerName, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static PrinterStatusInfo TryReadPrinterStatus(string printerName)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_Printer");
            foreach (ManagementObject printer in searcher.Get())
            {
                string name = printer["Name"]?.ToString() ?? "";
                if (!string.Equals(name, printerName, StringComparison.OrdinalIgnoreCase))
                    continue;

                string status = printer["Status"]?.ToString() ?? "";
                string detected = printer["DetectedErrorState"]?.ToString() ?? "";
                string printerStatus = printer["PrinterStatus"]?.ToString() ?? "";
                string extended = printer["ExtendedPrinterStatus"]?.ToString() ?? "";
                bool workOffline = TryGetBool(printer["WorkOffline"]);
                uint detectedState = TryGetUInt(printer["DetectedErrorState"]);
                uint statusCode = TryGetUInt(printer["PrinterStatus"]);
                string statusText = string.Join(" | ", new[] { status, $"PrinterStatus={printerStatus}", $"DetectedErrorState={detected}", $"Extended={extended}" }
                    .Where(x => !string.IsNullOrWhiteSpace(x)));
                string lower = statusText.ToLowerInvariant();

                bool isOffline = workOffline || statusCode == 7 || detectedState == 9 || lower.Contains("offline");
                bool isPaused = statusCode == 8 || lower.Contains("paused");
                bool isPaperOut = detectedState == 3 || detectedState == 4 || lower.Contains("paper");
                bool hasError = lower.Contains("error")
                    || lower.Contains("jam")
                    || lower.Contains("intervention")
                    || detectedState is 1 or 7 or 8 or 10 or 11;

                return new PrinterStatusInfo(statusText, isOffline, isPaused, hasError, isPaperOut);
            }
        }
        catch (Exception ex)
        {
            return new PrinterStatusInfo(ex.Message, false, false, true, false);
        }

        return new PrinterStatusInfo("Không đọc được trạng thái máy in.", false, false, false, false);
    }

    internal static List<PrinterQueueJob> ReadQueueJobs(string printerName)
    {
        if (TryReadQueueJobs(printerName, out var jobs, out _))
            return jobs;

        return new List<PrinterQueueJob>();
    }

    private static bool TryReadQueueJobs(string printerName, out List<PrinterQueueJob> jobs, out string error)
    {
        jobs = new List<PrinterQueueJob>();
        error = "";
        try
        {
            string normalized = (printerName ?? "").Trim();
            using var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_PrintJob");
            foreach (ManagementObject job in searcher.Get())
            {
                string name = job["Name"]?.ToString() ?? "";
                if (!string.IsNullOrWhiteSpace(normalized)
                    && !name.StartsWith(normalized + ",", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                jobs.Add(new PrinterQueueJob(
                    name,
                    job["Document"]?.ToString() ?? "",
                    job["JobStatus"]?.ToString() ?? "",
                    job["Status"]?.ToString() ?? "",
                    job));
            }

            return true;
        }
        catch (Exception ex)
        {
            error = $"Không đọc được hàng đợi máy in: {ex.Message}";
            AppLogger.Warning($"[PrinterPreflight] read queue failed printer={printerName} error={ex.Message}");
            return false;
        }
    }

    internal static bool IsQueueJobError(PrinterQueueJob job)
    {
        string text = ((job.JobStatus ?? "") + " " + (job.Status ?? "")).ToLowerInvariant();
        return text.Contains("error")
            || text.Contains("offline")
            || text.Contains("paper")
            || text.Contains("paused")
            || text.Contains("blocked")
            || text.Contains("intervention")
            || text.Contains("stalled")
            || text.Contains("deleting");
    }

    private static string BuildStatusText(string printerStatus, int queueJobCount, int errorJobCount)
        => $"{printerStatus}; queue={queueJobCount}; errorJobs={errorJobCount}";

    private static bool TryGetBool(object value)
        => value is bool b && b;

    private static uint TryGetUInt(object value)
    {
        try
        {
            if (value == null) return 0;
            return Convert.ToUInt32(value);
        }
        catch
        {
            return 0;
        }
    }

    private sealed record PrinterStatusInfo(
        string StatusText,
        bool IsOffline,
        bool IsPaused,
        bool HasError,
        bool IsPaperOut);
}

public sealed class PrinterMaintenanceService : IPrinterMaintenanceService
{
    private readonly IPrinterPreflightService _preflightService;

    public PrinterMaintenanceService(IPrinterPreflightService preflightService)
    {
        _preflightService = preflightService;
    }

    public async Task<PrinterMaintenanceResult> ClearStuckJobsAsync(
        string printerName,
        CancellationToken cancellationToken)
    {
        return await Task.Run(() =>
        {
            int cleared = 0;
            int failed = 0;
            var jobs = PrinterPreflightService.ReadQueueJobs(printerName);
            foreach (var job in jobs)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    job.ManagementObject.Delete();
                    cleared++;
                }
                catch (Exception ex)
                {
                    failed++;
                    AppLogger.Warning($"[PrinterMaintenance] delete job failed printer={printerName} job={job.Name} error={ex.Message}");
                }
            }

            return new PrinterMaintenanceResult
            {
                Success = failed == 0,
                PrinterName = printerName ?? "",
                ClearedJobCount = cleared,
                FailedJobCount = failed,
                Message = failed == 0
                    ? $"Đã xóa {cleared} job đang treo."
                    : $"Đã xóa {cleared} job, lỗi {failed} job."
            };
        }, cancellationToken).ConfigureAwait(false);
    }

    public Task<PrinterPreflightResult> RefreshStatusAsync(
        string printerName,
        CancellationToken cancellationToken)
        => _preflightService.CheckAsync(printerName, cancellationToken);
}

internal sealed record PrinterQueueJob(
    string Name,
    string Document,
    string JobStatus,
    string Status,
    ManagementObject ManagementObject);

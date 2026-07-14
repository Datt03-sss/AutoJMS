# Print Flow Refactoring Plan

## 1. Executive Summary
This report outlines the plan to refactor the printing flow in AutoJMS to introduce robust URL/payload-based caching, prevent concurrent print job spam using semaphore-based protection, validate print safety before execution, verify spooler submissions, and clear selections on both the Print and Dashboard grids upon success. 

Additionally, the integration plan details how to wire `PrintJobCoordinator` into `Main.cs` and correct the unit tests to align with the new caching behavior (bypassing the API request on a cache hit).

---

## 2. Caching Strategy
Currently, `PrintJobCoordinator` executes the JMS API request *before* checking the cache, resulting in redundant API calls. To achieve **exactly one API request to JMS** for duplicate print jobs within a **60s TTL**, the cache check must be executed at the entry of the print operation using a key based on the request URL and payload.

### Cache Entry Format (`PrintJobCacheEntry`)
The `PrintJobCacheEntry` class contains:
- `CacheKey` (`string`): Derived from the API URL and request payload.
- `WaybillNo` (`string`): The primary/first waybill in the list.
- `PdfBytes` (`byte[]`): The downloaded PDF binary data.
- `LocalPdfPath` (`string`): Path to the locally saved PDF (if written to disk).
- `CreatedAt` (`DateTime`): Timestamp when the PDF was fetched and cached.
- `ExpiresAt` (`DateTime`): Expiration timestamp (`CreatedAt.AddSeconds(60)`).
- `PdfHash` (`string`): Hash value of the PDF document.

### Cache Key Computation
The cache key must be computed deterministically based on:
1. The request endpoint URL (`request.ApiUrl`).
2. The serialized JSON payload sent to the JMS server, which contains:
   - `waybillIds` (`request.Waybills`)
   - `applyTypeCode` (`request.ApplyTypeCode`)
   - `printType` (`request.PrintType`)
   - `countryId` (`"1"`)

```csharp
var payload = new Dictionary<string, object>
{
    { "waybillIds", request.Waybills },
    { "applyTypeCode", request.ApplyTypeCode },
    { "printType", request.PrintType },
    { "pringType", request.PrintType }, // Keep original typo compatibility
    { "countryId", "1" }
};
string jsonPayload = System.Text.Json.JsonSerializer.Serialize(payload);
string cacheKey = $"{request.ApiUrl}|{jsonPayload}";
```

### Cache Lookup Flow
1. Check the local dictionary `_cache` using `cacheKey`.
2. If a non-expired entry exists (`!entry.IsExpired`), reuse its `PdfBytes` and log a cache hit.
3. If no entry exists (cache miss):
   - Log the start of the API request.
   - Send the POST request to the JMS API.
   - Parse `pdfUrl` from the response.
   - Fetch the raw PDF byte array using `GetByteArrayAsync`.
   - Store the new `PrintJobCacheEntry` in the dictionary with a 60-second TTL.

---

## 3. Concurrency Protection
To prevent duplicate print jobs from running concurrently and flooding the print spooler, a thread-safe semaphore is used.

- **Mechanism**: A class-level `SemaphoreSlim _semaphore = new(1, 1);` is used in `PrintJobCoordinator`.
- **Acquisition**: `PrintAsync` attempts to acquire the semaphore immediately with a timeout of 0 milliseconds:
  ```csharp
  if (!await _semaphore.WaitAsync(0, ct).ConfigureAwait(false))
  {
      AppLogger.Warning("DUPLICATE_PRINT_REQUEST_IGNORED");
      return new PrintSubmitResult
      {
          CompletedBySpooler = false,
          Reason = "DUPLICATE_PRINT_REQUEST_IGNORED"
      };
  }
  ```
- **Release**: The semaphore is released inside a `finally` block to ensure it is always freed, regardless of success, failure, or exceptions:
  ```csharp
  try
  {
      // Print flow logic...
  }
  finally
  {
      _semaphore.Release();
  }
  ```

---

## 4. Print Safety Validation
Before retrieving the PDF (from cache or API), the request must be validated to ensure it meets safety standards.
- **Service call**: Call `_printService.ValidateSelectedBeforePrintAsync(request.Waybills, request.CurrentInputText)`.
- **Validation steps**:
  1. Checks if the requested waybills are empty.
  2. Compares the selected waybills against the waybill codes entered in the input box. If they mismatch, validation fails.
  3. Verifies context (e.g. valid JMS token, site context).
  4. Runs safety validation on each waybill using `IPrintSafetyGuard.ValidateBeforePrint`.
- If safety validation returns `false`, `PrintAsync` aborts immediately and returns a failed status.

---

## 5. Spooler Submission Flow & Verification
The print spooler submission is extracted into a dedicated service implementing `IPrinterSpoolerSubmitter`:

1. **Preflight Check**: Call `IPrinterPreflightService.CheckAsync` to verify printer status (e.g., pause, offline, out-of-paper).
2. **Spooler Snapshots**: Retrieve current jobs from the queue to detect any stuck/error jobs. If a stuck job is found and the config dictates blocking, abort submission.
3. **Document Loading**: Load the PDF using `PdfDocument.Load(stream)` (from `PdfiumViewer`).
4. **Printer Configuration**: Bind the document to `PrintDocument`, set the printer name (resolving default if configured as `-1`), and apply paper settings (`ApplyPrintPaperSettings`).
5. **Print Command**: Issue `printDocument.Print()`.
6. **Verification**: Call `WaitForPrinterJobCompletedAsync` to monitor the Windows print spooler and verify that the document is successfully processed by the printer.
7. **Logging**:
   - Success: `AppLogger.Info($"PRINT_SPOOLER_SUBMIT_DONE: WaybillNo={firstWaybill}");`
   - Failure: `AppLogger.Warning($"PRINT_SPOOLER_FAILED: WaybillNo={firstWaybill}, Reason={reason}");`

---

## 6. Grid Selection Clearing
Upon successful spooler submission, selection state must be cleared on both the **Print Tab Grid** and the **Dashboard Grid**.

### Print Tab Grid Clearing (`PrintService.ClearSelection`)
To completely remove focus, selected cells, and row highlighting:
1. End grid edit mode (`_grid.EndEdit()`).
2. Deselect all rows and checkboxes via `SelectAll(false)`.
3. Clear selection and set `CurrentCell` to `null` to remove focus:
   ```csharp
   public void ClearSelection()
   {
       if (_grid.InvokeRequired)
       {
           _grid.Invoke(new Action(() => 
           {
               _grid.ClearSelection();
               _grid.CurrentCell = null;
           }));
       }
       else
       {
           _grid.ClearSelection();
           _grid.CurrentCell = null;
       }
       AppLogger.Info("PRINT_SELECTION_CLEAR_DONE");
       try { OnPrintSelectionCleared?.Invoke(); } catch { }
   }
   ```

### Dashboard Grid Clearing (`FullStackOperation.ClearDashGridSelection`)
In `FullStackOperation.cs`, update `ClearDashGridSelection` to also nullify `CurrentCell`:
```csharp
public void ClearDashGridSelection()
{
    if (tabDash_dataGridView == null) return;
    if (tabDash_dataGridView.InvokeRequired)
    {
        tabDash_dataGridView.Invoke(new Action(() => 
        {
            tabDash_dataGridView.ClearSelection();
            tabDash_dataGridView.CurrentCell = null;
        }));
    }
    else
    {
        tabDash_dataGridView.ClearSelection();
        tabDash_dataGridView.CurrentCell = null;
    }
}
```

### Integration Hook
Expose `event Action OnPrintSelectionCleared` on `IPrintService`. In `Main.cs`, subscribe to this event to trigger the clearing of the Dashboard grid:
```csharp
_printService.OnPrintSelectionCleared += () =>
{
    try { _fullStackForm?.ClearDashGridSelection(); } catch { }
};
```

---

## 7. Log Messages & Expected Formats

| Log Event Code | Trigger Location | Level | Expected Format |
|---|---|---|---|
| **`PRINT_JOB_START`** | `PrintJobCoordinator.PrintAsync` entry | `Info` | `PRINT_JOB_START: WaybillCount={Count}, PrimaryWaybill={Wb}, PrintType={Type}, ApplyTypeCode={Apply}` |
| **`DUPLICATE_PRINT_REQUEST_IGNORED`** | `PrintJobCoordinator.PrintAsync` semaphore fail | `Warning` | `DUPLICATE_PRINT_REQUEST_IGNORED` |
| **`PRINT_API_REQUEST_START`** | `PrintJobCoordinator.PrintAsync` cache miss | `Info` | `PRINT_API_REQUEST_START: Url={Url}` |
| **`PRINT_CACHE_HIT`** | `PrintJobCoordinator.PrintAsync` cache hit | `Info` | `PRINT_CACHE_HIT: CacheKey={Key}, WaybillNo={Wb}` |
| **`PRINT_SPOOLER_SUBMIT_DONE`** | `PrintJobCoordinator.PrintAsync` spooler success | `Info` | `PRINT_SPOOLER_SUBMIT_DONE: WaybillNo={Wb}` |
| **`PRINT_SPOOLER_FAILED`** | `PrintJobCoordinator.PrintAsync` spooler fail | `Warning` | `PRINT_SPOOLER_FAILED: WaybillNo={Wb}, Reason={Reason}` |
| **`PRINT_SELECTION_CLEAR_DONE`** | `PrintService.ClearSelection` execution | `Info` | `PRINT_SELECTION_CLEAR_DONE` |

---

## 8. Integration in Main.cs
The coordinator and its dependencies will be integrated as follows:

1. **Field Definitions**: Add fields to `Main.cs`:
   ```csharp
   private IPrinterSpoolerSubmitter _printerSpoolerSubmitter;
   private PrintJobCoordinator _printJobCoordinator;
   ```
2. **Initialization**: Instantiated in `Main.cs` after `_printService` setup:
   ```csharp
   _printerSpoolerSubmitter = new PrinterSpoolerSubmitter(() => _settings, _printerPreflightService);
   _printJobCoordinator = new PrintJobCoordinator(
       JmsApiClient.Instance,
       _printerSpoolerSubmitter,
       _printService);
   
   _printService.OnPrintSelectionCleared += () =>
   {
       try { _fullStackForm?.ClearDashGridSelection(); } catch { }
   };
   ```
3. **Execution refactoring (`ExecutePrintAsync`)**:
   Instead of manually driving the print process (ensure, download, print, clear), `Main.cs` delegates the process to `_printJobCoordinator`:
   ```csharp
   private async Task ExecutePrintAsync(bool isAutoMode)
   {
       if (_printService == null) return;
       
       var selected = _printService.GetSelectedWaybills();
       if (selected == null || selected.Count == 0)
       {
           if (!isAutoMode) ShowPrintMessage("Chưa chọn vận đơn nào!", true);
           return;
       }

       int printType = 1;
       int applyTypeCode = (_printService.CurrentMode == PrintMode.InChuyenTiep) ? 2 : 4;
       string apiUrl = AppConfig.Current.BuildJmsApiUrl("operatingplatform/rebackTransferExpress/printBase64");

       SetPrintButtonState(false);
       try
       {
           var request = new PrintJobRequest
           {
               Waybills = selected,
               CurrentInputText = tabPrint_inputWaybill.Text,
               PrintType = printType,
               ApplyTypeCode = applyTypeCode,
               ApiUrl = apiUrl
           };

           var result = await _printJobCoordinator.PrintAsync(request, _appCts.Token);
           
           if (result.CompletedBySpooler)
           {
               ShowPrintMessage("Đã in, đang cập nhật trạng thái sau in...", false, 2500);
               _printService.QueuePostPrintRefresh(selected, printType);
           }
           else
           {
               ShowPrintMessage($"In thất bại: {result.Reason}", true);
           }
       }
       catch (Exception ex)
       {
           ShowPrintMessage($"Lỗi hệ thống: {ex.Message}", true);
       }
       finally
       {
           SetPrintButtonState(true);
       }
   }
   ```

---

## 9. Unit Test Updates
The mock test assertions in `tests/AutoJMS.Tests/PrintJobCoordinatorTests.cs` must be updated to align with the new caching mechanism.

Specifically, in **`Case2_ReprintWithin60Seconds_ReusesCacheButHitsApi`**:
- Rename the test to: `Case2_ReprintWithin60Seconds_ReusesCacheAndBypassesApi`
- Change verification of the API request:
  ```csharp
  // API is only hit ONCE (second time is bypassed via cache)
  _mockJmsApiClient.Verify(x => x.PostJsonAsync(request.ApiUrl, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
  ```
- Change verification of PDF download:
  ```csharp
  // PDF download is only done ONCE (second time is cached)
  _mockJmsApiClient.Verify(x => x.GetByteArrayAsync("http://mockpdf/123456.pdf", It.IsAny<CancellationToken>()), Times.Once);
  ```
- Spooler submission must still occur twice:
  ```csharp
  // Spooler submission occurs twice
  _mockSpoolerSubmitter.Verify(x => x.SubmitPrintAsync(It.Is<PrintJobCacheEntry>(e => e.CacheKey.Contains(request.ApiUrl)), "123456"), Times.Exactly(2));
  ```

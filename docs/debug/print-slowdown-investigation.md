# Print Slowdown Investigation

Generated: 2026-06-09
Branch: `debug/print-slow-submit`
Scope: Diagnose first. Do not make broad behavior changes in this round.

## Objective

Find the code path that makes `Print totalMs` reach 20-63 seconds while the actual `printMs` is only about 200-700 ms.

## Hard Constraints

- Do not remove `PrintSafetyGuard`.
- Do not bypass `middleCode`.
- Do not use persistent cache to allow print.
- Do not log tokens, license keys, or full auth data.
- Do not run uninstall/installer destructive actions.
- Do not push git or commit unless explicitly requested.

## Allowed Changes In Later Rounds

- Add deeper instrumentation.
- Move post-print refresh work to background.
- Create a fast path for same-waybill reprint when a fresh verified context exists.
- Tune waiting strategy after the server has accepted a print request, but without issuing duplicate print requests.

## Current Print Flow

Search:

1. Normalize input waybill.
2. Fetch tracking via `keywordList`.
3. Fetch print approval info via `rebackTransferExpress/pringListPage`.
4. Run `PrintSafetyGuard`.
5. Store `PrintReadinessContext` with a 60 second TTL.

Print:

1. `PrintService.ValidateSelectedBeforePrintAsync`.
2. If readiness is fresh, skip tracking API preflight.
3. Build reprint plan.
4. Call `GetPdfUrlViaCSharpAsync`.
5. Call `DownloadPdfWithRetryAsync`.
6. Load PDF and submit to printer.
7. Queue post-print refresh in background.

Post print:

1. `QueuePostPrintRefresh` runs tracking + print info refresh in a background task.
2. UI update is marshaled back through `BeginInvoke`.

## Files Involved

- `src/AutoJMS/Printing/PrintService.cs`
  - `SearchAndLoadAsync`
  - `ValidateSelectedBeforePrintAsync`
  - `QueuePostPrintRefresh`
- `src/AutoJMS/Printing/PrintReadinessContext.cs`
- `src/AutoJMS/Printing/TabPrintReprintPolicy.cs`
- `src/AutoJMS/Forms/Main.cs`
  - `ExecutePrintAsync`
  - `GetPdfUrlViaCSharpAsync`
  - `DownloadPdfWithRetryAsync`
  - `SavePrintSuccessLog`

## Delay/Blocking Scan

Searched:

- `Task.Delay(20000)`
- `Task.Delay(21000)`
- `Thread.Sleep(20000)`
- `Thread.Sleep(21000)`
- `WaitForPrint`
- `WaitForSpooler`
- `WaitUntilPrinted`
- `WaitAfterPrint`
- awaited post-print refresh paths

Result:

- No hard 20/21 second delay was found in the current print path.
- Slow prints correlate with PDF URL/download stages, not `PrintDocument.Print()`.
- Post-print refresh is already queued in background and is not the dominant blocker in current logs.

## Sanitized Log Evidence

Fast normal case:

```text
[PrintPerf] phase=Search waybill=802759159319 trackingMs=201 printInfoMs=201 guardMs=2 totalMs=243
[PrintPerf] phase=Print usingFreshReadiness=true waybill=802759159319 readinessAgeMs=39 totalMs=0
[PrintPerf] phase=GetPdfUrl waybill=802759159319 elapsedMs=195 success=True
[PrintPerf] phase=DownloadPdf waybill=802759159319 elapsedMs=189 success=True
[PrintPerf] phase=PrintSubmit waybill=802759159319 printMs=218
[PrintPerf] phase=PrintTotal waybill=802759159319 totalMs=620
[PrintPerf] phase=PostRefresh waybill=802759159319 trackingMs=85 printInfoMs=73 totalMs=159
```

Fast reprint with cached PDF:

```text
[PrintPerf] phase=Print usingFreshReadiness=true waybill=842604579108 readinessAgeMs=44 totalMs=0
[TabPrintReprint] plan waybill=842604579108 jmsPrintCount=1 autoJmsReprintCount=0 isReprint=True canPrint=True
[PrintPerf] phase=PdfCache waybill=842604579108 hit=true path=842604579108-20260609_194442776.pdf
[PrintPerf] phase=PrintSubmit waybill=842604579108 printMs=197
[PrintPerf] phase=PrintTotal waybill=842604579108 totalMs=219
```

Slow case:

```text
[PrintPerf] phase=Print usingFreshReadiness=false waybill=802760520577-001 preflightTrackingMs=82 guardMs=85 totalMs=85
[PrintPerf] phase=GetPdfUrl waybill=802760520577-001 elapsedMs=212 success=True
[PrintPerf] phase=DownloadPdfAttempt waybill=802760520577-001 bytes=43204
[PrintPerf] phase=DownloadPdf waybill=802760520577-001 elapsedMs=42183 success=True
[PrintPerf] phase=PrintSubmit waybill=802760520577-001 printMs=255
[PrintPerf] phase=PrintTotal waybill=802760520577-001 totalMs=42755
```

Diagnosis from logs:

- The slow total time is explained by `DownloadPdf`, not by `PrintSafetyGuard` or printer submission.
- The print server returns a PDF URL quickly, but the PDF object may not be available immediately.
- If the app retries `printWaybill` to get a new URL, JMS may count an extra print even though the local app has not printed yet.
- Therefore, after `printWaybill` succeeds, the app should wait for the PDF URL to become downloadable instead of re-calling `printWaybill`.

## Current Risk

Earlier 8 second PDF download timeout was too short:

- JMS can count the print when `printWaybill` returns a URL.
- The PDF file may become available later.
- If the app times out and the user retries, JMS can increment print count again.

The safer policy is:

- Call `printWaybill` once.
- Wait longer for that returned PDF URL.
- Do not retry `printWaybill` automatically.
- Only mark AutoJMS print success after the PDF was downloaded and sent to `PrintDocument.Print()`.

## Current Round 1 Finding

The core slowdown is not the safety guard. The observed bottleneck is:

```text
GetPdfUrlViaCSharpAsync -> DownloadPdfWithRetryAsync -> remote PDF availability/download
```

`PrintDocument.Print()` itself is consistently small compared with the total.

## Round 2 Patch Direction

Recommended implementation direction:

1. Keep `PrintReadinessContext` fast path.
2. Keep `PrintSafetyGuard`.
3. Keep post-print refresh background.
4. Do not call `pringListPage` before print.
5. Do not retry `printWaybill` after a successful URL response.
6. Use a longer PDF download wait budget, e.g. 60-90 seconds.
7. Instrument:
   - `GetPdfUrlResponse`
   - PDF URL returned
   - first byte timing if feasible
   - final byte count
   - timeout/failure reason
8. Consider a user-visible state:
   - `JMS đã nhận lệnh in, đang chờ PDF...`

## Round 3 Verification Plan

Test with real app logs:

1. Search then print within 60 seconds.
2. Print same waybill again using cached PDF.
3. Print a suffix code such as `-001`.
4. Test slow PDF availability case.
5. Verify no duplicate `printWaybill` call for one click.
6. Verify JMS `printCount` increments only when intended.
7. Verify `Print totalMs` splits into:
   - readiness/preflight
   - `GetPdfUrl`
   - `DownloadPdf`
   - `PrintSubmit`
   - post refresh


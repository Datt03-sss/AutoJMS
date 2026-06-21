# Original User Request

## Initial Request — 2026-06-21T19:21:47Z

# Teamwork Project Prompt

Refactor the `tabPrint` print logic in AutoJMS (WinForms .NET 8) to introduce a `PrintJobCoordinator` that ensures exactly one API request per print (to correctly track print counts on JMS), reuses the cached PDF file for 60s, prevents double requests/re-entries, and robustly clears the grid selection upon successful submission to the printer spooler.

Working directory: d:\v1.2605.2(new-test)
Integrity mode: development

## Requirements

### R1. Exact API Request Count
Each physical print must trigger exactly one API request to JMS to register the print count. Never bypass the API request, even if a local cache of the PDF exists. Never send two API requests for the same print action (e.g. one for validation and one for printing).

### R2. PDF Caching (60s TTL)
If a waybill is printed again within 60 seconds, use the locally cached PDF file to send to the printer instead of downloading it again. This cache must not prevent R1 (the API must still be called to increment the count).

### R3. PrintJobCoordinator and Concurrency
Implement a centralized `PrintJobCoordinator` to prevent duplicate clicks, re-entry, and parallel printing of the same waybill. Protect against double requests across all entry points (button click, hotkey, auto print). Do not use a global lock that blocks the UI thread.

### R4. Clear Grid Selection
Clear the grid selection (SelectedRows, SelectedCells, CurrentRow, CurrentCell) on both `tabPrint` and `tabDash` immediately after the print job successfully enters the Windows spooler. Do not clear the selection if the API fails, validation blocks the print, or spooler submission fails.

### R5. Logging and Safety
Log the print flow explicitly with statuses like PRINT_JOB_START, PRINT_API_REQUEST_START, PRINT_CACHE_HIT, PRINT_SPOOLER_SUBMIT_DONE, PRINT_SELECTION_CLEAR_DONE. Preserve the existing `PrintSafetyGuard` flow without bypassing it.

## Verification Resources (Test Cases)

You must objectively verify your work against these explicit test cases:

- **Case 1 — First Print:** `apiRequestCountForJob = 1`, `cacheMiss = true`, `fileFromApiResponse = true`, `spoolerSubmit = success`, TabPrint selection cleared.
- **Case 2 — Reprint within 60s:** `apiRequestCountForJob = 1`, `cacheHit = true`, `fileFromCache = true` (no second download), `spoolerSubmit = success`, selection cleared, JMS print count increments by exactly 1.
- **Case 3 — Double-click/Rapid spam:** Only 1 PrintJob active, no double API request, no duplicate print, log DUPLICATE_PRINT_REQUEST_IGNORED.
- **Case 4 — API fail:** Do not print from cache, selection remains, show light warning.
- **Case 5 — Printer submit fail:** Log PRINT_SPOOLER_FAILED, do not clear selection, no auto-retry causing duplicate print.
- **Case 6 & 7 — Selection Clearing:** After successful print, TabPrint and TabDash clear all selected rows, cells, and currentCell. No custom highlight remains.

### Code Constraints
- Modifications are isolated to `tabPrint`, `PrintService`, API client, cache logic, and grid unselection logic.
- No modifications leak into HOME, DKCH, TRACKING, ABOUT, or release/installer scripts.

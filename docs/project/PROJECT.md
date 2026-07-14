# Project: AutoJMS Print Refactoring

## Architecture
- **PrintJobCoordinator**: Centralized manager that orchestrates the print process for a set of waybills. It enforces concurrency constraints, tracks print status, calls JMS APIs, manages cache, submits jobs to the spooler, handles errors, and clears UI selections.
- **PrintService**: Exposes search/load and UI grid state (selections).
- **Main Form**: Handlers for UI events (buttons, key down) delegate execution to `PrintJobCoordinator`.
- **AutoJMS.Tests**: Test suite in the `tests/` directory verifying happy path, reprints, spamming, failures, and selection clearing.

## Milestones
| # | Name | Scope | Dependencies | Status |
|---|------|-------|-------------|--------|
| 1 | E2E Testing Track | Design/create E2E test infrastructure & test cases (Tiers 1-4) in `tests/`, publish `TEST_READY.md` | none | PLANNED |
| 2 | Implementation Track | Implement `PrintJobCoordinator`, update `PrintService` and `Main.cs` to use it, ensure lock safety, 60s cache logic, robust selection clearing, and comprehensive logs | M1 | PLANNED |

## Interface Contracts
### PrintJobCoordinator ↔ Main / UI
- `Task<PrintSubmitResult> PrintAsync(PrintJobRequest request)`
  - Inputs:
    - `List<string> SelectedWaybills`
    - `int PrintType`
    - `int ApplyTypeCode`
    - `int KeepPdfs`
    - `string Source` (e.g. "Print", "AutoPrint")
  - Actions:
    - Protect against parallel print requests.
    - Validate printing readiness/safety via `PrintService.ValidateSelectedBeforePrintAsync`.
    - Retrieve PDF URL via JMS API.
    - Check cache by cache key (60s TTL) and URL cache.
    - Download PDF if cache miss.
    - Submit to spooler.
    - Clear UI grids on print/dash tab if spooler submission succeeds.
    - Expose logging flow with explicit trace markers.
  - Outputs:
    - `PrintSubmitResult` indicating spooler queue success, elapsed time, printer details, error reasons.

## Code Layout
- `src/AutoJMS/Printing/PrintJobCoordinator.cs` (New)
- `src/AutoJMS/Printing/PrintService.cs` (Modified)
- `src/AutoJMS/Forms/Main.cs` (Modified)
- `tests/AutoJMS.Tests/` (New project)

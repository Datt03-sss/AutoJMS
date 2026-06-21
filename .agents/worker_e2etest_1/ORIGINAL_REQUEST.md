## 2026-06-22T02:26:03Z
You are worker_e2etest_1. Your working directory is d:\v1.2605.2(new-test)\.agents\worker_e2etest_1.
Your task is to implement the E2E test project and mock interfaces as specified.

Please follow these steps exactly:
1. Before editing any files, read `d:\v1.2605.2(new-test)\.agent-lock.md`. If Current Writer is None, acquire the lock by writing:
   Current Writer: worker_e2etest_1
   Mode: WRITE_ACTIVE
   Scope: tests/AutoJMS.Tests, src/AutoJMS
2. Create/define mockable interfaces and classes in `src/AutoJMS/Printing/`:
   - `IJmsApiClient.cs` interface (representing the HTTP client calls to the JMS API)
   - `IPrinterSpoolerSubmitter.cs` interface (representing submitting to printer spooler)
   - `PrintJobRequest.cs` (DTO for input parameter to PrintAsync)
   - `PrintSubmitResult.cs` (DTO for print execution outcome, matching the one inside Main.cs but public in AutoJMS namespace so it does not conflict)
   - `PrintJobCoordinator.cs` (orchestrates validation, lock checks, API calls, cache checks, spooler submission, and unselection)
3. Modify `src/AutoJMS/Services/JmsApiClient.cs`:
   - Implement `IJmsApiClient` by exposing a static `Instance` property (of type `IJmsApiClient`) initialized with a default implementation that forwards calls to the original static logic.
   - Update static calls/methods to delegate to `Instance.PostJsonAsync(...)` so it is backward-compatible but mockable.
4. Implement `PrintJobCoordinator.cs` using constructor injection of `IJmsApiClient`, `IPrinterSpoolerSubmitter`, and `IPrintService`. In `PrintAsync(PrintJobRequest request)`:
   - Handle single-flight concurrency lock checks (using SemaphoreSlim, logging DUPLICATE_PRINT_REQUEST_IGNORED on duplicate).
   - Coordinate validation (via `IPrintService.ValidateSelectedBeforePrintAsync`), API call, cache check (keep/reuse local cache for 60s, but still hit the API once), spooler submit (via `IPrinterSpoolerSubmitter`), and selection clearing (via `IPrintService.SelectAll(false)` and `IPrintService.ClearSelection()`) on success spooler submission.
5. Create the test project directory `tests/AutoJMS.Tests` and create `AutoJMS.Tests.csproj` targeting `net8.0-windows` with Windows Forms enabled. Reference `src/AutoJMS/AutoJMS.csproj`, xUnit, and Moq.
6. Create `tests/AutoJMS.Tests/Helpers/StaTaskScheduler.cs` to allow running test code in Single-Threaded Apartment (STA) thread.
7. Create `tests/AutoJMS.Tests/PrintJobCoordinatorTests.cs` implementing the 7 test cases specified:
   - Case 1: First Print
   - Case 2: Reprint within 60s
   - Case 3: Double-click/Rapid spam
   - Case 4: API fail
   - Case 5: Printer submit fail
   - Case 6 & 7: Selection Clearing (on successful print, clear Print and Dash grid selection)
8. Modify `AutoJMS.slnx` to include `tests/AutoJMS.Tests/AutoJMS.Tests.csproj`.
9. Perform build using:
   dotnet restore .\AutoJMS.slnx
   dotnet build .\AutoJMS.slnx -c Release
10. Release the lock in `d:\v1.2605.2(new-test)\.agent-lock.md` by resetting:
    Current Writer: None
    Mode: READ_ONLY
    Scope: None
11. If the build passes, commit and push changes:
    git status
    git add .
    git commit -m "feat(tests): Implement E2E test project and mockable print structures"
    git push origin main
12. Report back with the build output, commit message, commit hash, and paths to created files.

MANDATORY INTEGRITY WARNING:
DO NOT CHEAT. All implementations must be genuine. DO NOT
hardcode test results, create dummy/facade implementations, or
circumvent the intended task. A Forensic Auditor will independently
verify your work. Integrity violations WILL be detected and your
work WILL be rejected.

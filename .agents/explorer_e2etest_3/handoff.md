# Handoff Report - E2E Testing Infrastructure Design

## 1. Observation

- **JmsApiClient Definition**: In `src/AutoJMS/Services/JmsApiClient.cs`, lines 24-25:
  ```csharp
  public static class JmsApiClient
  ```
- **JmsApiClient Usage**: In `src/AutoJMS/Printing/PrintService.cs`, lines 889 and 1216, direct calls to the static method are made:
  ```csharp
  using var response = await JmsApiClient.PostJsonAsync(apiUrl, jsonPayload, routeName: "trackingExpress").ConfigureAwait(false);
  ```
- **Printer Spooler Submission**: In `src/AutoJMS/Forms/Main.cs`, lines 3597-3602, the print jobs are submitted and monitored:
  ```csharp
  private async Task<PrintSubmitResult> SubmitPrintImmediatelyAsync(PrintJobCacheEntry job, string firstWaybill)
  ```
- **Selection Clearing**: In `src/AutoJMS/Forms/Main.cs`, lines 2913-2918, UI selections are cleared upon success:
  ```csharp
  _printService.SelectAll(false);
  tabPrint_btnSelectAll.Checked = false;
  ...
  try { _printService?.ClearSelection(); } catch { }
  try { _fullStackForm?.ClearDashGridSelection(); } catch { }
  ```
- **Solution File**: In `AutoJMS.slnx`, lines 1-4:
  ```xml
  <Solution>
    <Project Path="src/AutoJMS/AutoJMS.csproj" DefaultStartup="true" />
    <Project Path="src/AutoJMS.Abstractions/AutoJMS.Abstractions.csproj" />
  </Solution>
  ```
- **Framework and UI Technology**: In `src/AutoJMS/AutoJMS.csproj`, lines 5-7:
  ```xml
  <TargetFramework>net8.0-windows</TargetFramework>
  <Nullable>disable</Nullable>
  <UseWindowsForms>true</UseWindowsForms>
  ```

---

## 2. Logic Chain

1. **Test Project Alignment**: Because the target framework of the main application is `net8.0-windows` and it uses Windows Forms (`UseWindowsForms=true`), the new test project `tests/AutoJMS.Tests` must also target `net8.0-windows` and enable Windows Forms to allow testing grid/cell selection changes on `DataGridView`.
2. **Isolation for JmsApiClient**: Since `JmsApiClient` is a static class and is directly invoked in `PrintService.cs`, refactoring all caller sites would violate the **Minimal Edit Rule**. Introducing an interface `IJmsApiClient` and a static property `JmsApiClient.Instance` (which defaults to `DefaultJmsApiClient`) allows the test project to replace the HTTP communication layer in a single line (e.g. `JmsApiClient.Instance = mockClient`) without touching `PrintService.cs` or other client files.
3. **Isolation for Printer Spooler**: Submitting and checking jobs currently queries actual Win32 WMI and prints physically. Introducing `IPrinterSpoolerSubmitter` allows injecting a mock spooler that returns a simulated success or failure status immediately.
4. **Mocking UI Interaction**: Grid selections are managed across both `TabPrint` and `TabDash` grids. By declaring `IPrintUiController`, the mock coordinator can invoke `ClearPrintGridSelection()` and `ClearDashGridSelection()`, allowing test assertions to verify that grid selections are indeed cleared on success and retained on failure.
5. **Coverage of 7 Cases**: The 7 test cases directly verify the core functionality of the new `PrintJobCoordinator` (which coordinates `PrintService`, `JmsApiClient`, and `IPrinterSpoolerSubmitter`):
   - **Case 1 (First Print)** checks the default successful download & print flow and that selection is cleared.
   - **Case 2 (Reprint within 60s)** verifies that local URL cache is hit, bypassing PDF download (download count remains 1), while calling the API once (JMS count increments by 1).
   - **Case 3 (Rapid spam)** simulates concurrent execution, verifying that duplicate requests are ignored and log output contains the duplicate warning.
   - **Case 4 (API fail)** verifies that if the API fails, the system does not print from cache, selections remain, and a warning is shown.
   - **Case 5 (Printer submit fail)** ensures that if the spooler rejects the job, grid selections remain and no duplicate retry is executed.
   - **Cases 6 & 7 (Selection Clearing)** verify that both tabs have their selections cleared successfully.

---

## 3. Caveats

- We assumed that xUnit tests will run sequentially within the class (which is xUnit's default behavior) to avoid concurrent test execution interfering with the static `JmsApiClient.Instance` property. If parallel execution is required across multiple test classes, the assembly parallelization should be disabled in xUnit or the coordinate class should receive the `IJmsApiClient` instance in its constructor (as designed in the `PrintJobCoordinator` draft).
- Physical printer state is completely simulated; actual driver incompatibilities or physical paper jams cannot be reproduced in these unit/integration tests.

---

## 4. Conclusion

The designed test infrastructure in `tests/AutoJMS.Tests` utilizing xUnit, targeting `net8.0-windows`, and referencing `src/AutoJMS/AutoJMS.csproj` is fully capable of testing the new print flow in isolation. By introducing the `IJmsApiClient`, `IPrinterSpoolerSubmitter`, and `IPrintUiController` abstractions, we can execute and verify all 7 required test cases cleanly and reliably.

---

## 5. Verification Method

- **Build Solution Command**:
  ```powershell
  dotnet restore .\AutoJMS.slnx
  dotnet build .\AutoJMS.slnx -c Release
  ```
- **Run Tests Command**:
  ```powershell
  dotnet test .\tests\AutoJMS.Tests\AutoJMS.Tests.csproj -c Release
  ```
- **Expected Outcome**: The solution builds without errors, and all 7 unit tests pass with 100% success rate.

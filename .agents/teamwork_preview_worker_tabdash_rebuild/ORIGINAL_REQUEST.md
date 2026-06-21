## 2026-06-22T03:25:20Z

You are teamwork_preview_worker. Your working directory is d:\v1.2605.2(new-test)\.agents\teamwork_preview_worker_tabdash_rebuild.
Your task is to rebuild the tabDash UI in AutoJMS using WebView2 based on the Claude Design located in docs/layout/tabDash/extracted/.

Please perform the implementation steps as follows:

### Step 1. Acquire Workspace Lock
Read d:\v1.2605.2(new-test)\.agent-lock.md. If Current Writer is None, update it to your name ("teamwork_preview_worker") and set Mode: WRITE_ACTIVE, Scope: rebuild-tabDash-WebView2.

### Step 2. Setup Local Web Assets Directory (Strictly Offline)
1. Create directory `src/AutoJMS/Web/`.
2. Copy React & ReactDOM UMD files from the local Office cache:
   - Copy `C:\Users\DAT\AppData\Local\Microsoft\Office\SolutionPackages\32703957a2e459137739bdc59187406a\PackageResources\OfflineFiles\react.min_f22492ae996884949f5f0e0204796add.js` to `src/AutoJMS/Web/react.production.min.js`.
   - Copy `C:\Users\DAT\AppData\Local\Microsoft\Office\SolutionPackages\32703957a2e459137739bdc59187406a\PackageResources\OfflineFiles\react-dom.min_27a8068f7845b22ea825b6827cfd2b10.js` to `src/AutoJMS/Web/react-dom.production.min.js`.
3. Copy `docs/layout/tabDash/extracted/AutoJMS Dashboard.dc.html` to `src/AutoJMS/Web/index.html`.
4. Copy `docs/layout/tabDash/extracted/support.js` to `src/AutoJMS/Web/support.js`.
5. Clean `src/AutoJMS/Web/index.html`:
   - Remove Google Fonts link (`fonts.googleapis.com` and `fonts.gstatic.com`). The page will automatically fall back to Segoe UI and system fonts.
   - Add `<script src="react.production.min.js"></script>` and `<script src="react-dom.production.min.js"></script>` scripts in the `<head>` *before* `<script src="./support.js"></script>`.
6. Clean `src/AutoJMS/Web/support.js`:
   - Replace the external unpkg CDN links (`REACT_URL`, `REACT_DOM_URL`, `BABEL_URL`) with relative paths (`react.production.min.js`, `react-dom.production.min.js`, and `""` respectively).
7. Clean up any other remote resource references to ensure 100% offline loading capability.

### Step 3. MSBuild Project Update
Edit `src/AutoJMS/AutoJMS.csproj` to include the `Web/` folder contents in the build output directory:
```xml
  <ItemGroup>
    <Content Include="Web\**\*" CopyToOutputDirectory="PreserveNewest" />
  </ItemGroup>
```

### Step 4. WebView2 Integration in WinForms Dashboard
1. Declare `private Microsoft.Web.WebView2.WinForms.WebView2 _webView;` and a initialization tracker `private bool _webViewInitialized = false;` in `FullStackOperation.Dashboard.cs`.
2. Instantiate the WebView2 control and add it to `tabDash.Controls` in `BuildDashboardPageCodeFirst()`. Ensure all old WinForms controls (`tabDash_dataSource`, `_dashSearchBox`, etc.) are still instantiated in memory to prevent NullReferenceExceptions, but do NOT add them to the page controls layout. Dock the WebView2 control to fill the tabDash page.
3. Implement `private async Task InitializeWebView2Async()`:
   - Configure a clean userDataFolder `Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "AppData", "BrowserData")`.
   - Call `await _webView.EnsureCoreWebView2Async(env)`.
   - Call `_webView.CoreWebView2.SetVirtualHostNameToFolderMapping("autojms.local", Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Web"), CoreWebView2HostResourceAccessKind.Allow)`.
   - Set context menu and status bar settings to false if appropriate.
   - Bind `_webView.WebMessageReceived += OnWebViewMessageReceived`.
   - Navigate to `https://autojms.local/index.html`.
4. Call `InitializeWebView2Async()` when dashboard page builds or loads.

### Step 5. Communication Bridge (JS <-> C#)
1. In C# (OnWebViewMessageReceived), handle incoming actions from JS:
   - `SYNC`: Programmatically call `tabDash_updateData_Click(null, null)`.
   - `EXPORT`: Programmatically call `ExportOperationCurrentViewAsync(false)`.
   - `CHANGE_SOURCE`: Set `tabDash_dataSource.Text = source` (or SelectedItem) and trigger selection change event.
   - `CHANGE_SEARCH`: Set `_dashSearchBox.Text = text`.
   - `CHANGE_TIME_INTERVAL`: Set `tabDash_timeUpdateData.Text = text`.
   - `CHANGE_STATUS_SELECT`: Set `tabDash_statusSelect.Text = text`.
   - `FETCH_JOURNEY`: Call `ShowWaybillJourneyWorkspace(waybillNo)` which loads tracking history.
   - `TOGGLE_STAR`: Persist starred waybill state in C# SQLite or tracking metadata.
   - `SELECT_WAYBILL`: Update internal selected waybill text.
2. In C#, implement a waybill categorizer that labels each `WaybillDbModel` with its matching `mainQueues` and `subQueues` labels based on C# filters (`IsNeedsAction`, `IsNewArrival`, `IsNotDispatched`, `IsPendingReturn`, `IsSlaBreached`, `IsSlaUrgent`, `IsLostRisk`, etc.).
3. Whenever `UpdateDashGridDataSource(List<WaybillDbModel> data)` or `UpdatePriorityFocusCards()` is called, serialize the dataset and state metadata (sync status, last update time, site ID "214A02", counts, waybills list with categories) to JSON, and post it to WebView2 using `_webView.CoreWebView2.PostWebMessageAsJson(json)`.
4. In `index.html` / `support.js`, handle receiving JSON state updates (e.g. `window.chrome.webview.addEventListener('message', event => { ... })`). In the event handler, parse the message. If it is `UPDATE_DATA`, save it to the component's React state so that React dynamically renders the updated counts, subchips, and waybill lists.
5. In `FullStackOperation.WaybillWorkspace.cs`, hook into `BindJourneyGrid`: when it is called to bind journey details to grid, also serialize the journey rows (`JourneyEventViewModel` lists) to JSON and post it to WebView2 under type `JOURNEY_DATA`. React will catch it and display the real journey events in its HTML journey table.

### Step 6. Build and Verification
1. Run local build:
   `dotnet restore .\AutoJMS.slnx`
   `dotnet build .\AutoJMS.slnx -c Release`
2. Verify with harness:
   `powershell -ExecutionPolicy Bypass -File .\eng\harness\verify.ps1`
3. Verify that the app compiles and behaves correctly, and there are no regressions.
4. Release the lock: update `.agent-lock.md` to reset Current Writer to None and Mode: READ_ONLY.
5. Commit and push the changes:
   `git add .`
   `git commit -m "Rebuild tabDash UI using WebView2 with offline React/ReactDOM UMD and postMessage bridge based on Claude Design"`
   `git push origin main`

MANDATORY INTEGRITY WARNING:
DO NOT CHEAT. All implementations must be genuine. DO NOT hardcode test results, create dummy/facade implementations, or circumvent the intended task. A Forensic Auditor will independently verify your work. Integrity violations WILL be detected and your work WILL be rejected.

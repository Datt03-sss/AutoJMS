# Handoff Report: WebView2 Full-Screen Host Refactoring Plan

## 1. Observation
During the read-only exploration of `FullStackOperation.cs` and its partial classes under `src/AutoJMS/Forms/`, the following details were directly observed:

* **Form Fields and Controls**:
  `FullStackOperation.Fields.cs` declares all legacy UI controls including:
  * `private UITabControl uiTabControl1;` (line 11)
  * `private TabPage tabDash;` (line 12)
  * `private TabPage tabChat;` (line 29)
  * `private UITabControl uiTabControl2;` (line 24)
  * `private TabPage tabPage3;` (line 25)
  * `private TabPage tabPage4;` (line 27)
  * Grids like `tabDash_dataGridView` (line 26), `uiDataGridView2` (line 28), and `tabChat_dataGrid` (line 33).
  * Zalo chatbot WebView `private WebView2 tabChat_webViewZalo;` (line 31).

* **Dynamic Tab Pages**:
  In `FullStackOperation.cs`, dynamic tabs `_tabThoiHieu` (line 47) and `_tabDetail` (line 38) are defined and managed:
  * `_tabThoiHieu = new TabPage("Thời hiệu");` (line 2576).
  * `uiTabControl1.TabPages.Insert(1, _tabThoiHieu);` (line 2584).
  * `uiTabControl2.TabPages.Add(_tabDetail);` (line 2197).

* **WebView2 Host & Page Initialization**:
  * In `FullStackOperation.Dashboard.cs`, `_webView` (WebView2) is initialized and added to `tabDash` (lines 31-35):
    ```csharp
    _webView = new Microsoft.Web.WebView2.WinForms.WebView2
    {
        Dock = DockStyle.Fill
    };
    tabDash.Controls.Add(_webView);
    ```
  * In `FullStackOperation.Dashboard.cs`, `InitializeWebView2Async()` sets virtual host mapping to the local `Web` folder and navigates to `https://autojms.local/index.html` (lines 414-423).
  * `OnWebViewMessageReceived()` (lines 434-520) handles web messages like `"SYNC"`, `"CHANGE_SOURCE"`, `"CHANGE_SEARCH"`, `"CHANGE_TIME_INTERVAL"`, `"CHANGE_STATUS_SELECT"`, `"FETCH_JOURNEY"`, and `"TOGGLE_STAR"`.
  * `PostStateToWebView2()` (lines 836-847) formats and posts `UPDATE_DATA` messages to `_webView`, reading values directly from WinForms controls like `tabDash_dataSource?.Text` and `_dashSearchBox?.Text`.

* **SunnyUI Hiding Title Bar**:
  By compiling a temporary reflection checker referencing `SunnyUI.dll` (v3.9.6) under .NET 8.0, we verified that:
  * `ShowTitle` (System.Boolean) exists.
  * `TitleHeight` (System.Int32) exists.
  * Form padding is set via `Padding = new Padding(1, 36, 1, 1);` in `FullStackOperation.Layout.cs` (line 34).

* **Integration with Main.cs**:
  In `Main.cs`, the instance of `FullStackOperation` (`_fullStackForm`) is accessed exclusively using standard `System.Windows.Forms.Form` properties and methods: `Close()`, `Dispose()`, `FormClosed`, `StartPosition`, `WindowState`, `Visible`, `Show(this)`, `Activate()`, and `BringToFront()` (lines 1374-1524).

---

## 2. Logic Chain
1. According to the **UI Architecture Rule (FullStackOperation)**:
   * The form must act *exclusively* as a full-screen host for the WebView2 control.
   * No native tabs are allowed (like Dashboard, Thời hiệu, CHATBOT).
   * WebView2 must dock `Fill` directly to the Form's `Controls` collection.
2. Because the WebView2 control `_webView` is the sole UI element, the tab controls (`uiTabControl1`, `uiTabControl2`), tab pages (`tabDash`, `tabChat`, `_tabThoiHieu`, `_tabDetail`), native grids (`tabDash_dataGridView`, `_waybillJourneyGrid`, `_thoiHieuGrid`), Zalo WebView (`tabChat_webViewZalo`), sidebars, metrics panels, and headers are unused and completely obsolete.
3. In order to hide the Form header and title bar, setting `ShowTitle = false` (which is a verified SunnyUI property) is the correct approach.
4. Setting `ShowTitle = false` requires adjusting the top padding of the Form from the default 36px to 0px (`Padding = new Padding(0);` or `new Padding(1)` if a thin border is desired) to prevent a layout gap at the top.
5. In the new state-driven model, we must replace references to WinForms controls with internal class fields (e.g. `_selectedSource`, `_selectedTimeInterval`, `_selectedStatusSelect`, `_searchText`) to track the active filter state.
6. `Main.cs` does not call any custom properties or methods on `FullStackOperation`. This ensures that changing the internal layout, removing native controls, and deleting obsolete methods in `FullStackOperation.cs` and its partial classes will have zero compile-time impact on the rest of the application.

---

## 3. Caveats
* **Web App Capabilities**: We assume the web application (`index.html`) is fully self-contained and already handles its own tab navigation, styling, grids, detail panels, and chatbot operations.
* **External Services**: We assume Zalo Chatbot background reminders (`ZaloChatService`) do not need to be initialized on `FullStackOperation` anymore, as there is no chatbot tab to trigger them and `Main.cs` manages its own separate instance of `ZaloChatService` when needed.

---

## 4. Conclusion
The refactoring of `FullStackOperation` into a full-screen WebView2 host is highly feasible and clean.
* The form's title bar can be hidden using `ShowTitle = false` and setting the padding to `new Padding(0)` or `new Padding(1)`.
* We can safely delete all legacy layout code, control fields, event bindings, and obsolete UI methods.
* We must store filter states inside C# variables instead of native UI controls.
* Below is the precise file-by-file refactoring plan.

---

## 5. Refactoring Plan (File-by-File)

### 1. `FullStackOperation.Fields.cs`
* **Keep**:
  * `_webView` (WebView2) and `_webViewInitialized` (bool).
  * Core background services, cancellation tokens, timers, and collection state (e.g., `_cloudData`, `StarredWaybills`, `_fullStackDashboardService`, `_fullStackWorkflowService`, `_fullStackExportService`, `_fullStackJourneyService`, `_journeyAttachmentService`).
* **Add**:
  * Internal fields to track active filter states:
    ```csharp
    private string _selectedSource = "LOCAL";
    private string _selectedTimeInterval = "2 PHÚT";
    private string _selectedStatusSelect = "Tất cả tồn kho";
    private string _searchText = "";
    ```
* **Delete**:
  * All legacy WinForms control declarations (`uiTabControl1`, `tabDash`, `tabChat`, `uiTabControl2`, `tabDash_dataGridView`, `uiDataGridView2`, `tabChat_dataGrid`, `tabChat_webViewZalo`, and all label/button/panel fields).

### 2. `FullStackOperation.Layout.cs`
* **Update `ConfigureFormShell()`**:
  * Set `ShowTitle = false;`
  * Set `Padding = new Padding(0);` (or `new Padding(1)` to retain a 1px border).
* **Update `BuildUiInCode()`**:
  * Simplify the layout setup to build only the `_webView` control and add it to `Controls`:
    ```csharp
    private void BuildUiInCode()
    {
        SuspendLayout();
        try
        {
            Controls.Clear();

            _webView = new Microsoft.Web.WebView2.WinForms.WebView2
            {
                Dock = DockStyle.Fill,
                Name = "_webView"
            };
            Controls.Add(_webView);

            LoadStarredWaybills();
            _ = InitializeWebView2Async();
        }
        finally
        {
            ResumeLayout(true);
        }
    }
    ```
* **Delete**:
  * Unused helper layout methods: `CreateInlineLayout`, `CreatePlainPanel`, `CreateComboBox`, `CreateToolbarLabel`, `CreatePlainLabel`, `CreateMetricText`, `CreateGrid`.

### 3. `FullStackOperation.Events.cs`
* **Update `BindCodeFirstEvents()`**:
  * Remove all event subscriptions for removed native controls (grids, buttons, combo boxes, tab control index changed).
  * Keep only:
    ```csharp
    private void BindCodeFirstEvents()
    {
        Load += FullStackOperation_Load;
        FormClosing += FullStackOperation_FormClosing;
    }
    ```

### 4. `FullStackOperation.Chatbot.cs`
* **Action**: Delete this file completely, or empty the contents (leave the class declaration empty) since it only contains layout builders for the obsolete native Chatbot tab.

### 5. `FullStackOperation.Dashboard.cs`
* **Delete**:
  * All native UI page builder methods: `BuildDashboardPageCodeFirst`, `CreateTopBar`, `CreateFilterBar`, `CreateQueueNavigator`, `CreateBodyPanel`, `CreateLeftSidebar`, `CreateMetricsPanel`, `CreateDashboardGridHost`, `CreateRightDetailPanel`, `CreateFocusCard`, `CreateGridToolbar`, `CreateFilterField`, `CreateHeaderComboBox`, `CreateHeaderButton`, `CreateStatusFooter`.
* **Update `OnWebViewMessageReceived()`**:
  * Modify message handlers to update internal fields instead of WinForms controls:
    * `action == "SYNC"` -> call `_ = SyncDataAsync();`
    * `action == "CHANGE_SOURCE"` -> set `_selectedSource = source;` and call `_ = RefreshDashViewAsync(_cts.Token);`
    * `action == "CHANGE_SEARCH"` -> set `_searchText = text;` and call `RefreshFilteredGrid();`
    * `action == "CHANGE_TIME_INTERVAL"` -> set `_selectedTimeInterval = text;` and update `_autoRefreshTimer.Interval`.
    * `action == "CHANGE_STATUS_SELECT"` -> set `_selectedStatusSelect = text;` and call `RefreshFilteredGrid();`
* **Update `PostStateToWebView2()`**:
  * Populate payload with the internal fields:
    ```csharp
    selectedSource = _selectedSource,
    selectedTimeInterval = _selectedTimeInterval,
    selectedStatusSelect = _selectedStatusSelect,
    searchText = _searchText
    ```

### 6. `FullStackOperation.WaybillWorkspace.cs`
* **Delete**:
  * UI layout/grid methods: `BuildWaybillJourneyWorkspace`, `CreateAttachmentRow`, `CreateJourneyColumn`, `CreateJourneyDateColumn`, `CreateJourneyUploadedDateColumn`, `WaybillJourneyGrid_DataBindingComplete`, `WaybillJourneyGrid_CellToolTipTextNeeded`, `WaybillJourneyGrid_CellPainting`, `WaybillJourneyGrid_CellClick`, `WaybillJourneyGrid_CellDoubleClick`, `ShowJourneyError`, `SetJourneyLoading`, `ClearJourneyGrid`, `SetJourneyWorkspaceVisible`, `SetJourneyAuxButtons`.
* **Update `OpenJourneyAsync()`**:
  * Remove calls to obsolete UI mutation methods (`SetJourneyWorkspaceVisible`, `ClearJourneyGrid`, `SetJourneyLoading`, etc.). Keep the async data fetching logic.
* **Update `BindJourneyGrid()`**:
  * Keep only the conversion to `JourneyEventViewModel` and posting of `JOURNEY_DATA` to `_webView`. Remove the references updating `_waybillJourneyGrid.DataSource`.

### 7. `FullStackOperation.cs`
* **Update `FullStackOperation_Load()`**:
  * Remove calls to obsolete grid configurations and toolbar initializations (`SetupGrids()`, `InitializeEnhancedUI()`, `SetupDashToolbar()`).
  * Initialize filter fields:
    ```csharp
    _selectedSource = "LOCAL";
    _selectedTimeInterval = "2 PHÚT";
    _selectedStatusSelect = "Tất cả tồn kho";
    _searchText = string.Empty;
    ```
* **Update `FullStackOperation_FormClosing()`**:
  * Remove checks and disposals for timers and services that have been deleted (e.g. Zalo reminders).
* **Rename/Refactor `tabDash_updateData_Click`**:
  * Change signature to `private async Task SyncDataAsync()` (parameterless Task method) to act as the main data-sync function.
* **Update `RefreshFilteredGrid()`**:
  * Simplify to apply filter and call `UpdateDashGridDataSource()`.
* **Update `UpdateDashGridDataSource()`**:
  * Prune down to only update list state and trigger `PostStateToWebView2()`:
    ```csharp
    private void UpdateDashGridDataSource(List<WaybillDbModel> data)
    {
        _lastFilteredDashRows = data?.ToList() ?? new List<WaybillDbModel>();
        PostStateToWebView2();
    }
    ```
* **Update `RefreshDashViewAsync()`**:
  * Replace control getters with local variables: `_selectedSource`, `_selectedStatusSelect`.
* **Delete**:
  * All obsolete event handlers (`tabDash_dataSource_SelectedIndexChanged`, `tabDash_timeUpdateData_SelectedIndexChanged`, `tabDash_statusSelect_SelectedIndexChanged`, etc.).
  * All obsolete UI update helpers (`UpdatePriorityFocusCards`, `UpdateDashQueueInsight`, `UpdateOperationCenterChrome`, `UpdateOperationFilterToolbar`, `UpdateSelectedOperationDetailFromGrid`, `UpdateFilterInfo`, `UpdateDashQuickFilterButtons`, `EnsureDashStatusSelectionValid`, `GetSelectedDashStatus`, `GetControlTextSafe`, `ApplyStandardGridSettings`, `ApplyThoiHieuStandardGridSettings`, `FullStackGrid_CellFormatting`, `FullStackGrid_DataError`).
  * All obsolete Zalo chatbot handlers (`InitZaloWebViewAsync`, `tabChat_btnStart_Click`, `tabChat_btnReload_Click`, `tabChat_statusSelect_SelectedIndexChanged`).
  * All obsolete Thời hiệu methods (`SetupThoiHieuTab`, `RefreshThoiHieuKpiSheet`, `FilterThoiHieuGrid`, `ExportThoiHieuFullImageAsync`, `SaveThoiHieuAutoImage`, `ThoiHieuGrid_CellDoubleClick`, etc.).

---

## 6. Verification Method
1. **Compilation Check**:
   Propose the following build command on the solution:
   `dotnet build .\AutoJMS.slnx -c Release`
   Ensure it compiles without errors or warnings.
2. **Harness Verification**:
   Execute the verification harness:
   `powershell -ExecutionPolicy Bypass -File .\eng\harness\verify.ps1`
   Ensure all checks (Build, Secrets, and Layout constraints) pass successfully.
3. **Layout Compliance**:
   Ensure no source or test files are placed in `.agents/`.

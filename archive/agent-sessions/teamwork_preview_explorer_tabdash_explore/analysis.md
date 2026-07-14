# Analysis Report: AutoJMS tabDash Rebuild Plan (WebView2 Integration)

This report details the findings and implementation recommendations for rebuilding the **tabDash** (realtime post office coordination dashboard) in AutoJMS using WebView2 based on the Claude Design.

---

## 1. Analysis of Current tabDash Dashboard

The current tabDash dashboard is implemented in a code-first, WinForms-based layout across several `FullStackOperation` files (primarily `FullStackOperation.Dashboard.cs`, `FullStackOperation.cs`, and `FullStackOperation.WaybillWorkspace.cs`).

### Layout and Controls
- **Top Bar**: Displays the app name and status label ("Điều phối Vận hành Bưu cục Realtime").
- **Filter Bar**: Contains WinForms controls:
  - `tabDash_dataSource` ComboBox ("ALL", "LOCAL", "PHATLAI")
  - `tabDash_timeUpdateData` ComboBox ("2 PHÚT", "5 PHÚT", "10 PHÚT", "30 PHÚT", "1 GIỜ") controlling auto-refresh intervals.
  - `tabDash_statusSelect` ComboBox ("Tất cả tồn kho", etc.) filtering by queue type.
  - `_dashSearchBox` TextBox for dynamic search text filtering.
  - `tabDash_updateData` Button (Sync with JMS API).
  - `_operationRefreshLocalButton` Button (Force local SQLite reload).
  - `_dashExportBtn` Button (Export grid view).
  - `tabDash_lblLastUpdate` Label showing synchronization status.
- **Queue Navigator / Priority Cards**: 9 custom `KpiCardControl` panels displaying counts: "TỔNG TỒN", "HÀNG ĐẾN", "PHÁT HÀNG", "BACKLOG", "CHUYỂN HOÀN", "KIỂM KHO", "CSKH", "DỪNG TRẠM", "STAR". Clicking a card sets it as the active filter.
- **Body Panel**: A table layout containing:
  - Left panel: "Smart Context" - lists waybills or stats.
  - Center panel: Main grid containing the list of waybills (`tabDash_dataGridView`) or waybill journey history grid (`_waybillJourneyGrid`).
  - Right panel: Waybill detailed information cards.

### Data Population & Grid Mechanics
- **Data Source**: Local-first database (`details.db` SQLite) handled by `_fullStackDashboardService`.
- **Loading**: `LoadDataAndRefreshViewsAsync()` retrieves a snapshot from SQLite and updates `_cloudData`.
- **Manual Sync**: Triggered by `tabDash_updateData.Click`, fetching fresh inventory data from JMS API and writing it to SQLite before reloading.
- **Zalo Integration**: If the data source is set to "PHATLAI", waybills are fetched from Zalo chatbot via `ZaloChatService.GetWaybillsFromPhatLaiAsync()` and matched with local data.
- **Filtering**: `ApplyDashFilter()` processes the list in memory based on:
  - Selected quick filter or combobox selection.
  - Search textbox content (matching waybill number, operators, or last action).
  - Date ranges from date picker controls.
  - Selected data source.
- **Wired Grid Events**:
  - `SelectionChanged`: Triggers updating detail panels on the right side.
  - `CellFormatting`: Formats row backgrounds based on severity (e.g., SLA breached/urgent, pending return, failed delivery).
  - `CellDoubleClick`: Triggers `ShowWaybillJourneyWorkspace(waybillNo)` to fetch and display the history of tracking actions.
  - `ColumnHeaderMouseClick`: Performs programmatic column sorting.

---

## 2. Analysis of Claude Design Files & CDN Isolation

The Claude Design files in `docs/layout/tabDash/` provide the target HTML/CSS layout:
- `extracted/AutoJMS Dashboard.dc.html`
- `extracted/support.js`
- `AutoJMS Dashboard (standalone).html` (the bundled version)

### Asset & Remote Resource Analysis
1. **Fonts (Remote CDN)**:
   - The document helmet includes links to Google Fonts API:
     ```html
     <link rel="preconnect" href="https://fonts.googleapis.com">
     <link href="https://fonts.googleapis.com/css2?family=Inter:wght@400;500;600;700&display=swap" rel="stylesheet">
     ```
     *Action required*: Must be loaded offline using local font files.
2. **React & ReactDOM Libraries (Remote CDN)**:
   - The runtime bootstrap in `support.js` fetches React UMD scripts from unpkg.com:
     ```javascript
     var REACT_URL = "https://unpkg.com/react@18.3.1/umd/react.production.min.js";
     var REACT_DOM_URL = "https://unpkg.com/react-dom@18.3.1/umd/react-dom.production.min.js";
     ```
     *Action required*: Must be served locally from the application directory.
3. **Babel Standalone Library (Remote CDN)**:
   - `support.js` references Babel standalone on unpkg:
     ```javascript
     var BABEL_URL = "https://unpkg.com/@babel/standalone@7.26.4/babel.min.js";
     ```
     *Action required*: Since the dashboard logic does not require compiling runtime `.jsx` or `.tsx` templates (the DC compiler processes the XML tags in `support.js` without Babel), Babel is not loaded unless `<x-import>` JSX components are used. To ensure a strictly offline environment, we can replace this with a local path or dummy script, or include Babel standalone locally.
4. **Image Assets**:
   - There are no images referenced in `AutoJMS Dashboard.dc.html`.
   - All icons are rendered as inline vector SVGs.
   - Auxiliary files in `extracted/screenshots/` and `extracted/uploads/` are design mockups/previews and are not needed by the application runtime.

---

## 3. Proposed Offline Asset Structure & WebView2 Loading

To achieve a strictly offline WebView2 environment, we propose storing the assets inside the project under `src/AutoJMS/Web/` and using **Virtual Host Name Mapping** to resolve resource requests.

### Clean File Structure (`src/AutoJMS/Web/`)
```
src/AutoJMS/
└── Web/
    ├── index.html                  # Main entry (renamed AutoJMS Dashboard.dc.html)
    ├── support.js                  # Modified Design Component runtime
    ├── react.production.min.js     # React v18.3.1 (Downloaded UMD)
    ├── react-dom.production.min.js # ReactDOM v18.3.1 (Downloaded UMD)
    └── assets/
        ├── css/
        │   └── fonts.css           # Local font imports stylesheet
        └── fonts/
            ├── Inter-Regular.woff2  # Downloaded WOFF2 font files
            ├── Inter-Medium.woff2
            ├── Inter-SemiBold.woff2
            └── Inter-Bold.woff2
```

### MSBuild Integration (`AutoJMS.csproj`)
Include the local assets inside the build output directory so they are packaged alongside the executable:
```xml
<ItemGroup>
  <Content Include="Web\**\*">
    <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
  </Content>
</ItemGroup>
```

### WebView2 Offline Loading Mechanism
Initialize the WebView2 control and map the local `Web` directory to a virtual domain name (`https://autojms.local`). This avoids CORS issues associated with `file://` protocols and allows standard relative paths to resolve locally.

```csharp
// Setup virtual host name mapping inside the CoreWebView2 initialization handler
string localWebFolder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Web");

webView.CoreWebView2.SetVirtualHostNameToFolderMapping(
    "autojms.local",
    localWebFolder,
    CoreWebView2HostResourceAccessKind.Allow
);

// Navigate to the local virtual address
webView.CoreWebView2.Navigate("https://autojms.local/index.html");
```

---

## 4. postMessage Communication Bridge Design

To bridge the C# back-end data services with the HTML/React front-end, a message passing protocol will flow through WebView2's `postMessage` APIs.

### JS to C# Messages (`window.chrome.webview.postMessage`)

| Message Type | Payload Structure | Description |
|---|---|---|
| `SYNC_INVENTORY` | `{}` | Triggered when user clicks "Đồng bộ". C# will fetch fresh data from JMS API. |
| `FORCE_RELOAD_LOCAL`| `{}` | Triggers reloading data from local SQLite database. |
| `EXPORT_DATA` | `{}` | Requests C# to export the current view (or selected lines) to a file. |
| `FETCH_JOURNEY` | `{ waybillNo: "8027..." }` | Triggered on waybill double-click. Requests journey history from SQLite cache or Tracking API. |
| `TOGGLE_STAR` | `{ waybillNo: "8027...", isStarred: true/false }` | Requests C# to persist the "Star/Ghim" state for a waybill in SQLite metadata. |
| `CHANGE_SOURCE` | `{ source: "ALL" \| "LOCAL" \| "PHATLAI" }` | Notifies C# to switch datasets (e.g. fetching Zalo waybills for PHATLAI). |

### C# to JS Messages (`webView.CoreWebView2.PostWebMessageAsJson`)

| Message Type | Payload Structure | Description |
|---|---|---|
| `UPDATE_DATA` | `{ waybills: [ { WaybillNo, NguoiThaoTac, TrangThaiHienTai, ThaoTacCuoi, ThoiGianThaoTac, NhanVienKienVanDe, NguyenNhanKienVanDe, PrintCount, LastTrackedAt, ... } ] }` | Pushes the updated waybill list to the Webview. React handles sorting and filtering. |
| `SYNC_STATUS` | `{ text: "Đang đồng bộ...", isSyncing: true/false }` | Updates the sync progress and status labels in the UI header. |
| `JOURNEY_DATA` | `{ waybillNo: "8027...", statusText: "...", rows: [ { stt, ActionTime, UploadedAt, ActionType, Description, ScanSource, Weight, AttachmentText } ] }` | Pushes journey tracking data to populate the journey list view. |
| `STAR_UPDATED` | `{ starredCodes: { "8027...": true, "8028...": true } }` | Synchronizes the active starred waybill list. |

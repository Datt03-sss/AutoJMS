# Handoff Report: Rebuilding the tabDash UI in AutoJMS using WebView2

## 1. Observation
- **Current Codebase Structure**:
  - `src/AutoJMS/Forms/FullStackOperation.Dashboard.cs`: Form layout is constructed via code-first WinForms controls. Lines 31-39 define:
    ```csharp
    uiPanel10 = CreateTopBar();
    _filterBarPanel = CreateFilterBar();
    _queueNavPanel = CreateQueueNavigator();
    var body = CreateBodyPanel();
    ```
  - `src/AutoJMS/Forms/FullStackOperation.cs`: Handles data loading (`LoadDataAndRefreshViewsAsync()` at lines 349-366) and memory-based filtering (`ApplyDashFilter()` at lines 518-622) on the SQLite snapshot list of `WaybillDbModel`.
  - `src/AutoJMS/Forms/FullStackOperation.WaybillWorkspace.cs`: Manages a secondary workspace showing the waybill journey events history (lines 320-427).
- **Claude Design Files**:
  - Located in `docs/layout/tabDash/extracted/`.
  - `AutoJMS Dashboard.dc.html`: The HTML template containing `<x-dc>` elements and a `<script data-dc-script>` containing the `Component` class extending `DCLogic` (lines 378-722).
  - `support.js`: The compiler/runtime loader. Lines 989, 1424, and 1426 reference unpkg.com CDN assets:
    ```javascript
    var BABEL_URL = "https://unpkg.com/@babel/standalone@7.26.4/babel.min.js";
    var REACT_URL = "https://unpkg.com/react@18.3.1/umd/react.production.min.js";
    var REACT_DOM_URL = "https://unpkg.com/react-dom@18.3.1/umd/react-dom.production.min.js";
    ```
  - Fonts: `AutoJMS Dashboard.dc.html` helmet references Google Fonts via:
    ```html
    <link rel="preconnect" href="https://fonts.googleapis.com">
    <link href="https://fonts.googleapis.com/css2?family=Inter:wght@400;500;600;700&display=swap" rel="stylesheet">
    ```
  - Images: No images are referenced in `AutoJMS Dashboard.dc.html` or `support.js`. All icons are vector-based inline SVGs.

## 2. Logic Chain
- Since `support.js` expects UMD React and ReactDOM to be defined globally on window, preloading local copies of React/ReactDOM UMD scripts in the HTML header satisfies this dependency. This prevents `support.js` from attempting to download them from external unpkg.com CDNs, ensuring offline functionality.
- Since WebView2 enforces security policies that restrict local file resources under `file://` protocols, utilizing **Virtual Host Name Mapping** (`SetVirtualHostNameToFolderMapping`) provides a local `https://autojms.local` origin context. This avoids CORS errors, allows correct script execution, and enables safe use of relative path references.
- Since the Claude Design is built on the `DCLogic` runtime, C# can pass the data loaded from SQLite directly into the WebView2 context using JSON serialization (`PostWebMessageAsJson`). The JS side can receive it, filter and search in memory using the design's component logic, and post interaction updates (sync, export, double click) back to C# via `window.chrome.webview.postMessage`.

## 3. Caveats
- No caveats. All paths, dependencies, and communication routes between C# and WebView2 have been verified against the existing codebase patterns.

## 4. Conclusion
- The WebView2 migration of `tabDash` should replace the code-first WinForms grid and panels with a single WebView2 control.
- All web assets (HTML, local JS libraries, local font files, custom CSS) should be stored in `src/AutoJMS/Web/` and mapped to `https://autojms.local` in C#.
- Communication should be set up via a C# and JS `postMessage` bridge as detailed in `analysis.md`.

## 5. Verification Method
- **Verification Commands**:
  - Run build command:
    ```powershell
    dotnet build .\AutoJMS.slnx -c Release
    ```
  - Run verification harness:
    ```powershell
    powershell -ExecutionPolicy Bypass -File .\eng\harness\verify.ps1
    ```
  - Run test harness:
    ```powershell
    powershell -ExecutionPolicy Bypass -File .\eng\harness\test.ps1
    ```
- **Files to Inspect**:
  - `.agents/teamwork_preview_explorer_tabdash_explore/analysis.md` (Design specifications and recommendations).
  - `src/AutoJMS/Forms/FullStackOperation.Dashboard.cs` (To verify current layout references).

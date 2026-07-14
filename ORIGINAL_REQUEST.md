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

## Follow-up — 2026-06-21T20:15:26Z

# Teamwork Project Prompt

Rebuild the `tabDash` UI in AutoJMS using WebView2. The new UI must be exactly identical to the "Claude Design" file (located in `docs/`) and operate entirely offline using local HTML/CSS/JS. Business logic remains in C# and communicates via a `postMessage` bridge.

Working directory: d:\v1.2605.2(new-test)
Integrity mode: development

## Requirements

### R1. Local Offline WebView2 Host
Integrate a WebView2 control into `FullStackOperation.cs` (specifically within the `tabDash` tab). The web content must be completely static and local (no CDNs, no remote fonts, no remote tailwind runtime). The compiled static files should be structured nicely (e.g., `index.html`, `app.css`, `app.js`, `assets/`).

### R2. Phased Integration
Implement a phased rollout strategy:
- Phase 1: Embed the raw HTML/CSS from the Claude Design into the project.
- Phase 2: Establish the `postMessage` bridge between C# and JS.
- Phase 3: Bind real data from C# services (SQLite, JMS API, Journey service) to the UI.
- Phase 4: Replace the old grid.

### R3. Strict Architectural Boundary
The C# layer must retain all business logic (JMS API handling, DB repository, print logic). The WebView2 layer is strictly a dumb view that sends actions and receives state updates via `window.chrome.webview.postMessage`.

## Acceptance Criteria

### Execution & Architecture
- [ ] `index.html` loads perfectly when the computer is disconnected from the internet.
- [ ] The JS to C# bridge is functional (a test message from JS is received by C# and vice versa).
- [ ] The WebView2 control is hosted within `FullStackOperation.cs` inside the existing WinForms architecture.

### Constraints
- [ ] `Main.cs` and `Main.Designer.cs` are completely untouched.
- [ ] No changes leak into HOME, DKCH, TRACKING, PRINT, ABOUT, or release/installer scripts.
- [ ] No remote CDN URLs exist in the final HTML/CSS files.

## Follow-up — 2026-06-22T03:27:10Z

# Teamwork Project Prompt

Fix the WebView2 integration in `FullStackOperation.Dashboard.cs`. The WebView2 control (the "red area") must be placed strictly inside the `tabDash` page, preserving the native WinForms `TabControl` headers (the "green area" containing Dashboard, Thời hiệu, CHATBOT). Do not obscure the top navigation tabs.

Working directory: d:\v1.2605.2(new-test)
Integrity mode: development

## Requirements

### R1. Correct WebView2 Parent Container
Modify `FullStackOperation.Dashboard.cs` (or where the UI is built) so that the WebView2 control is added exclusively to the `tabDash` TabPage's `Controls` collection. It must not be added to the Form itself or to a container that obscures the `TabControl`.

### R2. TabControl Preservation
Ensure the native WinForms `TabControl` remains visible and fully functional, allowing the user to switch between "Dashboard", "Thời hiệu", and "CHATBOT".

### R3. Visual Layout
The WebView2 control should have `Dock = DockStyle.Fill` within the `tabDash` page so it correctly fills the designated content area without overlapping the top navigation area.

## Acceptance Criteria

### UI Constraints
- [ ] The WinForms TabControl headers are visible at the top of the window.
- [ ] Clicking on "Dashboard" shows the WebView2 UI perfectly filling the tab body.
- [ ] Clicking on "Thời hiệu" or "CHATBOT" shows their respective native WinForms UIs.

### Code Constraints
- [ ] `Main.cs`, `HOME`, `DKCH`, `TRACKING`, `PRINT` and `ABOUT` are completely untouched.
- [ ] No changes leak into release/installer scripts or backend logic.

## Follow-up — 2026-06-22T03:39:26Z

# Teamwork Project Prompt

Fix the UI architecture of the `tabDash` WebView2 integration. Remove the redundant "fake" desktop title bar from the local HTML design and style the native SunnyUI `UIForm` TitleBar to match the design's dark theme, ensuring the native `TabControl` is properly preserved in the correct visual hierarchy.

Working directory: d:\v1.2605.2(new-test)
Integrity mode: development

## Requirements

### R1. Remove Fake HTML TitleBar
Modify the static web assets (`src/AutoJMS/Web/index.html`, CSS, or JS) to completely remove or hide the custom desktop title bar (the dark header containing the logo, window title, and minimize/maximize/close buttons).

### R2. Style Native TitleBar
Modify the native `FullStackOperation` form (the `UIForm`) to change its `TitleColor`, `TitleForeColor`, and any other necessary SunnyUI TitleBar properties so that the native WinForms title bar perfectly matches the dark aesthetic of the removed HTML header.

### R3. Preserve TabControl Hierarchy
Ensure the native WinForms `TabControl` remains fully visible and functional directly below the newly styled native TitleBar, with the WebView2 control filling the `tabDash` TabPage area below it. Do not obscure or overlay the TabControl.

## Acceptance Criteria

### Visual & Functional Checks
- [ ] The `index.html` loads within the WebView2 without rendering its own window controls or duplicate title bar.
- [ ] The main `FullStackOperation` window title bar is dark-themed, matching the design.
- [ ] The `Dashboard | Thời hiệu | CHATBOT` tabs remain fully visible and clickable.
- [ ] The `postMessage` bridge and all WebView2 dashboard functionalities remain 100% operational.

### Constraints
- [ ] `Main.cs` and `Main.Designer.cs` are completely untouched.
- [ ] No changes leak into HOME, DKCH, TRACKING, PRINT, ABOUT, or release/installer scripts.

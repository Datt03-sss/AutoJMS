## 2026-06-21T20:15:26Z

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

## Follow-up — 2026-06-22T02:51:29Z

# Teamwork Project Prompt

Refactor `FullStackOperation.cs` to become a full-screen, single-page WebView2 host. Remove the native WinForms `UITabControl` (the "green box" containing Dashboard, Thời hiệu, CHATBOT) and the native form title bar, expanding the WebView2 UI (the "red box") to fill the entire application window.

Working directory: d:\v1.2605.2(new-test)
Integrity mode: development

## Requirements

### R1. Remove Native Tabs and Title Bar
In `FullStackOperation.Layout.cs` (and related files), completely remove the `uiTabControl1` and its associated tab pages (`tabDash`, `tabChat`, etc.). Hide or remove the native WinForms/SunnyUI title bar so the form has no native headers. 

### R2. Expand WebView2 to Full Screen
Dock the existing WebView2 control (`_webView`) directly to the `FullStackOperation` form's `Controls` collection with `DockStyle.Fill`. The WebView2 dashboard must occupy 100% of the window's client area.

### R3. Clean Up Legacy WinForms UI Code
Safely remove any unused UI construction code that was previously used to build the native `tabDash` or `tabChat` layouts, ensuring no compilation errors remain.

## Acceptance Criteria

### Execution & Architecture
- [ ] `UITabControl` is completely removed from `FullStackOperation`.
- [ ] The `_webView` control is added directly to `this.Controls` and fills the entire form.
- [ ] The native form title bar is hidden or removed.
- [ ] The project compiles successfully with `0` errors.

### Constraints
- [ ] `Main.cs` and `Main.Designer.cs` are completely untouched.
- [ ] No changes leak into HOME, DKCH, TRACKING, PRINT, ABOUT, or backend scripts.

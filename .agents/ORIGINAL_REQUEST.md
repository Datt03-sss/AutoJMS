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

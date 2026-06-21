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

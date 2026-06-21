# Project Plan: Rebuild tabDash UI with WebView2

## Architecture
- **Host**: Hosted within `FullStackOperation.cs` under the existing WinForms architecture. WebView2 control will be added to the `tabDash` control.
- **Offline Files**: Static web files (`index.html`, `app.css`, `app.js`, and local assets) stored in a local folder (e.g., `src/AutoJMS/Web/` or `src/AutoJMS/Resources/Web/` or similar, depending on the project structure) and copied to output or loaded via local folder mapping/virtual host name or custom scheme.
- **Communication Bridge**: Asynchronous messaging via `window.chrome.webview.postMessage` (JS -> C#) and `CoreWebView2.PostWebMessageAsJson` (C# -> JS).
- **Strict Separation**: WebView2 is a dumb view. C# maintains SQLite repositories, JMS API calls, and journey logic.

## Milestones

| # | Name | Scope | Dependencies | Status |
|---|------|-------|-------------|--------|
| 1 | Explore & Setup | Explore codebase, identify existing tabDash controls in `FullStackOperation.*.cs`, set up local static assets, extract Claude Design files, clean CDN references, and choose WebView2 loading technique. | None | PLANNED |
| 2 | Phase 1: Local Host | Integrate WebView2 control in `FullStackOperation.cs` (tabDash), configure virtual host mapping or file scheme, and verify raw HTML/CSS loads successfully offline. | M1 | PLANNED |
| 3 | Phase 2: Bridge Setup | Establish the C# and WebView2 `postMessage` bridge. Send test messages between JS and C# to verify bidirectional communication. | M2 | PLANNED |
| 4 | Phase 3: Data Binding | Implement message handlers to fetch real data from SQLite, JMS API, and Journey service and push updates to the UI, rendering the Claude Design dynamically. | M3 | PLANNED |
| 5 | Phase 4: Grid Replacement | Complete integration, handle action events (e.g., clicking on rows, triggering printing/journey actions), remove old WinForms grids, run full builds, verify functionality. | M4 | PLANNED |
| 6 | E2E Testing & Audit | Run all verification steps and Forensic Audit to ensure compliance with AGENTS.md rules. | M5 | PLANNED |

## Interface Contracts (JS ↔ C#)

### JS to C# Actions (`postMessage`)
```json
{
  "action": "loadData" | "performAction",
  "data": { ... }
}
```

### C# to JS Updates (`PostWebMessageAsJson`)
```json
{
  "type": "stateUpdate" | "notification",
  "payload": { ... }
}
```

## Constraints Checklist
- [ ] `Main.cs` and `Main.Designer.cs` remain completely untouched.
- [ ] No changes leak into HOME, DKCH, TRACKING, PRINT, ABOUT, or release/installer scripts.
- [ ] No remote CDN URLs exist in the final HTML/CSS files.
- [ ] Build and verify commands must pass.
- [ ] Forensic Auditor verdict is CLEAN.

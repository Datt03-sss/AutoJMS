# Project Plan: Fix WebView2 Parent Container Layout in FullStackOperation

## Architecture
- **Host**: WebView2 control must be housed strictly within the `tabDash` TabPage container inside `uiTabControl1` on `FullStackOperation`.
- **Top Tab Headers**: The top tab headers of `uiTabControl1` ("Dashboard", "Thời hiệu", "CHATBOT") must be visible and clickable at all times.
- **Strict Tab Isolation**: WebView2 must have `Dock = DockStyle.Fill` inside `tabDash` only. It must not overlap, cover, or be added to the parent form or any other panel that obscures the tab headers.

## Milestones

| # | Name | Scope | Dependencies | Status |
|---|------|-------|-------------|--------|
| 1 | Explore WebView2 Layout | Investigate how `_webView` and `tabDash` are laid out. Identify the root cause of the tab headers being obscured by the WebView2 area (e.g., parent container assignments, layout order, panel bounds). | None | PLANNED |
| 2 | Implementation of Layout Fix | Apply lock in `.agent-lock.md`, modify `FullStackOperation.Dashboard.cs` (or other layout files) to place `_webView` strictly inside `tabDash` and ensure native tab headers remain visible, build the project to verify. | M1 | PLANNED |
| 3 | Verification & Auditor Gate | Run build, tests, check project structure, and run Forensic Auditor to verify integrity and correctness. Release agent lock. | M2 | PLANNED |

## Constraints Checklist
- [ ] `Main.cs` and `Main.Designer.cs` remain completely untouched.
- [ ] No changes leak into HOME, DKCH, TRACKING, PRINT, ABOUT, or release/installer scripts.
- [ ] WebView2 parent must be strictly `tabDash`.
- [ ] Native WinForms TabControl headers remain fully functional and visible.
- [ ] Build and verify commands must pass.
- [ ] Forensic Auditor verdict is CLEAN.

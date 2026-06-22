# Project: WebView2 FullScreen Refactoring

## Architecture
- `FullStackOperation` form hosts WebView2 control `_webView` filling the entire screen (full-screen, single-page WebView2 host).
- Completely remove the native WinForms `uiTabControl1` and its associated tab pages (`tabDash`, `tabChat`, etc.).
- Hide/remove the native WinForms/SunnyUI title bar by configuring `ShowTitle = false` and setting form padding to zero so the WebView2 control occupies 100% of the client area.
- Clean up obsolete WinForms layout code and event handlers (e.g. tabDash/tabChat layouts, filters, toolbars) while keeping the WebView2 dashboard initialization and message posting logic intact.

## Milestones
| # | Name | Scope | Dependencies | Status |
|---|------|-------|-------------|--------|
| 1 | Exploration & Detailed Plan | Catalog all obsolete layout, event handlers, and field definitions in `FullStackOperation.*.cs` | None | DONE |
| 2 | Refactoring FullStackOperation & Code Cleanup | Modify `FullStackOperation.Layout.cs`, `FullStackOperation.cs`, `FullStackOperation.Fields.cs`, etc. to host WebView2 as full-screen and clean up unused code | M1 | IN_PROGRESS |
| 3 | Build & Verification | Restore, build, and run the verification script `verify.ps1` to ensure zero compile errors and zero test failures | M2 | PLANNED |
| 4 | Git Commit & Push | Commit modifications and push to remote `main` branch under workspace lock rules | M3 | PLANNED |

## Interface Contracts
### FullStackOperation ↔ WebView2
- `_webView` is docked with `DockStyle.Fill` directly inside `FullStackOperation` form controls.
- WebView2 initialized asynchronously as before and maps folder `https://autojms.local` to resources.
- Message dispatching via `PostWebMessageAsJson` remains functional.

## Code Layout
- `src/AutoJMS/Forms/FullStackOperation.cs` - Code-behind & initialization logic
- `src/AutoJMS/Forms/FullStackOperation.Layout.cs` - Code-first UI layout construction
- `src/AutoJMS/Forms/FullStackOperation.Fields.cs` - Variable & field declarations
- `src/AutoJMS/Forms/FullStackOperation.Dashboard.cs` - Dashboard methods & WebView2 integration
- `src/AutoJMS/Forms/FullStackOperation.Events.cs` - Event registrations & handlers
- `src/AutoJMS/Forms/FullStackOperation.Chatbot.cs` - Legacy Zalo chatbot UI and methods

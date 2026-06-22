# Original User Request

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

## Follow-up — 2026-06-22T02:51:55Z

You are the Project Orchestrator.
Your working directory is d:\v1.2605.2(new-test)\.agents\orchestrator_fullscreen.
Your task is to refactor FullStackOperation.cs to become a full-screen, single-page WebView2 host.
Follow all requirements in d:\v1.2605.2(new-test)\.agents\orchestrator_fullscreen\ORIGINAL_REQUEST.md.
Also respect the workspace lock rules and required workflow defined in AGENTS.md.
Please create a detailed plan in your working directory, update progress.md, and coordinate specialists (like explorers, workers, and reviewers) to do the actual code changes and verification.
Keep progress.md updated. When all requirements are satisfied, verified, built successfully, and pushed, report success by sending a message to me.

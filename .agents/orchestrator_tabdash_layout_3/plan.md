# Plan - Fix tabDash WebView2 UI Architecture

This plan aims to resolve the UI issues with the `tabDash` WebView2 integration by removing the fake title bar in HTML, styling the native SunnyUI `UIForm` TitleBar, and ensuring the native TabControl is correctly preserved and visible.

## Steps

### Step 1: Lock and Workspace Preparation
- Acquire the single-writer workspace lock in `.agent-lock.md`.
- Pull the latest changes from `origin/main` to make sure we work on a clean, up-to-date repo.

### Step 2: Investigation (Explorer)
- Dispatch an Explorer to review:
  - `src/AutoJMS/Web/index.html` to find the fake title bar HTML/CSS elements.
  - `src/AutoJMS/Forms/FullStackOperation.Dashboard.cs`, `FullStackOperation.cs`, `FullStackOperation.Designer.cs`, `FullStackOperation.Layout.cs`, etc. to see how the form title bar is styled and how `tabDash` controls and the WebView2 control are added.
  - Identify what SunnyUI property changes are needed to dark-theme the native title bar.
  - Formulate the exact code edits needed.

### Step 3: Implementation (Worker)
- Dispatch a Worker to implement:
  - Remove/hide the custom HTML/CSS desktop title bar from `src/AutoJMS/Web/index.html`.
  - Style the native `FullStackOperation` Form (UIForm) TitleBar to match the design's dark theme (setting `TitleColor`, `TitleForeColor`, etc.).
  - Ensure the native `TabControl` is properly positioned and not obscured by WebView2 or other panels.
  - Run the build commands to verify compilation succeeds.

### Step 4: Verification (Reviewer & Auditor)
- Dispatch a Reviewer and Forensic Auditor to:
  - Verify that compilation succeeds and the app builds cleanly.
  - Verify that `Main.cs` and `Main.Designer.cs` remain completely untouched.
  - Verify that no changes leak to HOME, DKCH, TRACKING, PRINT, ABOUT, or release/installer scripts.
  - Verify that the HTML title bar was removed, and the native title bar was styled correctly.
  - Ensure the audit reports CLEAN and all tests pass (if any test suite exists, or run the harness verification).

### Step 5: Lock Release & Push
- Commit and push changes to `origin/main`.
- Release the single-writer lock in `.agent-lock.md`.
- Report completion and handoff.

# Owner Testing & Validation Guide

This document guides the project Owner on testing local commits, performing smoke tests, reporting bugs to the AI, and executing rollbacks.

---

## 1. Post-Commit Smoke Test Checklist

Run these tests locally after the AI agent commits changes:

- [ ] **App Compile**: Build the solution in VS or run `powershell -ExecutionPolicy Bypass -File .\eng\harness\build.ps1`.
- [ ] **App Launch**: Double-click `AutoJMS.exe` in `src/AutoJMS/bin/Release/net8.0-windows/win-x64/` to ensure it launches without boot errors.
- [ ] **Tiers Resolution**:
  - Verify log shows successful license check.
  - Confirm BASE tier displays the 5 standard tabs.
  - Confirm ULTRA tier enables the auto-sync daemon.

---

## 2. Tab-by-Tab Smoke Checks

### HOME Tab
- [ ] Verify `jms.jtexpress.vn` loads.
- [ ] Verify you can log in manually or interact with the browser surface.

### DKCH Tab
- [ ] Open the tab and verify the return shipment registration controls are responsive.
- [ ] Try a test registration (if applicable) to verify script injection safety.

### TRACKING Tab
- [ ] Input a sample waybill number and click Search.
- [ ] Confirm status log history resolves and displays in the datagrid.

### PRINT Tab
- [ ] Click Print on a sample label.
- [ ] Verify the PDF renders in the preview pane and routes cleanly to your printer.

### ABOUT Tab
- [ ] Confirm version metadata is accurate.
- [ ] Click Update Check and verify it checks remote manifests without crashing.

### ULTRA Tier (FullStackOperation)
- [ ] Type `"DASH"` in the HOME URL bar.
- [ ] Verify the dashboard form launches, loads waybill stats, and updates Zalo chat alerts.

---

## 3. How to Handle Issues

### Scenario A: Minor Bug
If a commit introduces a minor issue (e.g. alignment problem or small logic bug):
1. Keep the app open and copy the debug logs from `AppData/logs/debug.log`.
2. Send the error log and a short description back to the AI agent.
3. The AI agent will fix it and commit a new patch on local `main`.

### Scenario B: Critical Crash
If the app fails to compile or crashes at startup:
1. Run the revert script to cleanly back out the commit:
   ```powershell
   powershell -ExecutionPolicy Bypass -File .\eng\git\revert-last-commit.ps1
   ```
2. Type `"REVERT"` to apply. This creates a clean revert commit.
3. Restart the task and instruct the AI to re-implement safely.

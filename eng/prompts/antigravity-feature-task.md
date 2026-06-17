# Antigravity — Feature Coding Task Prompt

This template defines the prompt format and system guidelines for Antigravity when executing code tasks on AutoJMS.

---

## 1. Safety Checklist
Before starting the coding task:
- Verify that none of the files to be modified are on the **🔒 Frozen** list (e.g. `Program.cs`, `Main.cs`, `VelopackUpdateService.cs`).
- Confirm that the target branch is a feature branch: `agent/antigravity/<task-description>`.
- Run the baseline verification: `powershell -ExecutionPolicy Bypass -File .\eng\harness\verify.ps1`.

---

## 2. Coding Guidelines
- **SunnyUI Integration**: Maintain existing UI styles. Do not modify the SunnyUI control styles.
- **Thread Safety**: Ensure all calls to WebView2 or control values run on the UI thread using `UiThread` helpers.
- **Base vs Ultra Isolation**: Ensure no background sync timers or databases are activated on the BASE tier.
- **Log Masking**: Mask all captured auth tokens to `first4...last4` before logging.

---

## 3. Post-Implementation Verification
When the coding changes are done, execute:
```powershell
powershell -ExecutionPolicy Bypass -File .\eng\harness\verify.ps1
```
All four gates must compile and pass without errors.

---

## 4. Final Output Format
Return the completion status in the following structure:
- **Summary**: Key achievements.
- **Files Modified**: List of paths.
- **Verification Output**: Copy of the `verify.ps1` results block.
- **Risks & Issues**: List of any design or security warnings.

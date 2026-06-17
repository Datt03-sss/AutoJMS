# AutoJMS Quality Gates

Every agent pull request (PR) must successfully pass these quality gates before requesting owner review.

---

## Gate 1: Project & Solution Build
* **Command**:
  ```powershell
  powershell -ExecutionPolicy Bypass -File .\eng\harness\build.ps1
  ```
* **Validation**:
  - `dotnet restore .\AutoJMS.slnx` must complete with exit code 0.
  - `dotnet build .\AutoJMS.slnx -c Release --no-restore` must compile successfully.
* **Tolerance**:
  - Warnings: Permitted (e.g. obsolete APIs, PdfiumViewer platform target warnings, nullable annotations).
  - Errors: Zero tolerance. Any build error fails this gate.

---

## Gate 2: Automated Tests
* **Command**:
  ```powershell
  powershell -ExecutionPolicy Bypass -File .\eng\harness\test.ps1
  ```
* **Validation**:
  - If C# test projects are found in the solution: All tests must run and pass.
* **Tolerance**:
  - If no test projects exist: Generates a warning but passes. It is not a blocker.

---

## Gate 3: Secret Scan
* **Command**:
  ```powershell
  powershell -ExecutionPolicy Bypass -File .\eng\harness\check-secrets.ps1
  ```
* **Validation**:
  - Git-tracked files must contain no private API keys, credentials, or private certificate blocks.
  - `.gitignore` must contain all required security exclusion patterns.
* **Tolerance**:
  - Zero tolerance. Any leaked secret pattern fails the gate immediately.

---

## Gate 4: Project Structure Conformity
* **Command**:
  ```powershell
  powershell -ExecutionPolicy Bypass -File .\eng\harness\check-project-structure.ps1
  ```
* **Validation**:
  - Validates that required directories (`docs/agent/`, `eng/harness/`, etc.) exist.
  - Verifies solution integrity and ensures core file paths are consistent.
* **Tolerance**:
  - Zero tolerance. Missing directories or inconsistent solutions fail the gate.

---

## Combined Harness Run

Agents must run the unified verification script:
```powershell
powershell -ExecutionPolicy Bypass -File .\eng\harness\verify.ps1
```
This runs Gate 1 → Gate 2 → Gate 3 → Gate 4. If any gate fails, the script returns exit code 1.

---

## Manual Review Checklist (For Agents)
- [ ] No changes have been made to frozen files (unless explicitly requested).
- [ ] No background jobs have been added to the BASE tier.
- [ ] `TierRuntimePolicy` property flags are used for all tier capabilities checks.
- [ ] All WebView2 operations are marshaled to the UI thread using `UiThread` methods.
- [ ] The `ABOUT` tab remains the last tab in the UI shell.

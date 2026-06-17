# Claude Code — Implementation Prompt

> System prompt template for Claude Code sessions on AutoJMS.

---

## Pre-Task Preparations
Before starting any coding task, read these guidelines in order:
1. `CLAUDE.md` in repository root.
2. `AGENTS.md` in repository root.
3. `docs/agent/PROJECT_BRIEF.md` (Tech stack, version and tiers specs).
4. `docs/agent/TAB_OWNERSHIP.md` (Boundaries between tabs and UI shell).
5. `docs/agent/SECRETS_POLICY.md` (Secrets rules and token masking).
6. `docs/agent/REFACTOR_RULES.md` (Safe vs risky refactoring boundaries).
7. `docs/agent/BRANCHING_RULES.md` (Branch naming and PR workflow).

---

## Coding Workflow

### 1. Understand
- What is the objective?
- Which files will be modified?
- Are any target files on the **🔒 Frozen** list? If yes, stop and ask the owner.
- Does this task affect tier separation (BASE vs ULTRA)?

### 2. Plan
- Document target files and the minimum code modification required.
- Confirm that no hardcoded credentials or unmasked tokens will be added.
- Submit the plan for review (if required by agent planning guidelines).

### 3. Implement
- Ensure you are working on a dedicated feature branch: `agent/claude/<description>`.
- Write clean, minimal C# code conforming to WinForms + SunnyUI styles.
- Marshal all WebView2 or UI control queries to the UI thread using control invocation.

### 4. Verify
- Run the full verification suite locally:
  ```powershell
  powershell -ExecutionPolicy Bypass -File .\eng\harness\verify.ps1
  ```
- All gates (Build, Tests, Secrets, Project Structure) must pass successfully with exit code 0.

### 5. Report
Provide the final task completion report:
- **Summary**: Description of work done.
- **Files Created**: List of new files.
- **Files Changed**: List of modified files.
- **Verify Result**: Output of `verify.ps1`.
- **Risks**: Security, build, or stability risks identified.
- **Suggested Commit Message**: Git-compliant commit text.

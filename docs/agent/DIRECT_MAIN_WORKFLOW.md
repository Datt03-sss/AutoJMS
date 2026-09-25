# Direct-Main Coding Workflow Guide

This document describes the direct-main development workflow for AI agents (Antigravity and Claude Code) on the AutoJMS project.

---

## 1. Core Workflow Flowchart

```
[Agent Edits Code] ──> [Run verify.ps1] ──> [ai-commit.ps1] ──> [Owner Tests locally]
                                                                        │
                                                                 (If bug found)
                                                                        │
                                                                        ▼
                                                             [ai-commit.ps1 (Fix)]
                                                                        or
                                                           [revert-last-commit.ps1]
```

---

## 2. Developer Rules
1. **Always develop on `main`**: Do not create or switch branches during this phase.
2. **Build Before Commit**: Commit only after the Release build (0 warnings / 0 errors) and `verify.ps1` pass. `ai-commit.ps1` runs both for you; committing by hand is equally fine as long as you stage explicit paths (never `git add .`).
3. **Push After a Passing Build, Never a Release**: `git push origin main` is allowed once build and verify pass (see `AGENTS.md` § Permissions). Never force-push, and never run `-Upload` release scripts or bump the version unless the Owner asks.
4. **No History Rewrites**: Never run `git rebase` or `git reset` on public commits. Fix issues by committing fixes or using `revert-last-commit.ps1`.

---

## 3. Script Reference

### Check workspace status:
```powershell
powershell -ExecutionPolicy Bypass -File .\eng\git\status-main.ps1
```
Prints active branch (`main`), git status, and the last 10 local commits.

### Stage and commit changes safely:
```powershell
powershell -ExecutionPolicy Bypass -File .\eng\git\ai-commit.ps1 -Message "fix(tab-print): resolve double printing spacing" -Paths "src/AutoJMS/Printing/PrintService.cs"
```
Restores dependencies, compiles in Release mode, executes verification tests, stages only the comma-separated `-Paths` (never `.`), commits them, and pushes to `origin/main`.

### Revert the latest commit:
If the Owner reports that the latest commit broke application execution:
```powershell
powershell -ExecutionPolicy Bypass -File .\eng\git\revert-last-commit.ps1
```
Displays commit details and requires typing `"REVERT"` to apply a clean rollback commit.

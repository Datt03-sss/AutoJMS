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
2. **Build Before Commit**: Never run `git commit` manually. Always use the automated script `ai-commit.ps1` which forces a release build or quality harness execution before committing.
3. **No Pushes**: AI agents are strictly prohibited from executing `git push` or `-Upload` release scripts. Pushing to origin remains an Owner-only command.
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
powershell -ExecutionPolicy Bypass -File .\eng\git\ai-commit.ps1 -Message "fix(tab-print): resolve double printing spacing"
```
Restores dependencies, compiles in Release mode, executes verification tests, stages files, and commits them locally.

### Revert the latest commit:
If the Owner reports that the latest commit broke application execution:
```powershell
powershell -ExecutionPolicy Bypass -File .\eng\git\revert-last-commit.ps1
```
Displays commit details and requires typing `"REVERT"` to apply a clean rollback commit.

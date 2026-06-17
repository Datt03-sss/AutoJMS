# GitHub Shared-Main Workflow Guide

This document defines the GitHub Shared-Main Workflow for AI agents and human developers on the AutoJMS project. Under this model, the GitHub repository acts as the central source of truth for Antigravity, Claude Code, and ChatGPT.

---

## 1. Concurrency & Flow
- **Active Branch**: All development takes place on the `main` branch.
- **Commit Authorization**: AI agents commit directly to local `main` after compilation validation.
- **Push Authorization**: AI agents push directly to `origin/main` after verification passes.
- **Validation**: The Owner retrieves changes from GitHub, tests them locally, and reports any issues.

---

## 2. Step-by-Step Task Workflow

### Step 1: Pre-Task Pull Check
Before modifying any files, ensure your workspace is synchronized with the latest GitHub commit:
```powershell
git switch main
git pull --ff-only origin main
git status
```

### Step 2: Acquire Lock
Set the lock state in `.agent-lock.md`:
- **Current Writer**: `Antigravity` (or `Claude Code` / `ChatGPT`)
- **Mode**: `SHARED_MAIN_WRITE`
- **Scope**: `<short description of the files you intend to edit>`

### Step 3: Implement Edits
Make minimal, atomic code changes within the declared scope. Do not touch protected folders (auth, updates, releases, forms shells).

### Step 4: Verification, Commit, & Push
Run the automated commit helper script. This handles build verification, commits, and remote synchronization in a single safe sequence:
```powershell
powershell -ExecutionPolicy Bypass -File .\eng\git\ai-commit.ps1 -Message "fix(tab-dkch): adjust J&T automation selectors"
```
*Note*: The commit will be blocked if verification compiles throw errors. Pushing will execute only if the remote `origin` is configured.

### Step 5: Release Lock
Reset the lock properties in `.agent-lock.md` back to default (`READ_ONLY`).

### Step 6: Post-Task Report
Submit the mandatory report containing commit details, hashes, test checklists, and risks back to the Owner and ChatGPT.

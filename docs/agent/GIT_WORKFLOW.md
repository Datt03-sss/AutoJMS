# Git Workflow Guide for AutoJMS

This guide defines the Git workflow rules and automation scripts for AI agents and human developers on the AutoJMS project.

---

## 1. Core Rule: Keep Main Clean
- **Never commit or push directly to `main`.**
- All code changes must be executed on isolated feature branches.
- Merges to `main` are restricted to the human **Owner** via manual pull requests.

---

## 2. Developer & Agent Workflow

### Step 1: Sync Main Branch
Before starting work on a new task, always fetch and synchronize the local `main` branch:
```powershell
powershell -ExecutionPolicy Bypass -File .\eng\git\sync-main.ps1
```
This script checks if the workspace is clean, switches to the `main` branch, fetches remote changes, and performs a safe `git pull --ff-only`.

### Step 2: Create a Feature Branch
To create a clean branch matching the conventions:
```powershell
powershell -ExecutionPolicy Bypass -File .\eng\git\new-branch.ps1 -BranchName "agent/claude/task-name"
```
Convention: `agent/<agent-name>/<description>` or `dev/<name>/<description>`.

### Step 3: Setup Worktrees for Task Isolation
Using `git worktree` prevents lock files and pollution of the active directory.

#### For Antigravity:
1. Create a worktree parallel to your main folder:
   ```powershell
   powershell -ExecutionPolicy Bypass -File .\eng\git\create-worktree.ps1 -Path "../autojms-task1" -BranchName "agent/antigravity/my-task" -NewBranch
   ```
2. Open the directory `../autojms-task1/` in Antigravity.
3. Keep the main folder untouched while coding.

#### For Claude Code:
1. Initialize the worktree:
   ```powershell
   powershell -ExecutionPolicy Bypass -File .\eng\git\create-worktree.ps1 -Path "../autojms-claude" -BranchName "agent/claude/my-task" -NewBranch
   ```
2. Navigate to the worktree folder:
   ```bash
   cd ../autojms-claude
   ```
3. Run `claude` in that directory to execute instructions.

### Step 4: Validate Changes (Pre-Merge Gates)
Before staging or pushing, run the pre-merge check to compile the code and scan for secrets:
```powershell
powershell -ExecutionPolicy Bypass -File .\eng\git\pre-merge-check.ps1
```
If this script fails, fix the errors or secrets before attempting to push.

### Step 5: Commit & Push Branch
AI agents are allowed to push their feature branches to the remote repository for owner review:
```bash
git add .
git commit -m "feat(module): description of changes"
git push origin agent/claude/task-name
```
*Never force-push (`--force`) to remote branches unless resolving a rebase conflicts.*

### Step 6: Rebasing on Main
If `main` has moved forward since you checked out the feature branch:
```bash
git checkout agent/claude/task-name
git fetch origin
git rebase origin/main
```
Resolve any conflicts, test, and push the updated branch.

---

## 3. Rollback & Disaster Recovery

### Case A: Workspace is dirty with bad edits
To discard all uncommitted changes and return to the latest commit:
```bash
git reset --hard HEAD
git clean -fd
```

### Case B: Deleting a Worktree Folder
If a worktree edit was corrupted or is no longer needed, clean it up safely:
```powershell
powershell -ExecutionPolicy Bypass -File .\eng\git\cleanup-worktree.ps1 -Path "../autojms-task1" -Force
```

### Case C: Soft rollback of commits
If you committed changes locally but wish to undo them while keeping the files edited:
```bash
git reset --soft HEAD~1
```

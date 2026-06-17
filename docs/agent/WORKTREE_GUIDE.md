# Git Worktree Operational Guide

This guide describes how to isolate tasks using Git Worktrees, enabling concurrent feature development for the project Owner, Antigravity, and Claude Code.

---

## 1. What is a Git Worktree?
A Git worktree allows you to check out multiple branches of the same repository in separate local directories. It shares the underlying `.git` object database but isolates the working directory files and active branch pointers.

---

## 2. Worktree Setup for AI Tools

### For Antigravity IDE
When developing a new feature in the Antigravity IDE, do not pollute the main repository folder:
1. **Initialize the Worktree**:
   ```powershell
   powershell -ExecutionPolicy Bypass -File .\eng\git\create-worktree.ps1 -Path "../autojms-antigravity" -BranchName "agent/antigravity/new-feature" -NewBranch
   ```
2. **Open in Antigravity**:
   Open the folder `../autojms-antigravity/` inside your IDE.
3. **Run Code**:
   Build, edit, and run the WinForms app in this isolated folder. All temp settings, `.vs/` configs, and C# caches will not interfere with the master folder.

### For Claude Code CLI
Claude Code runs commands directly on the workspace. To prevent it from staging incomplete code in the main folder:
1. **Initialize the Worktree**:
   ```powershell
   powershell -ExecutionPolicy Bypass -File .\eng\git\create-worktree.ps1 -Path "../autojms-claude" -BranchName "agent/claude/bugfix" -NewBranch
   ```
2. **Navigate & Execute**:
   ```bash
   cd ../autojms-claude
   claude
   ```
3. Claude Code will edit and execute tests strictly in that isolated directory.

---

## 3. Operations inside a Worktree

### Staging & Committing Changes
While inside the worktree directory, you stage and commit just like in a normal repository. Git automatically tracks the correct feature branch:
```bash
git add .
git commit -m "feat(service): implement waybill query pagination"
```

### Pulling & Pushing
Push the branch to the remote origin:
```bash
git push origin agent/claude/bugfix
```

### Rebasing on Main
If main has changed, pull main in your main folder first, then rebase the worktree:
```bash
# In the worktree directory
git fetch origin
git rebase origin/main
```

---

## 4. Rollback and Cleanup

### Aborting Edits
To discard all local changes made in the worktree:
```bash
git reset --hard HEAD
git clean -fd
```

### Removing the Worktree
Once the PR is merged by the Owner, clean up the worktree folder:
1. Navigate to the main directory.
2. Run the cleanup script:
   ```powershell
   powershell -ExecutionPolicy Bypass -File .\eng\git\cleanup-worktree.ps1 -Path "../autojms-claude"
   ```
This deletes the directory and cleans up the git index configuration using `git worktree prune`.

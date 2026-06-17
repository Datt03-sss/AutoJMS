# AutoJMS Claude Code Task Runner
# This script orchestrates task handoff from Antigravity to Claude Code.

$ErrorActionPreference = "Stop"

Write-Host "=============================================" -ForegroundColor Cyan
Write-Host "  AutoJMS Agent Orchestrator: Claude Runner  " -ForegroundColor Cyan
Write-Host "=============================================" -ForegroundColor Cyan

# 1. Read task spec
$taskSpecPath = Join-Path $PSScriptRoot "..\..\tasks\active\claude-task.md"
if (-not (Test-Path $taskSpecPath)) {
    Write-Error "Active task spec not found at $taskSpecPath"
}

Write-Host "Reading task spec from $taskSpecPath..." -ForegroundColor Yellow
$specContent = Get-Content $taskSpecPath -Raw

# Check if spec is approved by owner
if ($specContent -notmatch "(?ms)## Approved by owner\s*\r?\n\s*Yes") {
    Write-Host "Task is not approved by owner. Please set 'Approved by owner' to 'Yes' in tasks/active/claude-task.md. Exiting." -ForegroundColor Yellow
    exit 0
}


# 2. Verify git status and branch
Write-Host "Checking Git configuration..." -ForegroundColor Yellow
$gitStatus = git status
$currentBranch = git branch --show-current

if ($currentBranch -ne "main") {
    Write-Error "Runner must be executed on the 'main' branch. Current branch is '$currentBranch'."
}

# 3. Pull latest changes
Write-Host "Synchronizing with origin/main..." -ForegroundColor Yellow
git pull --ff-only origin main

# 4. Check for dirty working tree (except for lock file or spec file changes)
# We allow spec files or lock file to be modified by Antigravity prior to run.
# But source code must be clean before Claude starts.
$dirtyFiles = git status --porcelain
if ($dirtyFiles) {
    Write-Host "Warning: The working tree has local modifications:" -ForegroundColor Warning
    Write-Host $dirtyFiles -ForegroundColor Yellow
}

# 5. Invoke Claude Code
Write-Host "Invoking Claude Code to implement task spec..." -ForegroundColor Cyan
$claudePrompt = "Please execute the task specified in the active task specification file: tasks/active/claude-task.md. Follow all guidelines and verification rules in docs/agent/CLAUDE_WORKER_RULES.md exactly."

# Run Claude Code. We pass the prompt via -p.
& claude -p $claudePrompt

# 6. Post-execution status print
Write-Host "=============================================" -ForegroundColor Cyan
Write-Host "  Execution Complete. Printing Git Status... " -ForegroundColor Cyan
Write-Host "=============================================" -ForegroundColor Cyan

git status

Write-Host "`nRecent 3 commits on main:" -ForegroundColor Yellow
git log -n 3 --oneline

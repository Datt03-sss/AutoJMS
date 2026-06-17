<#
.SYNOPSIS
    Safely undoes the latest local commit on main.
.DESCRIPTION
    Prints latest commit metadata, prompts the user to input 'REVERT' for
    confirmation, and executes a git revert HEAD operation.
.EXAMPLE
    powershell -ExecutionPolicy Bypass -File .\eng\git\revert-last-commit.ps1
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$Root = Resolve-Path (Join-Path $PSScriptRoot '..\..') | Select-Object -ExpandProperty Path

Write-Host '========================================' -ForegroundColor Cyan
Write-Host '  AutoJMS Revert Latest Commit' -ForegroundColor Cyan
Write-Host '========================================' -ForegroundColor Cyan
Write-Host ''

# 1. Verify git repo
try {
    Push-Location $Root
    $isGit = & git rev-parse --is-inside-work-tree 2>&1
    if ($LASTEXITCODE -ne 0) {
        Write-Host "ERROR: Not a git repository." -ForegroundColor Red
        exit 1
    }
} finally {
    Pop-Location
}

# 2. Check if repo has commits
try {
    Push-Location $Root
    $commitCount = & git rev-list --all --count 2>&1
    if ($LASTEXITCODE -ne 0 -or $commitCount -eq 0) {
        Write-Host "No commits exist in this repository. Revert aborted." -ForegroundColor Yellow
        exit 0
    }
} finally {
    Pop-Location
}

# 3. Print latest commit info
Write-Host "Latest Commit Metadata:" -ForegroundColor Gray
try {
    Push-Location $Root
    & git log -n 1 --color 2>&1 | ForEach-Object { Write-Host "  $_" }
} finally {
    Pop-Location
}
Write-Host ''

# 4. Confirmation Prompt
Write-Host "WARNING: This will create a new commit that reverts all changes from the commit above." -ForegroundColor Yellow
$confirm = Read-Host "To confirm, please type exactly 'REVERT'"
if ($confirm -ne 'REVERT') {
    Write-Host "Revert operation cancelled by user." -ForegroundColor Red
    exit 1
}

# 5. Execute Revert
try {
    Push-Location $Root
    Write-Host "Executing git revert HEAD..." -ForegroundColor Yellow
    & git revert --no-edit HEAD 2>&1 | ForEach-Object { Write-Host "  $_" }
    
    if ($LASTEXITCODE -ne 0) {
        Write-Host "ERROR: Git revert failed." -ForegroundColor Red
        exit 1
    }
    
    Write-Host ""
    Write-Host "Revert commit created successfully." -ForegroundColor Green
    Write-Host "Current Head:" -ForegroundColor Gray
    & git log -n 1 --color 2>&1 | ForEach-Object { Write-Host "  $_" }
} catch {
    Write-Host "ERROR: Revert failed: $_" -ForegroundColor Red
    exit 1
} finally {
    Pop-Location
}

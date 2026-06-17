<#
.SYNOPSIS
    Safety check run before starting a coding task.
.DESCRIPTION
    Verifies that the current branch is not main, checks workspace status,
    and displays active writer locks.
.EXAMPLE
    powershell -ExecutionPolicy Bypass -File .\eng\git\safe-start-task.ps1
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$Root = Resolve-Path (Join-Path $PSScriptRoot '..\..') | Select-Object -ExpandProperty Path

Write-Host '========================================' -ForegroundColor Cyan
Write-Host '  AutoJMS Safe Start Coding Task' -ForegroundColor Cyan
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

# 2. Check branch
try {
    Push-Location $Root
    $branch = & git branch --show-current
    if ($branch -eq 'main') {
        Write-Host "WARNING: You are currently on the 'main' branch!" -ForegroundColor Red
        Write-Host "AI agents and developers should work on feature branches (e.g. agent/<name>/<task>)." -ForegroundColor Red
        Write-Host "Please checkout a feature branch before acquiring the lock." -ForegroundColor Red
        exit 1
    }
    Write-Host "Active Branch: $branch (Safe for development)" -ForegroundColor Green
} finally {
    Pop-Location
}
Write-Host ''

# 3. Check for uncommitted changes
try {
    Push-Location $Root
    $status = & git status --porcelain 2>&1
    if ($status) {
        Write-Host "WARNING: Workspace is dirty. Uncommitted changes exist:" -ForegroundColor Yellow
        $status | ForEach-Object { Write-Host "  $_" -ForegroundColor Yellow }
        Write-Host "It is highly recommended to run checkpoint.ps1 or discard-uncommitted.ps1 before locking a task." -ForegroundColor Yellow
    } else {
        Write-Host "Workspace status: Clean (Ready for development)" -ForegroundColor Green
    }
} finally {
    Pop-Location
}
Write-Host ''

# 4. Display Active Lock Status
$lockPath = Join-Path $Root '.agent-lock.md'
Write-Host "Workspace Lock (.agent-lock.md):" -ForegroundColor Gray
if (Test-Path $lockPath) {
    Get-Content $lockPath | ForEach-Object {
        if ($_ -match 'Current Writer|Mode|Branch|Scope') {
            Write-Host "  $_" -ForegroundColor Cyan
        }
    }
} else {
    Write-Host "  Lock file not found!" -ForegroundColor Red
}
Write-Host ''
exit 0

<#
.SYNOPSIS
    Prints the workspace and agent lock status.
.DESCRIPTION
    Outputs current git branch, untracked/unstaged changes, latest commits,
    and displays the active workspace writer locks.
.EXAMPLE
    powershell -ExecutionPolicy Bypass -File .\eng\git\agent-status.ps1
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$Root = Resolve-Path (Join-Path $PSScriptRoot '..\..') | Select-Object -ExpandProperty Path

Write-Host '========================================' -ForegroundColor Cyan
Write-Host '  AutoJMS Agent Workspace Status' -ForegroundColor Cyan
Write-Host '========================================' -ForegroundColor Cyan
Write-Host ''

# 1. Branch Info
try {
    Push-Location $Root
    $branch = & git branch --show-current
    if (-not $branch) {
        $branch = "No branch checked out (Detached HEAD?)"
    }
    Write-Host "Active Branch: " -NoNewline -ForegroundColor Gray
    Write-Host "$branch" -ForegroundColor Green
} finally {
    Pop-Location
}
Write-Host ''

# 2. Git status overview
try {
    Push-Location $Root
    Write-Host "Git Status Summary:" -ForegroundColor Gray
    $status = & git status -s 2>&1
    if ($status) {
        $status | ForEach-Object { Write-Host "  $_" -ForegroundColor Yellow }
    } else {
        Write-Host "  Clean working directory." -ForegroundColor Green
    }
} finally {
    Pop-Location
}
Write-Host ''

# 3. Last 5 commits
try {
    Push-Location $Root
    $commitCount = & git rev-list --all --count 2>&1
    if ($LASTEXITCODE -eq 0 -and $commitCount -gt 0) {
        Write-Host "Last 5 Commits:" -ForegroundColor Gray
        & git log -n 5 --oneline --color 2>&1 | ForEach-Object { Write-Host "  $_" }
    } else {
        Write-Host "Last 5 Commits: (No commits in this repository yet)" -ForegroundColor Yellow
    }
} catch {
    Write-Host "Last 5 Commits: (Failed to read log)" -ForegroundColor Red
} finally {
    Pop-Location
}
Write-Host ''

# 4. Agent Lock Status
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

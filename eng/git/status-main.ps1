<#
.SYNOPSIS
    Prints repository status and commit history on main.
.DESCRIPTION
    Outputs current branch, unstaged changes summary, recent commit list,
    and Git remote settings.
.EXAMPLE
    powershell -ExecutionPolicy Bypass -File .\eng\git\status-main.ps1
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$Root = Resolve-Path (Join-Path $PSScriptRoot '..\..') | Select-Object -ExpandProperty Path

Write-Host '========================================' -ForegroundColor Cyan
Write-Host '  AutoJMS Direct-Main Repository Status' -ForegroundColor Cyan
Write-Host '========================================' -ForegroundColor Cyan
Write-Host ''

# 1. Branch Info
try {
    Push-Location $Root
    $branch = & git branch --show-current
    Write-Host "Active Branch: " -NoNewline -ForegroundColor Gray
    Write-Host "$branch" -ForegroundColor Green
} finally {
    Pop-Location
}
Write-Host ''

# 2. Short Git Status
try {
    Push-Location $Root
    Write-Host "Workspace Changes (git status --short):" -ForegroundColor Gray
    $status = & git status --short 2>&1
    if ($status) {
        $status | ForEach-Object { Write-Host "  $_" -ForegroundColor Yellow }
    } else {
        Write-Host "  No uncommitted changes." -ForegroundColor Green
    }
} finally {
    Pop-Location
}
Write-Host ''

# 3. Last 10 Commits
try {
    Push-Location $Root
    $commitCount = & git rev-list --all --count 2>&1
    if ($LASTEXITCODE -eq 0 -and $commitCount -gt 0) {
        Write-Host "Last 10 Commits:" -ForegroundColor Gray
        & git log --oneline -10 --color 2>&1 | ForEach-Object { Write-Host "  $_" }
    } else {
        Write-Host "Last 10 Commits: (No commits in this repository yet)" -ForegroundColor Yellow
    }
} catch {
    Write-Host "Last 10 Commits: (Failed to read log)" -ForegroundColor Red
} finally {
    Pop-Location
}
Write-Host ''

# 4. Remotes
try {
    Push-Location $Root
    Write-Host "Git Remotes (git remote -v):" -ForegroundColor Gray
    $remotes = & git remote -v 2>&1
    if ($remotes) {
        $remotes | ForEach-Object { Write-Host "  $_" -ForegroundColor Cyan }
    } else {
        Write-Host "  No remotes configured." -ForegroundColor Yellow
    }
} finally {
    Pop-Location
}
Write-Host ''
exit 0

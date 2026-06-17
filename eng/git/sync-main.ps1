<#
.SYNOPSIS
    Safely syncs the local main branch with remote origin.
.DESCRIPTION
    Verifies git repository, checks for unstaged changes, and pulls updates
    using fast-forward only to ensure main remains clean.
.EXAMPLE
    powershell -ExecutionPolicy Bypass -File .\eng\git\sync-main.ps1
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$Root = Resolve-Path (Join-Path $PSScriptRoot '..\..') | Select-Object -ExpandProperty Path

Write-Host '========================================' -ForegroundColor Cyan
Write-Host '  AutoJMS Git Sync Main' -ForegroundColor Cyan
Write-Host '========================================' -ForegroundColor Cyan
Write-Host ''

# 1. Check if git repo
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

# 2. Check for uncommitted changes
try {
    Push-Location $Root
    $status = & git status --porcelain 2>&1
    if ($status) {
        Write-Host "WARNING: You have uncommitted changes in your workspace:" -ForegroundColor Yellow
        $status | ForEach-Object { Write-Host "  $_" -ForegroundColor Yellow }
        Write-Host "Please commit or stash your changes before syncing main." -ForegroundColor Red
        exit 1
    }
} finally {
    Pop-Location
}

# 3. Check if any commits exist in the repo
try {
    Push-Location $Root
    $commitCount = & git rev-list --all --count 2>&1
    if ($LASTEXITCODE -ne 0 -or $commitCount -eq 0) {
        Write-Host "INFO: This repository has no commits yet. Skipping remote fetch/pull." -ForegroundColor Yellow
        exit 0
    }
} finally {
    Pop-Location
}

# 4. Check for remote origin
try {
    Push-Location $Root
    $remotes = & git remote 2>&1
    if ($remotes -notcontains 'origin') {
        Write-Host "WARNING: No 'origin' remote found. Skipping fetch/pull." -ForegroundColor Yellow
        exit 0
    }
} finally {
    Pop-Location
}

# 5. Safely sync main
try {
    Push-Location $Root
    Write-Host "Fetching from origin..." -ForegroundColor Yellow
    & git fetch origin 2>&1 | ForEach-Object { Write-Host "  $_" }
    
    # Store current branch
    $currentBranch = & git branch --show-current
    
    if ($currentBranch -ne 'main') {
        Write-Host "Switching to main branch..." -ForegroundColor Yellow
        & git checkout main 2>&1 | ForEach-Object { Write-Host "  $_" }
        if ($LASTEXITCODE -ne 0) {
            Write-Host "ERROR: Failed to switch to main branch." -ForegroundColor Red
            exit 1
        }
    }
    
    Write-Host "Pulling main using --ff-only..." -ForegroundColor Yellow
    & git pull origin main --ff-only 2>&1 | ForEach-Object { Write-Host "  $_" }
    if ($LASTEXITCODE -ne 0) {
        Write-Host "ERROR: Pull failed. Ensure main has not diverged locally." -ForegroundColor Red
        exit 1
    }
    
    # Switch back if needed
    if ($currentBranch -ne 'main' -and $currentBranch) {
        Write-Host "Switching back to $currentBranch..." -ForegroundColor Yellow
        & git checkout $currentBranch 2>&1 | ForEach-Object { Write-Host "  $_" }
    }
    
    Write-Host "Main synchronized successfully." -ForegroundColor Green
} catch {
    Write-Host "ERROR: Sync failed: $_" -ForegroundColor Red
    exit 1
} finally {
    Pop-Location
}

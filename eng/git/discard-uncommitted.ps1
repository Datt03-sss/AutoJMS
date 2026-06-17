<#
.SYNOPSIS
    Safely discards all uncommitted modifications and untracked files.
.DESCRIPTION
    Previews untracked file deletions, prompts the user to type 'DISCARD'
    for confirmation, then runs git restore and git clean.
.PARAMETER Force
    If set, bypasses confirmation prompts.
.EXAMPLE
    powershell -ExecutionPolicy Bypass -File .\eng\git\discard-uncommitted.ps1
#>
[CmdletBinding()]
param(
    [switch]$Force
)

$ErrorActionPreference = 'Stop'
$Root = Resolve-Path (Join-Path $PSScriptRoot '..\..') | Select-Object -ExpandProperty Path

Write-Host '========================================' -ForegroundColor Cyan
Write-Host '  AutoJMS Discard Uncommitted Edits' -ForegroundColor Cyan
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

# 2. Check for changes
try {
    Push-Location $Root
    $status = & git status --porcelain 2>&1
    if (-not $status) {
        Write-Host "Workspace is already clean." -ForegroundColor Green
        exit 0
    }
} finally {
    Pop-Location
}

# 3. Preview modifications
Write-Host "Modified Tracked Files:" -ForegroundColor Gray
try {
    Push-Location $Root
    & git diff --name-status 2>&1 | ForEach-Object { Write-Host "  $_" -ForegroundColor Yellow }
} finally {
    Pop-Location
}
Write-Host ''

# 4. Preview untracked files
Write-Host "Untracked Files to Delete (Preview):" -ForegroundColor Gray
try {
    Push-Location $Root
    $cleanPreview = & git clean -fdn 2>&1
    if ($cleanPreview) {
        $cleanPreview | ForEach-Object { Write-Host "  $_" -ForegroundColor Red }
    } else {
        Write-Host "  No untracked files to delete." -ForegroundColor Green
    }
} finally {
    Pop-Location
}
Write-Host ''

# 5. Confirmation prompt
if (-not $Force) {
    Write-Host "WARNING: This action is DESTRUCTIVE. All uncommitted changes will be lost!" -ForegroundColor Yellow
    $confirm = Read-Host "To confirm, please type exactly 'DISCARD'"
    if ($confirm -ne 'DISCARD') {
        Write-Host "Discard cancelled by user." -ForegroundColor Red
        exit 1
    }
}

# 6. Execute discard
try {
    Push-Location $Root
    Write-Host "Reverting modified tracked files..." -ForegroundColor Yellow
    & git restore . 2>&1
    
    Write-Host "Deleting untracked files and folders..." -ForegroundColor Yellow
    & git clean -fd 2>&1 | ForEach-Object { Write-Host "  $_" }
    
    Write-Host "Workspace reset complete." -ForegroundColor Green
} catch {
    Write-Host "ERROR: Discard operation failed: $_" -ForegroundColor Red
    exit 1
} finally {
    Pop-Location
}

<#
.SYNOPSIS
    Safely discards uncommitted modifications to tracked files.
.DESCRIPTION
    Previews modified tracked files, lists untracked files, prompts the user to
    type 'DISCARD' for confirmation, then runs git restore. Untracked files are
    never deleted (agents must not delete files) - review them with the Owner.
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
    # No 2>&1: under 'Stop', PS 5.1 turns any git stderr line (CRLF warnings) into a throw.
    & git diff --name-status | ForEach-Object { Write-Host "  $_" -ForegroundColor Yellow }
} finally {
    Pop-Location
}
Write-Host ''

# 4. List untracked files (kept, never deleted)
Write-Host "Untracked Files (kept, not deleted):" -ForegroundColor Gray
try {
    Push-Location $Root
    $untracked = & git ls-files --others --exclude-standard
    if ($untracked) {
        $untracked | ForEach-Object { Write-Host "  $_" -ForegroundColor Cyan }
    } else {
        Write-Host "  No untracked files." -ForegroundColor Green
    }
} finally {
    Pop-Location
}
Write-Host ''

# 5. Confirmation prompt
if (-not $Force) {
    Write-Host "WARNING: This action is DESTRUCTIVE. All uncommitted changes to tracked files will be lost!" -ForegroundColor Yellow
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
    & git restore .
    if ($LASTEXITCODE -ne 0) { throw "git restore exited with code $LASTEXITCODE" }

    Write-Host "Workspace reset complete. Untracked files were kept - ask the Owner before removing any." -ForegroundColor Green
} catch {
    Write-Host "ERROR: Discard operation failed: $_" -ForegroundColor Red
    exit 1
} finally {
    Pop-Location
}

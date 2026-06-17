<#
.SYNOPSIS
    Saves the current code changes as a local Git checkpoint.
.DESCRIPTION
    Checks if there are any unstaged or untracked changes, stages them,
    and commits them with a prefix "checkpoint: <message>".
.PARAMETER Message
    Descriptive message for the checkpoint.
.EXAMPLE
    powershell -ExecutionPolicy Bypass -File .\eng\git\checkpoint.ps1 -Message "refactored configuration service"
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$Message
)

$ErrorActionPreference = 'Stop'
$Root = Resolve-Path (Join-Path $PSScriptRoot '..\..') | Select-Object -ExpandProperty Path

Write-Host '========================================' -ForegroundColor Cyan
Write-Host '  AutoJMS Agent Workspace Checkpoint' -ForegroundColor Cyan
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
        Write-Host "No changes detected. Checkpoint skipped." -ForegroundColor Green
        exit 0
    }
} finally {
    Pop-Location
}

# 3. Create Checkpoint
try {
    Push-Location $Root
    Write-Host "Staging changes..." -ForegroundColor Yellow
    & git add . 2>&1
    
    $fullMsg = "checkpoint: $Message"
    Write-Host "Committing checkpoint: '$fullMsg'..." -ForegroundColor Yellow
    & git commit -m $fullMsg 2>&1 | ForEach-Object { Write-Host "  $_" }
    
    if ($LASTEXITCODE -ne 0) {
        Write-Host "ERROR: Checkpoint commit failed." -ForegroundColor Red
        exit 1
    }
    
    Write-Host "Checkpoint saved successfully." -ForegroundColor Green
} catch {
    Write-Host "ERROR: Checkpoint failed: $_" -ForegroundColor Red
    exit 1
} finally {
    Pop-Location
}

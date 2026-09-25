<#
.SYNOPSIS
    Saves the current code changes as a local Git checkpoint.
.DESCRIPTION
    Checks if there are any unstaged or untracked changes, stages only the
    paths given in -Paths, and commits them with a prefix "checkpoint: <message>".
.PARAMETER Message
    Descriptive message for the checkpoint.
.PARAMETER Paths
    Repo-relative files or folders to checkpoint, comma-separated. Never "." - untracked
    files must not be swept into this PUBLIC repo.
.EXAMPLE
    powershell -ExecutionPolicy Bypass -File .\eng\git\checkpoint.ps1 -Message "refactored configuration service" -Paths "src/AutoJMS/Config"
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$Message,

    [Parameter(Mandatory = $true)]
    [string[]]$Paths
)

$ErrorActionPreference = 'Stop'
$Root = Resolve-Path (Join-Path $PSScriptRoot '..\..') | Select-Object -ExpandProperty Path
# -File hands "a,b" over as one string, so split it here.
$Paths = $Paths | ForEach-Object { $_ -split ',' } | ForEach-Object { $_.Trim() } | Where-Object { $_ }

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
    & git add -- $Paths
    if ($LASTEXITCODE -ne 0) {
        Write-Host "ERROR: Git add failed." -ForegroundColor Red
        exit 1
    }
    
    $fullMsg = "checkpoint: $Message"
    Write-Host "Committing checkpoint: '$fullMsg'..." -ForegroundColor Yellow
    # No 2>&1: under 'Stop', PS 5.1 turns any git stderr line (CRLF warnings) into a throw.
    & git commit -m $fullMsg -- $Paths | ForEach-Object { Write-Host "  $_" }
    
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

<#
.SYNOPSIS
    Safely removes a Git worktree folder.
.DESCRIPTION
    Navigates to the worktree path, validates that there are no uncommitted or
    unpushed changes, deletes the directory, and runs git worktree prune.
.PARAMETER Path
    Path to the worktree folder to clean up (e.g. ../autojms-refactor).
.PARAMETER Force
    If set, bypasses the checks and forces removal.
.EXAMPLE
    powershell -ExecutionPolicy Bypass -File .\eng\git\cleanup-worktree.ps1 -Path "../autojms-refactor"
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$Path,
    [switch]$Force
)

$ErrorActionPreference = 'Stop'
$Root = Resolve-Path (Join-Path $PSScriptRoot '..\..') | Select-Object -ExpandProperty Path

Write-Host '========================================' -ForegroundColor Cyan
Write-Host '  AutoJMS Cleanup Git Worktree' -ForegroundColor Cyan
Write-Host '========================================' -ForegroundColor Cyan
Write-Host ''

# 1. Resolve absolute path
$targetFullPath = [System.IO.Path]::GetFullPath((Join-Path $Root $Path))
Write-Host "Worktree path: $targetFullPath" -ForegroundColor Gray

# 2. Verify target directory exists
if (-not (Test-Path $targetFullPath)) {
    Write-Host "WARNING: Worktree directory not found: $targetFullPath" -ForegroundColor Yellow
    Write-Host "Running git worktree prune to clear stale metadata..." -ForegroundColor Yellow
    try {
        Push-Location $Root
        & git worktree prune 2>&1
        Write-Host "Prune complete." -ForegroundColor Green
    } finally {
        Pop-Location
    }
    exit 0
}

# 3. Check for uncommitted / unpushed changes inside worktree
if (-not $Force) {
    try {
        Push-Location $targetFullPath
        
        # Check status
        $status = & git status --porcelain 2>&1
        if ($status) {
            Write-Host "ERROR: Worktree has uncommitted changes:" -ForegroundColor Red
            $status | ForEach-Object { Write-Host "  $_" -ForegroundColor Red }
            Write-Host "Please commit, stash, or run with -Force to discard changes." -ForegroundColor Red
            exit 1
        }
        
        # Check unpushed commits
        $unpushed = & git log @{u}.. 2>&1
        if ($LASTEXITCODE -eq 0 -and $unpushed) {
            Write-Host "WARNING: Worktree has unpushed commits. If you delete it, you may lose work." -ForegroundColor Yellow
            $confirm = Read-Host "Are you sure you want to delete this worktree? (y/N)"
            if ($confirm -notmatch '^[yY]') {
                Write-Host "Cancelled by user." -ForegroundColor Red
                exit 1
            }
        }
    } catch {
        Write-Host "WARNING: Could not check tracking status for worktree. Proceeding with caution." -ForegroundColor Yellow
    } finally {
        Pop-Location
    }
}

# 4. Remove worktree
try {
    Push-Location $Root
    Write-Host "Removing worktree from git tracking..." -ForegroundColor Yellow
    
    & git worktree remove $targetFullPath 2>&1 | ForEach-Object { Write-Host "  $_" }
    
    if ($LASTEXITCODE -ne 0) {
        if ($Force) {
            Write-Host "Git remove failed, forcing file deletion..." -ForegroundColor Yellow
            & git worktree remove --force $targetFullPath 2>&1 | ForEach-Object { Write-Host "  $_" }
        } else {
            Write-Host "ERROR: Failed to remove worktree. Try running with -Force." -ForegroundColor Red
            exit 1
        }
    }
    
    Write-Host "Running git worktree prune..." -ForegroundColor Yellow
    & git worktree prune 2>&1
    
    # Verify cleanup
    if (Test-Path $targetFullPath) {
        Write-Host "Deleting residual files..." -ForegroundColor Yellow
        Remove-Item $targetFullPath -Recurse -Force -ErrorAction SilentlyContinue
    }
    
    Write-Host "Worktree cleaned up successfully." -ForegroundColor Green
} catch {
    Write-Host "ERROR: Cleanup failed: $_" -ForegroundColor Red
    exit 1
} finally {
    Pop-Location
}

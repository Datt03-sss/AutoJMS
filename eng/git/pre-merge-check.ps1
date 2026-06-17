<#
.SYNOPSIS
    Pre-merge validation gate for feature branches.
.DESCRIPTION
    Checks if the current branch is different from main, verifies if it is
    behind main (rebase check), and runs the verification suite.
.EXAMPLE
    powershell -ExecutionPolicy Bypass -File .\eng\git\pre-merge-check.ps1
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$Root = Resolve-Path (Join-Path $PSScriptRoot '..\..') | Select-Object -ExpandProperty Path

Write-Host '╔══════════════════════════════════════════╗' -ForegroundColor Cyan
Write-Host '║     AutoJMS Pre-Merge Git Check          ║' -ForegroundColor Cyan
Write-Host '╚══════════════════════════════════════════╝' -ForegroundColor Cyan
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

# 2. Verify current branch
try {
    Push-Location $Root
    $currentBranch = & git branch --show-current
    
    if ($currentBranch -eq 'main') {
        Write-Host "ERROR: You are on the 'main' branch. Pre-merge check must be run on feature branches." -ForegroundColor Red
        exit 1
    }
    Write-Host "Active branch: $currentBranch" -ForegroundColor Green
} finally {
    Pop-Location
}

# 3. Check if branch is behind main (divergence check)
try {
    Push-Location $Root
    
    # Check if main exists locally
    $mainExists = & git branch --list main
    if ($mainExists) {
        # Check diverged commits: left = commits on main not in HEAD, right = commits on HEAD not in main
        $revList = & git rev-list --left-right --count main...HEAD 2>&1
        if ($LASTEXITCODE -eq 0 -and $revList) {
            $parts = $revList.Trim().Split("`t")
            $behind = [int]$parts[0]
            $ahead = [int]$parts[1]
            
            Write-Host "Branch status relative to local 'main':" -ForegroundColor Gray
            Write-Host "  Ahead of main:  $ahead commit(s)" -ForegroundColor Gray
            Write-Host "  Behind main:   $behind commit(s)" -ForegroundColor Gray
            
            if ($behind -gt 0) {
                Write-Host ""
                Write-Host "WARNING: Your branch is behind 'main' by $behind commit(s)." -ForegroundColor Yellow
                Write-Host "Please rebase your branch onto main to avoid merge conflicts:" -ForegroundColor Yellow
                Write-Host "  git checkout $currentBranch" -ForegroundColor Yellow
                Write-Host "  git rebase main" -ForegroundColor Yellow
                Write-Host ""
            } else {
                Write-Host "  Status: Up-to-date with main." -ForegroundColor Green
            }
        }
    } else {
        Write-Host "WARNING: Local 'main' branch not found. Skipping rebase check." -ForegroundColor Yellow
    }
} catch {
    Write-Host "WARNING: Failed to run rebase check: $_" -ForegroundColor Yellow
} finally {
    Pop-Location
}
Write-Host ''

# 4. Run automated quality harness (verify.ps1)
Write-Host "Running full project verification (verify.ps1)..." -ForegroundColor Yellow
Write-Host ""

$verifyPath = Join-Path $Root 'eng/harness/verify.ps1'
if (-not (Test-Path $verifyPath)) {
    Write-Host "ERROR: Verification script verify.ps1 not found at $verifyPath" -ForegroundColor Red
    exit 1
}

& powershell -ExecutionPolicy Bypass -File $verifyPath
$verifyExit = $LASTEXITCODE

Write-Host ''
if ($verifyExit -eq 0) {
    Write-Host "========================================" -ForegroundColor Green
    Write-Host "  PRE-MERGE CHECK: PASSED" -ForegroundColor Green
    Write-Host "  This branch is clean and ready for PR." -ForegroundColor Green
    Write-Host "========================================" -ForegroundColor Green
    exit 0
} else {
    Write-Host "========================================" -ForegroundColor Red
    Write-Host "  PRE-MERGE CHECK: FAILED" -ForegroundColor Red
    Write-Host "  Please fix compilation or secret issues." -ForegroundColor Red
    Write-Host "========================================" -ForegroundColor Red
    exit 1
}

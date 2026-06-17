<#
.SYNOPSIS
    Automated Git commit and push helper for the GitHub Shared-Main workflow.
.DESCRIPTION
    Checks active branch is main, triggers full project verification,
    stages all files, commits them locally, and pushes to origin/main.
.PARAMETER Message
    Commit description message.
.EXAMPLE
    powershell -ExecutionPolicy Bypass -File .\eng\git\ai-commit.ps1 -Message "fix(tab-print): adjust labels spacing"
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$Message
)

$ErrorActionPreference = 'Stop'
$Root = Resolve-Path (Join-Path $PSScriptRoot '..\..') | Select-Object -ExpandProperty Path

Write-Host '========================================' -ForegroundColor Cyan
Write-Host '  AutoJMS AI Commit & Push Main' -ForegroundColor Cyan
Write-Host '========================================' -ForegroundColor Cyan
Write-Host ''

# 1. Verify branch is main
try {
    Push-Location $Root
    $branch = & git branch --show-current
    if ($branch -ne 'main') {
        Write-Host "ERROR: Active branch is not 'main'. Found: $branch" -ForegroundColor Red
        Write-Host "The direct-main workflow requires developing and committing on 'main'." -ForegroundColor Red
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
        Write-Host "No changes detected in the workspace. Commit skipped." -ForegroundColor Green
        exit 0
    }
} finally {
    Pop-Location
}

# 3. Compile and verify changes
$verifyPath = Join-Path $Root 'eng/harness/verify.ps1'
if (Test-Path $verifyPath) {
    Write-Host "Running verification harness (verify.ps1)..." -ForegroundColor Yellow
    & powershell -ExecutionPolicy Bypass -File $verifyPath
    $exitCode = $LASTEXITCODE
    if ($exitCode -ne 0) {
        Write-Host "ERROR: Verification harness failed. Fix errors before committing." -ForegroundColor Red
        exit 1
    }
} else {
    Write-Host "Harness verify.ps1 not found. Falling back to Release compilation check..." -ForegroundColor Yellow
    $slnFile = Join-Path $Root 'AutoJMS.slnx'
    try {
        Write-Host "Restoring solution..." -ForegroundColor Yellow
        & dotnet restore $slnFile 2>&1
        if ($LASTEXITCODE -ne 0) { throw "dotnet restore failed" }
        
        Write-Host "Building in Release configuration..." -ForegroundColor Yellow
        & dotnet build $slnFile -c Release --no-restore 2>&1
        if ($LASTEXITCODE -ne 0) { throw "dotnet build failed" }
    } catch {
        Write-Host "ERROR: Compilation check failed: $_" -ForegroundColor Red
        exit 1
    }
}
Write-Host ''

# 4. Stage, commit and push changes
try {
    Push-Location $Root
    Write-Host "Staging files..." -ForegroundColor Yellow
    & git add . 2>&1
    
    Write-Host "Committing changes to local main..." -ForegroundColor Yellow
    & git commit -m $Message 2>&1 | ForEach-Object { Write-Host "  $_" }
    if ($LASTEXITCODE -ne 0) {
        Write-Host "ERROR: Git commit failed." -ForegroundColor Red
        exit 1
    }
    
    # 5. Push changes
    $remotes = & git remote
    if ($remotes -contains 'origin') {
        Write-Host "Pushing commits to origin/main..." -ForegroundColor Yellow
        & git push origin main 2>&1 | ForEach-Object { Write-Host "  $_" }
        if ($LASTEXITCODE -ne 0) {
            Write-Host "ERROR: Git push to origin/main failed." -ForegroundColor Red
            exit 1
        }
        Write-Host "Successfully pushed to origin/main." -ForegroundColor Green
    } else {
        Write-Host "WARNING: No remote 'origin' configured. Skipping remote push." -ForegroundColor Yellow
    }
    
    Write-Host ""
    Write-Host "Commit & Sync successful!" -ForegroundColor Green
    Write-Host "Latest Commit Info:" -ForegroundColor Gray
    & git log -n 1 --color 2>&1 | ForEach-Object { Write-Host "  $_" }
} catch {
    Write-Host "ERROR: Commit & Push failed: $_" -ForegroundColor Red
    exit 1
} finally {
    Pop-Location
}

<#
.SYNOPSIS
    Creates a new feature branch from synchronized main.
.DESCRIPTION
    Verifies git workspace, validates branch naming conventions, switches to main
    to pull the latest commits, and checks out the new branch.
.PARAMETER BranchName
    Name of the new branch to create (e.g. agent/claude/my-task).
.EXAMPLE
    powershell -ExecutionPolicy Bypass -File .\eng\git\new-branch.ps1 -BranchName "agent/claude/refactor-ui"
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$BranchName
)

$ErrorActionPreference = 'Stop'
$Root = Resolve-Path (Join-Path $PSScriptRoot '..\..') | Select-Object -ExpandProperty Path

Write-Host '========================================' -ForegroundColor Cyan
Write-Host '  AutoJMS New Feature Branch' -ForegroundColor Cyan
Write-Host '========================================' -ForegroundColor Cyan
Write-Host ''

# 1. Validate Branch Name
if ($BranchName -notmatch '^(agent|dev|fix|feat)/[a-zA-Z0-9_-]+/[a-zA-Z0-9_-]+$') {
    Write-Host "WARNING: Branch name '$BranchName' does not follow conventions." -ForegroundColor Yellow
    Write-Host "Expected format: (agent|dev|fix|feat)/<developer-name>/<description>" -ForegroundColor Yellow
    Write-Host "Example: agent/claude/refactor-ui-service" -ForegroundColor Yellow
    Write-Host ""
    $confirm = Read-Host "Do you want to proceed anyway? (y/N)"
    if ($confirm -notmatch '^[yY]') {
        Write-Host "Cancelled by user." -ForegroundColor Red
        exit 1
    }
}

# 2. Check if git repo
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

# 3. Check for uncommitted changes
try {
    Push-Location $Root
    $status = & git status --porcelain 2>&1
    if ($status) {
        Write-Host "WARNING: You have uncommitted changes in your workspace:" -ForegroundColor Yellow
        $status | ForEach-Object { Write-Host "  $_" -ForegroundColor Yellow }
        Write-Host "Please commit or stash your changes before creating a branch." -ForegroundColor Red
        exit 1
    }
} finally {
    Pop-Location
}

# 4. Sync Main first (if commits exist)
try {
    Push-Location $Root
    $commitCount = & git rev-list --all --count 2>&1
    if ($LASTEXITCODE -eq 0 -and $commitCount -gt 0) {
        # Check if there is an origin remote
        $remotes = & git remote
        if ($remotes -contains 'origin') {
            Write-Host "Syncing main branch..." -ForegroundColor Yellow
            & powershell -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'sync-main.ps1')
            if ($LASTEXITCODE -ne 0) {
                Write-Host "WARNING: Sync main failed. Proceeding with local main." -ForegroundColor Yellow
            }
        }
    }
} finally {
    Pop-Location
}

# 5. Create new branch
try {
    Push-Location $Root
    Write-Host "Creating and checking out branch: $BranchName..." -ForegroundColor Yellow
    
    # Check if branch already exists
    $branchExists = & git branch --list $BranchName
    if ($branchExists) {
        Write-Host "ERROR: Branch '$BranchName' already exists." -ForegroundColor Red
        exit 1
    }
    
    & git checkout -b $BranchName 2>&1 | ForEach-Object { Write-Host "  $_" }
    if ($LASTEXITCODE -ne 0) {
        Write-Host "ERROR: Failed to create branch $BranchName." -ForegroundColor Red
        exit 1
    }
    
    Write-Host "Branch '$BranchName' created and checked out successfully." -ForegroundColor Green
} catch {
    Write-Host "ERROR: Branch creation failed: $_" -ForegroundColor Red
    exit 1
} finally {
    Pop-Location
}

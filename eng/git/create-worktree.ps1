<#
.SYNOPSIS
    Creates a new Git worktree for isolated task development.
.DESCRIPTION
    Validates workspace status, checks folder availability, and sets up
    a Git worktree linked to a branch.
.PARAMETER Path
    Local path where the worktree folder will be created (e.g., ../autojms-task).
.PARAMETER BranchName
    Branch name to bind to the worktree (e.g. agent/claude/refactor-configs).
.PARAMETER NewBranch
    If set, creates a new branch based on main rather than using an existing one.
.EXAMPLE
    powershell -ExecutionPolicy Bypass -File .\eng\git\create-worktree.ps1 -Path "../autojms-refactor" -BranchName "agent/claude/refactor-ui" -NewBranch
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$Path,
    [Parameter(Mandatory = $true)]
    [string]$BranchName,
    [switch]$NewBranch
)

$ErrorActionPreference = 'Stop'
$Root = Resolve-Path (Join-Path $PSScriptRoot '..\..') | Select-Object -ExpandProperty Path

Write-Host '========================================' -ForegroundColor Cyan
Write-Host '  AutoJMS Create Git Worktree' -ForegroundColor Cyan
Write-Host '========================================' -ForegroundColor Cyan
Write-Host ''

# 1. Resolve absolute path for the target worktree
$targetFullPath = [System.IO.Path]::GetFullPath((Join-Path $Root $Path))
Write-Host "Target worktree path: $targetFullPath" -ForegroundColor Gray
Write-Host "Branch name:          $BranchName" -ForegroundColor Gray
Write-Host ''

# 2. Check if target directory already exists
if (Test-Path $targetFullPath) {
    Write-Host "ERROR: Target path already exists: $targetFullPath" -ForegroundColor Red
    exit 1
}

# 3. Verify git repo
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

# 4. Check if repo has commits
try {
    Push-Location $Root
    $commitCount = & git rev-list --all --count 2>&1
    if ($LASTEXITCODE -ne 0 -or $commitCount -eq 0) {
        Write-Host "ERROR: Cannot create worktree in a repository with no commits." -ForegroundColor Red
        exit 1
    }
} finally {
    Pop-Location
}

# 5. Execute git worktree add
try {
    Push-Location $Root
    
    if ($NewBranch) {
        # Check if the branch already exists
        $branchExists = & git branch --list $BranchName
        if ($branchExists) {
            Write-Host "ERROR: Branch '$BranchName' already exists. Cannot create a new branch with this name." -ForegroundColor Red
            exit 1
        }
        
        Write-Host "Creating worktree and brand new branch '$BranchName' from main..." -ForegroundColor Yellow
        & git worktree add -b $BranchName $targetFullPath main 2>&1 | ForEach-Object { Write-Host "  $_" }
    } else {
        # Check if branch exists locally or on remote
        $branchExists = & git branch -a --list "*$BranchName"
        if (-not $branchExists) {
            Write-Host "ERROR: Branch '$BranchName' does not exist. Use -NewBranch to create it." -ForegroundColor Red
            exit 1
        }
        
        Write-Host "Checking out existing branch '$BranchName' into worktree..." -ForegroundColor Yellow
        & git worktree add $targetFullPath $BranchName 2>&1 | ForEach-Object { Write-Host "  $_" }
    }
    
    if ($LASTEXITCODE -ne 0) {
        Write-Host "ERROR: git worktree add failed." -ForegroundColor Red
        exit 1
    }
    
    Write-Host ""
    Write-Host "Worktree created successfully." -ForegroundColor Green
    Write-Host "To start developing in this worktree:" -ForegroundColor Green
    Write-Host "  cd '$targetFullPath'" -ForegroundColor Green
} catch {
    Write-Host "ERROR: Failed to create worktree: $_" -ForegroundColor Red
    exit 1
} finally {
    Pop-Location
}

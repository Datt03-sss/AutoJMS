<#
.SYNOPSIS
    Project structure validation harness for AutoJMS.
.DESCRIPTION
    Ensures key directories, configuration files, and solution projects exist
    in their expected locations, and checks target frameworks.
.EXAMPLE
    powershell -ExecutionPolicy Bypass -File .\eng\harness\check-project-structure.ps1
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$Root = Resolve-Path (Join-Path $PSScriptRoot '..\..') | Select-Object -ExpandProperty Path

Write-Host '========================================' -ForegroundColor Cyan
Write-Host '  AutoJMS Project Structure Check' -ForegroundColor Cyan
Write-Host '========================================' -ForegroundColor Cyan
Write-Host ''

$issues = @()

# 1. Verify Solution File
$slnFile = Join-Path $Root 'AutoJMS.slnx'
if (-not (Test-Path $slnFile)) {
    $issues += "Solution file missing: AutoJMS.slnx"
} else {
    Write-Host "  AutoJMS.slnx: OK" -ForegroundColor Green
}

# 2. Verify Key Directories
$requiredDirs = @(
    'docs/agent',
    'docs/architecture',
    'docs/decisions',
    'eng/harness',
    'eng/prompts',
    '.claude',
    'src/AutoJMS'
)

foreach ($dir in $requiredDirs) {
    $path = Join-Path $Root $dir
    if (-not (Test-Path $path -PathType Container)) {
        $issues += "Required directory missing: $dir"
    } else {
        Write-Host "  Directory '$dir': OK" -ForegroundColor Green
    }
}

# 3. Verify Critical Files
$requiredFiles = @(
    'AGENTS.md',
    'CLAUDE.md',
    'src/AutoJMS/Program.cs',
    'src/AutoJMS/Forms/Main.cs',
    'src/AutoJMS/Licensing/TierRuntimePolicy.cs',
    'src/AutoJMS/Licensing/LicenseApiService.cs'
)

foreach ($file in $requiredFiles) {
    $path = Join-Path $Root $file
    if (-not (Test-Path $path -PathType Leaf)) {
        $issues += "Required file missing: $file"
    } else {
        Write-Host "  File '$file': OK" -ForegroundColor Green
    }
}

# 4. Verify Target Framework (.NET 8)
$csprojPath = Join-Path $Root 'src/AutoJMS/AutoJMS.csproj'
if (Test-Path $csprojPath) {
    try {
        [xml]$csproj = Get-Content $csprojPath
        $targetFramework = $csproj.Project.PropertyGroup[0].TargetFramework
        if ($targetFramework -notmatch 'net8\.0-windows') {
            $issues += "Csproj TargetFramework is not net8.0-windows: $targetFramework"
        } else {
            Write-Host "  TargetFramework '$targetFramework': OK" -ForegroundColor Green
        }
    } catch {
        $issues += "Failed to parse AutoJMS.csproj xml properties"
    }
}

# 5. Summary
Write-Host ''
if ($issues.Count -gt 0) {
    Write-Host '========================================' -ForegroundColor Red
    Write-Host '  PROJECT STRUCTURE CHECK FAILED' -ForegroundColor Red
    Write-Host '========================================' -ForegroundColor Red
    foreach ($issue in $issues) {
        Write-Host "  - $issue" -ForegroundColor Red
    }
    Write-Host ''
    exit 1
}

Write-Host 'Project structure is clean and standardized.' -ForegroundColor Green
exit 0

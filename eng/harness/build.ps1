<#
.SYNOPSIS
    Build harness for AutoJMS.
.DESCRIPTION
    Restores and builds AutoJMS.slnx in Release configuration.
    Used by verify.ps1 and agents for pre-PR validation.
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$Root = Resolve-Path (Join-Path $PSScriptRoot '..\..') | Select-Object -ExpandProperty Path
$SlnFile = Join-Path $Root 'AutoJMS.slnx'

Write-Host '========================================' -ForegroundColor Cyan
Write-Host '  AutoJMS Build Harness' -ForegroundColor Cyan
Write-Host '========================================' -ForegroundColor Cyan
Write-Host ''

if (-not (Test-Path $SlnFile)) {
    Write-Host "ERROR: Solution file not found: $SlnFile" -ForegroundColor Red
    exit 1
}

# Step 1: Restore
Write-Host '[1/2] Restoring packages...' -ForegroundColor Yellow
try {
    & dotnet restore $SlnFile 2>&1 | ForEach-Object { Write-Host "  $_" }
    if ($LASTEXITCODE -ne 0) {
        Write-Host 'ERROR: dotnet restore failed.' -ForegroundColor Red
        exit 1
    }
    Write-Host '  Restore: OK' -ForegroundColor Green
} catch {
    Write-Host "ERROR: dotnet restore threw exception: $_" -ForegroundColor Red
    exit 1
}

Write-Host ''

# Step 2: Build Release
Write-Host '[2/2] Building Release...' -ForegroundColor Yellow
try {
    & dotnet build $SlnFile -c Release --no-restore 2>&1 | ForEach-Object { Write-Host "  $_" }
    if ($LASTEXITCODE -ne 0) {
        Write-Host 'ERROR: dotnet build Release failed.' -ForegroundColor Red
        exit 1
    }
    Write-Host '  Build: OK' -ForegroundColor Green
} catch {
    Write-Host "ERROR: dotnet build threw exception: $_" -ForegroundColor Red
    exit 1
}

Write-Host ''
Write-Host 'Build harness completed successfully.' -ForegroundColor Green
exit 0

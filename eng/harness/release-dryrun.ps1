<#
.SYNOPSIS
    Release dry-run for AutoJMS.
.DESCRIPTION
    Simulates a release build without uploading.
    Agents can use this to verify release readiness.
    Does NOT call build-release.ps1 -Upload.
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$Root = Resolve-Path (Join-Path $PSScriptRoot '..\..') | Select-Object -ExpandProperty Path
$CsprojPath = Join-Path $Root 'src\AutoJMS\AutoJMS.csproj'

Write-Host '========================================' -ForegroundColor Cyan
Write-Host '  AutoJMS Release Dry-Run' -ForegroundColor Cyan
Write-Host '========================================' -ForegroundColor Cyan
Write-Host ''
Write-Host '  NOTE: This is a DRY RUN. Nothing will be uploaded.' -ForegroundColor Yellow
Write-Host '  Agents must NEVER run build-release.ps1 -Upload.' -ForegroundColor Yellow
Write-Host ''

if (-not (Test-Path $CsprojPath)) {
    Write-Host "ERROR: Project file not found: $CsprojPath" -ForegroundColor Red
    exit 1
}

# Read version from csproj
[xml]$csproj = Get-Content $CsprojPath
$version = $csproj.Project.PropertyGroup[0].Version
$assemblyVersion = $csproj.Project.PropertyGroup[0].AssemblyVersion
$fileVersion = $csproj.Project.PropertyGroup[0].FileVersion

Write-Host "Version:          $version" -ForegroundColor White
Write-Host "AssemblyVersion:  $assemblyVersion" -ForegroundColor White
Write-Host "FileVersion:      $fileVersion" -ForegroundColor White
Write-Host ''

# Step 1: Build Release
Write-Host '[1/2] Building Release...' -ForegroundColor Yellow
try {
    & dotnet build $CsprojPath -c Release 2>&1 | ForEach-Object { Write-Host "  $_" }
    if ($LASTEXITCODE -ne 0) {
        Write-Host 'ERROR: Release build failed.' -ForegroundColor Red
        exit 1
    }
    Write-Host '  Build: OK' -ForegroundColor Green
} catch {
    Write-Host "ERROR: Build threw exception: $_" -ForegroundColor Red
    exit 1
}
Write-Host ''

# Step 2: Publish (dry-run, local only)
Write-Host '[2/2] Publishing (local only, no upload)...' -ForegroundColor Yellow
$publishDir = Join-Path $Root 'release\dryrun-output'
if (Test-Path $publishDir) {
    Remove-Item $publishDir -Recurse -Force
}

try {
    & dotnet publish $CsprojPath -c Release -o $publishDir --no-build 2>&1 | ForEach-Object { Write-Host "  $_" }
    if ($LASTEXITCODE -ne 0) {
        Write-Host 'ERROR: Publish failed.' -ForegroundColor Red
        exit 1
    }
    Write-Host '  Publish: OK' -ForegroundColor Green
} catch {
    Write-Host "ERROR: Publish threw exception: $_" -ForegroundColor Red
    exit 1
}
Write-Host ''

# Summary
$fileCount = (Get-ChildItem $publishDir -Recurse -File).Count
$totalSize = (Get-ChildItem $publishDir -Recurse -File | Measure-Object -Property Length -Sum).Sum / 1MB

Write-Host '========================================' -ForegroundColor Cyan
Write-Host '  DRY-RUN COMPLETE' -ForegroundColor Cyan
Write-Host '========================================' -ForegroundColor Cyan
Write-Host "  Version:    $version"
Write-Host "  Files:      $fileCount"
Write-Host "  Total size: $([math]::Round($totalSize, 2)) MB"
Write-Host "  Output:     $publishDir"
Write-Host ''
Write-Host '  This was a DRY RUN. To release production, the OWNER must run:' -ForegroundColor Yellow
Write-Host '  .\release\build-release.ps1 -Version "X.Y.Z.0" -Channel stable -Upload' -ForegroundColor Yellow
Write-Host ''
exit 0

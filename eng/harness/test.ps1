<#
.SYNOPSIS
    Test harness for AutoJMS.
.DESCRIPTION
    Searches for test projects and runs them.
    Does NOT fail if no test projects are found — only warns.
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$Root = Resolve-Path (Join-Path $PSScriptRoot '..\..') | Select-Object -ExpandProperty Path

Write-Host '========================================' -ForegroundColor Cyan
Write-Host '  AutoJMS Test Harness' -ForegroundColor Cyan
Write-Host '========================================' -ForegroundColor Cyan
Write-Host ''

# Search for test projects
$testProjects = Get-ChildItem -Path $Root -Recurse -Filter '*.csproj' |
    Where-Object {
        $_.Name -match '(?i)(test|tests)' -or
        (Select-String -Path $_.FullName -Pattern 'Microsoft\.NET\.Test\.Sdk' -Quiet)
    }

if ($testProjects.Count -eq 0) {
    Write-Host 'WARNING: No test projects found.' -ForegroundColor Yellow
    Write-Host '  Searched for: *Test*.csproj, *Tests*.csproj, or projects referencing Microsoft.NET.Test.Sdk' -ForegroundColor Yellow
    Write-Host '  Location searched: ' $Root -ForegroundColor Yellow
    Write-Host '' 
    Write-Host '  This is expected if tests have not been added yet.' -ForegroundColor Yellow
    Write-Host '  Future test projects should be placed in the tests/ directory.' -ForegroundColor Yellow
    Write-Host ''
    Write-Host 'Test harness completed with WARNING (no tests to run).' -ForegroundColor Yellow
    # Exit 0 — no tests is a warning, not a failure
    exit 0
}

Write-Host "Found $($testProjects.Count) test project(s):" -ForegroundColor Green
foreach ($proj in $testProjects) {
    Write-Host "  - $($proj.FullName)" -ForegroundColor White
}
Write-Host ''

$failed = $false
foreach ($proj in $testProjects) {
    Write-Host "Running tests: $($proj.Name)" -ForegroundColor Yellow
    & dotnet test $proj.FullName --no-build -c Release 2>&1 | ForEach-Object { Write-Host "  $_" }
    if ($LASTEXITCODE -ne 0) {
        Write-Host "ERROR: Tests failed in $($proj.Name)" -ForegroundColor Red
        $failed = $true
    } else {
        Write-Host "  Tests passed: $($proj.Name)" -ForegroundColor Green
    }
    Write-Host ''
}

if ($failed) {
    Write-Host 'Test harness FAILED.' -ForegroundColor Red
    exit 1
}

Write-Host 'Test harness completed successfully.' -ForegroundColor Green
exit 0

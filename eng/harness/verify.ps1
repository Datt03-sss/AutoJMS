<#
.SYNOPSIS
    Full verification harness for AutoJMS.
.DESCRIPTION
    Orchestrates dotnet info, build, test, secret scan, and project structure checks.
    Run this before every pull request.
.EXAMPLE
    powershell -ExecutionPolicy Bypass -File .\eng\harness\verify.ps1
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Continue'
$Root = Resolve-Path (Join-Path $PSScriptRoot '..\..') | Select-Object -ExpandProperty Path
$HarnessDir = $PSScriptRoot

Write-Host '╔══════════════════════════════════════════╗' -ForegroundColor Cyan
Write-Host '║     AutoJMS Verification Harness          ║' -ForegroundColor Cyan
Write-Host '║  Build → Test → Node → Secrets → Structure ║' -ForegroundColor Cyan
Write-Host '╚══════════════════════════════════════════╝' -ForegroundColor Cyan
Write-Host ''
Write-Host "Project root: $Root" -ForegroundColor Gray
Write-Host "Timestamp:    $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')" -ForegroundColor Gray
Write-Host ''

$results = @{}
$overallExit = 0

# ─── Step 0: Dotnet Info ───
Write-Host '━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━' -ForegroundColor White
Write-Host '  STEP 0: DOTNET INFO' -ForegroundColor White
Write-Host '━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━' -ForegroundColor White
Write-Host ''
& dotnet --info
Write-Host ''

# ─── Step 1: Build ───
Write-Host '━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━' -ForegroundColor White
Write-Host '  STEP 1: BUILD' -ForegroundColor White
Write-Host '━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━' -ForegroundColor White
Write-Host ''

& powershell -ExecutionPolicy Bypass -File (Join-Path $HarnessDir 'build.ps1')
$buildExit = $LASTEXITCODE

if ($buildExit -eq 0) {
    $results['Build'] = 'PASS'
    Write-Host ''
    Write-Host '  ✅ Build: PASS' -ForegroundColor Green
} else {
    $results['Build'] = 'FAIL'
    $overallExit = 1
    Write-Host ''
    Write-Host '  ❌ Build: FAIL' -ForegroundColor Red
}
Write-Host ''

# ─── Step 2: Tests ───
Write-Host '━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━' -ForegroundColor White
Write-Host '  STEP 2: TESTS' -ForegroundColor White
Write-Host '━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━' -ForegroundColor White
Write-Host ''

& powershell -ExecutionPolicy Bypass -File (Join-Path $HarnessDir 'test.ps1')
$testExit = $LASTEXITCODE

# 2 is test.ps1's "no test projects found" signal, kept distinct from 0 so a run
# where every test executed and passed is not reported as an empty one.
if ($testExit -eq 0) {
    $results['Tests'] = 'PASS'
    Write-Host ''
    Write-Host '  ✅ Tests: PASS' -ForegroundColor Green
} elseif ($testExit -eq 2) {
    $results['Tests'] = 'WARNING: no test projects found'
    Write-Host ''
    Write-Host '  ⚠️  Tests: WARNING (no test projects found)' -ForegroundColor Yellow
} else {
    $results['Tests'] = 'FAIL'
    $overallExit = 1
    Write-Host ''
    Write-Host '  ❌ Tests: FAIL' -ForegroundColor Red
}
Write-Host ''

# ─── Step 2b: Node Tests (license server) ───
# The Render license server is the other half of the backend and was outside every
# gate until now: not built by build.ps1 (it is not a .csproj) and not run by
# test.ps1 (it has no Microsoft.NET.Test.Sdk). Its suite therefore ran only when
# someone remembered to, which for a component that mints licence tokens is the
# weakest place to rely on memory.
Write-Host '━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━' -ForegroundColor White
Write-Host '  STEP 2b: NODE TESTS (license server)' -ForegroundColor White
Write-Host '━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━' -ForegroundColor White
Write-Host ''

& powershell -ExecutionPolicy Bypass -File (Join-Path $HarnessDir 'test-node.ps1')
$nodeExit = $LASTEXITCODE

if ($nodeExit -eq 0) {
    $results['NodeTests'] = 'PASS'
    Write-Host ''
    Write-Host '  ✅ Node Tests: PASS' -ForegroundColor Green
} else {
    $results['NodeTests'] = 'FAIL'
    $overallExit = 1
    Write-Host ''
    Write-Host '  ❌ Node Tests: FAIL' -ForegroundColor Red
}
Write-Host ''

# ─── Step 3: Secret Scan ───
Write-Host '━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━' -ForegroundColor White
Write-Host '  STEP 3: SECRET SCAN' -ForegroundColor White
Write-Host '━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━' -ForegroundColor White
Write-Host ''

& powershell -ExecutionPolicy Bypass -File (Join-Path $HarnessDir 'check-secrets.ps1')
$secretExit = $LASTEXITCODE

if ($secretExit -eq 0) {
    $results['Secrets'] = 'PASS'
    Write-Host ''
    Write-Host '  ✅ Secrets: PASS' -ForegroundColor Green
} else {
    $results['Secrets'] = 'FAIL'
    $overallExit = 1
    Write-Host ''
    Write-Host '  ❌ Secrets: FAIL' -ForegroundColor Red
}
Write-Host ''

# ─── Step 4: Project Structure ───
Write-Host '━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━' -ForegroundColor White
Write-Host '  STEP 4: PROJECT STRUCTURE' -ForegroundColor White
Write-Host '━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━' -ForegroundColor White
Write-Host ''

& powershell -ExecutionPolicy Bypass -File (Join-Path $HarnessDir 'check-project-structure.ps1')
$structureExit = $LASTEXITCODE

if ($structureExit -eq 0) {
    $results['Structure'] = 'PASS'
    Write-Host ''
    Write-Host '  ✅ Structure: PASS' -ForegroundColor Green
} else {
    $results['Structure'] = 'FAIL'
    $overallExit = 1
    Write-Host ''
    Write-Host '  ❌ Structure: FAIL' -ForegroundColor Red
}
Write-Host ''

# ─── Summary ───
Write-Host '╔══════════════════════════════════════════╗' -ForegroundColor Cyan
Write-Host '║           VERIFICATION SUMMARY            ║' -ForegroundColor Cyan
Write-Host '╚══════════════════════════════════════════╝' -ForegroundColor Cyan
Write-Host ''
foreach ($key in @('Build', 'Tests', 'NodeTests', 'Secrets', 'Structure')) {
    $val = $results[$key]
    $color = if ($val -match 'FAIL') { 'Red' } elseif ($val -match 'WARNING') { 'Yellow' } else { 'Green' }
    Write-Host "  $key : $val" -ForegroundColor $color
}
Write-Host ''

if ($overallExit -eq 0) {
    Write-Host '  OVERALL: ✅ ALL GATES PASSED' -ForegroundColor Green
} else {
    Write-Host '  OVERALL: ❌ VERIFICATION FAILED' -ForegroundColor Red
}

Write-Host ''
exit $overallExit

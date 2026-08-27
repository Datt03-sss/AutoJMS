<#
.SYNOPSIS
    Node test gate for the Render license server.
.DESCRIPTION
    Runs `npm run check` (syntax) and `npm test` (node:test suite) for
    backend/render-license-server.

    This lives in the harness rather than directly in .github/workflows/verify.yml
    on purpose. verify.yml states the invariant it exists to protect — the harness
    is the single definition of what "verified" means, so CI and a local run
    cannot drift apart. A job that ran npm test only on the runner would push the
    license server outside that definition: green locally, red on push.

    Every failure mode here is a hard FAIL, never a skip. The repository already
    carries two lessons about gates that pass by doing nothing (the "no test
    projects found" label and the "denylist not configured; INACTIVE" notice), and
    a missing toolchain is exactly when a silent skip is most convincing and most
    wrong.
.EXAMPLE
    powershell -ExecutionPolicy Bypass -File .\eng\harness\test-node.ps1
#>
[CmdletBinding()]
param()

# Continue, not Stop. npm writes notices and progress to stderr, and `2>&1` in a
# pipeline turns those into ErrorRecords that Stop would raise as
# NativeCommandError — a passing test run reported as a harness crash. Exit codes
# are checked explicitly instead, which is also what verify.ps1 does.
$ErrorActionPreference = 'Continue'

$Root = Resolve-Path (Join-Path $PSScriptRoot '..\..') | Select-Object -ExpandProperty Path
$ServerDir = Join-Path $Root 'backend\render-license-server'

Write-Host '========================================' -ForegroundColor Cyan
Write-Host '  AutoJMS Node Test Harness' -ForegroundColor Cyan
Write-Host '  backend/render-license-server' -ForegroundColor Cyan
Write-Host '========================================' -ForegroundColor Cyan
Write-Host ''

if (-not (Test-Path (Join-Path $ServerDir 'package.json') -PathType Leaf)) {
    Write-Host 'ERROR: backend/render-license-server/package.json not found.' -ForegroundColor Red
    Write-Host "  Looked in: $ServerDir" -ForegroundColor Red
    Write-Host '  The license server is a tracked part of this repository; its absence is a' -ForegroundColor Red
    Write-Host '  broken checkout, not a reason to skip the gate.' -ForegroundColor Red
    exit 1
}

foreach ($tool in @('node', 'npm')) {
    if (-not (Get-Command $tool -ErrorAction SilentlyContinue)) {
        Write-Host "ERROR: '$tool' is not on PATH." -ForegroundColor Red
        Write-Host '  The license server is half the backend. Install Node 22 LTS (the version' -ForegroundColor Red
        Write-Host '  backend/render-license-server/Dockerfile ships) and run this again.' -ForegroundColor Red
        exit 1
    }
}

Write-Host "Node:    $(& node --version)" -ForegroundColor Gray
Write-Host "Workdir: $ServerDir" -ForegroundColor Gray
Write-Host ''

$exitCode = 0
Push-Location $ServerDir
try {
    # `npm ci` only when the tree is absent — a fresh CI checkout — so a local run
    # stays fast and does not silently rewrite a developer's node_modules.
    if (-not (Test-Path (Join-Path $ServerDir 'node_modules') -PathType Container)) {
        Write-Host 'Installing dependencies (npm ci)...' -ForegroundColor Yellow
        & npm ci --no-audit --no-fund 2>&1 | ForEach-Object { Write-Host "  $_" }
        if ($LASTEXITCODE -ne 0) {
            Write-Host 'ERROR: npm ci failed.' -ForegroundColor Red
            exit 1
        }
        Write-Host ''
    }

    Write-Host 'Running: npm run check  (node --check server.js)' -ForegroundColor Yellow
    & npm run check 2>&1 | ForEach-Object { Write-Host "  $_" }
    if ($LASTEXITCODE -ne 0) {
        Write-Host '  ERROR: syntax check failed.' -ForegroundColor Red
        $exitCode = 1
    } else {
        Write-Host '  Syntax check passed.' -ForegroundColor Green
    }
    Write-Host ''

    Write-Host 'Running: npm test  (node --test)' -ForegroundColor Yellow
    & npm test 2>&1 | ForEach-Object { Write-Host "  $_" }
    if ($LASTEXITCODE -ne 0) {
        Write-Host '  ERROR: node:test suite failed.' -ForegroundColor Red
        $exitCode = 1
    } else {
        Write-Host '  Test suite passed.' -ForegroundColor Green
    }
    Write-Host ''
}
finally {
    Pop-Location
}

if ($exitCode -ne 0) {
    Write-Host 'Node test harness FAILED.' -ForegroundColor Red
} else {
    Write-Host 'Node test harness completed successfully.' -ForegroundColor Green
}

exit $exitCode

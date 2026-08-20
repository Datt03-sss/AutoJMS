[CmdletBinding()]
param(
    [string]$Root
)

$ErrorActionPreference = 'Stop'
$rootPath = if ([string]::IsNullOrWhiteSpace($Root)) {
    Join-Path $PSScriptRoot '..'
} else {
    $Root
}
$migrationPath = Join-Path $rootPath 'migrations/002_seed_policies.sql'
if (-not (Test-Path $migrationPath -PathType Leaf)) {
    throw "Policy migration is missing: $migrationPath"
}

$sql = Get-Content -Raw -LiteralPath $migrationPath
foreach ($expected in @("(1, 98, 'inventory')", "(1, 110, 'state_transition')")) {
    if ($sql -notmatch [regex]::Escape($expected)) {
        throw "Required policy seed is missing: $expected"
    }
}
if ($sql -notmatch "INSERT INTO schema_migrations\s*\(version\)\s*VALUES \('002_seed_policies'\)") {
    throw '002_seed_policies must record its applied version.'
}

$psql = Get-Command psql -ErrorAction SilentlyContinue
$databaseUrl = $env:DATAHUB_TEST_DATABASE_URL
if ($null -ne $psql -and -not [string]::IsNullOrWhiteSpace($databaseUrl)) {
    $rows = (& $psql.Source $databaseUrl --tuples-only --no-align --set ON_ERROR_STOP=1 --command "SELECT scan_type_code || ':' || event_kind FROM jms_event_policies WHERE reducer_version = 1 AND scan_type_code IN (98, 110) ORDER BY scan_type_code;").Trim()
    if ($LASTEXITCODE -ne 0 -or $rows -notcontains '98:inventory' -or $rows -notcontains '110:state_transition') {
        throw 'Seed policy catalog assertions failed.'
    }
}

Write-Host '002_seed_policies.sql static contract checks passed.' -ForegroundColor Green

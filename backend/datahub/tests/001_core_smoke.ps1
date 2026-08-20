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
$migrationPath = Join-Path $rootPath 'migrations/001_core.sql'
$catalogAssertionsPath = Join-Path $PSScriptRoot '001_core_catalog_assertions.sql'
$runnerPath = Join-Path $PSScriptRoot '..\scripts\apply-migrations.ps1'

if (-not (Test-Path $migrationPath -PathType Leaf)) {
    throw "Core migration is missing: $migrationPath"
}

$sql = Get-Content -Raw -LiteralPath $migrationPath
$requiredTables = @(
    'sites',
    'devices',
    'site_fetch_leases',
    'site_change_counters',
    'waybill_scan_events',
    'waybill_projections',
    'dashboard_changes',
    'jms_event_policies',
    'idempotency_records',
    'retention_policies',
    'audit_logs'
)

foreach ($table in $requiredTables) {
    if ($sql -notmatch "CREATE TABLE IF NOT EXISTS\s+$table\b") {
        throw "Required table declaration is missing: $table"
    }
}

if ($sql -notmatch "CREATE TABLE IF NOT EXISTS\s+schema_migrations\b") {
    throw 'schema_migrations is required for forward-only migration tracking.'
}
if ($sql -notmatch "INSERT INTO schema_migrations\s*\(version\)\s*VALUES \('001_core'\)") {
    throw '001_core must record its applied version.'
}

if ($sql -notmatch 'leader_device_id\s+uuid\s+NULL') {
    throw 'site_fetch_leases.leader_device_id must be nullable.'
}

if ($sql -match '(?i)uploadtime') {
    throw 'uploadTime must not be a migration column, index, or reducer field.'
}

if ($sql -match 'ix_dashboard_changes_site_seq') {
    throw 'The duplicate dashboard_changes site/sequence index must not exist.'
}

$dashboardDeclaration = [regex]::Match(
    $sql,
    '(?is)CREATE TABLE IF NOT EXISTS\s+dashboard_changes\s*\(.*?\);'
).Value
if ([string]::IsNullOrWhiteSpace($dashboardDeclaration)) {
    throw 'dashboard_changes table declaration is missing.'
}
if ($dashboardDeclaration -match 'GENERATED\s+ALWAYS\s+AS\s+IDENTITY') {
    throw 'dashboard_changes must use the per-site counter, not an identity cursor.'
}

if ($sql -notmatch 'PRIMARY KEY\s*\(\s*site_id\s*,\s*change_seq\s*\)') {
    throw 'dashboard_changes must have a per-site (site_id, change_seq) primary key.'
}

if ($sql -notmatch 'CREATE UNIQUE INDEX IF NOT EXISTS\s+ux_retention_policies_global_table') {
    throw 'The global retention policy uniqueness guard is missing.'
}

if ($sql -notmatch 'CREATE UNIQUE INDEX IF NOT EXISTS\s+ux_retention_policies_site_table') {
    throw 'The per-site retention policy uniqueness guard is missing.'
}

$psql = Get-Command psql -ErrorAction SilentlyContinue
$databaseUrl = $env:DATAHUB_TEST_DATABASE_URL
if ($null -ne $psql -and -not [string]::IsNullOrWhiteSpace($databaseUrl)) {
    for ($run = 1; $run -le 2; $run++) {
        & $runnerPath -DatabaseUrl $databaseUrl -MigrationDirectory (Join-Path $rootPath 'migrations')
        if ($LASTEXITCODE -ne 0) {
            throw "Migration runner failed on pass $run with exit code $LASTEXITCODE."
        }
    }

    & $psql.Source $databaseUrl --set ON_ERROR_STOP=1 --file $catalogAssertionsPath
    if ($LASTEXITCODE -ne 0) {
        throw "Core migration catalog assertions failed with exit code $LASTEXITCODE."
    }
}

Write-Host '001_core.sql static contract checks passed.' -ForegroundColor Green
if ($null -eq $psql -or [string]::IsNullOrWhiteSpace($databaseUrl)) {
    Write-Host 'PostgreSQL execution skipped: set DATAHUB_TEST_DATABASE_URL and install psql to run catalog assertions.' -ForegroundColor Yellow
}

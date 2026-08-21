[CmdletBinding()]
param([string]$Root)

$ErrorActionPreference = 'Stop'
$rootPath = if ([string]::IsNullOrWhiteSpace($Root)) { Join-Path $PSScriptRoot '..' } else { $Root }
$migrationPath = Join-Path $rootPath 'migrations/003_seed_retention.sql'
if (-not (Test-Path $migrationPath -PathType Leaf)) { throw "Retention migration is missing: $migrationPath" }
$sql = Get-Content -Raw -LiteralPath $migrationPath
foreach ($table in @('waybill_scan_events', 'dashboard_changes', 'audit_logs')) {
    if ($sql -notmatch [regex]::Escape("'$table'")) { throw "Retention policy seed is missing: $table" }
}
if ($sql -notmatch "INSERT INTO schema_migrations\s*\(version\)\s*VALUES \('003_seed_retention'\)") {
    throw '003_seed_retention must record its applied version.'
}
Write-Host '003_seed_retention.sql static contract checks passed.' -ForegroundColor Green

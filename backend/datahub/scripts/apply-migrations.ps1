[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$DatabaseUrl,

    [string]$MigrationDirectory
)

$ErrorActionPreference = 'Stop'
$migrationRoot = if ([string]::IsNullOrWhiteSpace($MigrationDirectory)) {
    Join-Path $PSScriptRoot '..\migrations'
} else {
    $MigrationDirectory
}
$psql = Get-Command psql -ErrorAction SilentlyContinue
if ($null -eq $psql) {
    throw 'psql is required to apply DataHub migrations.'
}
if (-not (Test-Path $migrationRoot -PathType Container)) {
    throw "Migration directory does not exist: $migrationRoot"
}

& $psql.Source $DatabaseUrl --set ON_ERROR_STOP=1 --command @"
CREATE TABLE IF NOT EXISTS schema_migrations (
    version text PRIMARY KEY,
    applied_at timestamptz NOT NULL DEFAULT now()
);
"@
if ($LASTEXITCODE -ne 0) {
    throw "Unable to bootstrap schema_migrations (exit code $LASTEXITCODE)."
}

$migrationFiles = Get-ChildItem -LiteralPath $migrationRoot -Filter '*.sql' -File |
    Where-Object { $_.Name -match '^\d+_[^/\\]+\.sql$' } |
    Sort-Object Name

foreach ($migration in $migrationFiles) {
    $version = [IO.Path]::GetFileNameWithoutExtension($migration.Name)
    $escapedVersion = $version.Replace("'", "''")
    $applied = (& $psql.Source $DatabaseUrl --tuples-only --no-align --set ON_ERROR_STOP=1 --command "SELECT 1 FROM schema_migrations WHERE version = '$escapedVersion';").Trim()
    if ($LASTEXITCODE -ne 0) {
        throw "Unable to inspect migration state for $version (exit code $LASTEXITCODE)."
    }
    if ($applied -eq '1') {
        Write-Host "SKIP $version" -ForegroundColor DarkGray
        continue
    }

    Write-Host "APPLY $version" -ForegroundColor Cyan
    & $psql.Source $DatabaseUrl --set ON_ERROR_STOP=1 --single-transaction --file $migration.FullName
    if ($LASTEXITCODE -ne 0) {
        throw "Migration $version failed with exit code $LASTEXITCODE."
    }

    $recorded = (& $psql.Source $DatabaseUrl --tuples-only --no-align --set ON_ERROR_STOP=1 --command "SELECT 1 FROM schema_migrations WHERE version = '$escapedVersion';").Trim()
    if ($LASTEXITCODE -ne 0 -or $recorded -ne '1') {
        throw "Migration $version completed without recording its version marker."
    }
}

Write-Host 'DataHub migrations complete.' -ForegroundColor Green

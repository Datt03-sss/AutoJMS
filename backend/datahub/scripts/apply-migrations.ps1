[CmdletBinding()]
param(
    [string]$DatabaseUrl,

    [string]$MigrationDirectory,

    [string]$ComposeFile,

    [string]$ComposeEnvFile,

    [string]$PostgresService = 'postgres'
)

$ErrorActionPreference = 'Stop'
$migrationRoot = if ([string]::IsNullOrWhiteSpace($MigrationDirectory)) {
    Join-Path $PSScriptRoot '..\migrations'
} else {
    $MigrationDirectory
}
$psql = Get-Command psql -ErrorAction SilentlyContinue
$docker = Get-Command docker -ErrorAction SilentlyContinue
$useCompose = -not [string]::IsNullOrWhiteSpace($ComposeFile)
if ($useCompose -and $null -eq $docker) {
    throw 'docker is required when ComposeFile is supplied.'
}
if (-not $useCompose -and [string]::IsNullOrWhiteSpace($DatabaseUrl)) {
    throw 'DatabaseUrl is required when ComposeFile is not supplied.'
}
if (-not $useCompose -and $null -eq $psql) {
    throw 'psql is required to apply DataHub migrations.'
}
if (-not (Test-Path $migrationRoot -PathType Container)) {
    throw "Migration directory does not exist: $migrationRoot"
}

function Get-ComposeArguments {
    $arguments = @('compose', '--file', (Resolve-Path -LiteralPath $ComposeFile).Path)
    if (-not [string]::IsNullOrWhiteSpace($ComposeEnvFile)) {
        $arguments += @('--env-file', (Resolve-Path -LiteralPath $ComposeEnvFile).Path)
    }
    return $arguments
}

function Get-ComposePsqlArguments {
    param([string[]]$Arguments)

    return @(Get-ComposeArguments) + @(
        'exec', '-T', $PostgresService,
        'sh', '-ec',
        'exec psql --username "$POSTGRES_USER" --dbname "$POSTGRES_DB" "$@"',
        'sh'
    ) + $Arguments
}

function Invoke-Psql {
    param(
        [string[]]$Arguments,
        [string]$InputFile
    )

    if (-not $useCompose) {
        & $psql.Source $DatabaseUrl @Arguments
    } else {
        $dockerArguments = Get-ComposePsqlArguments $Arguments
        if ([string]::IsNullOrWhiteSpace($InputFile)) {
            & $docker.Source @dockerArguments
        } else {
            Get-Content -Raw -LiteralPath $InputFile | & $docker.Source @dockerArguments
        }
    }
    if ($LASTEXITCODE -ne 0) {
        throw "psql failed with exit code $LASTEXITCODE."
    }
}

function Invoke-PsqlQuery {
    param([string]$Sql)

    if (-not $useCompose) {
        return (& $psql.Source $DatabaseUrl --tuples-only --no-align --set ON_ERROR_STOP=1 --command $Sql)
    }

    $dockerArguments = Get-ComposePsqlArguments @('--tuples-only', '--no-align', '--set', 'ON_ERROR_STOP=1', '--command', $Sql)
    return (& $docker.Source @dockerArguments)
}

Invoke-Psql @('--set', 'ON_ERROR_STOP=1', '--command', @"
CREATE TABLE IF NOT EXISTS schema_migrations (
    version text PRIMARY KEY,
    applied_at timestamptz NOT NULL DEFAULT now()
);
"@)

$migrationFiles = Get-ChildItem -LiteralPath $migrationRoot -Filter '*.sql' -File |
    Where-Object { $_.Name -match '^\d+_[^/\\]+\.sql$' } |
    Sort-Object Name

foreach ($migration in $migrationFiles) {
    $version = [IO.Path]::GetFileNameWithoutExtension($migration.Name)
    $escapedVersion = $version.Replace("'", "''")
    $applied = (Invoke-PsqlQuery "SELECT 1 FROM schema_migrations WHERE version = '$escapedVersion';" | Out-String).Trim()
    if ($LASTEXITCODE -ne 0) {
        throw "Unable to inspect migration state for $version (exit code $LASTEXITCODE)."
    }
    if ($applied -eq '1') {
        Write-Host "SKIP $version" -ForegroundColor DarkGray
        continue
    }

    Write-Host "APPLY $version" -ForegroundColor Cyan
    Invoke-Psql @('--set', 'ON_ERROR_STOP=1', '--single-transaction') $migration.FullName

    $recorded = (Invoke-PsqlQuery "SELECT 1 FROM schema_migrations WHERE version = '$escapedVersion';" | Out-String).Trim()
    if ($LASTEXITCODE -ne 0 -or $recorded -ne '1') {
        throw "Migration $version completed without recording its version marker."
    }
}

Write-Host 'DataHub migrations complete.' -ForegroundColor Green

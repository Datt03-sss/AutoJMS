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
        # --file, not stdin. $InputFile was accepted and then ignored on this branch,
        # so `-DatabaseUrl` mode handed psql no file and no --command: psql falls back
        # to reading the console, meaning the host-psql path never actually applied a
        # migration. Only the container branch below was ever exercised.
        if ([string]::IsNullOrWhiteSpace($InputFile)) {
            & $psql.Source $DatabaseUrl @Arguments
        } else {
            & $psql.Source $DatabaseUrl @Arguments '--file' $InputFile
        }
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

# Postgres refuses CREATE INDEX CONCURRENTLY (and ALTER TYPE ... ADD VALUE, and
# DROP INDEX CONCURRENTLY) inside a transaction block, while every file here was
# applied with --single-transaction. That made the two retention indexes the deploy
# plan calls for unwritable: building them non-concurrently takes an ACCESS EXCLUSIVE
# lock on a hot table for the duration of the build.
#
# Two opt-outs, checked in this order:
#   * filename suffix `_notx`, e.g. 006_dashboard_changes_time_index_notx.sql
#   * a line `-- no-transaction` anywhere in the file
# The suffix is visible in a directory listing; the marker is visible in review.
#
# The cost is real and is why this is opt-in per file: without a transaction a
# failure part-way leaves the earlier statements applied and the version marker
# unwritten, so the runner throws and the next run starts the file again from the
# top. Such a file MUST be idempotent statement by statement (IF NOT EXISTS on
# every object). One trap that idempotency alone does not cover: a failed CREATE
# INDEX CONCURRENTLY leaves an INVALID index behind, and IF NOT EXISTS then sees a
# name that exists and skips it forever. Recovering means dropping it by hand —
#   SELECT indexrelid::regclass FROM pg_index WHERE NOT indisvalid;
#   DROP INDEX CONCURRENTLY <name>;
# — before re-running.
function Test-NoTransactionMigration {
    param([string]$Version, [string]$Path)

    if ($Version -match '_notx$') { return $true }
    # (\s|$), not \b: .NET puts a word boundary before a hyphen, so \b would accept
    # `-- no-transaction-not-really` while the POSIX ERE in the bash runner rejects
    # it — the same file would then be atomic or not depending on the deploy host.
    return ((Get-Content -LiteralPath $Path -Raw) -match '(?m)^\s*--\s*no-transaction(\s|$)')
}

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

    if (Test-NoTransactionMigration $version $migration.FullName) {
        Write-Host "APPLY $version (NO TRANSACTION)" -ForegroundColor Yellow
        Write-Host '      not atomic: a mid-file failure leaves earlier statements applied' -ForegroundColor DarkYellow
        Write-Host '      and the marker unwritten, so this file must be re-runnable as-is.' -ForegroundColor DarkYellow
        Invoke-Psql @('--set', 'ON_ERROR_STOP=1') $migration.FullName
    } else {
        Write-Host "APPLY $version" -ForegroundColor Cyan
        Invoke-Psql @('--set', 'ON_ERROR_STOP=1', '--single-transaction') $migration.FullName
    }

    $recorded = (Invoke-PsqlQuery "SELECT 1 FROM schema_migrations WHERE version = '$escapedVersion';" | Out-String).Trim()
    if ($LASTEXITCODE -ne 0 -or $recorded -ne '1') {
        throw "Migration $version completed without recording its version marker."
    }
}

Write-Host 'DataHub migrations complete.' -ForegroundColor Green

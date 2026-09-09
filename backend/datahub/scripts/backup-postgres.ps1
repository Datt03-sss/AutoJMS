[CmdletBinding()]
param(
    [string]$DatabaseUrl,

    [Parameter(Mandatory = $true)]
    [string]$OutputDirectory,

    [string]$ComposeFile,

    [string]$ComposeEnvFile,

    [string]$PostgresService = 'postgres',

    # Full schema for the whole database, but no row data for the observation
    # tables named in critical-backup-exclusions.txt. Turns a server migration
    # into a small dump plus a re-fetch from JMS. Read that file before using it.
    [switch]$CriticalOnly
)

$ErrorActionPreference = 'Stop'
$pgDump = Get-Command pg_dump -ErrorAction SilentlyContinue
$docker = Get-Command docker -ErrorAction SilentlyContinue
$useCompose = -not [string]::IsNullOrWhiteSpace($ComposeFile)
if ($useCompose -and $null -eq $docker) { throw 'docker is required when ComposeFile is supplied.' }
if (-not $useCompose -and [string]::IsNullOrWhiteSpace($DatabaseUrl)) { throw 'DatabaseUrl is required when ComposeFile is not supplied.' }
if (-not $useCompose -and $null -eq $pgDump) { throw 'pg_dump is required to create a DataHub backup.' }
if (-not (Test-Path $OutputDirectory -PathType Container)) {
    New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
}

# The table list lives in a sibling file because backup-postgres.sh reads the
# same one: two hand-maintained copies in two languages would drift, and the
# drift would only surface during a real recovery.
$excludedTables = @()
if ($CriticalOnly) {
    $exclusionFile = Join-Path $PSScriptRoot 'critical-backup-exclusions.txt'
    if (-not (Test-Path $exclusionFile -PathType Leaf)) { throw "-CriticalOnly requires $exclusionFile, which is missing." }
    $excludedTables = @(Get-Content -LiteralPath $exclusionFile |
        ForEach-Object { ($_ -replace '#.*$', '').Trim() } |
        Where-Object { $_ -ne '' })
    if ($excludedTables.Count -eq 0) { throw "$exclusionFile names no tables, so -CriticalOnly would silently produce a full backup." }
}
$excludeArguments = @($excludedTables | ForEach-Object { "--exclude-table-data=$_" })

$stamp = [DateTime]::UtcNow.ToString('yyyyMMddTHHmmssZ')
$target = Join-Path $OutputDirectory "datahub-$stamp.dump"
if (-not $useCompose) {
    & $pgDump.Source $DatabaseUrl --format=custom --compress=6 @excludeArguments --file=$target
    if ($LASTEXITCODE -ne 0) { throw "pg_dump failed with exit code $LASTEXITCODE." }
} else {
    $composeArguments = @('compose', '--file', (Resolve-Path -LiteralPath $ComposeFile).Path)
    if (-not [string]::IsNullOrWhiteSpace($ComposeEnvFile)) {
        $composeArguments += @('--env-file', (Resolve-Path -LiteralPath $ComposeEnvFile).Path)
    }
    $containerTarget = "/tmp/datahub-$stamp-$([Guid]::NewGuid().ToString('N')).dump"
    try {
        # The exclusions travel as positional arguments rather than being spliced
        # into the command string: a table name is data, and building shell text
        # out of data is how a quoting bug turns into command injection.
        & $docker.Source @composeArguments exec -T $PostgresService sh -ec `
            'file="$1"; shift; exec pg_dump --username "$POSTGRES_USER" --dbname "$POSTGRES_DB" --format=custom --compress=6 --file "$file" "$@"' `
            sh $containerTarget @excludeArguments
        if ($LASTEXITCODE -ne 0) { throw "container pg_dump failed with exit code $LASTEXITCODE." }
        & $docker.Source @composeArguments cp "${PostgresService}:$containerTarget" $target
        if ($LASTEXITCODE -ne 0) { throw "docker compose cp failed with exit code $LASTEXITCODE." }
    } finally {
        & $docker.Source @composeArguments exec -T $PostgresService rm -f -- $containerTarget 2>$null
    }
}
Write-Host "Created $target. Encrypt and upload it outside this script." -ForegroundColor Green
if ($CriticalOnly) {
    Write-Host "Critical-only: full schema, no data for $($excludedTables -join ', ')." -ForegroundColor Yellow
    Write-Host 'After restoring this dump, run: UPDATE site_change_counters SET pruned_through_seq = change_seq;' -ForegroundColor Yellow
    Write-Host 'Skipping that leaves a station that was offline during the migration believing it is up to date.' -ForegroundColor Yellow
}

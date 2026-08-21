[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [string]$DatabaseUrl,

    [Parameter(Mandatory = $true)]
    [string]$DumpFile,

    [string]$ComposeFile,

    [string]$ComposeEnvFile,

    [string]$PostgresService = 'postgres',

    [switch]$AllowExistingData
)

$ErrorActionPreference = 'Stop'
$pgRestore = Get-Command pg_restore -ErrorAction SilentlyContinue
$docker = Get-Command docker -ErrorAction SilentlyContinue
$useCompose = -not [string]::IsNullOrWhiteSpace($ComposeFile)
if ($useCompose -and $null -eq $docker) { throw 'docker is required when ComposeFile is supplied.' }
if (-not $useCompose -and [string]::IsNullOrWhiteSpace($DatabaseUrl)) { throw 'DatabaseUrl is required when ComposeFile is not supplied.' }
if (-not $useCompose -and $null -eq $pgRestore) { throw 'pg_restore is required to restore a DataHub backup.' }
if (-not (Test-Path $DumpFile -PathType Leaf)) { throw "Dump file does not exist: $DumpFile" }

if ($PSCmdlet.ShouldProcess(($useCompose ? $PostgresService : $DatabaseUrl), "restore $DumpFile")) {
    if (-not $useCompose) {
        $restoreArgs = @('--dbname=' + $DatabaseUrl, '--format=custom', '--exit-on-error', '--single-transaction', '--no-owner', '--no-privileges')
        if ($AllowExistingData) { $restoreArgs += @('--clean', '--if-exists') }
        & $pgRestore.Source @restoreArgs $DumpFile
        if ($LASTEXITCODE -ne 0) { throw "pg_restore failed with exit code $LASTEXITCODE." }
    } else {
        $composeArguments = @('compose', '--file', (Resolve-Path -LiteralPath $ComposeFile).Path)
        if (-not [string]::IsNullOrWhiteSpace($ComposeEnvFile)) {
            $composeArguments += @('--env-file', (Resolve-Path -LiteralPath $ComposeEnvFile).Path)
        }
        $containerDump = "/tmp/datahub-restore-$([Guid]::NewGuid().ToString('N')).dump"
        try {
            & $docker.Source @composeArguments cp (Resolve-Path -LiteralPath $DumpFile).Path "${PostgresService}:$containerDump"
            if ($LASTEXITCODE -ne 0) { throw "docker compose cp failed with exit code $LASTEXITCODE." }
            $restoreCommand = 'pg_restore --username "$POSTGRES_USER" --dbname "$POSTGRES_DB" --format=custom --exit-on-error --single-transaction --no-owner --no-privileges'
            if ($AllowExistingData) { $restoreCommand += ' --clean --if-exists' }
            $restoreCommand += ' "$1"'
            & $docker.Source @composeArguments exec -T $PostgresService sh -ec `
                "exec $restoreCommand" `
                sh $containerDump
            if ($LASTEXITCODE -ne 0) { throw "container pg_restore failed with exit code $LASTEXITCODE." }
        } finally {
            & $docker.Source @composeArguments exec -T $PostgresService rm -f -- $containerDump 2>$null
        }
    }
    Write-Host 'Restore completed. Run migrations and catalog assertions before serving traffic.' -ForegroundColor Green
}

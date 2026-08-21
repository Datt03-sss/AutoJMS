[CmdletBinding()]
param(
    [string]$DatabaseUrl,

    [Parameter(Mandatory = $true)]
    [string]$OutputDirectory,

    [string]$ComposeFile,

    [string]$ComposeEnvFile,

    [string]$PostgresService = 'postgres'
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

$stamp = [DateTime]::UtcNow.ToString('yyyyMMddTHHmmssZ')
$target = Join-Path $OutputDirectory "datahub-$stamp.dump"
if (-not $useCompose) {
    & $pgDump.Source $DatabaseUrl --format=custom --compress=6 --file=$target
    if ($LASTEXITCODE -ne 0) { throw "pg_dump failed with exit code $LASTEXITCODE." }
} else {
    $composeArguments = @('compose', '--file', (Resolve-Path -LiteralPath $ComposeFile).Path)
    if (-not [string]::IsNullOrWhiteSpace($ComposeEnvFile)) {
        $composeArguments += @('--env-file', (Resolve-Path -LiteralPath $ComposeEnvFile).Path)
    }
    $containerTarget = "/tmp/datahub-$stamp-$([Guid]::NewGuid().ToString('N')).dump"
    try {
        & $docker.Source @composeArguments exec -T $PostgresService sh -ec `
            'exec pg_dump --username "$POSTGRES_USER" --dbname "$POSTGRES_DB" --format=custom --compress=6 --file "$1"' `
            sh $containerTarget
        if ($LASTEXITCODE -ne 0) { throw "container pg_dump failed with exit code $LASTEXITCODE." }
        & $docker.Source @composeArguments cp "${PostgresService}:$containerTarget" $target
        if ($LASTEXITCODE -ne 0) { throw "docker compose cp failed with exit code $LASTEXITCODE." }
    } finally {
        & $docker.Source @composeArguments exec -T $PostgresService rm -f -- $containerTarget 2>$null
    }
}
Write-Host "Created $target. Encrypt and upload it outside this script." -ForegroundColor Green

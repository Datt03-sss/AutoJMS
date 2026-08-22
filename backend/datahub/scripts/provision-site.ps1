[CmdletBinding()]
param(
    [string]$DatabaseUrl,

    [Parameter(Mandatory = $true)]
    [Guid]$SiteId,

    [Parameter(Mandatory = $true)]
    [ValidatePattern('^[^\s]+$')]
    [string]$SiteCode,

    [string]$ComposeFile,

    [string]$ComposeEnvFile,

    [string]$PostgresService = 'postgres'
)

$ErrorActionPreference = 'Stop'
$SiteCode = $SiteCode.Trim().ToUpperInvariant()
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
    throw 'psql is required to provision a DataHub site.'
}

# psql expands :'var' only for SQL read from stdin or -f; a string passed to
# --command is forwarded to the server verbatim and fails to parse the colon.
$provisionSql = "BEGIN; SELECT create_datahub_site(:'site_id'::uuid, :'site_code'); COMMIT;"
$psqlArguments = @(
    '--set', 'ON_ERROR_STOP=1',
    '--variable', "site_id=$SiteId",
    '--variable', "site_code=$SiteCode"
)
if (-not $useCompose) {
    $provisionSql | & $psql.Source $DatabaseUrl @psqlArguments
} else {
    $composeArguments = @('compose', '--file', (Resolve-Path -LiteralPath $ComposeFile).Path)
    if (-not [string]::IsNullOrWhiteSpace($ComposeEnvFile)) {
        $composeArguments += @('--env-file', (Resolve-Path -LiteralPath $ComposeEnvFile).Path)
    }
    $composeArguments += @(
        'exec', '-T', $PostgresService,
        'sh', '-ec',
        'exec psql --username "$POSTGRES_USER" --dbname "$POSTGRES_DB" "$@"',
        'sh'
    ) + $psqlArguments
    $provisionSql | & $docker.Source @composeArguments
}
if ($LASTEXITCODE -ne 0) {
    throw "Site provisioning failed with exit code $LASTEXITCODE."
}

Write-Host "Provisioned site $SiteCode ($SiteId) with lease and change counter." -ForegroundColor Green

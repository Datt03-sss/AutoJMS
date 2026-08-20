[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$DatabaseUrl,

    [Parameter(Mandatory = $true)]
    [Guid]$SiteId,

    [Parameter(Mandatory = $true)]
    [ValidatePattern('^[^\s]+$')]
    [string]$SiteCode
)

$ErrorActionPreference = 'Stop'
$psql = Get-Command psql -ErrorAction SilentlyContinue
if ($null -eq $psql) {
    throw 'psql is required to provision a DataHub site.'
}

& $psql.Source $DatabaseUrl `
    --set ON_ERROR_STOP=1 `
    --variable "site_id=$SiteId" `
    --variable "site_code=$SiteCode" `
    --command "BEGIN; SELECT create_datahub_site(:'site_id'::uuid, :'site_code'); COMMIT;"
if ($LASTEXITCODE -ne 0) {
    throw "Site provisioning failed with exit code $LASTEXITCODE."
}

Write-Host "Provisioned site $SiteCode ($SiteId) with lease and change counter." -ForegroundColor Green

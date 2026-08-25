<#
.SYNOPSIS
    Publishes the control-plane seed objects in backend/datahub/seeds to a DataHub API.

.DESCRIPTION
    A fresh VPS serves 404 for every policy path, and that 404 is not neutral:
    VpsRuntimePolicyService falls through to SafeDefault("BASE"), so every ULTRA
    station silently runs as BASE with nothing failing anywhere. Running this after
    apply-migrations is what makes a new deployment usable. See ../seeds/README.md.

    Every object is published and then read back over the ANONYMOUS path, and the
    two ETags are compared. That second request is the point: a PUT accepted by
    the API while the public GET still 404s is what a misconfigured reverse proxy
    looks like, and it is invisible if you only check the publish response.

    The admin token is never a parameter: a value passed on the command line ends
    up in PSReadLine history and in transcript logs. It comes from
    $env:DATAHUB_ADMIN_TOKEN, or from DATAHUB_ADMIN_TOKEN in -EnvFile.

.EXAMPLE
    ./publish-manifests.ps1 -EnvFile C:\secrets\.env.staging

.EXAMPLE
    $env:DATAHUB_ADMIN_TOKEN = (Read-Host -MaskInput 'admin token')
    ./publish-manifests.ps1 -ApiUrl https://datahub.example.com

.EXAMPLE
    ./publish-manifests.ps1 -EnvFile C:\secrets\.env.production -DryRun
#>
[CmdletBinding()]
param(
    [string]$ApiUrl,

    [string]$EnvFile,

    [string]$SeedDir,

    [switch]$DryRun
)

$ErrorActionPreference = 'Stop'
if ($PSVersionTable.PSVersion.Major -lt 7) {
    throw 'PowerShell 7 or later is required (-SkipHttpErrorCheck).'
}

$datahubDir = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($SeedDir)) {
    $SeedDir = Join-Path $datahubDir 'seeds'
}
if (-not (Test-Path -LiteralPath $SeedDir -PathType Container)) {
    throw "Seed directory does not exist: $SeedDir"
}
$SeedDir = (Resolve-Path -LiteralPath $SeedDir).Path

# Reads one key out of the env file and returns only that value, never the file.
function Get-EnvFileValue {
    param([string]$Path, [string]$Key)

    foreach ($line in Get-Content -LiteralPath $Path) {
        if ($line -match "^\s*$([regex]::Escape($Key))\s*=\s*(.*)$") {
            return $Matches[1].Trim()
        }
    }
    return ''
}

if ($EnvFile) {
    if (-not (Test-Path -LiteralPath $EnvFile -PathType Leaf)) {
        throw "Env file not found: $EnvFile"
    }
    $EnvFile = (Resolve-Path -LiteralPath $EnvFile).Path
}

# --- where to publish --------------------------------------------------------
if ([string]::IsNullOrWhiteSpace($ApiUrl)) {
    if (-not $EnvFile) {
        throw 'Pass -ApiUrl, or -EnvFile so DATAHUB_PUBLIC_HOST can supply it.'
    }
    $publicHost = Get-EnvFileValue -Path $EnvFile -Key 'DATAHUB_PUBLIC_HOST'
    if ([string]::IsNullOrWhiteSpace($publicHost)) {
        throw "DATAHUB_PUBLIC_HOST is not set in $EnvFile; pass -ApiUrl instead."
    }
    $ApiUrl = "https://$publicHost"
}
$ApiUrl = $ApiUrl.TrimEnd('/')

# Plain http would put the admin token on the wire in clear text. Localhost is the
# one exception, for publishing from inside the VPS before DNS or TLS exists.
if ($ApiUrl -notmatch '^https://' -and $ApiUrl -notmatch '^http://(localhost|127\.0\.0\.1)(:\d+)?$') {
    throw "Refusing to send the admin token over $ApiUrl; use https, or http on localhost only."
}

# --- the admin token ---------------------------------------------------------
$adminToken = $env:DATAHUB_ADMIN_TOKEN
if ([string]::IsNullOrWhiteSpace($adminToken) -and $EnvFile) {
    $adminToken = Get-EnvFileValue -Path $EnvFile -Key 'DATAHUB_ADMIN_TOKEN'
}
if ([string]::IsNullOrWhiteSpace($adminToken)) {
    throw 'No admin token: set $env:DATAHUB_ADMIN_TOKEN, or pass -EnvFile containing DATAHUB_ADMIN_TOKEN.'
}
$adminToken = $adminToken.Trim()
if ($adminToken.StartsWith('REPLACE_WITH')) {
    throw 'DATAHUB_ADMIN_TOKEN is still the template placeholder.'
}

# first4...last4, so the log proves which secret was used without disclosing it.
$maskedToken = if ($adminToken.Length -le 12) {
    '<redacted>'
} else {
    '{0}...{1} ({2} chars)' -f $adminToken.Substring(0, 4), $adminToken.Substring($adminToken.Length - 4), $adminToken.Length
}

Write-Host 'DataHub manifest publish'
Write-Host ('  api         {0}' -f $ApiUrl)
Write-Host ('  seeds       {0}' -f $SeedDir)
Write-Host ('  admin token {0}' -f $maskedToken)

function Get-HeaderValue {
    param($Response, [string]$Name)

    if ($null -eq $Response -or $null -eq $Response.Headers) { return '' }
    if (-not $Response.Headers.ContainsKey($Name)) { return '' }
    return (@($Response.Headers[$Name]) -join '')
}

$published = 0
$failed = 0

# The directory layout is the object path: seeds/configs/runtime-policy.json is
# published as configs/runtime-policy.json.
$files = Get-ChildItem -LiteralPath $SeedDir -Recurse -File |
    Where-Object { $_.Name -ne 'README.md' -and -not $_.Name.StartsWith('.') } |
    Sort-Object FullName
if ($files.Count -eq 0) {
    throw "No publishable files found under $SeedDir"
}

foreach ($file in $files) {
    $objectPath = $file.FullName.Substring($SeedDir.Length).TrimStart('\', '/').Replace('\', '/')
    $publishUri = "$ApiUrl/api/v1/admin/manifests/$objectPath"

    if ($DryRun) {
        Write-Host ('DRY   {0} -> PUT {1}' -f $objectPath, $publishUri)
        continue
    }

    $contentType = if ($objectPath.EndsWith('.json')) { 'application/json; charset=utf-8' } else { 'application/octet-stream' }
    # Bytes, not a string: handing Invoke-WebRequest a string re-encodes it, and
    # these documents contain Vietnamese text that must reach the API as the exact
    # UTF-8 bytes on disk.
    $body = [System.IO.File]::ReadAllBytes($file.FullName)

    try {
        $putResponse = Invoke-WebRequest -Method Put -Uri $publishUri `
            -Headers @{ Authorization = "Bearer $adminToken" } `
            -ContentType $contentType `
            -Body $body `
            -SkipHttpErrorCheck
    } catch {
        Write-Host ('FAIL  {0} ({1})' -f $objectPath, $_.Exception.Message)
        $failed++
        continue
    }

    if ($putResponse.StatusCode -ne 200 -and $putResponse.StatusCode -ne 201) {
        Write-Host ('FAIL  {0} (HTTP {1}) {2}' -f $objectPath, $putResponse.StatusCode, $putResponse.Content)
        $failed++
        continue
    }
    $putETag = Get-HeaderValue -Response $putResponse -Name 'ETag'

    # No credentials on this request on purpose: this is the request a station
    # makes, and the only one that proves the object is actually reachable.
    try {
        $getResponse = Invoke-WebRequest -Method Head -Uri "$ApiUrl/$objectPath" -SkipHttpErrorCheck
    } catch {
        Write-Host ('WARN  {0} published, but the anonymous read-back failed: {1}' -f $objectPath, $_.Exception.Message)
        $published++
        continue
    }

    if ($getResponse.StatusCode -ne 200) {
        Write-Host ('FAIL  {0} published (HTTP {1}) but GET /{0} answered {2} — check the reverse proxy' -f `
            $objectPath, $putResponse.StatusCode, $getResponse.StatusCode)
        $failed++
        continue
    }

    $getETag = Get-HeaderValue -Response $getResponse -Name 'ETag'
    if ($putETag -and $putETag -ne $getETag) {
        Write-Host ('FAIL  {0} published but the served copy has a different ETag ({1} vs {2}) — something else is answering' -f `
            $objectPath, $putETag, $getETag)
        $failed++
        continue
    }

    Write-Host ('OK    {0} (HTTP {1}, etag {2})' -f $objectPath, $putResponse.StatusCode, $getETag)
    $published++
}

if ($DryRun) {
    Write-Host 'Dry run only; nothing was published.'
    return
}

Write-Host ('DataHub manifest publish complete ({0} published, {1} failed).' -f $published, $failed)
if ($failed -gt 0) {
    throw "$failed object(s) failed to publish."
}

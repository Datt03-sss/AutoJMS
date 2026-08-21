[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$SigningKey,
    [Parameter(Mandatory = $true)][string]$Issuer,
    [Parameter(Mandatory = $true)][string]$Audience,
    [Parameter(Mandatory = $true)][string[]]$SiteCode,
    [int]$Seats = 1,
    [int]$TokenVersion = 1,
    [datetime]$ExpiresAt = (Get-Date).ToUniversalTime().AddHours(8),
    [string]$DataHubUrl
)

$ErrorActionPreference = 'Stop'
if ($SigningKey.Length -lt 32) { throw 'SigningKey must be at least 32 characters.' }
if ($Seats -lt 1 -or $TokenVersion -lt 1) { throw 'Seats and TokenVersion must be positive.' }
$expiryUtc = $ExpiresAt.ToUniversalTime()
if ($expiryUtc -le [DateTime]::UtcNow) { throw 'ExpiresAt must be in the future.' }
$sites = @($SiteCode | ForEach-Object { $_.Trim().ToUpperInvariant() } | Where-Object { $_ } | Select-Object -Unique)
if ($sites.Count -eq 0) { throw 'At least one SiteCode is required.' }
if ($DataHubUrl -and (-not [Uri]::TryCreate($DataHubUrl, [UriKind]::Absolute, [ref]$null) -or -not $DataHubUrl.StartsWith('https://', [StringComparison]::OrdinalIgnoreCase))) {
    throw 'DataHubUrl must be an HTTPS URL.'
}

$payload = [ordered]@{
    Channel = 'staging'
    SiteCodes = $sites
    ExpiresAt = [DateTimeOffset]::new($expiryUtc).ToUnixTimeSeconds()
    DataHubUrl = if ($DataHubUrl) { $DataHubUrl } else { $null }
    Seats = $Seats
    TokenVersion = $TokenVersion
    Issuer = $Issuer
    Audience = $Audience
}
$json = $payload | ConvertTo-Json -Compress
$payloadBytes = [Text.Encoding]::UTF8.GetBytes($json)
$base64Payload = [Convert]::ToBase64String($payloadBytes).TrimEnd('=').Replace('+', '-').Replace('/', '_')
$keyBytes = [Text.Encoding]::UTF8.GetBytes($SigningKey)
$hmac = [Security.Cryptography.HMACSHA256]::new($keyBytes)
$signatureBytes = $hmac.ComputeHash([Text.Encoding]::UTF8.GetBytes($base64Payload))
$hmac.Dispose()
$signature = [Convert]::ToBase64String($signatureBytes).TrimEnd('=').Replace('+', '-').Replace('/', '_')
Write-Output "v1.$base64Payload.$signature"

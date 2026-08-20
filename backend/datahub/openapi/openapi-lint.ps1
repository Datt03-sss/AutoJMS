[CmdletBinding()]
param(
    [string]$Path
)

$ErrorActionPreference = 'Stop'

$specPath = if ([string]::IsNullOrWhiteSpace($Path)) {
    Join-Path $PSScriptRoot 'datahub-v1.yaml'
} else {
    (Resolve-Path -LiteralPath $Path).Path
}

if (-not (Test-Path -LiteralPath $specPath -PathType Leaf)) {
    throw "OpenAPI document does not exist: $specPath"
}

$document = Get-Content -LiteralPath $specPath -Raw
if ([string]::IsNullOrWhiteSpace($document)) {
    throw "OpenAPI document is empty: $specPath"
}

function Assert-Contains {
    param(
        [Parameter(Mandatory = $true)][string]$Pattern,
        [Parameter(Mandatory = $true)][string]$Message
    )

    if ($document -notmatch $Pattern) {
        throw "OpenAPI contract check failed: $Message"
    }
}

function Get-PathBlock {
    param([Parameter(Mandatory = $true)][string]$PathKey)

    $escaped = [regex]::Escape($PathKey)
    $match = [regex]::Match($document, "(?ms)^  ${escaped}:\s*.*?(?=^  /|\z)")
    if (-not $match.Success) {
        throw "OpenAPI contract check failed: missing path $PathKey"
    }
    return $match.Value
}

function Assert-PathContains {
    param(
        [Parameter(Mandatory = $true)][string]$PathKey,
        [Parameter(Mandatory = $true)][string]$Pattern,
        [Parameter(Mandatory = $true)][string]$Message
    )

    $block = Get-PathBlock -PathKey $PathKey
    if ($block -notmatch $Pattern) {
        throw "OpenAPI contract check failed: $Message ($PathKey)"
    }
}

# Prefer a locally installed standards linter. --no-install prevents this check
# from changing the workstation or downloading packages during a build. The
# DataHub-specific checks below always run as well.
$fullLintRan = $false
$redocly = Get-Command redocly -ErrorAction SilentlyContinue
if ($null -ne $redocly) {
    & $redocly.Source lint $specPath
    if ($LASTEXITCODE -ne 0) {
        throw "Redocly lint failed (exit code $LASTEXITCODE)."
    }
    $fullLintRan = $true
}

$npx = Get-Command npx -ErrorAction SilentlyContinue
if (-not $fullLintRan -and $null -ne $npx) {
    $npxHasRedocly = $false
    try {
        $probe = @(& $npx.Source '--no-install' '@redocly/cli' '--version' 2>$null)
        $npxHasRedocly = ($LASTEXITCODE -eq 0)
    } catch {
        $npxHasRedocly = $false
    }
    if ($npxHasRedocly) {
        & $npx.Source '--no-install' '@redocly/cli' 'lint' $specPath
        if ($LASTEXITCODE -ne 0) {
            throw "Redocly lint failed (exit code $LASTEXITCODE)."
        }
        $fullLintRan = $true
    }
}

# Static fallback for environments without an installed OpenAPI linter. These
# checks intentionally cover the contract invariants that are easy to regress;
# a CI image with Redocly should still be used for full YAML/reference validation.
$requiredTokens = @(
    'openapi: 3\.1\.0',
    '^info:',
    '^paths:',
    '^components:',
    '^    LicenseAssertion:',
    '^    DeviceBearer:',
    '/api/v1/devices/enroll:',
    '/api/v1/sites/\{siteId\}/lease/acquire:',
    '/api/v1/sites/\{siteId\}/lease/renew:',
    '/api/v1/sites/\{siteId\}/lease/release:',
    '/api/v1/sites/\{siteId\}/jms/ingest:',
    '/api/v1/sites/\{siteId\}/jms/observations:',
    '/api/v1/sites/\{siteId\}/changes:',
    '/api/v1/sites/\{siteId\}/projections/snapshot:',
    '^  /health/live:',
    '^  /health/ready:',
    '^  /hubs/site:'
)
foreach ($token in $requiredTokens) {
    Assert-Contains -Pattern "(?m)$token" -Message "missing required contract token '$token'"
}

Assert-PathContains '/api/v1/devices/enroll' '(?ms)security:\s+- LicenseAssertion:\s*\[\]' 'enrollment must use the signed license assertion'
Assert-PathContains '/api/v1/devices/enroll' 'x-license-assertion-only:\s*true' 'enrollment must be license-assertion-only'
Assert-PathContains '/api/v1/sites/{siteId}/jms/ingest' '(?ms)security:\s+- DeviceBearer:\s*\[\]' 'bulk ingest must require a device bearer token'
Assert-PathContains '/api/v1/sites/{siteId}/jms/observations' '(?ms)security:\s+- DeviceBearer:\s*\[\]' 'interactive ingest must require a device bearer token'
$idempotencyRef = [regex]::Escape('$ref: ''#/components/parameters/IdempotencyKey''')
$leaderTermRef = [regex]::Escape('$ref: ''#/components/parameters/LeaderTerm''')
Assert-PathContains '/api/v1/sites/{siteId}/jms/ingest' $idempotencyRef 'bulk ingest must require Idempotency-Key'
Assert-PathContains '/api/v1/sites/{siteId}/jms/observations' $idempotencyRef 'interactive ingest must require Idempotency-Key'
Assert-PathContains '/api/v1/sites/{siteId}/jms/ingest' $leaderTermRef 'bulk ingest must require X-Leader-Term'
$interactiveBlock = Get-PathBlock -PathKey '/api/v1/sites/{siteId}/jms/observations'
if ($interactiveBlock -match $leaderTermRef) {
    throw 'OpenAPI contract check failed: interactive observations must not carry X-Leader-Term.'
}

$bodyLimitCount = ([regex]::Matches($document, 'x-max-body-bytes:\s*1048576')).Count
if ($bodyLimitCount -lt 2) {
    throw 'OpenAPI contract check failed: both ingest paths must declare the 1 MiB body limit.'
}
$itemLimitCount = ([regex]::Matches($document, 'x-max-items:\s*200')).Count
if ($itemLimitCount -lt 2) {
    throw 'OpenAPI contract check failed: both ingest paths must declare the 200-item limit.'
}

Assert-Contains '(?m)^      required: \[waybillNo, scanTime, payload\]' 'scanTime and raw payload must be required observation fields'
Assert-Contains '(?m)^        scanTime:' 'observation schema must expose scanTime'
Assert-Contains '(?m)^        payload:' 'observation schema must expose a raw payload container'
Assert-Contains 'uploadTime.*only' 'uploadTime must be documented as raw-payload-only'
if ($document -match '(?m)^\s{8}uploadTime\s*:') {
    throw 'OpenAPI contract check failed: uploadTime must not be a hot/top-level observation property.'
}

Assert-Contains 'snapshot_seq' 'snapshot response must expose snapshot_seq'
Assert-Contains 'REPEATABLE READ' 'snapshot contract must require REPEATABLE READ'
Assert-Contains 'x-transaction-isolation:\s*REPEATABLE READ' 'snapshot isolation extension is missing'
foreach ($field in @('siteId', 'changeSeq', 'entityType', 'entityKey')) {
    Assert-Contains "(?m)^        ${field}:" "SignalR doorbell is missing $field"
}

foreach ($errorCode in @(
    'CHANNEL_MISMATCH',
    'SITE_NOT_LICENSED',
    'LEADER_FENCED',
    'LEASE_HELD',
    'IDEMPOTENCY_KEY_REUSED',
    'INVALID_SCAN_TIME',
    'UNAUTHORIZED',
    'FORBIDDEN',
    'NOT_FOUND',
    'PAYLOAD_TOO_LARGE',
    'VALIDATION_FAILED',
    'RATE_LIMITED',
    'SERVICE_UNAVAILABLE'
)) {
    Assert-Contains ([regex]::Escape($errorCode)) "required error code $errorCode is missing"
}

Assert-Contains '(?m)^    DataHubChannel:' 'DataHub channel schema is missing'
Assert-Contains 'DATAHUB_CHANNEL' 'deployment channel binding is missing'
Assert-Contains 'channel.*staging.*production|staging.*production.*channel' 'staging/production channel values are missing'
Assert-Contains '(?m)^        leaderDeviceId:\s*$' 'lease response must expose nullable leaderDeviceId'
Assert-Contains "(?ms)leaderDeviceId:\s*\r?\n\s+type:\s+\[string, 'null'\]\s*\r?\n\s+format:\s+uuid" 'leaderDeviceId must be nullable'

if ($fullLintRan) {
    Write-Host 'OpenAPI Redocly and static contract checks passed.' -ForegroundColor Green
} else {
    Write-Host 'OpenAPI static contract checks passed (no local Redocly CLI available).' -ForegroundColor Green
}

[CmdletBinding()]
param([string]$Root)

$ErrorActionPreference = 'Stop'
$rootPath = if ([string]::IsNullOrWhiteSpace($Root)) { Join-Path $PSScriptRoot '..' } else { $Root }
$compose = Get-Content -Raw (Join-Path $rootPath 'docker-compose.yml')
$caddy = Get-Content -Raw (Join-Path $rootPath 'Caddyfile')
$dockerfile = Get-Content -Raw (Join-Path $rootPath 'Dockerfile')
$migrationRunner = Get-Content -Raw (Join-Path $rootPath 'scripts/apply-migrations.ps1')
$provisionRunner = Get-Content -Raw (Join-Path $rootPath 'scripts/provision-site.ps1')
$backupRunner = Get-Content -Raw (Join-Path $rootPath 'scripts/backup-postgres.ps1')
$restoreRunner = Get-Content -Raw (Join-Path $rootPath 'scripts/restore-postgres.ps1')
$startRunner = Get-Content -Raw (Join-Path $rootPath 'scripts/start-stack.ps1')
$issuerRunner = Get-Content -Raw (Join-Path $rootPath 'scripts/issue-staging-assertion.ps1')
$repositoryRoot = (Resolve-Path (Join-Path $rootPath '..\..')).Path
$retentionRepository = Get-Content -Raw (Join-Path $repositoryRoot 'src/AutoJMS.DataHub.Api/Infrastructure/RetentionRepository.cs')
$program = Get-Content -Raw (Join-Path $repositoryRoot 'src/AutoJMS.DataHub.Api/Program.cs')
$authenticationMiddleware = Get-Content -Raw (Join-Path $repositoryRoot 'src/AutoJMS.DataHub.Api/Auth/DeviceAuthenticationMiddleware.cs')
$ingressRateLimitMiddleware = Get-Content -Raw (Join-Path $repositoryRoot 'src/AutoJMS.DataHub.Api/Auth/IngressRateLimitMiddleware.cs')
$rootDockerIgnore = Get-Content -Raw (Join-Path $repositoryRoot '.dockerignore')

if ($compose -match '(?ms)postgres:.*?ports:') { throw 'PostgreSQL must not publish a host port.' }
if ($compose -notmatch '- "80:80"' -or $compose -notmatch '- "443:443"') {
    throw 'Only Caddy should publish ports 80 and 443.'
}
if ($compose -notmatch 'internal:[ \t]+true') { throw 'The PostgreSQL network must be internal.' }
if ($compose -notmatch 'condition:[ \t]+service_healthy') { throw 'API/Caddy health-gated dependencies are required.' }
if ($compose -notmatch 'max-size:' -or $compose -notmatch 'max-file:') { throw 'Container log rotation must be configured.' }
if ($compose -notmatch 'ASPNETCORE_ENVIRONMENT') { throw 'Environment channel must be explicit.' }
if ($compose -match '(?m)^\s+build:') { throw 'The VPS Compose file must consume a prebuilt image, not rebuild source.' }
if ($caddy -notmatch '/hubs/site' -or $caddy -notmatch 'flush_interval -1') { throw 'SignalR WebSocket proxy settings are missing.' }
if ($dockerfile -notmatch 'mcr\.microsoft\.com/dotnet/aspnet:10\.0') { throw 'API must run on the .NET 10 ASP.NET runtime image.' }
if ($dockerfile -notmatch 'apt-get install -y --no-install-recommends curl') { throw 'API runtime must include curl for the readiness healthcheck.' }
if ($compose -notmatch 'curl.*health/ready') { throw 'Compose API healthcheck must use the installed curl binary.' }
if ($migrationRunner -notmatch "exec psql --username.*POSTGRES_USER.*--dbname.*POSTGRES_DB") { throw 'Compose migration mode must run psql inside the private PostgreSQL service.' }
if ($provisionRunner -notmatch 'ComposeFile' -or $provisionRunner -notmatch 'docker') { throw 'Site provisioning must support the private Compose database.' }
if ($backupRunner -notmatch 'ComposeFile' -or $backupRunner -notmatch 'docker.*compose') { throw 'Backup must support the private Compose database.' }
if ($restoreRunner -notmatch 'ComposeFile' -or $restoreRunner -notmatch 'docker.*compose') { throw 'Restore must support the private Compose database.' }
if ($restoreRunner -notmatch 'single-transaction' -or $restoreRunner -notmatch 'AllowExistingData') { throw 'Restore must be transactional and explicit before cleaning an existing database.' }
if ($retentionRepository -notmatch 'own short transaction' -or $retentionRepository -notmatch 'RunPartAsync' -or $retentionRepository -notmatch 'ORDER BY site_id\s+FOR UPDATE') {
    throw 'Retention categories must use short transactions and lock change counters before deleting changes.'
}
$ingestRepository = Get-Content -Raw (Join-Path $repositoryRoot 'src/AutoJMS.DataHub.Api/Infrastructure/IngestRepository.cs')
if ($ingestRepository -notmatch 'lease_expires_at\s*>\s*clock_timestamp\(\)') {
    throw 'Bulk fencing must use wall-clock time, not transaction-start now().'
}
if ($startRunner -notmatch '@sha256:\[0-9a-fA-F\]\{64\}' -or $startRunner -notmatch 'up.*--no-build') {
    throw 'Stack startup must reject mutable API tags and disable source builds.'
}
if ($issuerRunner -notmatch 'Channel = .staging.' -or $issuerRunner -notmatch 'HMACSHA256') { throw 'The staging assertion issuer helper is missing or not HMAC-compatible.' }
if ($dockerfile -match '(?i)(password|secret|token|BEGIN [A-Z ]+ PRIVATE KEY)\s*[:=]') { throw 'Dockerfile contains a possible secret.' }
if ($rootDockerIgnore -notmatch '(?m)^\.env$' -or $rootDockerIgnore -notmatch '(?m)^\.env\.\*$') {
    throw 'The repository-root Docker build context must exclude .env and .env.* secrets.'
}
if ($rootDockerIgnore -notmatch '(?m)^service[Aa]ccount.*\.json$' -or $rootDockerIgnore -notmatch '(?m)^\*\.pfx$') {
    throw 'The repository-root Docker build context must exclude credential JSON and certificates.'
}

$forwardedIndex = $program.IndexOf('app.UseForwardedHeaders()', [StringComparison]::Ordinal)
$ingressRateLimitIndex = $program.IndexOf('app.UseMiddleware<IngressRateLimitMiddleware>()', [StringComparison]::Ordinal)
$authenticationIndex = $program.IndexOf('app.UseMiddleware<DeviceAuthenticationMiddleware>()', [StringComparison]::Ordinal)
$rateLimitIndex = $program.IndexOf('app.UseRateLimiter()', [StringComparison]::Ordinal)
$deviceStatusIndex = $program.IndexOf('app.UseMiddleware<DeviceStatusMiddleware>()', [StringComparison]::Ordinal)
$pipelineOrdered = $forwardedIndex -ge 0 -and $ingressRateLimitIndex -ge 0 -and $authenticationIndex -ge 0 -and $rateLimitIndex -ge 0 -and $deviceStatusIndex -ge 0 `
    -and $forwardedIndex -lt $ingressRateLimitIndex -and $ingressRateLimitIndex -lt $authenticationIndex `
    -and $authenticationIndex -lt $rateLimitIndex -and $rateLimitIndex -lt $deviceStatusIndex
if (-not $pipelineOrdered) {
    throw 'Pipeline must process forwarded headers, ingress rate-limit, validate tokens, device rate-limit, then touch device status.'
}
if ($ingressRateLimitMiddleware -notmatch 'PartitionedRateLimiter' -or $ingressRateLimitMiddleware -notmatch 'RemoteIpAddress') {
    throw 'Public ingress must rate-limit invalid credentials by forwarded client IP before token validation.'
}
if ($ingressRateLimitMiddleware -notmatch 'AcquireDeviceAsync' -or $ingressRateLimitMiddleware -notmatch 'PermitLimit = 240') {
    throw 'Authenticated requests must have a per-device limiter before database activity.'
}
if ((Get-Content -Raw (Join-Path $repositoryRoot 'src/AutoJMS.DataHub.Api/Auth/DeviceStatusMiddleware.cs')) -notmatch 'AcquireDeviceAsync') {
    throw 'Device status updates must occur after the per-device limiter.'
}
if ($authenticationMiddleware -match 'DeviceRepository') {
    throw 'Token validation middleware must not touch PostgreSQL before rate limiting.'
}
if ($program -notmatch 'MapHub<SiteHub>\("/hubs/site"\)\.RequireRateLimiting\("device"\)') {
    throw 'SignalR hub must use the authenticated device rate-limit policy.'
}
if ($program -notmatch 'UnmappedMemberHandling\s*=\s*JsonUnmappedMemberHandling\.Disallow') {
    throw 'HTTP JSON binding must reject fields outside the OpenAPI request schemas.'
}

foreach ($envName in @('env.staging.template', 'env.production.template')) {
    $envText = Get-Content -Raw (Join-Path $rootPath $envName)
    if ($envText -notmatch 'REPLACE_WITH_') { throw "$envName must contain placeholders, not committed secrets." }
}
$staging = Get-Content -Raw (Join-Path $rootPath 'env.staging.template')
$production = Get-Content -Raw (Join-Path $rootPath 'env.production.template')
if ($staging -notmatch 'DATAHUB_CHANNEL=staging' -or $production -notmatch 'DATAHUB_CHANNEL=production') { throw 'Staging/production channels are not isolated.' }
if ($staging -notmatch 'datahub-dev\.example\.com' -or $production -notmatch 'datahub\.example\.com') { throw 'Stable DNS names are missing.' }
if ($staging -notmatch 'DATAHUB_API_IMAGE=.*@sha256:REPLACE_WITH_64_HEX_IMAGE_DIGEST' `
    -or $production -notmatch 'DATAHUB_API_IMAGE=.*@sha256:REPLACE_WITH_64_HEX_IMAGE_DIGEST') {
    throw 'Both environment templates must pin the API image by digest.'
}

Write-Host 'DataHub deployment static checks passed.' -ForegroundColor Green

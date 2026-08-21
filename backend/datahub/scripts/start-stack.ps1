[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$ComposeEnvFile,
    [string]$ComposeFile = (Join-Path $PSScriptRoot '..\docker-compose.yml')
)

$ErrorActionPreference = 'Stop'

foreach ($path in @($ComposeEnvFile, $ComposeFile)) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Required deployment file was not found: $path"
    }
}

$envText = Get-Content -Raw -LiteralPath $ComposeEnvFile
$imageMatch = [regex]::Match($envText, '(?m)^DATAHUB_API_IMAGE=(?<image>[^\r\n]+)$')
if (-not $imageMatch.Success) {
    throw 'DATAHUB_API_IMAGE is required in the deployment env file.'
}

$image = $imageMatch.Groups['image'].Value.Trim()
if ($image -notmatch '^.+@sha256:[0-9a-fA-F]{64}$') {
    throw 'DATAHUB_API_IMAGE must be an immutable registry reference ending in @sha256:<64 hex chars>.'
}

$composeArgs = @('--env-file', $ComposeEnvFile, '-f', $ComposeFile)
& docker compose @composeArgs config --quiet
if ($LASTEXITCODE -ne 0) { throw 'docker compose config validation failed.' }

& docker compose @composeArgs pull api
if ($LASTEXITCODE -ne 0) { throw 'The pinned API image could not be pulled.' }

$expectedDigest = ($image -split '@', 2)[1]
$repoDigests = @(docker image inspect $image --format '{{range .RepoDigests}}{{println .}}{{end}}')
if ($LASTEXITCODE -ne 0 -or -not ($repoDigests -match [regex]::Escape($expectedDigest))) {
    throw "Pulled API image does not match the requested digest: $expectedDigest"
}

& docker compose @composeArgs up -d --no-build
if ($LASTEXITCODE -ne 0) { throw 'The pinned DataHub stack failed to start.' }

Write-Host "DataHub stack started from pinned image digest $expectedDigest." -ForegroundColor Green

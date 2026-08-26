<#
.SYNOPSIS
    Installs the official Render CLI into tools/render-cli/bin/ for this repository.
.DESCRIPTION
    Downloads a PINNED release of https://github.com/render-oss/cli (the official Render CLI,
    Apache-2.0, homepage https://render.com/docs/cli), verifies its SHA-256 against the
    checksum manifest published with the same release, and extracts render.exe next to this
    script.

    Repo-local on purpose, not a machine-wide install:
      * There is no winget / choco / scoop package for the Render CLI (checked 2026-08-26), so
        the alternative is an unpinned manual download that nobody can reproduce.
      * The version is pinned here, so every machine and every agent gets the same CLI, and a
        bump is a reviewable diff instead of "whatever was latest that day".
      * bin/ is git-ignored. An 8.7 MB platform binary does not belong in a PUBLIC repo.

    Comments and messages are deliberately ASCII-only: a .ps1 with non-ASCII characters and no
    UTF-8 BOM fails to PARSE on any Windows machine whose ANSI code page is not UTF-8. See
    section 5 of eng/harness/check-project-structure.ps1.

    This script never authenticates. `render login` is an interactive browser flow and belongs
    to the Owner; CI uses the RENDER_API_KEY environment variable. See README.md next to this
    file.
.PARAMETER Version
    Release to install, without the leading "v". Defaults to the pinned version.
.PARAMETER Force
    Reinstall even when the requested version is already present.
.EXAMPLE
    powershell -ExecutionPolicy Bypass -File .\tools\render-cli\install-render-cli.ps1
.EXAMPLE
    powershell -ExecutionPolicy Bypass -File .\tools\render-cli\install-render-cli.ps1 -Version 2.24.0 -Force
#>
[CmdletBinding()]
param(
    [string]$Version = '2.24.0',
    [switch]$Force
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

$InstallDir = Join-Path $PSScriptRoot 'bin'
$ExePath = Join-Path $InstallDir 'render.exe'
$BaseUrl = "https://github.com/render-oss/cli/releases/download/v$Version"

Write-Host '========================================' -ForegroundColor Cyan
Write-Host '  Render CLI installer (repo-local)' -ForegroundColor Cyan
Write-Host '========================================' -ForegroundColor Cyan
Write-Host ''

# TLS 1.2 is not the default on stock Windows PowerShell 5.1; without this the download fails
# with a bare "connection was closed" that reads like a network outage.
try {
    [Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12
} catch {
    Write-Host '  WARNING: could not raise the TLS version; download may fail.' -ForegroundColor Yellow
}

switch ([System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture) {
    'X64'   { $arch = 'amd64' }
    'Arm64' { $arch = 'arm64' }
    'X86'   { $arch = '386' }
    default { throw "Unsupported OS architecture: $([System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture)" }
}
Write-Host "  Target      : windows_$arch"
Write-Host "  Version     : v$Version"
Write-Host "  Install dir : $InstallDir"
Write-Host ''

if ((Test-Path $ExePath) -and -not $Force) {
    $current = (& $ExePath --version 2>&1 | Out-String).Trim()
    if ($current -match [regex]::Escape($Version)) {
        Write-Host "  Already installed: $current" -ForegroundColor Green
        Write-Host '  Use -Force to reinstall.' -ForegroundColor Green
        exit 0
    }
    Write-Host "  Found a different version ($current); replacing it." -ForegroundColor Yellow
}

$archiveName = "cli_${Version}_windows_${arch}.zip"
$sumsName = "cli_${Version}_SHA256SUMS"
$workDir = Join-Path ([System.IO.Path]::GetTempPath()) "render-cli-$Version-$PID"
New-Item -ItemType Directory -Path $workDir -Force | Out-Null

try {
    $archivePath = Join-Path $workDir $archiveName
    $sumsPath = Join-Path $workDir $sumsName

    Write-Host "  Downloading $archiveName ..." -ForegroundColor Yellow
    Invoke-WebRequest -Uri "$BaseUrl/$archiveName" -OutFile $archivePath -UseBasicParsing
    Write-Host "  Downloading $sumsName ..." -ForegroundColor Yellow
    Invoke-WebRequest -Uri "$BaseUrl/$sumsName" -OutFile $sumsPath -UseBasicParsing

    # Integrity check against the manifest published with the same release. This catches a
    # truncated or tampered download; it is NOT a signature check. The release also ships
    # SHA256SUMS.sig, which would need cosign and Render's public key - neither is pinned here,
    # so do not claim more than an integrity check.
    $expected = $null
    foreach ($line in (Get-Content $sumsPath)) {
        $parts = ($line.Trim() -split '\s+', 2)
        if ($parts.Count -eq 2 -and $parts[1].TrimStart('*') -eq $archiveName) {
            $expected = $parts[0].ToLowerInvariant()
            break
        }
    }
    if (-not $expected) { throw "No SHA-256 entry for $archiveName in $sumsName." }

    $actual = (Get-FileHash -Path $archivePath -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($actual -ne $expected) {
        throw "SHA-256 mismatch for ${archiveName}: expected $expected, got $actual. Nothing was installed."
    }
    Write-Host "  SHA-256 OK  : $actual" -ForegroundColor Green

    $extractDir = Join-Path $workDir 'extracted'
    Expand-Archive -Path $archivePath -DestinationPath $extractDir -Force

    # The archive layout is goreleaser's, and the binary inside is not necessarily named
    # render.exe - upstream's own install script renames it. Pick the single executable rather
    # than hard-coding a name that a future release may change.
    $binary = Get-ChildItem -Path $extractDir -Filter '*.exe' -Recurse -File | Select-Object -First 1
    if (-not $binary) { throw "No .exe found inside $archiveName." }

    New-Item -ItemType Directory -Path $InstallDir -Force | Out-Null
    Copy-Item -LiteralPath $binary.FullName -Destination $ExePath -Force

    $installed = (& $ExePath --version 2>&1 | Out-String).Trim()
    Write-Host ''
    Write-Host "  Installed   : $installed" -ForegroundColor Green
    Write-Host "  Path        : $ExePath" -ForegroundColor Green
    Write-Host ''
    Write-Host '  Add it to PATH for this shell only:' -ForegroundColor Cyan
    Write-Host ('    $env:PATH = "' + $InstallDir + ';$env:PATH"') -ForegroundColor Cyan
    Write-Host ''
    Write-Host '  Next step is the Owner''s, not an agent''s: run `render login` (opens a browser).' -ForegroundColor Cyan
    Write-Host '  See tools/render-cli/README.md before touching the license server service.' -ForegroundColor Cyan
} finally {
    if (Test-Path $workDir) {
        Remove-Item -LiteralPath $workDir -Recurse -Force -ErrorAction SilentlyContinue
    }
}

exit 0

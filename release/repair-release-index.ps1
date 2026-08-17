<#
.SYNOPSIS
    One-off repair: publish the missing Velopack release index to GitHub Releases.

.DESCRIPTION
    Velopack 1.x clients read the release feed from a GitHub Release asset named
    exactly "releases.{channel}.json" (Velopack.CoreUtil.GetVeloReleaseIndexName).
    build-release.ps1 used to upload only the legacy Squirrel "RELEASES" text file,
    which the Velopack 1.x UpdateManager never reads. Result: GetReleaseFeed() came
    back empty, CheckForUpdatesAsync() returned null, and the app reported
    "Ban dang dung phien ban moi nhat." for both Stable and Beta.

    build-release.ps1 is fixed for future builds. This script repairs the releases
    that were already published, without rebuilding anything:

      1. Reads release\output\{channel}\releases.{channel}.json
      2. Rewrites the FileName field to the asset name actually uploaded
         (AutoJMS-{version}-{channel}-full.nupkg -> AutoJMS-{version}-full.nupkg)
      3. Verifies SHA1 + Size against the local .nupkg so the index cannot point
         at a package that does not match
      4. Uploads the index to the release tag with gh --clobber
      5. Re-reads the release assets to confirm

.EXAMPLE
    cd D:\v1.2605.2(new-test)
    .\release\repair-release-index.ps1

.EXAMPLE
    .\release\repair-release-index.ps1 -Channel beta -WhatIf
#>
[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [ValidateSet("stable", "beta", "both")]
    [string]$Channel = "both",

    [string]$Repo = "Datt03-sss/AutoJMS-Update",

    [string]$OutputRoot = (Join-Path $PSScriptRoot "output")
)

$ErrorActionPreference = "Stop"

function Write-Step { param([string]$Text, [string]$Color = "Cyan") Write-Host $Text -ForegroundColor $Color }

if (-not (Get-Command gh -ErrorAction SilentlyContinue)) {
    throw "GitHub CLI 'gh' not found. Install it (winget install --id GitHub.cli) then run: gh auth login"
}
& gh auth status 2>&1 | Out-Null
if ($LASTEXITCODE -ne 0) { throw "GitHub CLI not authenticated. Run: gh auth login" }

$channels = if ($Channel -eq "both") { @("stable", "beta") } else { @($Channel) }
$utf8NoBom = New-Object System.Text.UTF8Encoding($false)
$failed = @()

foreach ($ch in $channels) {
    Write-Host ""
    Write-Step "=== channel: $ch ===" "Yellow"

    $dir = Join-Path $OutputRoot $ch
    $indexPath = Join-Path $dir "releases.$ch.json"

    if (-not (Test-Path $indexPath)) {
        Write-Host "  SKIP: $indexPath not found (rebuild this channel to regenerate it)." -ForegroundColor DarkYellow
        $failed += $ch
        continue
    }

    $index = Get-Content -LiteralPath $indexPath -Raw | ConvertFrom-Json
    $full = $index.Assets | Where-Object { $_.Type -eq "Full" } | Select-Object -First 1
    if (-not $full) { throw "No Full package entry in $indexPath." }

    $version = $full.Version
    $tag = "v$version-Release"
    $targetNupkgName = "AutoJMS-$version-full.nupkg"
    $nupkgPath = Join-Path $dir $targetNupkgName

    Write-Host "  version       : $version"
    Write-Host "  tag           : $tag"
    Write-Host "  index FileName: $($full.FileName)"
    Write-Host "  upload asset  : $targetNupkgName"

    if (-not (Test-Path $nupkgPath)) {
        throw "Local package '$nupkgPath' not found — cannot verify the index against it."
    }

    # The index must describe the package byte-for-byte, otherwise Velopack
    # detects the update and then fails the integrity check after download.
    $localSha1 = (Get-FileHash -LiteralPath $nupkgPath -Algorithm SHA1).Hash.ToUpperInvariant()
    $localSize = (Get-Item -LiteralPath $nupkgPath).Length
    if ($localSha1 -ne $full.SHA1.ToUpperInvariant()) {
        throw "SHA1 mismatch for $targetNupkgName. index=$($full.SHA1) local=$localSha1"
    }
    if ([int64]$localSize -ne [int64]$full.Size) {
        throw "Size mismatch for $targetNupkgName. index=$($full.Size) local=$localSize"
    }
    Write-Host "  SHA1/Size     : verified against local package" -ForegroundColor Green

    # Point the index at the asset name that is actually on the release.
    if ($full.FileName -ne $targetNupkgName) {
        $raw = [System.IO.File]::ReadAllText($indexPath)
        $raw = $raw.Replace($full.FileName, $targetNupkgName)
        [System.IO.File]::WriteAllText($indexPath, $raw, $utf8NoBom)
        Write-Host "  rewrote FileName -> $targetNupkgName" -ForegroundColor Green
    }
    else {
        Write-Host "  FileName already correct." -ForegroundColor Green
    }

    # Confirm the release actually carries that package asset before publishing
    # an index that promises it.
    $assetsJson = & gh release view $tag --repo $Repo --json assets 2>$null
    if ($LASTEXITCODE -ne 0) { throw "Release $tag not found in $Repo." }
    $assetNames = ($assetsJson | ConvertFrom-Json).assets.name
    if ($assetNames -notcontains $targetNupkgName) {
        throw "Release $tag has no asset named '$targetNupkgName'. Assets present: $($assetNames -join ', ')"
    }
    Write-Host "  release has   : $targetNupkgName" -ForegroundColor Green

    if ($PSCmdlet.ShouldProcess("$Repo $tag", "upload releases.$ch.json")) {
        & gh release upload $tag $indexPath --repo $Repo --clobber
        if ($LASTEXITCODE -ne 0) { throw "gh release upload failed for $tag." }

        $after = (& gh release view $tag --repo $Repo --json assets | ConvertFrom-Json).assets.name
        if ($after -contains "releases.$ch.json") {
            Write-Step "  OK: releases.$ch.json is now on $tag" "Green"
            Write-Host "  assets: $($after -join ', ')" -ForegroundColor Gray
        }
        else {
            $failed += $ch
            Write-Host "  FAILED: releases.$ch.json still missing from $tag" -ForegroundColor Red
        }
    }
}

Write-Host ""
if ($failed.Count -gt 0) {
    Write-Host "Channels not repaired: $($failed -join ', ')" -ForegroundColor Red
    exit 1
}
Write-Step "Done. Open AutoJMS -> check for updates; Stable and Beta should both offer 1.26.9 now." "Green"

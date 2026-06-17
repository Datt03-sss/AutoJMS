<#
.SYNOPSIS
    AutoJMS Velopack Release Builder (GitHub Releases + update manifests)
.DESCRIPTION
    Builds a Velopack update package and publishes it to GitHub Releases.

    The split is:
      • GitHub Releases → Velopack binaries (RELEASES, *.nupkg, Setup.exe)
      • update.xml      → small UI/channel manifest read by About dialog
      • Supabase        → version-latest.json + hash-manifest.json control manifests

    The app reads update.xml from:
      https://raw.githubusercontent.com/Datt03-sss/AutoJMS-Update/main/update.xml

    For updates, the app uses Velopack GithubSource against the GitHub repo.
    setupUrl in update.xml is only a fallback/manual installer link; it is not
    used as a Velopack feed.

    Steps:
      1. dotnet publish (Release, win-x64, self-contained)
      2. Protect AutoJMS.dll with .NET Reactor (optional)
      3. Verify self-contained publish
      4. vpk pack → produces .nupkg + Setup.exe + RELEASES
      5. Upload: binaries → GitHub Release (gh), update.xml → GitHub repo,
         Supabase manifests → Storage bucket manifest/

    GitHub release tag format:
      stable: v{VelopackVersion}-Release   (prerelease=false)
      beta  : v{VelopackVersion}-Release   (prerelease=true)

.PARAMETER Version
    Velopack SemVer version.
    Stable: x.y.z, for example "1.26.6".
    Beta:   x.y.z-beta.n, for example "1.26.6-beta.1".
    Do not pass four-part versions such as "1.26.6.1"; use internalBuild only.
.PARAMETER Channel
    Release channel: "stable" or "beta" (default: stable)
.PARAMETER DisplayVersion
    User-facing version. Can be four-part, for example "1.26.6.1".
.PARAMETER InternalBuild
    Internal build metadata, for example "20260607.1".
.PARAMETER ReleaseNotes
    Release notes written into update.xml and GitHub Release notes.
.PARAMETER ReleaseNotesFile
    Optional text file containing release notes.
.PARAMETER SkipReactor
    Skip .NET Reactor protection
.PARAMETER SkipPublish
    Skip dotnet publish (use existing artifacts\publish\win-x64 folder)
.PARAMETER Upload
    Publish binaries to GitHub Release + update.xml to the GitHub repo.
.PARAMETER GitHubRepo
    GitHub repo hosting the binaries (default: Datt03-sss/AutoJMS-Update)
.PARAMETER SupabaseProjectRef
    Supabase project ref hosting small manifests (default: bnsnnrlwfzxemmizknwy)
.PARAMETER SupabaseBucket
    Supabase Storage bucket for AutoJMS manifests (default: autojms-modules)
.EXAMPLE
    .\build-release.ps1 -Version "1.26.6" -Channel stable
    .\build-release.ps1 -Version "1.26.6-beta.1" -Channel beta
    .\build-release.ps1 -Version "1.26.6" -DisplayVersion "1.26.6.1" -Upload
    .\build-release.ps1 -Version "1.26.6-beta.1" -Channel beta -DisplayVersion "1.26.6.2" -SkipReactor -SkipPublish -Upload
#>

param(
    [Parameter(Mandatory = $false)]
    [string]$Version,

    [ValidateSet("stable", "beta")]
    [string]$Channel = "stable",

    [string]$DisplayVersion,

    [string]$InternalBuild,

    [string]$ReleaseNotes,

    [string]$ReleaseNotesFile,

    [switch]$SkipReactor,
    [switch]$SkipPublish,
    [switch]$Upload,

    # GitHub repo that hosts the large Velopack binaries (RELEASES/.nupkg/Setup.exe).
    [string]$GitHubRepo = "Datt03-sss/AutoJMS-Update",

    [string]$UpdateXmlPath = "update.xml",

    [string]$UpdateXmlBranch = "main",

    [string]$SupabaseProjectRef = "bnsnnrlwfzxemmizknwy",

    [string]$SupabaseBucket = "autojms-modules",

    [string]$SupabaseWorkdir,

    [switch]$SkipSupabaseManifestUpload
)

$ErrorActionPreference = "Stop"
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$ProjectRoot = Split-Path -Parent $ScriptDir
$Project = Join-Path $ProjectRoot "src\AutoJMS\AutoJMS.csproj"
$PublishDir = Join-Path $ProjectRoot "artifacts\publish\win-x64"
$OutputDir = Join-Path $ScriptDir "output\$Channel"
$InnoBuilder = Join-Path $ProjectRoot "installer\inno\build-installer.ps1"
if ([string]::IsNullOrWhiteSpace($SupabaseWorkdir)) {
    $SupabaseWorkdir = Join-Path $ProjectRoot "backend\supabase"
}

function Write-Log {
    param([string]$Message, [string]$Color = "White")
    Write-Host "[$(Get-Date -Format 'HH:mm:ss')] $Message" -ForegroundColor $Color
}

function Resolve-ReleaseVersion {
    param(
        [string]$Ver,
        [ValidateSet("stable", "beta")]
        [string]$Channel,
        [bool]$Interactive = $false
    )

    if ([string]::IsNullOrWhiteSpace($Ver)) {
        throw "Version is empty. Stable must be x.y.z; beta must be x.y.z-beta.n."
    }

    $clean = $Ver.Trim().TrimStart('v','V')
    $clean = [regex]::Replace($clean, '\.{2,}', '.')
    $clean = $clean.Trim('.')

    $stablePattern = '^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)$'
    $betaPattern = '^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)-beta\.([1-9]\d*)$'
    $fourPartPattern = '^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)\.([0-9]+)$'

    if ($clean -match $fourPartPattern) {
        $suggested = "$($Matches[1]).$($Matches[2]).$($Matches[3])-beta.$([int]$Matches[4])"
        if ($Interactive -and $Channel -eq "beta") {
            $answer = Read-Host "Four-part version '$clean' is not valid Velopack SemVer. Convert to '$suggested'? [y/N]"
            if ($answer -match '^(y|yes)$') {
                $clean = $suggested
            } else {
                throw "Version '$Ver' rejected. Use beta SemVer '$suggested' or stable SemVer x.y.z."
            }
        } else {
            throw "Version '$Ver' rejected. Do not convert 4-part versions to a different Velopack patch. Stable must be x.y.z; beta should be '$suggested'."
        }
    }

    if ($Channel -eq "stable") {
        if ($clean -notmatch $stablePattern) {
            throw "Stable version '$Ver' invalid. Stable VelopackVersion must be x.y.z, for example 1.26.6."
        }

        return [pscustomobject]@{
            VelopackVersion = $clean
            DisplayVersion  = $clean
            InternalBuild   = (Get-Date).ToString("yyyyMMdd.1")
            VelopackChannel = "stable"
            IsPrerelease    = $false
        }
    }

    if ($clean -notmatch $betaPattern) {
        throw "Beta version '$Ver' invalid. Beta VelopackVersion must be x.y.z-beta.n, for example 1.26.6-beta.1."
    }

    return [pscustomobject]@{
        VelopackVersion = $clean
        DisplayVersion  = "$($Matches[1]).$($Matches[2]).$($Matches[3]) beta $($Matches[4])"
        InternalBuild   = "$(Get-Date -Format 'yyyyMMdd').beta.$($Matches[4])"
        VelopackChannel = "beta"
        IsPrerelease    = $true
    }
}

function Get-AssemblyFileVersion {
    param([string]$VelopackVersion)

    $clean = $VelopackVersion.Trim().TrimStart('v','V')
    $main = $clean
    $revision = 0
    $dash = $clean.IndexOf('-')
    if ($dash -ge 0) {
        $main = $clean.Substring(0, $dash)
        $pre = $clean.Substring($dash + 1)
        $parts = $pre.Split('.')
        if ($parts.Length -gt 1) {
            $parsedRevision = 0
            if ([int]::TryParse($parts[$parts.Length - 1], [ref]$parsedRevision)) {
                $revision = $parsedRevision
            }
        }
    }

    $mainParts = $main.Split('.')
    if ($mainParts.Length -ne 3) {
        throw "Cannot derive AssemblyVersion from VelopackVersion '$VelopackVersion'."
    }

    return "$($mainParts[0]).$($mainParts[1]).$($mainParts[2]).$revision"
}

function Assert-SelfContainedPublish {
    param([string]$Dir)
    $required = @("AutoJMS.exe","AutoJMS.dll","AutoJMS.runtimeconfig.json","AutoJMS.deps.json",
                  "coreclr.dll","hostfxr.dll","hostpolicy.dll","System.Private.CoreLib.dll")
    foreach ($f in $required) {
        if (!(Test-Path (Join-Path $Dir $f))) {
            throw "NOT self-contained. Missing: $f"
        }
    }
    $rc = Get-Content (Join-Path $Dir "AutoJMS.runtimeconfig.json") -Raw
    if ($rc -match '"framework"\s*:') {
        throw "runtimeconfig.json contains framework reference — this is framework-dependent!"
    }
    Write-Log "  Self-contained verified." "Green"
}

function Stop-AutoJMSProcesses {
    # Kill processes that commonly lock files inside publish output:
    #   - AutoJMS.exe (and its Velopack child Update.exe)
    #   - dotnet build server (MSBuild + VBCSCompiler) which keeps satellite
    #     resource DLLs (e.g. Microsoft.VisualBasic.Forms.resources.dll) open
    #     between builds. This is the #1 cause of "Access denied" on republish.
    $names = @('AutoJMS', 'Update', 'MSBuild', 'VBCSCompiler')
    foreach ($n in $names) {
        $procs = Get-Process -Name $n -ErrorAction SilentlyContinue
        if ($procs) {
            Write-Log "  Stopping $n process(es): $($procs.Count)" "Yellow"
            $procs | ForEach-Object {
                try { $_ | Stop-Process -Force -ErrorAction Stop } catch { }
            }
        }
    }

    # Also tell the .NET build server itself to release every file handle.
    # This is the official, non-destructive way and is required when the
    # project uses C#/F#/VB compilers that keep references mmap'd.
    try {
        $null = & dotnet build-server shutdown 2>&1
    } catch { }

    Start-Sleep -Milliseconds 500
}

function Remove-PathSafe {
    <#
    Forcefully delete a directory even when it contains read-only files
    (e.g. resource DLLs from a previous publish) or transiently-locked
    files (AV scan, Search indexer, build server). Strategy:
      1. Strip ReadOnly/Hidden/System recursively.
      2. Retry Remove-Item with backoff.
      3. As a last resort, rename the directory out of the way so the
         next build can proceed even if a stray handle outlives us.
    #>
    param([string]$Path, [int]$MaxAttempts = 8)
    if (-not (Test-Path $Path)) { return }

    for ($i = 1; $i -le $MaxAttempts; $i++) {
        try {
            Get-ChildItem -LiteralPath $Path -Recurse -Force -ErrorAction SilentlyContinue |
                ForEach-Object {
                    try { $_.Attributes = 'Normal' } catch { }
                }
            Remove-Item -LiteralPath $Path -Recurse -Force -ErrorAction Stop
            return
        }
        catch {
            $msg = $_.Exception.Message
            if ($i -eq $MaxAttempts) {
                # Fallback: rename the locked folder so a fresh publish can
                # use the original path. The OS will free handles eventually
                # and the renamed copy can be cleaned up next run / manually.
                $stash = "$Path.locked.$(Get-Date -Format 'yyyyMMddHHmmss')"
                try {
                    Rename-Item -LiteralPath $Path -NewName (Split-Path $stash -Leaf) -Force -ErrorAction Stop
                    Write-Log "  Could not delete '$Path' ($msg). Renamed to '$stash' so build can continue." "Yellow"
                    return
                }
                catch {
                    throw "Cannot delete or rename '$Path' after $MaxAttempts attempts.`n  Last delete error : $msg`n  Rename error      : $($_.Exception.Message)`n  Tip: close Visual Studio/VS Code on this folder, exclude it from Defender, then retry."
                }
            }
            Write-Log "  Delete retry $i/$MaxAttempts on '$Path' ($msg)" "Yellow"
            Start-Sleep -Milliseconds (500 * $i)
        }
    }
}

# ─── GitHub Releases (binary hosting) ─────────────────────

function Assert-GitHubCli {
    <#
    Ensure the GitHub CLI is installed and authenticated. Throws with a clear
    message otherwise. GitHub Releases hosts the large Velopack binaries.
    #>
    $gh = Get-Command gh -ErrorAction SilentlyContinue
    if (-not $gh) {
        throw "GitHub CLI 'gh' not found. Install it: winget install --id GitHub.cli  (then run: gh auth login)."
    }
    Write-Log "  gh found: $($gh.Source)" "Gray"

    & gh auth status 2>&1 | Out-Null
    if ($LASTEXITCODE -ne 0) {
        throw "GitHub CLI not authenticated. Run:  gh auth login  then retry."
    }
    Write-Log "  gh auth OK." "Gray"
}

function Get-ReleaseTag {
    param(
        [string]$VelopackVersion,
        [ValidateSet("stable", "beta")]
        [string]$Channel
    )

    return "v$VelopackVersion-Release"
}

function Rename-VelopackAssets {
    param(
        [string]$Dir,
        [string]$VelopackVersion
    )

    $releases = Get-ChildItem $Dir -File | Where-Object { $_.Name -like "RELEASES*" } | Select-Object -First 1
    $nupkg = Get-ChildItem $Dir -File -Filter "*.nupkg" | Select-Object -First 1
    $setup = Get-ChildItem $Dir -File -Filter "*Setup.exe" | Select-Object -First 1

    if (-not $releases) { throw "Velopack RELEASES file not found in $Dir." }
    if (-not $nupkg) { throw "Velopack .nupkg file not found in $Dir." }
    if (-not $setup) { throw "Velopack Setup.exe file not found in $Dir." }

    $originalNupkgName = $nupkg.Name
    $targetReleases = Join-Path $Dir "RELEASES"
    $targetNupkg = Join-Path $Dir "AutoJMS-$VelopackVersion-full.nupkg"
    $targetSetup = Join-Path $Dir "AutoJMS-win-Setup.exe"

    if ($releases.FullName -ne $targetReleases) {
        if (Test-Path $targetReleases) { Remove-Item -LiteralPath $targetReleases -Force }
        Rename-Item -LiteralPath $releases.FullName -NewName "RELEASES" -Force
    }

    if ($nupkg.FullName -ne $targetNupkg) {
        if (Test-Path $targetNupkg) { Remove-Item -LiteralPath $targetNupkg -Force }
        Rename-Item -LiteralPath $nupkg.FullName -NewName "AutoJMS-$VelopackVersion-full.nupkg" -Force
    }

    if ($setup.FullName -ne $targetSetup) {
        if (Test-Path $targetSetup) { Remove-Item -LiteralPath $targetSetup -Force }
        Rename-Item -LiteralPath $setup.FullName -NewName "AutoJMS-win-Setup.exe" -Force
    }

    $targetNupkgName = Split-Path $targetNupkg -Leaf
    $releasesContent = [System.IO.File]::ReadAllText($targetReleases)
    if ($releasesContent.Contains($originalNupkgName)) {
        $releasesContent = $releasesContent.Replace($originalNupkgName, $targetNupkgName)
        [System.IO.File]::WriteAllText($targetReleases, $releasesContent, [System.Text.Encoding]::ASCII)
    }

    return @($targetReleases, $targetNupkg, $targetSetup)
}

function Publish-GitHubRelease {
    <#
    Upload the Velopack assets (RELEASES / *.nupkg / Setup.exe) to a GitHub
    Release. Creates the release if the tag does not exist, otherwise uploads
    assets with --clobber. Beta channel marks the release as prerelease.
    #>
    param(
        [string]$Repo,
        [string]$Tag,
        [string]$Title,
        [bool]$IsPrerelease,
        [string[]]$Assets
    )

    Write-Log "GitHub repo:   $Repo" "Green"
    Write-Log "Release tag:   $Tag (prerelease=$IsPrerelease)" "Green"
    Write-Log "Asset upload list:" "Green"
    foreach ($a in $Assets) {
        $name = Split-Path $a -Leaf
        $sizeMB = [math]::Round((Get-Item $a).Length / 1MB, 1)
        Write-Log "  - $name ($sizeMB MB)" "Gray"
    }

    # Does the release/tag already exist? gh writes an error for missing
    # releases; keep that check non-fatal because the next branch creates it.
    $oldErrorActionPreference = $ErrorActionPreference
    $ErrorActionPreference = "Continue"
    try {
        & gh release view $Tag --repo $Repo 2>$null | Out-Null
        $exists = ($LASTEXITCODE -eq 0)
    }
    finally {
        $ErrorActionPreference = $oldErrorActionPreference
    }

    if (-not $exists) {
        Write-Log "  Creating GitHub release $Tag..." "Cyan"
        $ghArgs = @("release", "create", $Tag,
                    "--repo", $Repo,
                    "--title", $Title,
                    "--notes", $script:EffectiveReleaseNotes)
        if ($IsPrerelease) { $ghArgs += "--prerelease" }
        $ghArgs += $Assets
        & gh @ghArgs
        if ($LASTEXITCODE -ne 0) { throw "gh release create failed for $Tag." }
    }
    else {
        Write-Log "  Release $Tag exists — uploading assets with --clobber..." "Cyan"
        $ghArgs = @("release", "upload", $Tag) + $Assets + @("--repo", $Repo, "--clobber")
        & gh @ghArgs
        if ($LASTEXITCODE -ne 0) { throw "gh release upload failed for $Tag." }

        # Make sure the prerelease flag matches the channel.
        if ($IsPrerelease) {
            & gh release edit $Tag --repo $Repo --prerelease 2>&1 | Out-Null
        } else {
            & gh release edit $Tag --repo $Repo --prerelease=false 2>&1 | Out-Null
        }
    }

    Write-Log "  GitHub release publish complete." "Green"
}

function Build-InnoInstaller {
    param(
        [string]$VelopackVersion,
        [string]$DisplayVersion,
        [string]$Channel,
        [string]$VelopackSetupExe,
        [string]$OutputDir
    )

    if (-not (Test-Path $InnoBuilder)) {
        throw "Inno builder not found: $InnoBuilder"
    }
    if (-not (Test-Path $VelopackSetupExe)) {
        throw "Velopack setup for Inno not found: $VelopackSetupExe"
    }

    Write-Log "Step 4b: Building Inno first-install wizard..." "Cyan"
    $args = @(
        "-NoProfile",
        "-ExecutionPolicy", "Bypass",
        "-File", $InnoBuilder,
        "-Version", $DisplayVersion,
        "-InstallerVersion", $VelopackVersion,
        "-Channel", $Channel,
        "-VelopackSetupExe", $VelopackSetupExe,
        "-OutputDir", $OutputDir,
        "-NoPause"
    )

    & powershell.exe @args 2>&1 | ForEach-Object { Write-Host $_ }
    if ($LASTEXITCODE -ne 0) {
        throw "Inno installer build failed."
    }

    $installerPath = Join-Path $OutputDir "AutoJMS-Installer-$VelopackVersion.exe"
    if (-not (Test-Path $installerPath)) {
        throw "Inno installer output not found: $installerPath"
    }

    Write-Log "  Inno installer: $(Split-Path $installerPath -Leaf)" "Green"
    return $installerPath
}

# ─── update.xml (GitHub raw UI manifest) ───────────────────

function ConvertTo-XmlEscaped {
    param([string]$Value)
    if ($null -eq $Value) { $Value = "" }
    return [System.Security.SecurityElement]::Escape($Value)
}

function Get-GitHubFileContent {
    param(
        [string]$Repo,
        [string]$Path,
        [string]$Branch
    )

    $oldErrorActionPreference = $ErrorActionPreference
    $ErrorActionPreference = "Continue"
    try {
        $json = & gh api "repos/$Repo/contents/$Path" --method GET -f ref=$Branch 2>$null
        if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($json)) {
            return $null
        }
    }
    finally {
        $ErrorActionPreference = $oldErrorActionPreference
    }

    return $json | ConvertFrom-Json
}

function Read-ExistingUpdateXml {
    param(
        [string]$Repo,
        [string]$Path,
        [string]$Branch
    )

    $content = Get-GitHubFileContent -Repo $Repo -Path $Path -Branch $Branch
    if (-not $content) { return $null }
    $raw = ($content.content -replace '\s', '')
    $bytes = [Convert]::FromBase64String($raw)
    return [pscustomobject]@{
        Sha = $content.sha
        Xml = [Text.Encoding]::UTF8.GetString($bytes)
    }
}

function New-UpdateXml {
    param(
        [string]$Channel,
        [string]$VelopackVersion,
        [string]$DisplayVersion,
        [string]$InternalBuild,
        [string]$VelopackChannel,
        [string]$Repo,
        [string]$Tag,
        [bool]$IsPrerelease,
        [string]$SetupUrl,
        [string]$VelopackSetupUrl,
        [string]$ReleaseNotes,
        [string]$ExistingXml
    )

    $repoUrl = "https://github.com/$Repo"
    $updatedAt = (Get-Date).ToString("yyyy-MM-ddTHH:mm:sszzz")
    $notes = ConvertTo-XmlEscaped $ReleaseNotes

    $channels = @{}
    if (-not [string]::IsNullOrWhiteSpace($ExistingXml)) {
        try {
            $xmlText = $ExistingXml.TrimStart([char]0xFEFF)
            $existingDoc = New-Object System.Xml.XmlDocument
            $existingDoc.PreserveWhitespace = $true
            $existingDoc.LoadXml($xmlText)
            foreach ($node in $existingDoc.AutoJMSUpdateManifest.channels.channel) {
                $name = [string]$node.name
                if ([string]::IsNullOrWhiteSpace($name)) { $name = [string]$node.GetAttribute("name") }
                if (-not [string]::IsNullOrWhiteSpace($name)) {
                    $channels[$name.ToLowerInvariant()] = $node.OuterXml
                }
            }
        } catch {
            Write-Log "  Existing update.xml parse failed; generating fresh XML. $($_.Exception.Message)" "Yellow"
        }
    }

    $channelXml = @"
    <channel name="$(ConvertTo-XmlEscaped $Channel)" enabled="true" prerelease="$($IsPrerelease.ToString().ToLowerInvariant())">
      <velopackVersion>$(ConvertTo-XmlEscaped $VelopackVersion)</velopackVersion>
      <displayVersion>$(ConvertTo-XmlEscaped $DisplayVersion)</displayVersion>
      <internalBuild>$(ConvertTo-XmlEscaped $InternalBuild)</internalBuild>
      <releaseTag>$(ConvertTo-XmlEscaped $Tag)</releaseTag>
      <setupUrl>$(ConvertTo-XmlEscaped $SetupUrl)</setupUrl>
      <velopackSetupUrl>$(ConvertTo-XmlEscaped $VelopackSetupUrl)</velopackSetupUrl>
      <releaseNotesUrl></releaseNotesUrl>
      <mandatory>false</mandatory>
      <manualOnly>true</manualOnly>
      <releaseNotes>
$notes
      </releaseNotes>
    </channel>
"@

    $channels[$Channel.ToLowerInvariant()] = $channelXml
    if (-not $channels.ContainsKey("stable")) {
        $channels["stable"] = @"
    <channel name="stable" enabled="false" prerelease="false">
      <velopackVersion></velopackVersion>
      <displayVersion></displayVersion>
      <internalBuild></internalBuild>
      <releaseTag></releaseTag>
      <setupUrl></setupUrl>
      <velopackSetupUrl></velopackSetupUrl>
      <releaseNotesUrl></releaseNotesUrl>
      <mandatory>false</mandatory>
      <manualOnly>true</manualOnly>
      <releaseNotes></releaseNotes>
    </channel>
"@
    }
    if (-not $channels.ContainsKey("beta")) {
        $channels["beta"] = @"
    <channel name="beta" enabled="false" prerelease="true">
      <velopackVersion></velopackVersion>
      <displayVersion></displayVersion>
      <internalBuild></internalBuild>
      <releaseTag></releaseTag>
      <setupUrl></setupUrl>
      <velopackSetupUrl></velopackSetupUrl>
      <releaseNotesUrl></releaseNotesUrl>
      <mandatory>false</mandatory>
      <manualOnly>true</manualOnly>
      <releaseNotes></releaseNotes>
    </channel>
"@
    }

    $orderedChannels = @($channels["stable"], $channels["beta"]) + ($channels.Keys | Where-Object { @("stable","beta") -notcontains $_ } | Sort-Object | ForEach-Object { $channels[$_] })
    $channelsText = ($orderedChannels -join "`r`n")

    return @"
<?xml version="1.0" encoding="utf-8"?>
<AutoJMSUpdateManifest schemaVersion="2">
  <updatedAt>$updatedAt</updatedAt>
  <githubRepo>$repoUrl</githubRepo>
  <channels>
$channelsText
  </channels>
</AutoJMSUpdateManifest>
"@
}

function Publish-UpdateXml {
    param(
        [string]$LocalPath,
        [string]$Repo,
        [string]$Path,
        [string]$Branch,
        [string]$Message
    )

    $existing = Get-GitHubFileContent -Repo $Repo -Path $Path -Branch $Branch
    $contentBytes = [IO.File]::ReadAllBytes($LocalPath)
    $contentBase64 = [Convert]::ToBase64String($contentBytes)

    $body = @{
        message = $Message
        content = $contentBase64
        branch = $Branch
    }

    if ($existing -and $existing.sha) {
        $body.sha = $existing.sha
    }

    $json = $body | ConvertTo-Json -Depth 5
    $tmpBody = Join-Path ([IO.Path]::GetTempPath()) "autojms-updatexml-$([guid]::NewGuid()).json"
    $utf8NoBom = New-Object System.Text.UTF8Encoding($false)
    [IO.File]::WriteAllText($tmpBody, $json, $utf8NoBom)
    try {
        & gh api "repos/$Repo/contents/$Path" --method PUT --header "Content-Type: application/json" --input $tmpBody
        if ($LASTEXITCODE -ne 0) { throw "gh api update.xml upload failed." }
    } finally {
        Remove-Item -LiteralPath $tmpBody -Force -ErrorAction SilentlyContinue
    }

    Write-Log "  update.xml published to $Repo/$Path ($Branch)." "Green"
}

# ─── Supabase manifests (small control-plane files only) ───

function Get-SupabasePublicObjectUrl {
    param(
        [string]$ProjectRef,
        [string]$Bucket,
        [string]$ObjectPath
    )

    return "https://$ProjectRef.supabase.co/storage/v1/object/public/$Bucket/$ObjectPath"
}

function Read-PublicJson {
    param([string]$Url)

    try {
        return Invoke-RestMethod -Uri $Url -Method Get -TimeoutSec 20
    }
    catch {
        Write-Log "  Could not read existing manifest $Url ($($_.Exception.Message)); generating from current release metadata." "Yellow"
        return $null
    }
}

function Write-Utf8NoBom {
    param(
        [string]$Path,
        [string]$Content
    )

    $utf8NoBom = New-Object System.Text.UTF8Encoding($false)
    [System.IO.File]::WriteAllText($Path, $Content, $utf8NoBom)
}

function Convert-ObjectPropertiesToOrderedMap {
    param($Object)

    $map = [ordered]@{}
    if ($null -eq $Object) { return $map }

    foreach ($prop in $Object.PSObject.Properties) {
        $map[$prop.Name] = $prop.Value
    }

    return $map
}

function New-VersionLatestManifestJson {
    param(
        [string]$Channel,
        [string]$VelopackVersion,
        [string]$DisplayVersion,
        [string]$InternalBuild,
        [string]$VelopackChannel,
        [string]$Repo,
        [string]$Tag,
        [bool]$IsPrerelease,
        [string]$SetupUrl,
        [string]$VelopackSetupUrl,
        [string]$ReleaseNotes,
        $ExistingManifest
    )

    $channels = [ordered]@{}
    if ($ExistingManifest -and $ExistingManifest.channels) {
        foreach ($prop in $ExistingManifest.channels.PSObject.Properties) {
            $channels[$prop.Name] = $prop.Value
        }
    }

    $channels[$Channel] = [ordered]@{
        version          = $VelopackVersion
        displayVersion   = $DisplayVersion
        internalBuild    = $InternalBuild
        velopackChannel  = $VelopackChannel
        provider         = "github"
        githubRepo       = $Repo
        githubRepoUrl    = "https://github.com/$Repo"
        tag              = $Tag
        prerelease       = $IsPrerelease
        manualOnly       = $true
        mandatory        = $false
        setupUrl         = $SetupUrl
        velopackSetupUrl = $VelopackSetupUrl
        releaseNotes     = $ReleaseNotes
    }

    $manifest = [ordered]@{
        schemaVersion = 1
        updatedAt     = (Get-Date).ToString("yyyy-MM-ddTHH:mm:sszzz")
        channels      = $channels
    }

    return $manifest | ConvertTo-Json -Depth 20
}

function New-HashManifestJson {
    param(
        [string]$VelopackVersion,
        [string]$DisplayVersion,
        [string]$AssemblyFileVersion,
        [string]$DllPath,
        $ExistingManifest
    )

    if (-not (Test-Path $DllPath)) {
        throw "Cannot build hash-manifest.json. AutoJMS.dll not found: $DllPath"
    }

    $versions = [ordered]@{}
    if ($ExistingManifest -and $ExistingManifest.versions) {
        foreach ($prop in $ExistingManifest.versions.PSObject.Properties) {
            $versions[$prop.Name] = $prop.Value
        }
    }

    $dllHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $DllPath).Hash.ToLowerInvariant()
    $entry = [ordered]@{
        displayVersion = $DisplayVersion
        files          = [ordered]@{
            "AutoJMS.dll" = $dllHash
        }
    }

    $keys = New-Object System.Collections.Generic.List[string]
    foreach ($key in @($VelopackVersion, $DisplayVersion, $AssemblyFileVersion)) {
        if (-not [string]::IsNullOrWhiteSpace($key) -and -not $keys.Contains($key)) {
            $keys.Add($key)
        }
    }

    foreach ($key in $keys) {
        $versions[$key] = $entry
    }

    $manifest = [ordered]@{
        schemaVersion = 1
        updatedAt     = (Get-Date).ToString("yyyy-MM-ddTHH:mm:sszzz")
        versions      = $versions
    }

    return $manifest | ConvertTo-Json -Depth 20
}

function Publish-SupabaseManifestObject {
    param(
        [string]$LocalPath,
        [string]$ObjectPath,
        [string]$ProjectRef,
        [string]$Bucket,
        [string]$Workdir
    )

    if (-not (Test-Path $LocalPath)) {
        throw "Supabase manifest file not found: $LocalPath"
    }

    if (-not [string]::IsNullOrWhiteSpace($env:SUPABASE_SERVICE_ROLE_KEY)) {
        $url = "https://$ProjectRef.supabase.co/storage/v1/object/$Bucket/$ObjectPath"
        $headers = @{
            Authorization = "Bearer $env:SUPABASE_SERVICE_ROLE_KEY"
            apikey        = $env:SUPABASE_SERVICE_ROLE_KEY
            "x-upsert"    = "true"
            "cache-control" = "60"
        }

        Invoke-WebRequest -Uri $url `
            -Method Post `
            -Headers $headers `
            -ContentType "application/json" `
            -InFile $LocalPath `
            -TimeoutSec 60 | Out-Null

        Write-Log "  Supabase uploaded: $ObjectPath (service role)" "Green"
        return
    }

    $supabase = Get-Command supabase -ErrorAction SilentlyContinue
    if (-not $supabase) {
        throw "SUPABASE_SERVICE_ROLE_KEY is not set and Supabase CLI was not found. Cannot upload $ObjectPath."
    }

    if (-not (Test-Path $Workdir)) {
        throw "Supabase workdir not found: $Workdir"
    }

    Push-Location $Workdir
    try {
        $sourceForCli = Resolve-Path -LiteralPath $LocalPath -Relative
    }
    finally {
        Pop-Location
    }
    $target = "ss:///$Bucket/$ObjectPath"

    $uploadArgs = @(
        "--workdir", $Workdir,
        "--experimental",
        "--yes",
        "storage", "cp",
        "--linked",
        "--content-type", "application/json",
        "--cache-control", "60",
        $sourceForCli,
        $target
    )

    $oldErrorActionPreference = $ErrorActionPreference
    $ErrorActionPreference = "Continue"
    try {
        $uploadOutput = & supabase @uploadArgs 2>&1
        $uploadExitCode = $LASTEXITCODE
    }
    finally {
        $ErrorActionPreference = $oldErrorActionPreference
    }

    if ($uploadExitCode -ne 0) {
        $uploadText = ($uploadOutput | Out-String)
        if ($uploadText -match "Duplicate") {
            Write-Log "  Supabase object exists; replacing $ObjectPath." "Yellow"

            $oldErrorActionPreference = $ErrorActionPreference
            $ErrorActionPreference = "Continue"
            try {
                $removeOutput = & supabase --workdir $Workdir --experimental --yes storage rm --linked $target 2>&1
                $removeExitCode = $LASTEXITCODE
            }
            finally {
                $ErrorActionPreference = $oldErrorActionPreference
            }

            if ($removeExitCode -ne 0) {
                $removeText = ($removeOutput | Out-String)
                Write-Log $removeText "Yellow"
                throw "Supabase CLI remove failed for existing $ObjectPath."
            }

            $oldErrorActionPreference = $ErrorActionPreference
            $ErrorActionPreference = "Continue"
            try {
                $uploadOutput = & supabase @uploadArgs 2>&1
                $uploadExitCode = $LASTEXITCODE
            }
            finally {
                $ErrorActionPreference = $oldErrorActionPreference
            }
        }

        if ($uploadExitCode -ne 0) {
            $uploadText = ($uploadOutput | Out-String)
            throw "Supabase CLI upload failed for $ObjectPath. $uploadText"
        }
    }

    Write-Log "  Supabase uploaded: $ObjectPath (linked CLI)" "Green"
}

# ─── Main ─────────────────────────────────────────────────

try {
    Write-Host ""
    Write-Host "============================================" -ForegroundColor Cyan
    Write-Host "  AutoJMS Velopack Release Builder" -ForegroundColor Cyan
    Write-Host "============================================" -ForegroundColor Cyan
    Write-Host ""

    # Version
    $interactive = [string]::IsNullOrWhiteSpace($Version)
    if ($interactive) {
        # Re-prompt loop: a single typo shouldn't force a full restart.
        while ($true) {
            $Version = Read-Host "Enter VelopackVersion (stable: 1.26.6, beta: 1.26.6-beta.1)"
            if ([string]::IsNullOrWhiteSpace($Version)) {
                Write-Host "Version required." -ForegroundColor Yellow
                continue
            }
            try {
                $releaseVersion = Resolve-ReleaseVersion -Ver $Version -Channel $Channel -Interactive $true
                break
            } catch {
                Write-Host $_.Exception.Message -ForegroundColor Yellow
            }
        }
    } else {
        $releaseVersion = Resolve-ReleaseVersion -Ver $Version -Channel $Channel -Interactive $false
    }
    $VelopackVersion = $releaseVersion.VelopackVersion
    if ([string]::IsNullOrWhiteSpace($DisplayVersion)) {
        $DisplayVersion = $releaseVersion.DisplayVersion
    }
    if ([string]::IsNullOrWhiteSpace($InternalBuild)) {
        $InternalBuild = $releaseVersion.InternalBuild
    }
    $VelopackChannel = $releaseVersion.VelopackChannel
    if (-not [string]::IsNullOrWhiteSpace($ReleaseNotesFile)) {
        if (-not (Test-Path $ReleaseNotesFile)) { throw "ReleaseNotesFile not found: $ReleaseNotesFile" }
        $ReleaseNotes = Get-Content -Path $ReleaseNotesFile -Raw
    }
    if ([string]::IsNullOrWhiteSpace($ReleaseNotes)) {
        $ReleaseNotes = if ($Channel -eq "beta") { "Bản thử nghiệm nội bộ." } else { "AutoJMS stable release." }
    }
    $script:EffectiveReleaseNotes = $ReleaseNotes.Trim()
    $AssemblyFileVersion = Get-AssemblyFileVersion -VelopackVersion $VelopackVersion

    Write-Log "Velopack version: $VelopackVersion" "Green"
    Write-Log "Display version:  $DisplayVersion" "Green"
    Write-Log "Internal build:   $InternalBuild" "Green"
    Write-Log "Assembly/File:    $AssemblyFileVersion" "Green"
    Write-Log "Channel:          $Channel" "Green"
    Write-Log "Output:           $OutputDir" "Green"
    Write-Host ""

    # Step 1: Publish
    if (-not $SkipPublish) {
        Write-Log "Step 1: Publishing self-contained..." "Cyan"

        # 1a. Kill any running AutoJMS so it doesn't lock DLLs.
        Stop-AutoJMSProcesses

        # 1b. Wipe previous publish + bin/obj publish caches.
        #     The 'Access denied' on Microsoft.VisualBasic.Forms.resources.dll
        #     comes from MSBuild trying to overwrite a read-only file left
        #     over from a previous publish. Remove-PathSafe strips ReadOnly
        #     and retries on transient locks.
        Remove-PathSafe $PublishDir
        Remove-PathSafe (Join-Path $ProjectRoot "src\AutoJMS\bin\Release\net8.0-windows\win-x64\publish")
        Remove-PathSafe (Join-Path $ProjectRoot "src\AutoJMS\obj\Release\net8.0-windows\win-x64")

        dotnet publish $Project `
            -c Release -r win-x64 --self-contained true `
            -p:SelfContained=true -p:UseAppHost=true `
            -p:PublishSingleFile=false -p:PublishTrimmed=false `
            -p:IncludeNativeLibrariesForSelfExtract=true `
            "-p:RunReactor=false" `
            "-p:UseSharedCompilation=false" `
            "-p:Version=$VelopackVersion" `
            "-p:AssemblyVersion=$AssemblyFileVersion" `
            "-p:FileVersion=$AssemblyFileVersion" `
            "-p:InformationalVersion=$DisplayVersion" `
            -o $PublishDir
        if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed." }
        Write-Log "  Publish complete." "Green"
    } else {
        Write-Log "Step 1: Skipping publish (using existing folder)." "Yellow"
    }

    # Step 2: Verify
    Write-Log "Step 2: Verifying self-contained..." "Cyan"
    Assert-SelfContainedPublish -Dir $PublishDir

    # Step 3: .NET Reactor (optional)
    if (-not $SkipReactor) {
        $ReactorExe = "D:\Cshap\.NET Reactor\dotNET_Reactor.Console.exe"
        $ReactorProject = Join-Path $ProjectRoot "tools\reactor\AutoJMS_Reactor.nrproj"
        $TargetDll = Join-Path $PublishDir "AutoJMS.dll"

        if ((Test-Path $ReactorExe) -and (Test-Path $ReactorProject) -and (Test-Path $TargetDll)) {
            Write-Log "Step 3: Protecting with .NET Reactor..." "Cyan"
            & $ReactorExe -project "$ReactorProject" -file "$TargetDll" -targetfile "$TargetDll"
            if ($LASTEXITCODE -ne 0) { throw ".NET Reactor failed." }
            Assert-SelfContainedPublish -Dir $PublishDir
            Write-Log "  Reactor complete." "Green"
        } else {
            Write-Log "Step 3: .NET Reactor not found, skipping." "Yellow"
        }
    } else {
        Write-Log "Step 3: Skipping .NET Reactor." "Yellow"
    }

    # Step 4: vpk pack
    Write-Log "Step 4: Packing with Velopack..." "Cyan"
    Remove-PathSafe $OutputDir
    New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null

    vpk pack `
        --packId AutoJMS `
        --packTitle AutoJMS `
        --packVersion $VelopackVersion `
        --packDir $PublishDir `
        --mainExe AutoJMS.exe `
        --outputDir $OutputDir `
        --channel $Channel `
        --shortcuts Desktop,StartMenuRoot

    if ($LASTEXITCODE -ne 0) { throw "vpk pack failed." }
    Write-Log "  Pack complete." "Green"
    $releaseAssets = Rename-VelopackAssets -Dir $OutputDir -VelopackVersion $VelopackVersion
    Write-Log "  Normalized assets:" "Green"
    foreach ($asset in $releaseAssets) {
        Write-Log "    $(Split-Path $asset -Leaf)" "Gray"
    }

    $velopackSetupAsset = Join-Path $OutputDir "AutoJMS-win-Setup.exe"
    $innoDisplayVersion = if ($DisplayVersion -match '^\d+\.\d+\.\d+(\.\d+)?$') { $DisplayVersion } else { $AssemblyFileVersion }
    $innoInstallerAsset = Build-InnoInstaller -VelopackVersion $VelopackVersion `
        -DisplayVersion $innoDisplayVersion `
        -Channel $Channel `
        -VelopackSetupExe $velopackSetupAsset `
        -OutputDir $OutputDir
    $releaseAssets = @($releaseAssets + $innoInstallerAsset)

    # ── Compute the GitHub release tag for this channel ──
    # Stable: v{VelopackVersion}-Release       (prerelease=false)
    # Beta  : v{VelopackVersion}-Release       (prerelease=true)
    $isPrerelease = [bool]$releaseVersion.IsPrerelease
    $releaseTag   = Get-ReleaseTag -VelopackVersion $VelopackVersion -Channel $Channel
    $releaseTitle = "v$VelopackVersion ($Channel)"
    $setupUrl = "https://github.com/$GitHubRepo/releases/download/$releaseTag/AutoJMS-Installer-$VelopackVersion.exe"
    $velopackSetupUrl = "https://github.com/$GitHubRepo/releases/download/$releaseTag/AutoJMS-win-Setup.exe"

    Write-Log "Release tag:      $releaseTag" "Green"
    Write-Host ""

    Write-Log "Step 4c: Generating Supabase manifests..." "Cyan"
    $versionManifestUrl = Get-SupabasePublicObjectUrl -ProjectRef $SupabaseProjectRef -Bucket $SupabaseBucket -ObjectPath "manifest/version-latest.json"
    $hashManifestUrl = Get-SupabasePublicObjectUrl -ProjectRef $SupabaseProjectRef -Bucket $SupabaseBucket -ObjectPath "manifest/hash-manifest.json"
    $existingVersionManifest = Read-PublicJson -Url $versionManifestUrl
    $existingHashManifest = Read-PublicJson -Url $hashManifestUrl

    $versionLatestJson = New-VersionLatestManifestJson -Channel $Channel `
        -VelopackVersion $VelopackVersion `
        -DisplayVersion $DisplayVersion `
        -InternalBuild $InternalBuild `
        -VelopackChannel $VelopackChannel `
        -Repo $GitHubRepo `
        -Tag $releaseTag `
        -IsPrerelease $isPrerelease `
        -SetupUrl $setupUrl `
        -VelopackSetupUrl $velopackSetupUrl `
        -ReleaseNotes $script:EffectiveReleaseNotes `
        -ExistingManifest $existingVersionManifest

    $hashManifestJson = New-HashManifestJson -VelopackVersion $VelopackVersion `
        -DisplayVersion $DisplayVersion `
        -AssemblyFileVersion $AssemblyFileVersion `
        -DllPath (Join-Path $PublishDir "AutoJMS.dll") `
        -ExistingManifest $existingHashManifest

    $versionLatestPath = Join-Path $OutputDir "version-latest.json"
    $hashManifestPath = Join-Path $OutputDir "hash-manifest.json"
    Write-Utf8NoBom -Path $versionLatestPath -Content $versionLatestJson
    Write-Utf8NoBom -Path $hashManifestPath -Content $hashManifestJson
    Write-Log "  version-latest.json generated." "Green"
    Write-Log "  hash-manifest.json generated." "Green"

    # Step 5: Upload (optional)
    if ($Upload) {
        Write-Log "Step 5: Publishing release (GitHub Release assets + update.xml + Supabase manifests)..." "Cyan"

        Assert-GitHubCli
        Publish-GitHubRelease -Repo $GitHubRepo -Tag $releaseTag -Title $releaseTitle `
            -IsPrerelease $isPrerelease -Assets $releaseAssets

        $existingUpdateXml = Read-ExistingUpdateXml -Repo $GitHubRepo -Path $UpdateXmlPath -Branch $UpdateXmlBranch
        $existingUpdateXmlText = if ($existingUpdateXml) { $existingUpdateXml.Xml } else { "" }
        $updateXml = New-UpdateXml -Channel $Channel `
            -VelopackVersion $VelopackVersion `
            -DisplayVersion $DisplayVersion `
            -InternalBuild $InternalBuild `
            -VelopackChannel $VelopackChannel `
            -Repo $GitHubRepo `
            -Tag $releaseTag `
            -IsPrerelease $isPrerelease `
            -SetupUrl $setupUrl `
            -VelopackSetupUrl $velopackSetupUrl `
            -ReleaseNotes $script:EffectiveReleaseNotes `
            -ExistingXml $existingUpdateXmlText

        $tmpUpdateXml = Join-Path $OutputDir "update.xml"
        Write-Utf8NoBom -Path $tmpUpdateXml -Content $updateXml
        Publish-UpdateXml -LocalPath $tmpUpdateXml `
            -Repo $GitHubRepo `
            -Path $UpdateXmlPath `
            -Branch $UpdateXmlBranch `
            -Message "Update $UpdateXmlPath for $releaseTag"

        if ($SkipSupabaseManifestUpload) {
            Write-Log "  Supabase manifest upload skipped by -SkipSupabaseManifestUpload." "Yellow"
        } else {
            Publish-SupabaseManifestObject -LocalPath $versionLatestPath `
                -ObjectPath "manifest/version-latest.json" `
                -ProjectRef $SupabaseProjectRef `
                -Bucket $SupabaseBucket `
                -Workdir $SupabaseWorkdir

            Publish-SupabaseManifestObject -LocalPath $hashManifestPath `
                -ObjectPath "manifest/hash-manifest.json" `
                -ProjectRef $SupabaseProjectRef `
                -Bucket $SupabaseBucket `
                -Workdir $SupabaseWorkdir
        }

        Write-Log "  Publish complete (assets=GitHub Release, update.xml=GitHub raw, manifests=Supabase)." "Green"
    } else {
        Write-Log "Step 5: Skipping upload. Run with -Upload to push GitHub Release assets, update.xml, and Supabase manifests." "Yellow"
    }

    # Summary
    Write-Host ""
    Write-Host "============================================" -ForegroundColor Green
    Write-Host "  RELEASE BUILD SUCCESSFUL" -ForegroundColor Green
    Write-Host "============================================" -ForegroundColor Green
    Write-Host ""
    Write-Log "Output files:"
    Get-ChildItem $OutputDir -File | ForEach-Object {
        $size = if ($_.Length -gt 1MB) { "{0:N1} MB" -f ($_.Length / 1MB) } else { "{0:N0} KB" -f ($_.Length / 1KB) }
        Write-Log "  $($_.Name) ($size)" "Gray"
    }
    Write-Host ""
    if (-not $Upload) {
        Write-Log "Next: publish (GitHub Release assets + update.xml + Supabase manifests) with:" "Yellow"
        Write-Log "  .\build-release.ps1 -Version `"$VelopackVersion`" -Channel $Channel -Upload -SkipPublish" "White"
    }
    Write-Host ""

} catch {
    Write-Host ""
    Write-Host "============================================" -ForegroundColor Red
    Write-Host "  RELEASE BUILD FAILED" -ForegroundColor Red
    Write-Host "============================================" -ForegroundColor Red
    Write-Host $_.Exception.Message -ForegroundColor Red
    Write-Host $_.ScriptStackTrace -ForegroundColor Yellow
    Write-Host ""
    Read-Host "Press Enter to exit"
    exit 1
}

Read-Host "Press Enter to exit"

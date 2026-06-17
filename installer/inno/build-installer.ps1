<#
.SYNOPSIS
    AutoJMS Bootstrapper Installer Builder
.DESCRIPTION
    Builds AutoJMS_Installer.exe using Inno Setup.
    This is a BOOTSTRAPPER: it bundles the Velopack Setup exe and runtime
    redistributables. It does NOT package the artifacts\publish\win-x64 folder.

    Flow:
      1. Verify Inno Setup (iscc.exe) is installed.
      2. Locate the Velopack Setup exe (..\..\release\output\<channel>\AutoJMS-*-Setup.exe).
      3. Compile AutoJMS.iss, passing the Velopack Setup path as a define.
      4. Output: installer-output\AutoJMS_Installer.exe
.PARAMETER Version
    App version for the installer (e.g. "1.26.5.2").
.PARAMETER InstallerVersion
    Version used in the installer filename (e.g. VelopackVersion "1.26.7").
.PARAMETER Channel
    Release channel ("stable" or "beta", default: stable).
.PARAMETER VelopackSetupExe
    Explicit path to the Velopack Setup exe. If omitted, it is auto-discovered
    under ..\..\release\output\<channel>\.
.PARAMETER OutputDir
    Output folder for the installer (default: installer-output).
.PARAMETER NoPause
    Don't pause on error (for CI use).
.EXAMPLE
    .\build-installer.ps1 -Version "1.26.5.2"
    .\build-installer.ps1 -Version "1.26.5.2" -Channel beta
    .\build-installer.ps1 -Version "1.26.5.2" -VelopackSetupExe "D:\out\AutoJMS-stable-Setup.exe"
#>

param(
    [Parameter(Mandatory = $false)]
    [string]$Version,

    [string]$InstallerVersion,

    [ValidateSet("stable", "beta")]
    [string]$Channel = "stable",

    [string]$VelopackSetupExe,

    [string]$OutputDir = "installer-output",

    [switch]$NoPause
)

$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$ErrorActionPreference = "Stop"

function Write-Log {
    param([string]$Message, [string]$Color = "White")
    $timestamp = Get-Date -Format "HH:mm:ss"
    Write-Host "[$timestamp] $Message" -ForegroundColor $Color
}

function Invoke-Pause {
    if (-not $NoPause) {
        Write-Host ""
        Write-Host "Press Enter to exit..." -ForegroundColor Gray
        $null = Read-Host
    }
}

# ---------- Locate the Velopack Setup exe ----------
function Resolve-VelopackSetup {
    param(
        [string]$Explicit,
        [string]$Channel,
        [string]$ScriptDir
    )

    if (-not [string]::IsNullOrWhiteSpace($Explicit)) {
        $full = if ([System.IO.Path]::IsPathRooted($Explicit)) { $Explicit }
                else { Join-Path $ScriptDir $Explicit }
        $full = [System.IO.Path]::GetFullPath($full)
        if (-not (Test-Path $full)) {
            throw "Velopack Setup exe not found at: $full"
        }
        return $full
    }

    # Auto-discover under ..\..\release\output\<channel>\ (new pipeline) with
    # backward-compatible fallback to ..\velopack-release\<channel>\.
    $candidates = @(
        [System.IO.Path]::GetFullPath((Join-Path $ScriptDir "..\..\release\output\$Channel")),
        [System.IO.Path]::GetFullPath((Join-Path $ScriptDir "..\..\velopack-release\$Channel"))
    )

    $releaseDir = $null
    foreach ($dir in $candidates) {
        if (Test-Path $dir) { $releaseDir = $dir; break }
    }

    if (-not $releaseDir) {
        throw "Velopack release folder not found. Tried:`n$($candidates -join "`n")`nRun release\build-release.bat first."
    }

    # Match AutoJMS-*-Setup.exe (e.g. AutoJMS-stable-Setup.exe, AutoJMS-win-Setup.exe)
    $candidates = Get-ChildItem $releaseDir -Filter "*Setup.exe" -File -ErrorAction SilentlyContinue |
        Sort-Object LastWriteTime -Descending

    if (-not $candidates -or $candidates.Count -eq 0) {
        throw "No Velopack Setup exe (*Setup.exe) found in: $releaseDir`nRun 'vpk pack' first."
    }

    return $candidates[0].FullName
}

# ---------- Main ----------
try {
    Write-Host ""
    Write-Host "============================================" -ForegroundColor Cyan
    Write-Host "  AutoJMS Bootstrapper Installer Builder" -ForegroundColor Cyan
    Write-Host "============================================" -ForegroundColor Cyan
    Write-Host ""

    # Normalize output dir
    $OutputDirFull = if ([System.IO.Path]::IsPathRooted($OutputDir)) { $OutputDir }
                     else { Join-Path $ScriptDir $OutputDir }
    $OutputDirFull = [System.IO.Path]::GetFullPath($OutputDirFull)

    $IssFile = Join-Path $ScriptDir "AutoJMS.iss"
    if (-not (Test-Path $IssFile)) {
        throw "Inno Setup script not found: $IssFile"
    }

    # ----- Step 1: Version -----
    if ([string]::IsNullOrWhiteSpace($Version)) {
        Write-Log "No version specified." "Yellow"
        $Version = Read-Host "Enter app version (e.g. 1.26.5.2)"
        if ([string]::IsNullOrWhiteSpace($Version)) {
            throw "Version is required."
        }
    }
    Write-Log "App version: $Version" "Green"
    if ([string]::IsNullOrWhiteSpace($InstallerVersion)) {
        $InstallerVersion = $Version
    }
    Write-Log "Installer version: $InstallerVersion" "Green"
    Write-Log "Channel:     $Channel" "Green"

    # ----- Step 2: Resolve Velopack Setup exe -----
    Write-Log "Locating Velopack Setup exe..."
    $VelopackSetupFull = Resolve-VelopackSetup -Explicit $VelopackSetupExe -Channel $Channel -ScriptDir $ScriptDir
    $setupInfo = Get-Item $VelopackSetupFull
    $setupSize = "{0:N1} MB" -f ($setupInfo.Length / 1MB)
    Write-Log "  Found: $VelopackSetupFull ($setupSize)" "Green"

    # ----- Step 3: Find Inno Setup -----
    Write-Log "Looking for Inno Setup..."
    $IsccPath = $null
    $possiblePaths = @(
        "${env:ProgramFiles(x86)}\Inno Setup 6\iscc.exe",
        "${env:ProgramFiles}\Inno Setup 6\iscc.exe",
        "${env:ProgramFiles(x86)}\Inno Setup 5\iscc.exe",
        "${env:ProgramFiles}\Inno Setup 5\iscc.exe"
    )
    foreach ($path in $possiblePaths) {
        if (Test-Path $path) { $IsccPath = $path; break }
    }
    if (-not $IsccPath) {
        throw "Inno Setup not found.`nInstall Inno Setup 6 from: https://jrsoftware.org/isdl.php`nChecked:`n$($possiblePaths -join "`n")"
    }
    Write-Log "  Found: $IsccPath" "Green"

    # ----- Step 4: Output dir -----
    if (-not (Test-Path $OutputDirFull)) {
        New-Item -ItemType Directory -Force -Path $OutputDirFull | Out-Null
        Write-Log "  Created output directory: $OutputDirFull" "Green"
    }

    # ----- Step 5: Redist info -----
    $RedistDir = Join-Path $ScriptDir "redist"
    if (Test-Path $RedistDir) {
        $redistFiles = Get-ChildItem $RedistDir -File -ErrorAction SilentlyContinue
        if ($redistFiles) {
            Write-Log "Offline redist files found:"
            foreach ($f in $redistFiles) {
                Write-Log "  $($f.Name) ($([math]::Round($f.Length/1MB, 1)) MB)" "Gray"
            }
        } else {
            Write-Log "Redist folder empty. Runtimes will be downloaded if missing." "Yellow"
        }
    } else {
        Write-Log "Redist folder not found. Runtimes will be downloaded if missing." "Yellow"
    }

    # ----- Step 6: Compile -----
    $Defines = @(
        "/dAppVersion=""$Version""",
        "/dInstallerVersion=""$InstallerVersion""",
        "/dVelopackSetupExe=""$VelopackSetupFull""",
        "/dOutputDir=""$OutputDirFull"""
    )

    Write-Host ""
    Write-Host "============================================" -ForegroundColor Cyan
    Write-Host "  Compiling bootstrapper installer..." -ForegroundColor Cyan
    Write-Host "============================================" -ForegroundColor Cyan
    Write-Log "  Script:        $IssFile"
    Write-Log "  Velopack Setup:$VelopackSetupFull"
    Write-Log "  Output:        $OutputDirFull"
    Write-Log "  Version:       $Version"
    Write-Log "  InstallerVer:  $InstallerVersion"
    Write-Host ""

    $isccArgs = @("""$IssFile""") + $Defines
    Write-Log "Running: iscc.exe $($isccArgs -join ' ')" "Gray"

    $process = Start-Process -FilePath $IsccPath -ArgumentList $isccArgs -NoNewWindow -Wait -PassThru
    if ($process.ExitCode -ne 0) {
        throw "Inno Setup compiler failed with exit code $($process.ExitCode)."
    }

    # ----- Step 7: Verify output -----
    $SetupExe = Join-Path $OutputDirFull "AutoJMS-Installer-$InstallerVersion.exe"
    if (Test-Path $SetupExe) {
        $fileInfo = Get-Item $SetupExe
        $size = if ($fileInfo.Length -gt 1MB) { "{0:N1} MB" -f ($fileInfo.Length / 1MB) }
                else { "{0:N0} KB" -f ($fileInfo.Length / 1KB) }
        Write-Host ""
        Write-Host "============================================" -ForegroundColor Green
        Write-Host "  BUILD SUCCESSFUL" -ForegroundColor Green
        Write-Host "============================================" -ForegroundColor Green
        Write-Log "  Installer: $SetupExe"
        Write-Log "  Size:      $size"
        Write-Host ""
    } else {
        throw "Installer was not created at expected path: $SetupExe"
    }

} catch {
    Write-Host ""
    Write-Host "============================================" -ForegroundColor Red
    Write-Host "  BUILD FAILED" -ForegroundColor Red
    Write-Host "============================================" -ForegroundColor Red
    Write-Host $_.Exception.Message -ForegroundColor Red
    Write-Host ""
    Write-Host $_.ScriptStackTrace -ForegroundColor Yellow
    Write-Host ""
    Invoke-Pause
    exit 1
}

Invoke-Pause

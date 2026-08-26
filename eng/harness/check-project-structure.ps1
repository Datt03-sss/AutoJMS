<#
.SYNOPSIS
    Project structure validation harness for AutoJMS.
.DESCRIPTION
    Ensures key directories, configuration files, and solution projects exist
    in their expected locations, and checks target frameworks.
.EXAMPLE
    powershell -ExecutionPolicy Bypass -File .\eng\harness\check-project-structure.ps1
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$Root = Resolve-Path (Join-Path $PSScriptRoot '..\..') | Select-Object -ExpandProperty Path

Write-Host '========================================' -ForegroundColor Cyan
Write-Host '  AutoJMS Project Structure Check' -ForegroundColor Cyan
Write-Host '========================================' -ForegroundColor Cyan
Write-Host ''

$issues = @()

# 1. Verify Solution File
$slnFile = Join-Path $Root 'AutoJMS.slnx'
if (-not (Test-Path $slnFile)) {
    $issues += "Solution file missing: AutoJMS.slnx"
} else {
    Write-Host "  AutoJMS.slnx: OK" -ForegroundColor Green
}

# 2. Verify Key Directories
$requiredDirs = @(
    'docs/agent',
    'docs/architecture',
    'docs/decisions',
    'eng/harness',
    'eng/prompts',
    '.claude',
    'src/AutoJMS'
)

foreach ($dir in $requiredDirs) {
    $path = Join-Path $Root $dir
    if (-not (Test-Path $path -PathType Container)) {
        $issues += "Required directory missing: $dir"
    } else {
        Write-Host "  Directory '$dir': OK" -ForegroundColor Green
    }
}

# 3. Verify Critical Files
$requiredFiles = @(
    'AGENTS.md',
    'CLAUDE.md',
    'src/AutoJMS/Program.cs',
    'src/AutoJMS/Forms/Main.cs',
    'src/AutoJMS/Licensing/TierRuntimePolicy.cs',
    'src/AutoJMS/Licensing/LicenseApiService.cs'
)

foreach ($file in $requiredFiles) {
    $path = Join-Path $Root $file
    if (-not (Test-Path $path -PathType Leaf)) {
        $issues += "Required file missing: $file"
    } else {
        Write-Host "  File '$file': OK" -ForegroundColor Green
    }
}

# 4. Verify Target Framework (.NET 8)
$csprojPath = Join-Path $Root 'src/AutoJMS/AutoJMS.csproj'
if (Test-Path $csprojPath) {
    try {
        [xml]$csproj = Get-Content $csprojPath
        $targetFramework = $csproj.Project.PropertyGroup[0].TargetFramework
        if ($targetFramework -notmatch 'net8\.0-windows') {
            $issues += "Csproj TargetFramework is not net8.0-windows: $targetFramework"
        } else {
            Write-Host "  TargetFramework '$targetFramework': OK" -ForegroundColor Green
        }
    } catch {
        $issues += "Failed to parse AutoJMS.csproj xml properties"
    }
}

# 5. Verify harness script encoding (UTF-8 BOM when non-ASCII)
#
# Windows PowerShell 5.1 decodes a .ps1 that has no byte-order mark using the machine's ANSI
# code page. A dev box with the "Use Unicode UTF-8 worldwide" option has ACP 65001, so a
# BOM-less UTF-8 script parses there; a GitHub windows-latest runner has ACP 1252, where the
# byte 0x94 inside box-drawing characters such as U+2500 / U+2501 decodes to U+201D - a curly
# quote that PowerShell accepts as a string delimiter. String parsing then desynchronises and
# the file fails to PARSE, before a single line of it runs.
#
# This is not cosmetic. verify.ps1 died exactly that way on every CI run from the day CI was
# added until 2026-08-26: the log showed only "The string is missing the terminator", so none
# of the four gates ever executed on a runner, while every local run reported ALL GATES PASSED.
# A BOM makes the encoding explicit and independent of the code page. Keep it.
#
# Scope is eng/harness on purpose - these are the scripts CI executes. Other .ps1 in the repo
# (release, eng/git, backend/datahub/scripts, tools/maintenance) carry the same latent risk but
# run on the Owner machine, and one of them is a Protected File.
$harnessDir = Join-Path $Root 'eng/harness'
if (Test-Path $harnessDir -PathType Container) {
    foreach ($script in (Get-ChildItem -Path $harnessDir -Filter '*.ps1' -File | Sort-Object Name)) {
        $bytes = [System.IO.File]::ReadAllBytes($script.FullName)
        $hasNonAscii = $false
        foreach ($b in $bytes) {
            if ($b -gt 127) { $hasNonAscii = $true; break }
        }
        if (-not $hasNonAscii) {
            Write-Host "  Encoding '$($script.Name)': OK (pure ASCII)" -ForegroundColor Green
            continue
        }
        $hasBom = ($bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF)
        if ($hasBom) {
            Write-Host "  Encoding '$($script.Name)': OK (non-ASCII + UTF-8 BOM)" -ForegroundColor Green
        } else {
            $issues += "Harness script has non-ASCII characters but no UTF-8 BOM (will fail to PARSE on a runner whose ANSI code page is not UTF-8): eng/harness/$($script.Name)"
        }
    }
}

# 6. Summary
Write-Host ''
if ($issues.Count -gt 0) {
    Write-Host '========================================' -ForegroundColor Red
    Write-Host '  PROJECT STRUCTURE CHECK FAILED' -ForegroundColor Red
    Write-Host '========================================' -ForegroundColor Red
    foreach ($issue in $issues) {
        Write-Host "  - $issue" -ForegroundColor Red
    }
    Write-Host ''
    exit 1
}

Write-Host 'Project structure is clean and standardized.' -ForegroundColor Green
exit 0

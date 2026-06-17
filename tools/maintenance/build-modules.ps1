param(
    [string]$Configuration = "Debug",
    [string]$ModuleVersion = "1.0.0"
)

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Definition
$root = [System.IO.Path]::GetFullPath((Join-Path $scriptDir "..\.."))
$legacyModuleRoot = Join-Path $root "archive\old-module-system"
$appProjectDir = Join-Path $root "src\AutoJMS"
$modulesDir = Join-Path $appProjectDir "modules"
$errorCount = 0

Write-Host "== Building module projects (Configuration: $Configuration) ==" -ForegroundColor Cyan

$projects = @(
    "AutoJMS.RetryPolicy\AutoJMS.RetryPolicy.csproj",
    "AutoJMS.Selectors\AutoJMS.Selectors.csproj"
)

foreach ($proj in $projects) {
    $projPath = Join-Path $legacyModuleRoot $proj
    Write-Host "Building $proj..." -ForegroundColor Yellow
    $result = dotnet build $projPath --configuration $Configuration 2>&1
    if ($LASTEXITCODE -ne 0) {
        Write-Host "FAILED: $proj" -ForegroundColor Red
        $result | Select-String -Pattern "error CS" | ForEach-Object { Write-Host "  $_" -ForegroundColor Red }
        $errorCount++
    } else {
        Write-Host "OK: $proj" -ForegroundColor Green
    }
}

Write-Host "`nDeploying modules to versioned folders..." -ForegroundColor Cyan

# Define modules: name -> source DLL path (relative to archive\old-module-system)
$moduleDefs = @(
    @{name="retry"; src="AutoJMS.RetryPolicy\bin\$Configuration\net8.0-windows\AutoJMS.RetryPolicy.dll"; file="AutoJMS.RetryPolicy.dll"; storagePath="modules/AutoJMS.RetryPolicy.dll"; version=$ModuleVersion},
    @{name="selectors"; src="AutoJMS.Selectors\bin\$Configuration\net8.0-windows\AutoJMS.Selectors.dll"; file="AutoJMS.Selectors.dll"; storagePath="configs/selectors.json"; version=$ModuleVersion}
)

$binDir = $legacyModuleRoot
$activeEntries = @{}

foreach ($m in $moduleDefs) {
    $srcPath = Join-Path $binDir $m.src
    $versionDir = [System.IO.Path]::Combine($modulesDir, $m.name, $m.version)
    $dstPath = Join-Path $versionDir $m.file

    if (Test-Path $srcPath) {
        if (-not (Test-Path $versionDir)) { New-Item -ItemType Directory -Path $versionDir -Force | Out-Null }
        Copy-Item -LiteralPath $srcPath -Destination $dstPath -Force
        Write-Host "  $($m.name) v$($m.version) -> $($m.name)\$($m.version)\$($m.file)" -ForegroundColor Green
        $activeEntries[$m.name] = $m.version
    } else {
        Write-Host "  MISSING: $srcPath" -ForegroundColor Yellow
    }
}

# Generate active_modules.json
$activeJson = @{modules=$activeEntries} | ConvertTo-Json
$activeJson | Set-Content (Join-Path $modulesDir "active_modules.json") -Force
Write-Host "  Generated active_modules.json" -ForegroundColor Green

# Generate modules.json (server manifest — storage paths)
$serverEntries = @()
foreach ($m in $moduleDefs) {
    $serverEntries += @{
        name=$m.name
        version=$m.version
        file=$m.storagePath
        sha256=""
        signature=""
        requires=@()
        required=$false
    }
}
$serverJson = @{
    manifestVersion="2026.05.25"
    modules=$serverEntries
} | ConvertTo-Json -Depth 5
$serverJson | Set-Content (Join-Path $modulesDir "modules.json") -Force
Write-Host "  Generated modules.json (server manifest)" -ForegroundColor Green

# Generate modules-cache.json (local cache — plain filenames)
$cacheEntries = @()
foreach ($m in $moduleDefs) {
    $cacheEntries += @{
        name=$m.name
        version=$m.version
        file=$m.file
        sha256=""
        signature=""
        requires=@()
        required=$false
    }
}
$cacheJson = @{
    manifestVersion="2026.05.25"
    appVersion="1.26.05.0"
    modules=$cacheEntries
} | ConvertTo-Json -Depth 5
$cacheJson | Set-Content (Join-Path $modulesDir "modules-cache.json") -Force
Write-Host "  Generated modules-cache.json" -ForegroundColor Green

# Copy to build output directory
$outputRootDir = Join-Path $appProjectDir "bin\$Configuration\net8.0-windows"
$outputModulesDir = Join-Path $outputRootDir "modules"
if (Test-Path $outputRootDir) {
    if (-not (Test-Path $outputModulesDir)) { New-Item -ItemType Directory -Path $outputModulesDir -Force | Out-Null }

    # Copy entire modules tree (versioned folders + JSON)
    Get-ChildItem -LiteralPath $modulesDir -Recurse -Exclude "manifest.json" | ForEach-Object {
        $relative = $_.FullName.Substring($modulesDir.Length + 1)
        $target = Join-Path $outputModulesDir $relative
        if ($_.PSIsContainer) {
            if (-not (Test-Path $target)) { New-Item -ItemType Directory -Path $target -Force | Out-Null }
        } else {
            Copy-Item -LiteralPath $_.FullName -Destination $target -Force
            Write-Host "  Copied to output: $relative" -ForegroundColor Green
        }
    }

    # Copy root config files to output root
    $rootConfigs = @("tier-definitions.json")
    foreach ($rc in $rootConfigs) {
        $srcPath = Join-Path $appProjectDir $rc
        if (Test-Path $srcPath) {
            Copy-Item -LiteralPath $srcPath -Destination (Join-Path $outputRootDir $rc) -Force
            Write-Host "  Copied to output: $rc" -ForegroundColor Green
        }
    }

    # Generate hash-manifest.json (for reference / upload to Supabase)
    $dllPath = Join-Path $outputRootDir "AutoJMS.dll"
    if (Test-Path $dllPath) {
        $hash = (Get-FileHash -LiteralPath $dllPath -Algorithm SHA256).Hash.ToLower()
        # Read version from the built DLL
        $versionInfo = [System.Diagnostics.FileVersionInfo]::GetVersionInfo((Join-Path $outputRootDir "AutoJMS.exe"))
        $version = "$($versionInfo.ProductMajorPart).$($versionInfo.ProductMinorPart).$($versionInfo.ProductBuildPart).$($versionInfo.ProductPrivatePart)"
        $hashManifest = @{
            versions = @{
                "$version" = @{ sha256 = $hash }
            }
        } | ConvertTo-Json -Depth 5
        $manifestDir = Join-Path $root "infra\supabase\autojms-modules\manifest"
        if (-not (Test-Path $manifestDir)) { New-Item -ItemType Directory -Path $manifestDir -Force | Out-Null }
        $hashManifest | Set-Content (Join-Path $manifestDir "hash-manifest.json") -Force
        Write-Host "  Generated manifest/hash-manifest.json (v$version)" -ForegroundColor Green
    }
}

Write-Host "`n== Build complete. Errors: $errorCount ==" -ForegroundColor Cyan
exit $errorCount

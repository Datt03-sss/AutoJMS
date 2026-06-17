param(
    [Parameter(Mandatory=$true)]
    [ValidateSet("retry", "selectors", "all")]
    [string]$Module,
    [string]$Configuration = "Release",
    [string]$SupabaseUrl = "https://bnsnnrlwfzxemmizknwy.supabase.co",
    [string]$SupabaseKey = $env:AUTOJMS_SUPABASE_ANON_KEY,
    [string]$FirebaseBucket = "autojms-modules",
    [string]$ModuleVersion = ""
)

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Definition
$root = [System.IO.Path]::GetFullPath((Join-Path $scriptDir "..\.."))
$legacyModuleRoot = Join-Path $root "archive\old-module-system"
$modulesDir = Join-Path $root "src\AutoJMS\modules"

# Version defaults
if (-not $ModuleVersion) {
    switch ($Module) {
        "retry"    { $ModuleVersion = "1.0.0" }
        "selectors" { $ModuleVersion = "1.0.0" }
        "all"      { $ModuleVersion = "1.0.0" }
    }
}

Write-Host "============================================" -ForegroundColor Cyan
Write-Host "  AutoJMS Module Upload Tool" -ForegroundColor Cyan
Write-Host "  Target: Supabase + Firebase Storage" -ForegroundColor Cyan
Write-Host "============================================" -ForegroundColor Cyan

# ─── Build if needed ─────────────────────────────────────

if ($Module -eq "all" -or $Module -eq "retry") {
    Write-Host "`nBuilding AutoJMS.RetryPolicy ($Configuration)..." -ForegroundColor Yellow
    dotnet build (Join-Path $legacyModuleRoot "AutoJMS.RetryPolicy\AutoJMS.RetryPolicy.csproj") --configuration $Configuration 2>&1 | Out-Null
}

if ($Module -eq "all" -or $Module -eq "selectors") {
    Write-Host "Building AutoJMS.Selectors ($Configuration)..." -ForegroundColor Yellow
    dotnet build (Join-Path $legacyModuleRoot "AutoJMS.Selectors\AutoJMS.Selectors.csproj") --configuration $Configuration 2>&1 | Out-Null
}

Write-Host "Build complete." -ForegroundColor Green

# ─── Module definitions ──────────────────────────────────

$modules = @()

if ($Module -eq "all" -or $Module -eq "retry") {
    $modules += @{
        name="retry"
        version="1.0.1"
        file="AutoJMS.RetryPolicy.dll"
        src=(Join-Path $legacyModuleRoot "AutoJMS.RetryPolicy\bin\$Configuration\net8.0-windows\AutoJMS.RetryPolicy.dll")
        firebasePath="retry/v1.0.1/AutoJMS.RetryPolicy.dll"
    }
}

if ($Module -eq "all" -or $Module -eq "selectors") {
    $modules += @{
        name="selectors"
        version="1.0.1"
        file="AutoJMS.Selectors.dll"
        src=(Join-Path $legacyModuleRoot "AutoJMS.Selectors\bin\$Configuration\net8.0-windows\AutoJMS.Selectors.dll")
        firebasePath="selectors/v1.0.1/AutoJMS.Selectors.dll"
    }
}

# ─── Steps for each module ───────────────────────────────

foreach ($m in $modules) {
    Write-Host "`n--- Processing $($m.name) v$($m.version) ---" -ForegroundColor Magenta

    # 1. Compute SHA256
    if (-not (Test-Path $m.src)) {
        Write-Host "  ERROR: Source not found: $($m.src)" -ForegroundColor Red
        continue
    }
    $sha256 = (Get-FileHash -LiteralPath $m.src -Algorithm SHA256).Hash.ToLower()
    Write-Host "  SHA256: $sha256" -ForegroundColor Gray

    # 2. Compute Firebase Storage URL
    $firebaseUrl = "https://firebasestorage.googleapis.com/v0/b/$FirebaseBucket/o/$($m.firebasePath)?alt=media"
    Write-Host "  Firebase URL: $firebaseUrl" -ForegroundColor Gray

    # 3. Upload to Firebase Storage (requires firebase CLI or gcloud)
    Write-Host "`n  === UPLOAD TO FIREBASE STORAGE ===" -ForegroundColor Yellow
    Write-Host "  Run this command manually (requires firebase-tools):" -ForegroundColor White
    Write-Host "  firebase storage:upload $($m.src) --bucket $FirebaseBucket --path $($m.firebasePath)" -ForegroundColor Gray
    Write-Host ""
    Write-Host "  Or via curl with a download token:" -ForegroundColor Gray
    Write-Host "  (Upload manually through Firebase Console > Storage > Upload File)" -ForegroundColor Gray

    # 4. Upsert to Supabase
    Write-Host "`n  === UPSERT TO SUPABASE ===" -ForegroundColor Yellow

    $body = @{
        name = $m.name
        version = $m.version
        file = $m.file
        sha256 = $sha256
        signature = $null
        firebase_url = $firebaseUrl
        requires = @()
        required = $false
        enabled = $true
    } | ConvertTo-Json -Depth 3

    $restUrl = "$SupabaseUrl/rest/v1/app_modules"
    $anonKey = $SupabaseKey
    if ([string]::IsNullOrWhiteSpace($anonKey)) {
        Write-Host "  ERROR: Supabase anon key missing. Set AUTOJMS_SUPABASE_ANON_KEY or pass -SupabaseKey." -ForegroundColor Red
        continue
    }

    Write-Host "  POST $restUrl" -ForegroundColor Gray
    Write-Host "  Body: $body" -ForegroundColor Gray
    Write-Host ""
    Write-Host "  To upsert via curl (run after Firebase upload):" -ForegroundColor White
    Write-Host '  curl -X POST "'$restUrl'" \' -ForegroundColor Gray
    Write-Host '    -H "apikey: '$anonKey'" \' -ForegroundColor Gray
    Write-Host '    -H "Authorization: Bearer '$anonKey'" \' -ForegroundColor Gray
    Write-Host '    -H "Content-Type: application/json" \' -ForegroundColor Gray
    Write-Host '    -H "Prefer: resolution=merge-duplicates" \' -ForegroundColor Gray
    Write-Host "    -d '$body'" -ForegroundColor Gray
    Write-Host ""

    # 5. Update versioned folder locally
    $localVersionDir = Join-Path $modulesDir $m.name $m.version
    if (-not (Test-Path $localVersionDir)) { New-Item -ItemType Directory -Path $localVersionDir -Force | Out-Null }
    Copy-Item -LiteralPath $m.src -Destination (Join-Path $localVersionDir $m.file) -Force
    Write-Host "  Copied locally: $($m.name)\$($m.version)\$($m.file)" -ForegroundColor Green

    # 6. Update active_modules.json
    $activePath = Join-Path $modulesDir "active_modules.json"
    $active = Get-Content $activePath -Raw | ConvertFrom-Json
    $active.modules.$($m.name) = $m.version
    $active | ConvertTo-Json | Set-Content $activePath -Force
    Write-Host "  Updated active_modules.json: $($m.name) -> $($m.version)" -ForegroundColor Green
}

Write-Host "`n=== DEPLOY STEPS ===" -ForegroundColor Cyan
Write-Host "1. Upload files to Firebase Storage (manual via Console or firebase CLI)"
Write-Host "2. Upsert rows to Supabase app_modules table"
Write-Host "3. Run .\build-modules.ps1 to sync local versioned folders"
Write-Host "=====================" -ForegroundColor Cyan

@echo off
cd /d "%~dp0"

title Upload Release Manifests to VPS

echo ============================================
echo  Upload Existing Release Manifests
echo ============================================
echo.
echo This uploads only small JSON manifests to the VPS DataHub API.
echo Velopack binaries remain on the configured binary provider.
echo.

set "APP_CHANNEL=stable"
if "%1"=="beta" set "APP_CHANNEL=beta"
set "OUTPUT_DIR=%~dp0output\%APP_CHANNEL%"

if not exist "%OUTPUT_DIR%" (
    echo ERROR: No release found at %OUTPUT_DIR%
    exit /b 1
)

if not defined DATAHUB_MANIFEST_UPLOAD_URL (
    echo ERROR: Set DATAHUB_MANIFEST_UPLOAD_URL to the VPS manifest endpoint.
    exit /b 1
)
if not defined DATAHUB_ADMIN_TOKEN (
    echo ERROR: Set DATAHUB_ADMIN_TOKEN for VPS publishing.
    exit /b 1
)

powershell.exe -NoProfile -ExecutionPolicy Bypass -Command ^
    "$ErrorActionPreference='Stop'; $dir='%OUTPUT_DIR%'; $base=$env:DATAHUB_MANIFEST_UPLOAD_URL.TrimEnd('/'); $token=$env:DATAHUB_ADMIN_TOKEN; $allowed=@('version-latest.json','hash-manifest.json'); $files=Get-ChildItem $dir -File | Where-Object { $allowed -contains $_.Name }; if (-not $files) { throw 'No manifest files found.' }; foreach ($f in $files) { if ($f.Length -gt 1MB) { throw \"Manifest too large: $($f.Name)\" }; $url=\"$base/api/v1/admin/manifests/manifest/$($f.Name)\"; Invoke-WebRequest -Uri $url -Method Put -Headers @{Authorization=\"Bearer $token\"} -ContentType 'application/json' -InFile $f.FullName | Out-Null; Write-Host \"Uploaded $($f.Name)\" }"

if %ERRORLEVEL% neq 0 exit /b %ERRORLEVEL%
echo Manifest upload complete.

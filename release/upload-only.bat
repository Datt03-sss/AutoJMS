@echo off
cd /d "%~dp0"

title Upload Release Manifests to Supabase

echo ============================================
echo  Upload Existing Release Manifests
echo ============================================
echo.
echo This uploads ONLY small JSON manifests from output\^<channel^>
echo to Supabase Storage. Velopack binaries stay on GitHub Releases.
echo.
echo Blocked by policy: RELEASES, .nupkg, Setup.exe, zip, or other binaries.
echo.

echo Select channel:
echo   1 = stable (default)
echo   2 = beta
choice /c 12 /n /m "Enter choice [1/2] (default: 1): " /d 1 /t 10
if %ERRORLEVEL%==2 (
    set "APP_CHANNEL=beta"
) else (
    set "APP_CHANNEL=stable"
)

set "OUTPUT_DIR=%~dp0output\%APP_CHANNEL%"

if not exist "%OUTPUT_DIR%" (
    echo ERROR: No release found at %OUTPUT_DIR%
    echo Run build-release.bat first.
    pause
    exit /b 1
)

REM ─── Pre-flight auth check ────────────────────────────────────────────
echo.
echo Checking Supabase auth...

if defined SUPABASE_SERVICE_ROLE_KEY (
    echo   Found SUPABASE_SERVICE_ROLE_KEY env var. Will upload via REST API.
) else (
    where supabase >nul 2>&1
    if errorlevel 1 (
        echo.
        echo ERROR: Supabase CLI not installed and no SUPABASE_SERVICE_ROLE_KEY set.
        echo   1. scoop bucket add supabase https://github.com/supabase/scoop-bucket.git
        echo      scoop install supabase
        echo   2. Or set env SUPABASE_SERVICE_ROLE_KEY for REST upload.
        echo.
        pause
        exit /b 1
    )
    supabase projects list >nul 2>&1
    if errorlevel 1 (
        echo.
        echo Launching  supabase login ...
        supabase login
        if errorlevel 1 (
            echo Login failed/cancelled.
            pause
            exit /b 1
        )
    )
)

REM Manifest-only upload. Do not upload Velopack binaries to Supabase.

echo.
echo Uploading manifest files from: %OUTPUT_DIR%
echo To: autojms-modules/manifest/
echo.

powershell.exe -NoProfile -ExecutionPolicy Bypass -Command ^
    "$ErrorActionPreference='Stop'; $dir='%OUTPUT_DIR%'; $bucket='autojms-modules'; $proj='bnsnnrlwfzxemmizknwy'; $key=$env:SUPABASE_SERVICE_ROLE_KEY; $allowed=@('version-latest.json','hash-manifest.json'); $files=Get-ChildItem $dir -File | Where-Object { $allowed -contains $_.Name }; if (-not $files) { throw 'No version-latest.json or hash-manifest.json found. Run build-release.ps1 -Upload or copy the generated manifest JSON into the output channel folder.' }; foreach ($f in $files) { if ($f.Length -gt 1MB) { throw \"Manifest too large: $($f.Name)\" }; $remote=\"manifest/$($f.Name)\"; Write-Host \"  Uploading $remote...\"; if ($key) { $h=@{Authorization=\"Bearer $key\"; 'x-upsert'='true'; 'Cache-Control'='max-age=60'}; Invoke-RestMethod -Method Post -Uri \"https://$proj.supabase.co/storage/v1/object/$bucket/$remote\" -Headers $h -ContentType 'application/json' -InFile $f.FullName | Out-Null } else { & supabase storage cp $f.FullName \"ss:///$bucket/$remote\" --linked --experimental; if ($LASTEXITCODE -ne 0) { throw \"Failed to upload $($f.Name)\" } } }; Write-Host 'Manifest upload complete.' -ForegroundColor Green"

if %ERRORLEVEL% neq 0 (
    echo.
    echo Upload failed.
    pause
    exit /b %ERRORLEVEL%
)

echo.
echo Users will receive this update via Check Update after GitHub assets and Supabase manifest match.
echo.
pause

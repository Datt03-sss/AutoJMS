@echo off
cd /d "%~dp0"

title AutoJMS Velopack Release Builder

echo ============================================
echo  AutoJMS Velopack Release Builder
echo ============================================
echo.
echo This builds a Velopack update package for GitHub Releases.
echo Users who already have AutoJMS installed will receive this update
echo automatically via the in-app Check Update button.
echo.

set /p APP_VERSION="Enter VelopackVersion (stable: 1.26.6, beta: 1.26.6-beta.1): "
if "%APP_VERSION%"=="" (
    echo ERROR: Version is required
    pause
    exit /b 1
)

set /p DISPLAY_VERSION="Enter DisplayVersion (optional, e.g. 1.26.6.1): "
set /p INTERNAL_BUILD="Enter InternalBuild (optional, e.g. 20260607.1): "
set /p RELEASE_NOTES="Enter release notes (optional): "

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

echo.
echo Upload to GitHub Release and update update.xml after build?
echo   1 = No, build only (default)
echo   2 = Yes, build and upload
choice /c 12 /n /m "Enter choice [1/2] (default: 1): " /d 1 /t 10
if %ERRORLEVEL%==2 (
    set "UPLOAD_FLAG=-Upload"
) else (
    set "UPLOAD_FLAG="
)

REM ─── Pre-flight: if uploading, ensure GitHub auth is in place ──
if defined UPLOAD_FLAG (
    echo.
    echo Checking GitHub CLI auth (hosts Velopack binaries and update.xml)...
    where gh >nul 2>&1
    if errorlevel 1 (
        echo.
        echo ERROR: Upload requested but GitHub CLI 'gh' is not installed.
        echo   Install: winget install --id GitHub.cli
        echo   Then run: gh auth login
        echo.
        pause
        exit /b 1
    )
    gh auth status >nul 2>&1
    if errorlevel 1 (
        echo.
        echo GitHub CLI not authenticated. Launching  gh auth login ...
        gh auth login
        if errorlevel 1 (
            echo ERROR: gh auth login failed or was cancelled.
            pause
            exit /b 1
        )
    ) else (
        echo   GitHub CLI logged in - OK.
    )
)

:build
echo.
echo Building release v%APP_VERSION% [%APP_CHANNEL%]...
echo.

powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0build-release.ps1" -Version "%APP_VERSION%" -Channel "%APP_CHANNEL%" -DisplayVersion "%DISPLAY_VERSION%" -InternalBuild "%INTERNAL_BUILD%" -ReleaseNotes "%RELEASE_NOTES%" %UPLOAD_FLAG%

if %ERRORLEVEL% neq 0 (
    echo.
    echo Build failed with error code %ERRORLEVEL%
    pause
    exit /b %ERRORLEVEL%
)

pause

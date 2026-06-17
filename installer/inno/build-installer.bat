@echo off
cd /d "%~dp0"

title AutoJMS Bootstrapper Installer Builder

echo ============================================
echo  AutoJMS Bootstrapper Installer Builder
echo ============================================
echo.

REM Check for Inno Setup
set "ISCC="
if exist "%ProgramFiles(x86)%\Inno Setup 6\iscc.exe" set "ISCC=%ProgramFiles(x86)%\Inno Setup 6\iscc.exe"
if exist "%ProgramFiles%\Inno Setup 6\iscc.exe" set "ISCC=%ProgramFiles%\Inno Setup 6\iscc.exe"

if "%ISCC%"=="" (
    echo ERROR: Inno Setup 6 not found.
    echo.
    echo Please install Inno Setup from:
    echo   https://jrsoftware.org/isdl.php
    echo.
    pause
    exit /b 1
)

echo Found Inno Setup: %ISCC%
echo.
echo This builds the BOOTSTRAPPER installer.
echo It bundles the Velopack Setup exe from release\output\^<channel^>.
echo Make sure you have run release\build-release.bat (vpk pack) first.
echo.

REM ----- Ask for version -----
set /p APP_VERSION="Enter app version (e.g. 1.26.5.2): "
if "%APP_VERSION%"=="" (
    echo ERROR: Version is required
    pause
    exit /b 1
)

REM ----- Ask for channel -----
echo.
echo Select release channel:
echo   1 = stable (default)
echo   2 = beta
choice /c 12 /n /m "Enter choice [1/2] (default: 1): " /d 1 /t 10
if %ERRORLEVEL%==2 (
    set "APP_CHANNEL=beta"
) else (
    set "APP_CHANNEL=stable"
)

echo.
echo Building bootstrapper for version %APP_VERSION%, channel %APP_CHANNEL%...
echo.

powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0build-installer.ps1" -Version "%APP_VERSION%" -Channel "%APP_CHANNEL%" %*

if %ERRORLEVEL% neq 0 (
    echo.
    echo Build failed with error code %ERRORLEVEL%
    pause
    exit /b %ERRORLEVEL%
)

echo.
echo Build completed successfully!
echo Installer is in: installer-output\AutoJMS-win-Setup.exe
echo.
pause

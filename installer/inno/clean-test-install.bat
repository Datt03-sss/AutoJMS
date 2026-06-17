@echo off
echo ============================================
echo  Clean Test Install
echo ============================================
echo.

echo Stopping AutoJMS processes...
taskkill /IM AutoJMS.exe /F /T >nul 2>&1
taskkill /IM msedgewebview2.exe /F /T >nul 2>&1

echo.
echo Removing installed folder...
rmdir /s /q "C:\Program Files\AutoJMS" >nul 2>&1

echo.
echo Removing user data folder...
rmdir /s /q "%LOCALAPPDATA%\AutoJMS" >nul 2>&1

echo.
echo Removing old Velopack folder...
rmdir /s /q "%LOCALAPPDATA%\AutoJMS_vely" >nul 2>&1

echo.
echo Cleaning test install completed.
echo.
echo Next steps:
echo   1. Build installer: installer\inno\build-installer.bat
echo   2. Run setup: installer\inno\installer-output\AutoJMS-win-Setup.exe
echo   3. Check installed folder has coreclr.dll, hostfxr.dll, hostpolicy.dll
echo.
pause

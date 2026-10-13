@echo off
setlocal

:: Checks CPU virtualization (VT), Hyper-V conflicts, Nox install/process and ADB connection.
:: Optional: pass extra Nox ADB ports, e.g.  check_android_emulator_env.bat -Ports 62001,62025
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0check_android_emulator_env.ps1" %*
set "RESULT=%ERRORLEVEL%"

echo.
pause
exit /b %RESULT%

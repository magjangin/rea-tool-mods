@echo off
setlocal enabledelayedexpansion

echo ===================================================================
echo   [REA Tool Mods] Setup REA Workflows, Skills, and MCP Server
echo ===================================================================
echo.

set "REA_SOURCE=H:\source\repos\rea-main\rea-main"
if not exist "%REA_SOURCE%" (
    set "REA_SOURCE=H:\source\repos\rea-main"
)

:: 0. Prerequisite checks (fail early with a clear message)
where node >nul 2>&1
if %ERRORLEVEL% neq 0 (
    echo [ERROR] Node.js/npm not found. Install Node.js LTS first, then rerun.
    goto :end
)
if not defined GHIDRA_INSTALL_DIR for /f "tokens=2,*" %%A in ('reg query HKCU\Environment /v GHIDRA_INSTALL_DIR 2^>nul') do set "GHIDRA_INSTALL_DIR=%%B"
if defined GHIDRA_INSTALL_DIR if exist "%GHIDRA_INSTALL_DIR%\ghidraRun.bat" (
    echo [OK] Ghidra: %GHIDRA_INSTALL_DIR%
) else (
    echo [WARN] Ghidra not configured. Run download_analysis_tools.bat for native-binary analysis.
)
if not defined GHIDRA_INSTALL_DIR echo [WARN] Ghidra not configured. Run download_analysis_tools.bat for native-binary analysis.
echo.
echo [1/4] Checking REA source path: %REA_SOURCE%
if not exist "%REA_SOURCE%" (
    echo [INFO] Local repo path not found: %REA_SOURCE%
    echo Using online npm package for REA tools.
)
echo.

:: 1. Copy workflow skills using PowerShell to avoid xcopy prompt hangs
echo [2/4] Synchronizing REA workflow skills to .agents/skills...
set "TARGET_SKILLS=%~dp0..\.agents\skills"

if exist "%REA_SOURCE%\.agents\skills" (
    powershell -NoProfile -ExecutionPolicy Bypass -Command ^
        "try { Copy-Item -Path '%REA_SOURCE%\.agents\skills\*' -Destination '%TARGET_SKILLS%' -Recurse -Force; Write-Host '  - Successfully copied REA workflow skills.' } catch { Write-Host '  - Warning: Failed to copy skills:' $_ }"
) else (
    echo   - Local skills folder not found. Skipping local copy.
)
echo.

:: 2. Ensure rea-agents CLI is installed
echo [3/4] Checking REA CLI and configuring agent MCP integration...
where rea >nul 2>&1
if %ERRORLEVEL% neq 0 (
    echo   - REA CLI not found globally. Installing rea-agents via npm...
    call npm install -g rea-agents
)

:: Run rea setup with automated flags to avoid interactive terminal freezing
echo   - Running REA agent configuration (auto-detect all agents)...
call rea setup --all-detected --yes
set "REA_RC=%ERRORLEVEL%"

if "%REA_RC%"=="0" (
    echo.
    echo ===================================================================
    echo   [SUCCESS] REA workflows and MCP integration configured!
    echo   - Local skills path : %TARGET_SKILLS%
    echo   - REA CLI Version   : installed
    echo ===================================================================
) else (
    echo.
    echo ===================================================================
    echo   [NOTICE] REA setup finished with code %REA_RC%.
    echo   If you need interactive selection, run: rea setup
    echo ===================================================================
)

echo.
:end
pause

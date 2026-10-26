@echo off
setlocal enabledelayedexpansion

echo ===================================================================
echo   [REA Tool Mods] Setup REA Workflows, Skills, and MCP Server
echo ===================================================================
echo.

set "REA_SOURCE=H:\source\repos\rea-hwa"
if not exist "%REA_SOURCE%" (
    set "REA_SOURCE=%~dp0..\..\rea-hwa"
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
if exist "%REA_SOURCE%" (
    echo   - Found local rea-hwa repository: %REA_SOURCE%
) else (
    echo   - [INFO] Local repo path not found. Will use online npm package.
)
echo.

:: 1. Copy workflow skills and slicing tools
echo [2/4] Synchronizing REA workflow skills and tools...
set "TARGET_SKILLS=%~dp0..\.agents\skills"
set "TARGET_TOOLS=%~dp0..\tools"

if exist "%REA_SOURCE%\.agents\skills" (
    powershell -NoProfile -ExecutionPolicy Bypass -Command ^
        "try { Copy-Item -Path '%REA_SOURCE%\.agents\skills\*' -Destination '%TARGET_SKILLS%' -Recurse -Force; Write-Host '  - Successfully copied REA workflow skills.' } catch { Write-Host '  - Warning: Failed to copy skills:' $_ }"
) else (
    echo   - Local skills folder not found. Skipping local copy.
)

if exist "%REA_SOURCE%\tools" (
    powershell -NoProfile -ExecutionPolicy Bypass -Command ^
        "try { Copy-Item -Path '%REA_SOURCE%\tools\*' -Destination '%TARGET_TOOLS%' -Recurse -Force; Write-Host '  - Successfully synchronized tools (pe_slice / il2cpp_asm).' } catch { Write-Host '  - Warning: Failed to sync tools:' $_ }"
)
echo.

:: 2. Ensure rea-agents CLI is linked to rea-hwa (or installed via npm)
echo [3/4] Configuring REA CLI and linking to rea-hwa...
if exist "%REA_SOURCE%" (
    echo   - Checking rea-hwa build runtime...
    if not exist "%REA_SOURCE%\dist" (
        echo   - Compiling rea-hwa TypeScript runtime (npm run compile)...
        pushd "%REA_SOURCE%"
        call npm run compile
        popd
    )
    echo   - Linking rea-hwa globally (npm link)...
    pushd "%REA_SOURCE%"
    call npm link
    popd
) else (
    where rea >nul 2>&1
    if %ERRORLEVEL% neq 0 (
        echo   - REA CLI not found globally. Installing rea-agents via npm...
        call npm install -g rea-agents
    )
)

:: Run rea setup with automated flags to avoid interactive terminal freezing
echo.
echo [4/4] Running REA agent configuration (auto-detect all agents)...
call rea setup --all-detected --yes
set "REA_RC=%ERRORLEVEL%"

if "%REA_RC%"=="0" (
    echo.
    echo ===================================================================
    echo   [SUCCESS] REA (rea-hwa) workflows and MCP integration configured!
    echo   - Local source path : %REA_SOURCE%
    echo   - Local skills path : %TARGET_SKILLS%
    echo   - REA CLI Version   : rea-hwa linked
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

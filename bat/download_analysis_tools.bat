@echo off
setlocal enabledelayedexpansion

echo ===================================================================
echo   [REA Tool Mods] Auto-download Analysis Tools (EXE, DLL, APK, APP)
echo ===================================================================
echo.

set "TOOLS_DIR=%~dp0..\tools"
if not exist "%TOOLS_DIR%" mkdir "%TOOLS_DIR%"

:: 1. .NET DLL / EXE Decompiler (ilspycmd)
echo [1/4] Checking and installing .NET analysis tool (ilspycmd)...
dotnet tool update -g ilspycmd > nul 2>&1
if %ERRORLEVEL% equ 0 (
    echo  - ilspycmd ready: installed or updated globally.
) else (
    echo  - Installing ilspycmd globally: dotnet tool install -g ilspycmd
    dotnet tool install -g ilspycmd
)
echo.

:: 2. Electron / JS App Unpacker (@electron/asar)
echo [2/4] Checking APP / Electron package (.asar) tool...
call npm install -g @electron/asar --silent > nul 2>&1
if %ERRORLEVEL% equ 0 (
    echo  - @electron/asar ready: npx asar command is available.
) else (
    echo  - Warning: npm command failed. Please check Node.js/npm environment.
)
echo.

:: 3. Android APK Decompiler (JADX latest release)
echo [3/4] Downloading and setting up APK decompiler (JADX)...
set "JADX_DIR=%TOOLS_DIR%\jadx"
if exist "%JADX_DIR%\bin\jadx.bat" (
    echo  - JADX is already installed: %JADX_DIR%
) else (
    echo  - Downloading latest JADX release zip from GitHub...
    if not exist "%JADX_DIR%" mkdir "%JADX_DIR%"
    powershell -NoProfile -ExecutionPolicy Bypass -Command ^
        "$ProgressPreference = 'SilentlyContinue';" ^
        "$release = Invoke-RestMethod -Uri 'https://api.github.com/repos/skylot/jadx/releases/latest';" ^
        "$asset = $release.assets | Where-Object { $_.name -like 'jadx-*.zip' -and $_.name -notlike '*gui*' } | Select-Object -First 1;" ^
        "if (-not $asset) { $asset = $release.assets | Where-Object { $_.name -like 'jadx-*.zip' } | Select-Object -First 1; }" ^
        "if ($asset) { Write-Host '  Downloading:' $asset.name; Invoke-WebRequest -Uri $asset.browser_download_url -OutFile '%TOOLS_DIR%\jadx.zip'; Expand-Archive -Path '%TOOLS_DIR%\jadx.zip' -DestinationPath '%JADX_DIR%' -Force; Remove-Item '%TOOLS_DIR%\jadx.zip' -Force; Write-Host '  JADX installed successfully.'; } else { Write-Host '  Could not find JADX release asset.'; }"
)
echo.

:: 4. Android APK Resource Tool (Apktool)
echo [4/4] Setting up APK resource decoder (Apktool)...
set "APKTOOL_DIR=%TOOLS_DIR%\apktool"
if not exist "%APKTOOL_DIR%" mkdir "%APKTOOL_DIR%"
if exist "%APKTOOL_DIR%\apktool.jar" (
    echo  - Apktool is already installed: %APKTOOL_DIR%
) else (
    echo  - Downloading latest Apktool jar...
    powershell -NoProfile -ExecutionPolicy Bypass -Command ^
        "$ProgressPreference = 'SilentlyContinue';" ^
        "try {" ^
        "  $rel = Invoke-RestMethod -Uri 'https://api.github.com/repos/iBotPeaches/Apktool/releases/latest';" ^
        "  $jar = $rel.assets | Where-Object { $_.name -like 'apktool_*.jar' } | Select-Object -First 1;" ^
        "  if ($jar) { Write-Host '  Downloading:' $jar.name; Invoke-WebRequest -Uri $jar.browser_download_url -OutFile '%APKTOOL_DIR%\apktool.jar'; Write-Host '  Apktool downloaded successfully.'; }" ^
        "} catch { Write-Host '  Failed to download Apktool. Check network or GitHub API limits.' }"
    
    :: Generate apktool.bat wrapper
    (
        echo @echo off
        echo java -jar "%%~dp0apktool.jar" %%*
    ) > "%APKTOOL_DIR%\apktool.bat"
)
echo.

echo ===================================================================
echo   [Setup Complete] All analysis tools are ready:
echo   - Tools directory  : %TOOLS_DIR%
echo   - .NET / DLL / EXE : ilspycmd (global)
echo   - APP (Electron)   : asar (global)
echo   - APK Dex Decompile: %TOOLS_DIR%\jadx\bin\jadx.bat
echo   - APK Resource     : %TOOLS_DIR%\apktool\apktool.bat
echo ===================================================================
echo.
pause

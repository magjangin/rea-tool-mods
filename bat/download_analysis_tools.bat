@echo off
setlocal enabledelayedexpansion

echo ===================================================================
echo   [REA Tool Mods] Auto-download Analysis Tools (EXE, DLL, APK, APP, Unity)
echo ===================================================================
echo.

set "TOOLS_DIR=%~dp0..\tools"
if not exist "%TOOLS_DIR%" mkdir "%TOOLS_DIR%"

:: 1. .NET DLL / EXE Decompiler (ilspycmd)
echo [1/6] Checking and installing .NET analysis tool (ilspycmd)...
dotnet tool update -g ilspycmd > nul 2>&1
if %ERRORLEVEL% equ 0 (
    echo  - ilspycmd ready: installed or updated globally.
) else (
    echo  - Installing ilspycmd globally: dotnet tool install -g ilspycmd
    dotnet tool install -g ilspycmd
)
echo.

:: 2. Electron / JS App Unpacker (@electron/asar)
echo [2/6] Checking APP / Electron package (.asar) tool...
call npm install -g @electron/asar --silent > nul 2>&1
if %ERRORLEVEL% equ 0 (
    echo  - @electron/asar ready: npx asar command is available.
) else (
    echo  - Warning: npm command failed. Please check Node.js/npm environment.
)
echo.

:: 3. Android APK Decompiler (JADX latest release)
echo [3/6] Downloading and setting up APK decompiler (JADX)...
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
echo [4/6] Setting up APK resource decoder (Apktool)...
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
)
rem Write the apktool.bat wrapper outside the if-block: a "::" comment plus a nested
rem redirected block inside it made cmd redirect the whole block (download log included).
if exist "%APKTOOL_DIR%\apktool.jar" (
    > "%APKTOOL_DIR%\apktool.bat" echo @echo off
    >> "%APKTOOL_DIR%\apktool.bat" echo java -jar "%%~dp0apktool.jar" %%*
)
echo.

:: 5. Unity IL2CPP metadata dumper (Il2CppDumper)
echo [5/6] Setting up Unity IL2CPP dumper (Il2CppDumper)...
set "IL2CPPDUMPER_DIR=%TOOLS_DIR%\il2cppdumper"
if exist "%IL2CPPDUMPER_DIR%\Il2CppDumper.exe" (
    echo  - Il2CppDumper is already installed: %IL2CPPDUMPER_DIR%
) else (
    echo  - Downloading latest Il2CppDumper release zip from GitHub...
    if not exist "%IL2CPPDUMPER_DIR%" mkdir "%IL2CPPDUMPER_DIR%"
    powershell -NoProfile -ExecutionPolicy Bypass -Command ^
        "$ProgressPreference = 'SilentlyContinue';" ^
        "try {" ^
        "  $rel = Invoke-RestMethod -Uri 'https://api.github.com/repos/Perfare/Il2CppDumper/releases/latest';" ^
        "  $zip = $rel.assets | Where-Object { $_.name -like 'Il2CppDumper-win-*.zip' } | Select-Object -First 1;" ^
        "  if ($zip) { Write-Host '  Downloading:' $zip.name; Invoke-WebRequest -Uri $zip.browser_download_url -OutFile '%TOOLS_DIR%\il2cppdumper.zip'; Expand-Archive -Path '%TOOLS_DIR%\il2cppdumper.zip' -DestinationPath '%IL2CPPDUMPER_DIR%' -Force; Remove-Item '%TOOLS_DIR%\il2cppdumper.zip' -Force; Write-Host '  Il2CppDumper installed successfully.'; } else { Write-Host '  Could not find Il2CppDumper release asset.'; }" ^
        "} catch { Write-Host '  Failed to download Il2CppDumper. Check network or GitHub API limits.' }"
)
:: Il2CppDumper waits for a key press after dumping by default, which hangs agent / CLI runs.
powershell -NoProfile -ExecutionPolicy Bypass -Command ^
    "$q = [char]34;" ^
    "Get-ChildItem -Path '%IL2CPPDUMPER_DIR%' -Filter config.json -Recurse -ErrorAction SilentlyContinue | ForEach-Object {" ^
    "  $text = [IO.File]::ReadAllText($_.FullName);" ^
    "  $new = $text -replace ($q + 'RequireAnyKey' + $q + ':\s*true'), ($q + 'RequireAnyKey' + $q + ': false');" ^
    "  if ($new -ne $text) { [IO.File]::WriteAllText($_.FullName, $new); Write-Host '  - config.json: RequireAnyKey disabled for non-interactive runs.' }" ^
    "}"
echo.

:: 6. Unity IL2CPP alternative dumper (Cpp2IL, newer metadata versions)
echo [6/6] Setting up Unity IL2CPP alternative dumper (Cpp2IL)...
set "CPP2IL_DIR=%TOOLS_DIR%\cpp2il"
if exist "%CPP2IL_DIR%\Cpp2IL.exe" (
    echo  - Cpp2IL is already installed: %CPP2IL_DIR%
) else (
    echo  - Downloading latest Cpp2IL Windows build from GitHub...
    if not exist "%CPP2IL_DIR%" mkdir "%CPP2IL_DIR%"
    powershell -NoProfile -ExecutionPolicy Bypass -Command ^
        "$ProgressPreference = 'SilentlyContinue';" ^
        "try {" ^
        "  $rel = Invoke-RestMethod -Uri 'https://api.github.com/repos/SamboyCoding/Cpp2IL/releases/latest';" ^
        "  $exe = $rel.assets | Where-Object { $_.name -like 'Cpp2IL-*-Windows.exe' } | Select-Object -First 1;" ^
        "  if ($exe) { Write-Host '  Downloading:' $exe.name; Invoke-WebRequest -Uri $exe.browser_download_url -OutFile '%CPP2IL_DIR%\Cpp2IL.exe'; Write-Host '  Cpp2IL installed successfully.'; } else { Write-Host '  Could not find Cpp2IL release asset.'; }" ^
        "} catch { Write-Host '  Failed to download Cpp2IL. Check network or GitHub API limits.' }"
)
echo.

echo ===================================================================
echo   [Setup Complete] All analysis tools are ready:
echo   - Tools directory  : %TOOLS_DIR%
echo   - .NET / DLL / EXE : ilspycmd (global)
echo   - APP (Electron)   : asar (global)
echo   - APK Dex Decompile: %TOOLS_DIR%\jadx\bin\jadx.bat
echo   - APK Resource     : %TOOLS_DIR%\apktool\apktool.bat
echo   - Unity IL2CPP     : %TOOLS_DIR%\il2cppdumper\Il2CppDumper.exe
echo   - Unity IL2CPP alt : %TOOLS_DIR%\cpp2il\Cpp2IL.exe
echo ===================================================================
echo.
pause

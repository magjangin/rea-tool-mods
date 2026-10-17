@echo off
setlocal enabledelayedexpansion

echo ===================================================================
echo   [REA Tool Mods] Auto-download Analysis Tools (EXE, DLL, APK, APP, Unity)
echo ===================================================================
echo.

set "TOOLS_DIR=%~dp0..\tools"
if not exist "%TOOLS_DIR%" mkdir "%TOOLS_DIR%"
for %%I in ("%TOOLS_DIR%") do set "TOOLS_DIR=%%~fI"

:: 1. .NET DLL / EXE Decompiler (ilspycmd)
echo [1/7] Checking and installing .NET analysis tool (ilspycmd)...
dotnet tool update -g ilspycmd > nul 2>&1
if %ERRORLEVEL% equ 0 (
    echo  - ilspycmd ready: installed or updated globally.
) else (
    echo  - Installing ilspycmd globally: dotnet tool install -g ilspycmd
    dotnet tool install -g ilspycmd
)
echo.

:: 2. Electron / JS App Unpacker (@electron/asar)
echo [2/7] Checking APP / Electron package (.asar) tool...
call npm install -g @electron/asar --silent > nul 2>&1
if %ERRORLEVEL% equ 0 (
    echo  - @electron/asar ready: npx asar command is available.
) else (
    echo  - Warning: npm command failed. Please check Node.js/npm environment.
)
echo.

:: 3. Android APK Decompiler (JADX latest release)
echo [3/7] Downloading and setting up APK decompiler (JADX)...
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
echo [4/7] Setting up APK resource decoder (Apktool)...
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
echo [5/7] Setting up Unity IL2CPP dumper (Il2CppDumper)...
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
echo [6/7] Setting up Unity IL2CPP alternative dumper (Cpp2IL)...
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

:: 7. Ghidra native decompiler (needs JDK 21+; REA reads GHIDRA_INSTALL_DIR)
echo [7/7] Setting up Ghidra (native decompiler, JDK 21+ required)...
set "JDK21="
for /f "usebackq delims=" %%J in (`powershell -NoProfile -ExecutionPolicy Bypass -Command "$d = Get-ChildItem 'C:\Program Files\Eclipse Adoptium','C:\Program Files\Java','C:\Program Files\Microsoft' -Directory -Filter 'jdk-*' -ErrorAction SilentlyContinue | Where-Object { $_.Name -match '^jdk-(\d+)' -and [int]$Matches[1] -ge 21 -and (Test-Path (Join-Path $_.FullName 'bin\javac.exe')) } | Sort-Object Name -Descending | Select-Object -First 1; if ($d) { $d.FullName }"`) do set "JDK21=%%J"
if not defined JDK21 (
    echo  - JDK 21+ not found. Installing Temurin 21 via winget...
    winget install --id EclipseAdoptium.Temurin.21.JDK --source winget --silent --accept-package-agreements --accept-source-agreements
    for /f "usebackq delims=" %%J in (`powershell -NoProfile -ExecutionPolicy Bypass -Command "$d = Get-ChildItem 'C:\Program Files\Eclipse Adoptium' -Directory -Filter 'jdk-21*' -ErrorAction SilentlyContinue | Sort-Object Name -Descending | Select-Object -First 1; if ($d) { $d.FullName }"`) do set "JDK21=%%J"
)
if defined JDK21 (
    echo  - JDK 21+ ready: !JDK21!
    setx JAVA_HOME "!JDK21!" > nul
) else (
    echo  - Warning: JDK 21+ install failed. Install Temurin 21 manually, then rerun.
)
set "GHIDRA_DIR=%TOOLS_DIR%\ghidra"
if not defined GHIDRA_INSTALL_DIR for /f "tokens=2,*" %%A in ('reg query HKCU\Environment /v GHIDRA_INSTALL_DIR 2^>nul') do set "GHIDRA_INSTALL_DIR=%%B"
if defined GHIDRA_INSTALL_DIR if exist "%GHIDRA_INSTALL_DIR%\ghidraRun.bat" (
    echo  - Ghidra is already configured: %GHIDRA_INSTALL_DIR%
    goto :ghidra_done
)
for /d %%G in ("%GHIDRA_DIR%\ghidra_*") do if exist "%%G\ghidraRun.bat" set "GHIDRA_FOUND=%%G"
if not defined GHIDRA_FOUND (
    echo  - Downloading latest Ghidra release zip from GitHub ^(about 570 MB^)...
    if not exist "%GHIDRA_DIR%" mkdir "%GHIDRA_DIR%"
    powershell -NoProfile -ExecutionPolicy Bypass -Command ^
        "$ProgressPreference = 'SilentlyContinue';" ^
        "try {" ^
        "  $rel = Invoke-RestMethod -Uri 'https://api.github.com/repos/NationalSecurityAgency/ghidra/releases/latest';" ^
        "  $zip = $rel.assets | Where-Object { $_.name -like 'ghidra_*_PUBLIC_*.zip' } | Select-Object -First 1;" ^
        "  if ($zip) { Write-Host '  Downloading:' $zip.name; $f = '%GHIDRA_DIR%\ghidra.zip'; Invoke-WebRequest -Uri $zip.browser_download_url -OutFile $f; if ((Get-Item $f).Length -ne $zip.size) { Remove-Item $f -Force; throw 'size mismatch' }; Expand-Archive -Path $f -DestinationPath '%GHIDRA_DIR%' -Force; Remove-Item $f -Force; Write-Host '  Ghidra extracted.'; } else { Write-Host '  Could not find Ghidra release asset.'; }" ^
        "} catch { Write-Host '  Failed to download Ghidra:' $_ }"
    for /d %%G in ("%GHIDRA_DIR%\ghidra_*") do if exist "%%G\ghidraRun.bat" set "GHIDRA_FOUND=%%G"
)
if defined GHIDRA_FOUND (
    setx GHIDRA_INSTALL_DIR "!GHIDRA_FOUND!" > nul
    echo  - GHIDRA_INSTALL_DIR set: !GHIDRA_FOUND!
    set "GHIDRA_INSTALL_DIR=!GHIDRA_FOUND!"
    echo    Restart the terminal / Claude app so REA picks up the new variables.
) else (
    echo  - Warning: Ghidra not installed. Check network or GitHub API limits.
)
:ghidra_done
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
echo   - Native (Ghidra)  : !GHIDRA_INSTALL_DIR! ^(JDK 21+^)
echo ===================================================================
echo.
pause

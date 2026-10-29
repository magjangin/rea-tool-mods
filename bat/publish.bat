@echo off
setlocal
cd /d "%~dp0.."
echo [REA Tool Mods] Publishing Release Single-File to Publish\...
dotnet publish ReaToolMods\ReaToolMods.csproj -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o Publish
if %errorlevel% neq 0 (
    echo [FAIL] Publish failed.
    exit /b %errorlevel%
)

REM Native Skia / HarfBuzz / ANGLE libraries copy (for local direct load fallback)
copy /y "ReaToolMods\bin\Release\net10.0\win-x64\libSkiaSharp.dll" "Publish\" >nul 2>&1
copy /y "ReaToolMods\bin\Release\net10.0\win-x64\libHarfBuzzSharp.dll" "Publish\" >nul 2>&1
copy /y "ReaToolMods\bin\Release\net10.0\win-x64\av_libglesv2.dll" "Publish\" >nul 2>&1

echo [OK] Published successfully to %cd%\Publish\ReaToolMods.exe

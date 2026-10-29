@echo off
setlocal
cd /d "%~dp0.."
echo [REA Tool Mods] Publishing Release Single-File to Publish\...
dotnet publish ReaToolMods\ReaToolMods.csproj -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o Publish
if %errorlevel% equ 0 (
    echo [OK] Published successfully to %cd%\Publish\ReaToolMods.exe
) else (
    echo [FAIL] Publish failed.
)

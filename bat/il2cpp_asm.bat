@echo off
setlocal
rem Prints IL2CPP methods as annotated assembly (ARM64/x64; ELF, Mach-O, PE) using dump.cs offsets
rem and script.json names - for libil2cpp.so / UnityFramework that Windows REA cannot open.
rem Also: --check-encryption <Mach-O> reports the iOS FairPlay cryptid. See tools\il2cpp_asm.py --help.
set "PYTHON_SCRIPT=%~dp0..\tools\il2cpp_asm.py"

if not exist "%PYTHON_SCRIPT%" (
    echo [ERROR] %PYTHON_SCRIPT% not found.
    exit /b 1
)

python -I "%PYTHON_SCRIPT%" %*
exit /b %ERRORLEVEL%

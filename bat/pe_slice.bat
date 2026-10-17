@echo off
setlocal
rem Builds a small PE slice (target functions + nearby pages + script.json names as exports)
rem so REA/Ghidra can decompile one IL2CPP method in about a minute instead of timing out
rem on the whole GameAssembly.dll. See tools\pe_slice.py --help.
set "PYTHON_SCRIPT=%~dp0..\tools\pe_slice.py"

if not exist "%PYTHON_SCRIPT%" (
    echo [ERROR] %PYTHON_SCRIPT% not found.
    exit /b 1
)

python -I "%PYTHON_SCRIPT%" %*
exit /b %ERRORLEVEL%

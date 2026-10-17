@echo off
setlocal
set "SCRIPT_DIR=%~dp0.."
set "PYTHON_SCRIPT=%SCRIPT_DIR%\tools\fast_il2cpp_disasm.py"

if not exist "%PYTHON_SCRIPT%" (
    echo [ERROR] %PYTHON_SCRIPT% not found.
    exit /b 1
)

python "%PYTHON_SCRIPT%" %*
exit /b %ERRORLEVEL%

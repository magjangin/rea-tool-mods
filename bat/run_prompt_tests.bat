@echo off
setlocal

rem Generates prompts for all 8 targets x 5 work-folder states, runs the commands they contain
rem against fake fixtures (ilspycmd, bsdtar, Il2CppDumper, Cpp2IL, asar, jadx, apktool)
rem and checks the ViewModel flow. Report + sample prompts go to %%TEMP%%\reatool-prompttest\out.
dotnet run --project "%~dp0..\tests\PromptTests" -- %*
set "RESULT=%ERRORLEVEL%"

echo.
pause
exit /b %RESULT%

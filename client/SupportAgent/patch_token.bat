@echo off
REM Local token patcher (same as server version)
REM Usage: patch_token.bat <token>

setlocal
set SCRIPT_DIR=%~dp0
set EXE=%SCRIPT_DIR%publish\SupportAgent.exe
set TOKEN=%~1

if "%TOKEN%"=="" (
    echo Usage: patch_token.bat ^<token^>
    echo Example: patch_token.bat x1_test_123
    exit /b 1
)

if not exist "%EXE%" (
    echo ERROR: %EXE% not found
    echo Run build-release.bat first
    exit /b 1
)

echo Patching %EXE% with token: %TOKEN%

node "%SCRIPT_DIR%..\server\src\patcher.js" "%EXE%" "%TOKEN%"

endlocal

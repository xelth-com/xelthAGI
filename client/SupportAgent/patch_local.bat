@echo off
REM Local patcher: injects token directly into SupportAgent.exe
REM Usage: patch_local.bat <token>

setlocal

set EXE=publish\SupportAgent.exe
set TOKEN=%~1

if "%TOKEN%"=="" (
    echo Usage: patch_local.bat ^<token^>
    echo Example: patch_local.bat x1_test_token
    exit /b 1
)

if not exist "%EXE%" (
    echo ERROR: %EXE% not found!
    echo Run build-release.bat first
    exit /b 1
)

echo Patching %EXE% with token: %TOKEN%

REM Call PowerShell script to patch
powershell -ExecutionPolicy Bypass -File Scripts\inject_token.ps1 "%EXE%" "%TOKEN%"

if %errorlevel% equ 0 (
    echo SUCCESS: Token injected
) else (
    echo FAILED: Could not patch executable
)

endlocal
pause

@echo off
setlocal enabledelayedexpansion
echo ==================================================
echo   XELTH CI/CD CYCLE (Automated)
echo   Build - Mint - Patch - Run
echo ==================================================

cd /d "%~dp0"

REM 1. BUILD (Direct dotnet command to avoid batch pauses)
echo [1/4] Building Client...
if exist "publish" rmdir /s /q "publish"
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:PublishReadyToRun=true -o publish
if %errorlevel% neq 0 (
    echo BUILD FAILED!
    exit /b 1
)

REM 2. INJECT SLOT
echo [1.5/4] Injecting Token Slot...
powershell -ExecutionPolicy Bypass -File "Scripts\inject_token_slot.ps1"
if %errorlevel% neq 0 (
    echo SLOT INJECTION FAILED!
    exit /b 1
)

REM 3. GENERATE TOKENS (Agent + Console)
echo [2/4] Minting XLT Tokens...
cd ..\..\server
node scripts/generate_dev_token.js > temp_tokens.txt 2>&1
for /f "delims=" %%i in (temp_tokens.txt) do (
    echo %%i | findstr /B "CONSOLE:" >nul
    if !errorlevel! == 0 (
        set "LINE=%%i"
        set "CONSOLE_TOKEN=!LINE:~8!"
    ) else (
        set "TOKEN=%%i"
    )
)
del temp_tokens.txt
cd ..\client\SupportAgent

if "!TOKEN!"=="" (
    echo ERROR: Agent token generation failed.
    exit /b 1
)
echo    Agent Token Generated
echo    Console Token Generated

REM 4. PATCH
echo [3/4] Patching Binary...
node ..\..\server\src\patcher.js "publish\SupportAgent.exe" "!TOKEN!"
if %errorlevel% neq 0 (
    echo PATCH FAILED!
    exit /b 1
)

echo.
echo ========================================
echo    CONSOLE URL (Read-Only):
echo    https://xelth.com/AGI/?token=!CONSOLE_TOKEN!
echo ========================================
echo.

REM 5. RUN
echo [4/4] Running Client Test...
echo    Target: https://xelth.com/AGI
echo.

REM Run client against PROD server
publish\SupportAgent.exe --server "https://xelth.com/AGI" --task "открой калькулятор посчитай 3-1 ответ скопируй, открой notepad и запиши его туда" --auto-approve

echo.
echo [TEST COMPLETE]

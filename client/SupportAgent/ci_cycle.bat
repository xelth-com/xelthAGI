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

REM 3. GENERATE TOKEN
echo [2/4] Minting XLT Token...
cd ..\..\server
for /f "delims=" %%i in ('node scripts/generate_dev_token.js') do set TOKEN=%%i
cd ..\client\SupportAgent

if "!TOKEN!"=="" (
    echo ERROR: Token generation failed.
    exit /b 1
)
echo    Token Generated (Length: !TOKEN_LENGTH!)

REM 4. PATCH
echo [3/4] Patching Binary...
node ..\..\server\src\patcher.js "publish\SupportAgent.exe" "!TOKEN!"
if %errorlevel% neq 0 (
    echo PATCH FAILED!
    exit /b 1
)

REM 5. RUN
echo [4/4] Running Client Test...
echo    Target: https://xelth.com/AGI
echo.

REM Run client against PROD server
publish\SupportAgent.exe --server "https://xelth.com/AGI" --task "XLT Prod Integration Test"

echo.
echo [TEST COMPLETE]

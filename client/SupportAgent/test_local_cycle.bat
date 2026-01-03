@echo off
setlocal
echo ==================================================
echo   XELTH LOCAL DEV CYCLE (XLT Protocol)
echo   Build -> Mint Token -> Patch -> Run
echo ==================================================

REM 1. BUILD
echo [1/4] Building Client...
call build-release.bat >nul
if %errorlevel% neq 0 (
    echo BUILD FAILED!
    exit /b 1
)
echo    OK.

REM 2. GENERATE XLT TOKEN
echo [2/4] Minting Dev Token (AES-256 + HMAC)...
cd ..\..\server
for /f "delims=" %%i in ('node scripts/generate_dev_token.js') do set TOKEN=%%i
cd ..\client\SupportAgent

if "%TOKEN%"=="" (
    echo ERROR: Token generation failed.
    exit /b 1
)
echo    Token: %TOKEN%

REM 3. PATCH BINARY
echo [3/4] Injecting Token into EXE...
node ..\..\server\src\patcher.js "publish\SupportAgent.exe" "%TOKEN%"
if %errorlevel% neq 0 (
    echo PATCH FAILED.
    exit /b 1
)
echo    OK.

REM 4. RUN
echo [4/4] Launching Client...
echo    Server: http://localhost:3232
echo.
echo    [PRESS ANY KEY TO START]
pause >nul

publish\SupportAgent.exe --server "http://localhost:3232" --task "XLT Protocol Verification"

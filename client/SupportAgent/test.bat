@echo off
REM Test Script for Support Agent

echo ========================================
echo    Support Agent Test Script
echo ========================================
echo.

REM Check if server is running
echo [1/3] Checking server...
curl -s http://localhost:5000/health >nul 2>&1
if %errorlevel% neq 0 (
    echo ERROR: Server is not running!
    echo Start server first in WSL: cd server ^&^& npm start
    exit /b 1
)
echo OK: Server is running
echo.

REM Start Notepad
echo [2/3] Starting Notepad...
start notepad
timeout /t 2 /nobreak >nul
echo.

REM Run the client
echo [3/3] Running automation...
echo ----------------------------------------
dotnet run -- --app "Notepad" --task "Type hello world" --server http://localhost:5000
echo ----------------------------------------
echo.

if %errorlevel% equ 0 (
    echo SUCCESS: Test completed!
) else (
    echo ERROR: Test failed with code %errorlevel%
)

pause

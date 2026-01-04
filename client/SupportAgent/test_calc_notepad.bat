@echo off
chcp 65001 >nul
echo ==================================================
echo   XELTH WORKFLOW TEST: CALC -> NOTEPAD
echo ==================================================

echo [1/3] Preparing Environment...
start calc
start notepad
echo    Apps launched. Waiting 2 seconds...
timeout /t 2 >nul

echo [2/3] Checking Client Binary...
if not exist "publish\SupportAgent.exe" (
    echo ERROR: patched client not found! Run ci_cycle.bat first.
    pause
    exit /b 1
)

echo [3/3] Running Agent...
echo    Task: Calculate 88 * 5 and save to Notepad
echo.

publish\SupportAgent.exe --server "https://xelth.com/AGI" --task "Switch to Calculator. Calculate 88 * 5. Press Equals. Get the result. Switch to Notepad. Type 'Test Result: ' and the number."

echo.
echo [TEST COMPLETE]
pause

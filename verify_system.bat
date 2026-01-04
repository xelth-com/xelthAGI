@echo off
echo ════════════════════════════════════════════
echo    XELTH GRAND INTEGRATION TEST v1.4.1
echo ════════════════════════════════════════════
echo.

echo [1/4] Cleaning Environment...
taskkill /F /IM SupportAgent.exe 2>nul
if exist "%LOCALAPPDATA%\XelthAGI\client-id.txt" (
    del /F "%LOCALAPPDATA%\XelthAGI\client-id.txt"
    echo   OK Deleted client-id.txt ^(Testing Identity Sync^)
) else (
    echo   OK Clean state confirmed
)

echo.
echo [2/4] Running CI Cycle ^(Build + Patch + Run^)...
cd client\SupportAgent
call ci_cycle.bat

echo.
echo [3/4] TEST INSTRUCTIONS
echo 1. Look at the output above for the 'CONSOLE URL'.
echo 2. Open that URL in your browser.
echo 3. Check if Status is 'ONLINE' ^(Green^).
echo 4. Check if 'Client ID' matches the one in the URL token.
echo 5. Click the 'Shutdown Agent' button in the dashboard.
echo 6. Verify that the SupportAgent window CLOSES automatically.
echo.
echo [4/4] WAITING FOR RESULT...
echo If the agent window closes after you click the button -^> SUCCESS.
echo.
pause

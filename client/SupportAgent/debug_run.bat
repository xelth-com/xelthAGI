@echo off
echo [DEBUG] Launching SupportAgent with logging...
echo [DEBUG] Time: %TIME%

cd publish
SupportAgent.exe --server "https://xelth.com/AGI" --task "Debug Run" > ..\client_debug.log 2>&1

if %errorlevel% neq 0 (
    echo [ERROR] Process exited with code %errorlevel%
) else (
    echo [SUCCESS] Process finished normally
)

echo.
echo === LOG OUTPUT ===
type ..\client_debug.log
pause

@echo off
REM Example run script for remote client
REM This will be copied to publish folder

echo Starting Support Agent...
echo.

REM Example: Notepad automation
SupportAgent.exe --app "Notepad" --task "Type hello world"

echo.
echo Press any key to exit...
pause >nul

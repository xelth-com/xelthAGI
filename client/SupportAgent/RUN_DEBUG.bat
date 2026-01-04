@echo off
REM Quick launch for visible debug mode
REM Double-click this file to run the agent in a visible window

cd /d "%~dp0"
powershell -ExecutionPolicy Bypass -File "run_visible_debug.ps1"

@echo off
cd /d "%~dp0"
echo ================================================
echo   v1.6.4 Smart i18n Context Validation Test
echo ================================================
echo.
echo [TEST] Building latest version...
dotnet build
if %errorlevel% neq 0 (
    echo [ERROR] Build failed!
    pause
    exit /b 1
)
echo.
echo [TEST] Running i18n Validation Task...
echo Task: Open Notepad ^-^> Type text ^-^> Click 'Close' (expecting 'Schließen') ^-^> Click 'Don't Save' (expecting 'Nicht speichern')
echo.
echo Expected Smart i18n mappings:
echo   - 'Close' ^-^> 'Schließen' (DE)
echo   - 'Don't Save' ^-^> 'Nicht speichern' (DE)
echo.
dotnet run -- --server "https://xelth.com/AGI" --unsafe --task "Open Notepad. Type 'i18n Test v1.6.4'. Click 'Close'. Click 'Don't Save'."
echo.
echo [TEST] Validation complete!
pause

@echo off
REM Build standalone executable for remote deployment

echo ========================================
echo   Building Support Agent - Release (Trimmed)
echo ========================================
echo.

REM Clean previous builds
echo [1/3] Cleaning previous builds...
if exist "bin\Release" rmdir /s /q "bin\Release"
if exist "publish" rmdir /s /q "publish"
echo.

REM Build standalone executable with ReadyToRun (crossgen)
echo [2/3] Building standalone executable (R2R)...
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:PublishReadyToRun=true -o publish
echo.

if %errorlevel% neq 0 (
    echo ERROR: Build failed!
    pause
    exit /b 1
)

REM Inject token slot placeholder for binary patching
echo [2.5/3] Injecting token slot placeholder...
powershell -ExecutionPolicy Bypass -File "Scripts\inject_token_slot.ps1"
echo.

if %errorlevel% neq 0 (
    echo ERROR: Token slot injection failed!
    pause
    exit /b 1
)

REM Create deployment package
echo [3/3] Creating deployment package...
mkdir publish\config 2>nul

REM Copy run example
copy run-example.bat publish\ >nul 2>&1

REM Create config example
echo # Support Agent Configuration > publish\config\server.txt
echo # Edit this file to point to your server >> publish\config\server.txt
echo. >> publish\config\server.txt
echo # Example for remote server: >> publish\config\server.txt
echo # http://192.168.1.100:5000 >> publish\config\server.txt
echo. >> publish\config\server.txt
echo http://localhost:5000 >> publish\config\server.txt

REM Create usage instructions
echo ========================================== > publish\README.txt
echo   Support Agent - Remote Client >> publish\README.txt
echo ========================================== >> publish\README.txt
echo. >> publish\README.txt
echo SETUP: >> publish\README.txt
echo 1. Edit config\server.txt and enter your server URL >> publish\README.txt
echo    Example: http://192.168.1.100:5000 >> publish\README.txt
echo. >> publish\README.txt
echo USAGE: >> publish\README.txt
echo SupportAgent.exe --app "Notepad" --task "Type hello world" >> publish\README.txt
echo. >> publish\README.txt
echo Or use the default server from config: >> publish\README.txt
echo SupportAgent.exe --app "Notepad" --task "Type hello world" >> publish\README.txt
echo. >> publish\README.txt
echo EXAMPLES: >> publish\README.txt
echo - Notepad automation: >> publish\README.txt
echo   SupportAgent.exe --app "Notepad" --task "Type hello world" >> publish\README.txt
echo. >> publish\README.txt
echo - Calculator: >> publish\README.txt
echo   SupportAgent.exe --app "Calculator" --task "Calculate 5 + 3" >> publish\README.txt
echo. >> publish\README.txt

echo.
echo ========================================
echo ✓ Build completed successfully!
echo ========================================
echo.
echo Standalone executable: publish\SupportAgent.exe
echo Size:
dir publish\SupportAgent.exe | find "SupportAgent.exe"
echo.
echo Deployment package ready in: publish\
echo.
echo To deploy to remote machine:
echo 1. Copy the entire 'publish' folder
echo 2. Edit config\server.txt with your server URL
echo 3. Run SupportAgent.exe
echo.

pause

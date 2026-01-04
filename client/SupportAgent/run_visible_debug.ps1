# Set encoding to UTF-8 to fix strange characters
$OutputEncoding = [Console]::InputEncoding = [Console]::OutputEncoding = [System.Text.Encoding]::UTF8

Write-Host "============================================" -ForegroundColor Cyan
Write-Host "   XELTH VISIBLE DEBUG MODE" -ForegroundColor Cyan
Write-Host "============================================" -ForegroundColor Cyan
Write-Host ""

$LogFile = "..\..\debug_output.txt"
$ExePath = ".\publish\SupportAgent.exe"

if (-not (Test-Path $ExePath)) {
    Write-Host "ERROR: SupportAgent.exe not found in publish folder!" -ForegroundColor Red
    Write-Host "Please run ci_cycle.bat first."
    Pause
    exit
}

Write-Host "Launching Agent..." -ForegroundColor Yellow
Write-Host "Server: https://xelth.com/AGI" -ForegroundColor Green
Write-Host "Log: $LogFile" -ForegroundColor Green
Write-Host ""

# Run agent and split output to console and file
# --auto-approve: Skip security prompts for testing
& $ExePath --server "https://xelth.com/AGI" --task "Open Calculator and type 123" --auto-approve | Tee-Object -FilePath $LogFile

Write-Host ""
Write-Host "============================================" -ForegroundColor Green
Write-Host "Execution Finished."
Write-Host "Review debug_output.txt if you missed anything."
Write-Host "============================================" -ForegroundColor Green
Pause

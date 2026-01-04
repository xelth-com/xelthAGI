# XelthAGI System Verification Suite (v1.4.1)
$ErrorActionPreference = "Stop"
$OutputEncoding = [Console]::InputEncoding = [Console]::OutputEncoding = [System.Text.Encoding]::UTF8

Write-Host "╔════════════════════════════════════════════╗" -ForegroundColor Cyan
Write-Host "║   XELTH GRAND INTEGRATION TEST             ║" -ForegroundColor Cyan
Write-Host "╚════════════════════════════════════════════╝" -ForegroundColor Cyan
Write-Host ""

# 1. CLEANUP
Write-Host "[1/4] Cleaning Environment..." -ForegroundColor Yellow
Stop-Process -Name "SupportAgent" -ErrorAction SilentlyContinue
$IdFile = "$env:LOCALAPPDATA\XelthAGI\client-id.txt"
if (Test-Path $IdFile) {
    Remove-Item $IdFile -Force
    Write-Host "  ✅ Deleted old client-id.txt (Testing Identity Sync)" -ForegroundColor Green
} else {
    Write-Host "  ✅ Clean state confirmed" -ForegroundColor Green
}

# 2. EXECUTE CYCLE
Write-Host "`n[2/4] Running CI Cycle (Build + Patch + Run)..." -ForegroundColor Yellow
# We run ci_cycle.bat and capture output to find the Token URL
$proc = Start-Process -FilePath "cmd.exe" -ArgumentList "/c client\SupportAgent\ci_cycle.bat" -PassThru -NoNewWindow -Wait

# 3. INSTRUCTIONS
Write-Host "`n[3/4] TEST INSTRUCTIONS" -ForegroundColor Yellow
Write-Host "1. Look at the output above for the 'CONSOLE URL'."
Write-Host "2. Open that URL in your browser."
Write-Host "3. Check if Status is 'ONLINE' (Green)."
Write-Host "4. Check if 'Client ID' matches the one in the URL token."
Write-Host "5. Click the 'Shutdown Agent' button in the dashboard."
Write-Host "6. Verify that the SupportAgent window CLOSES automatically."

Write-Host "`n[4/4] WAITING FOR RESULT..." -ForegroundColor Yellow
Write-Host "If the agent window closes after you click the button -> SUCCESS."
Write-Host "Press Enter to exit this verifier."
Read-Host

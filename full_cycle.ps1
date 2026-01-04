# Full CI cycle with ASCII-safe headers
Write-Host "=== XELTH CI CYCLE ===" -ForegroundColor Cyan

# 1. Inject token slot
Write-Host "[1/4] Injecting token slot..."
Set-Location 'C:\Users\Dmytro\xelthAGI\client\SupportAgent'
$injectResult = powershell -ExecutionPolicy Bypass -File 'Scripts\inject_token_slot.ps1'
Write-Host $injectResult

# 2. Generate token
Write-Host "[2/4] Generating XLT token..."
Set-Location 'C:\Users\Dmytro\xelthAGI\server'
$token = Invoke-Expression 'node scripts/generate_dev_token.js'
Write-Host "Token: $($token.Substring(0, 20))..."

# 3. Patch binary
Write-Host "[3/4] Patching binary..."
Set-Location 'C:\Users\Dmytro\xelthAGI\server'
Invoke-Expression "node src/patcher.js 'C:\Users\Dmytro\xelthAGI\client\SupportAgent\publish\SupportAgent.exe' '$token'"

# 4. Run client
Write-Host "[4/4] Running client..."
Set-Location 'C:\Users\Dmytro\xelthAGI\client\SupportAgent'
$proc = Start-Process 'publish\SupportAgent.exe' -ArgumentList '--server http://xelth.com/AGI --task ASCII Test' -PassThru -NoNewWindow
Start-Sleep 15

Write-Host "Done. Check output above."

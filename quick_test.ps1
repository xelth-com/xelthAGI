# Quick test: generate token, patch, run
Write-Host "=== QUICK XLT TEST ===" -ForegroundColor Cyan

# Generate token on server (via SSH)
$tokenResult = ssh -o StrictHostKeyChecking=no antigravity "cd /var/www/xelthAGI/server && node scripts/generate_dev_token.js"
$token = $tokenResult.Trim()
Write-Host "Token: $($token.Substring(0, 40))..."

# Patch binary
Set-Location 'C:\Users\Dmytro\xelthAGI\server'
$patchResult = node -e "const p=require('./src/patcher.js'); const b=p.patchExe('C:/Users/Dmytro/xelthAGI/client/SupportAgent/publish/SupportAgent.exe', '$token'); require('fs').writeFileSync('C:/Users/Dmytro/xelthAGI/client/SupportAgent/publish/SupportAgent.exe', b); console.log('PATCHED')"
Write-Host $patchResult

# Run client
Set-Location 'C:\Users\Dmytro\xelthAGI\client\SupportAgent'
$proc = Start-Process 'publish\SupportAgent.exe' -ArgumentList '--server http://xelth.com/AGI --task Quick Test' -PassThru -NoNewWindow
Write-Host "Client running... waiting 10s"
Start-Sleep 10
Write-Host "Done"

# Generate token and run client
Set-Location 'C:\Users\Dmytro\xelthAGI\server'
$token = Invoke-Expression 'node scripts/generate_dev_token.js'
Write-Host "Token generated: $($token.Substring(0, 30))..."

# Patch binary
Set-Location 'C:\Users\Dmytro\xelthAGI\server'
Invoke-Expression "node src/patcher.js 'C:\Users\Dmytro\xelthAGI\client\SupportAgent\publish\SupportAgent.exe' '$token'"

# Run client
Set-Location 'C:\Users\Dmytro\xelthAGI\client\SupportAgent'
$proc = Start-Process 'publish\SupportAgent.exe' -ArgumentList '--server http://xelth.com/AGI --task Final XLT Test' -PassThru -NoNewWindow
Write-Host "Client started. Waiting 15s..."
Start-Sleep 15
Write-Host "Done."

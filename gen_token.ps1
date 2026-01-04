Set-Location 'C:\Users\Dmytro\xelthAGI\server'
$t = Invoke-Expression 'node scripts/generate_dev_token.js'
Write-Host $t

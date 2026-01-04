$proxy = [System.Net.WebRequest]::GetSystemWebProxy()
$uri = New-Object System.Uri('http://xelth.com')
$proxyUri = $proxy.GetProxy($uri)
Write-Host "Proxy for xelth.com: $proxyUri"
Write-Host "Is bypassed: $($proxy.IsBypassed($uri))"

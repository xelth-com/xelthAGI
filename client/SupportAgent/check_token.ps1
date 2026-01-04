$bytes = [System.IO.File]::ReadAllBytes("publish\SupportAgent.exe")
$lastKB = $bytes[($bytes.Length - 1100)..($bytes.Length - 1)]
$text = [System.Text.Encoding]::Unicode.GetString($lastKB)
Write-Host "Last 550 chars from binary:"
Write-Host $text.Substring(0, [Math]::Min(550, $text.Length))

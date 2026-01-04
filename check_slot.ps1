$b = [System.IO.File]::ReadAllBytes('C:\Users\Dmytro\xelthAGI\client\SupportAgent\publish\SupportAgent.exe')
$l = $b.Length
Write-Host "File size: $l"
$slot = $b[($l-1030)..($l-1)]
$txt = [System.Text.Encoding]::Unicode.GetString($slot)
Write-Host "Slot region (first 80 chars):"
Write-Host $txt.Substring(0, 80)

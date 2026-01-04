# Check token read from binary
Add-Type -AssemblyName System.Runtime.WindowsRuntime
$bytes = [System.IO.File]::ReadAllBytes('C:\Users\Dmytro\xelthAGI\client\SupportAgent\publish\SupportAgent.exe')
$l = $bytes.Length
Write-Host "File size: $l"
$slot = $bytes[($l-1030)..($l-1)]
$txt = [System.Text.Encoding]::Unicode.GetString($slot)
Write-Host "Token (trimmed):"
Write-Host $txt.Trim()

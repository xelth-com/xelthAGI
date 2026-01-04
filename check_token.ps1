$b = [System.IO.File]::ReadAllBytes('C:\Users\Dmytro\xelthAGI\client\SupportAgent\publish\SupportAgent.exe')
$l = $b.Length
$s = $b[($l-200)..($l-1)]
$t = [System.Text.Encoding]::Unicode.GetString($s)
Write-Host "Token region:"
Write-Host $t

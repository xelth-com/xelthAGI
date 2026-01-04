# Simple connection test
$proc = Start-Process 'C:\Users\Dmytro\xelthAGI\client\SupportAgent\publish\SupportAgent.exe' -ArgumentList '--server http://xelth.com/AGI --task test' -PassThru -NoNewWindow
Start-Sleep 8
Write-Host "Client exited: $($proc.HasExited)"

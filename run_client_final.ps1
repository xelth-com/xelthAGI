$p = Start-Process 'C:\Users\Dmytro\xelthAGI\client\SupportAgent\publish\SupportAgent.exe' -ArgumentList '--server https://xelth.com/AGI --task XLT TLS Test' -PassThru -NoNewWindow
Start-Sleep 10
Write-Host "Process exited: $($p.HasExited)"

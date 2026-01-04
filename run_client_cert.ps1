$p = Start-Process 'C:\Users\Dmytro\xelthAGI\client\SupportAgent\publish\SupportAgent.exe' -ArgumentList '--server http://xelth.com/AGI --task XLT HTTP Test' -PassThru -NoNewWindow
Start-Sleep 12
Write-Host "Process exited: $($p.HasExited)"

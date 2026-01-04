$p = Start-Process 'C:\Users\Dmytro\xelthAGI\client\SupportAgent\publish\SupportAgent.exe' -ArgumentList '--server https://xelth.com/AGI --task XLT Integration Test' -PassThru -NoNewWindow
Start-Sleep 8
Write-Host "Process exited: $($p.HasExited)"
Get-Process SupportAgent -ErrorAction SilentlyContinue | Select-Object Id, ProcessName

# Test Script for Support Agent
# Run this from PowerShell to test the client

param(
    [string]$App = "Notepad",
    [string]$Task = "Type hello world",
    [string]$Server = "http://localhost:5000"
)

Write-Host "╔════════════════════════════════════════════╗" -ForegroundColor Cyan
Write-Host "║   Support Agent Test Script              ║" -ForegroundColor Cyan
Write-Host "╚════════════════════════════════════════════╝" -ForegroundColor Cyan
Write-Host ""

# 1. Check if server is running
Write-Host "[1/4] Checking server..." -ForegroundColor Yellow
try {
    $response = Invoke-WebRequest -Uri "$Server/health" -TimeoutSec 2 -ErrorAction Stop
    $health = $response.Content | ConvertFrom-Json
    Write-Host "✓ Server is running: $($health.llm_provider) - $($health.model)" -ForegroundColor Green
} catch {
    Write-Host "✗ Server is not running at $Server" -ForegroundColor Red
    Write-Host "  Start server first: cd server && npm start" -ForegroundColor Yellow
    exit 1
}

# 2. Start the app
Write-Host "`n[2/4] Starting $App..." -ForegroundColor Yellow
if ($App -eq "Notepad") {
    Start-Process notepad
    Start-Sleep -Seconds 1
    Write-Host "✓ Notepad started" -ForegroundColor Green
}

# 3. Build the client
Write-Host "`n[3/4] Building client..." -ForegroundColor Yellow
$buildOutput = dotnet build 2>&1
if ($LASTEXITCODE -eq 0) {
    Write-Host "✓ Build successful" -ForegroundColor Green
} else {
    Write-Host "✗ Build failed:" -ForegroundColor Red
    Write-Host $buildOutput
    exit 1
}

# 4. Run the client
Write-Host "`n[4/4] Running automation task..." -ForegroundColor Yellow
Write-Host "App: $App" -ForegroundColor Cyan
Write-Host "Task: $Task" -ForegroundColor Cyan
Write-Host "Server: $Server" -ForegroundColor Cyan
Write-Host "`n" + ("─" * 50) + "`n" -ForegroundColor Gray

# Capture output and display
$output = dotnet run -- --app $App --task $Task --server $Server 2>&1

# Display output
$output | ForEach-Object {
    $line = $_.ToString()
    if ($line -match "✓|SUCCESS|completed") {
        Write-Host $line -ForegroundColor Green
    } elseif ($line -match "✗|ERROR|Failed") {
        Write-Host $line -ForegroundColor Red
    } elseif ($line -match "Step|Action") {
        Write-Host $line -ForegroundColor Cyan
    } else {
        Write-Host $line
    }
}

Write-Host "`n" + ("─" * 50) -ForegroundColor Gray

# Check result
if ($LASTEXITCODE -eq 0) {
    Write-Host "`n✓ Test completed successfully!" -ForegroundColor Green
} else {
    Write-Host "`n✗ Test failed with exit code: $LASTEXITCODE" -ForegroundColor Red
}

# Save output to log
$logFile = "test-output-$(Get-Date -Format 'yyyy-MM-dd-HHmmss').log"
$output | Out-File -FilePath $logFile -Encoding UTF8
Write-Host "`nOutput saved to: $logFile" -ForegroundColor Gray

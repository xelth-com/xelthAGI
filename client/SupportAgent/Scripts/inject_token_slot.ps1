<#
.SYNOPSIS
    Injects a token slot placeholder into a compiled .NET executable.
.DESCRIPTION
    This script appends a placeholder string to the executable that can later
    be replaced by the server for token authentication.
.PARAMETER InputPath
    Path to the input executable
.PARAMETER OutputPath
    Path for the output executable
.PARAMETER Placeholder
    The placeholder string to inject (default: XELTH_TOKEN_SLOT_...)
#>
param(
    [string]$InputPath = "",
    [string]$OutputPath = "",
    [string]$Placeholder = "XELTH_TOKEN_SLOT_00000000000000000000000000000000000000000000000"
)

# Get script directory and use default paths if not specified
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
if (-not $InputPath) { $InputPath = Join-Path $scriptDir "..\publish\SupportAgent.exe" }
if (-not $OutputPath) { $OutputPath = Join-Path $scriptDir "..\publish\SupportAgent.exe" }

# Convert paths to full Windows format
$InputPath = [System.IO.Path]::GetFullPath($InputPath)
$OutputPath = [System.IO.Path]::GetFullPath($OutputPath)

Write-Host "Injecting token slot into executable..." -ForegroundColor Cyan
Write-Host "  Input:  $InputPath"
Write-Host "  Output: $OutputPath"
Write-Host "  Placeholder length: $($Placeholder.Length) chars"

# Convert to UTF-16LE bytes
$bytes = [System.Text.Encoding]::Unicode.GetBytes($Placeholder)
Write-Host "  Placeholder bytes: $($bytes.Length) bytes"

# Read the executable
$exeBytes = [System.IO.File]::ReadAllBytes($InputPath)
Write-Host "  Input size: $($exeBytes.Length) bytes"

# Check for MZ signature
if ($exeBytes[0] -ne 0x4D -or $exeBytes[1] -ne 0x5A) {
    Write-Error "Not a valid PE executable (missing MZ header)"
    exit 1
}

# Append the placeholder bytes to the end of the file
$outputBytes = $exeBytes + $bytes

# Write the output
[System.IO.File]::WriteAllBytes($OutputPath, $outputBytes)
Write-Host "[OK] Token slot injected successfully" -ForegroundColor Green
Write-Host "  Output size: $($outputBytes.Length) bytes"

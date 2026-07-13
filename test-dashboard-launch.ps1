#!/usr/bin/env pwsh
# Test if the tray can successfully launch the dashboard with the new path resolution.

$ErrorActionPreference = 'Stop'

Write-Host "Testing dashboard path resolution..." -ForegroundColor Cyan

# Verify tray executable exists
$trayPath = 'src\Porthole.Tray\bin\Debug\net8.0-windows10.0.19041.0\Porthole.Tray.exe'

if (-not (Test-Path $trayPath)) {
    Write-Host "ERROR: Tray executable not found at $trayPath" -ForegroundColor Red
    exit 1
}

# Verify dashboard executable exists
$dashboardPath = 'src\Porthole.App\bin\Debug\net8.0-windows10.0.19041.0\win-x64\Porthole.App.exe'

if (-not (Test-Path $dashboardPath)) {
    Write-Host "ERROR: Dashboard executable not found at $dashboardPath" -ForegroundColor Red
    exit 1
}

Write-Host "SUCCESS: Tray executable found: $trayPath" -ForegroundColor Green
Write-Host "SUCCESS: Dashboard executable found: $dashboardPath" -ForegroundColor Green

Write-Host "Starting tray process..." -ForegroundColor Cyan

# Start the tray process
$trayProcess = Start-Process $trayPath -PassThru

# Wait for tray to initialize and launch the dashboard
Write-Host "Waiting for dashboard to launch (5 seconds)..." -ForegroundColor Yellow
Start-Sleep -Seconds 5

# Check if dashboard launched
$dashboardProcess = Get-Process Porthole.App -ErrorAction SilentlyContinue

if ($dashboardProcess) {
    Write-Host "SUCCESS: Dashboard launched successfully!" -ForegroundColor Green
    Write-Host "Dashboard PID: $($dashboardProcess.Id)" -ForegroundColor Green
    
    # Clean up
    Stop-Process -InputObject $dashboardProcess -Force -ErrorAction SilentlyContinue
    Write-Host "Dashboard process stopped." -ForegroundColor Cyan
} else {
    Write-Host "WARNING: Dashboard did not launch" -ForegroundColor Yellow
}

# Stop tray
Stop-Process -InputObject $trayProcess -Force -ErrorAction SilentlyContinue
Write-Host "Tray process stopped." -ForegroundColor Cyan

Write-Host "Test completed." -ForegroundColor Green

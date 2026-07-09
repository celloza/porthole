<#
.SYNOPSIS
    Builds the Porthole installer and launches it inside Windows Sandbox for manual testing.

.DESCRIPTION
    Optionally builds the x64 Release MSI, then spins up a Windows Sandbox instance
    with the installer folder mapped in and a startup script that copies the MSI to
    the desktop and launches it for interactive installation.

.PARAMETER SkipBuild
    Skip the MSI build step and use the most recently built installer.

.PARAMETER Version
    Product version to pass to the MSI build (default: 0.0.0-test).
    Must be numeric only (e.g., '0.0.5', not '0.0.5-alpha').
    WiX/Windows Installer requires major.minor.build format with no pre-release labels.

.EXAMPLE
    .\Test-Sandbox.ps1
    .\Test-Sandbox.ps1 -SkipBuild
    .\Test-Sandbox.ps1 -Version 0.0.5
#>
[CmdletBinding()]
param(
    [switch]$SkipBuild,
    [string]$Version = '0.0.0-test'
)

$ErrorActionPreference = 'Stop'

$RepoRoot     = Split-Path $PSScriptRoot -Parent
$InstallerDir = Join-Path $RepoRoot 'src\Porthole.Installer'
$OutputDir    = Join-Path $InstallerDir 'bin\x64\Release'

# ── 1. Build ─────────────────────────────────────────────────────────────────
if (-not $SkipBuild) {
    Write-Host "Building installer (Version=$Version)..." -ForegroundColor Cyan
    & dotnet build "$InstallerDir\Porthole.Installer.wixproj" `
        -c Release `
        -p:Platform=x64 `
        -p:ProductVersion=$Version `
        --nologo
    if ($LASTEXITCODE -ne 0) { throw "Installer build failed." }
} else {
    Write-Host "Skipping build (-SkipBuild specified)." -ForegroundColor Yellow
}

# ── 2. Locate MSI ────────────────────────────────────────────────────────────
$msi = Get-ChildItem $OutputDir -Filter '*.msi' -ErrorAction SilentlyContinue |
       Sort-Object LastWriteTime -Descending |
       Select-Object -First 1

if (-not $msi) {
    throw "No MSI found under '$OutputDir'. Run without -SkipBuild first."
}
Write-Host "Using installer: $($msi.FullName)" -ForegroundColor Green

# ── 3. Stage startup script alongside the MSI ────────────────────────────────
# Windows Sandbox maps HostFolder as read-only, so the startup cmd just copies
# the MSI to a writable location and launches it for interactive installation.
$startupScript = Join-Path $OutputDir 'SandboxStart.cmd'
$msiName = $msi.Name

@"
@echo off
echo Copying Porthole installer to Desktop...
copy /Y "C:\Users\WDAGUtilityAccount\Desktop\Porthole\$msiName" "%USERPROFILE%\Desktop\$msiName"
echo Launching installer...
start "" msiexec /i "%USERPROFILE%\Desktop\$msiName"
"@ | Set-Content $startupScript -Encoding ASCII

# ── 4. Write .wsb config to a temp file ──────────────────────────────────────
$wsbContent = @"
<Configuration>
  <MappedFolders>
    <MappedFolder>
      <HostFolder>$OutputDir</HostFolder>
      <SandboxFolder>C:\Users\WDAGUtilityAccount\Desktop\Porthole</SandboxFolder>
      <ReadOnly>true</ReadOnly>
    </MappedFolder>
  </MappedFolders>
  <LogonCommand>
    <Command>C:\Users\WDAGUtilityAccount\Desktop\Porthole\SandboxStart.cmd</Command>
  </LogonCommand>
</Configuration>
"@

$wsbPath = Join-Path $env:TEMP 'Porthole-Sandbox.wsb'
$wsbContent | Set-Content $wsbPath -Encoding UTF8
Write-Host "Sandbox config written to: $wsbPath" -ForegroundColor Cyan

# ── 5. Launch Windows Sandbox ─────────────────────────────────────────────────
$sandboxExe = Join-Path $env:SystemRoot 'System32\WindowsSandbox.exe'
if (-not (Test-Path $sandboxExe)) {
    throw "Windows Sandbox is not available. Enable it with:`n  Enable-WindowsOptionalFeature -FeatureName 'Containers-DisposableClientVM' -All -Online"
}

Write-Host "Launching Windows Sandbox..." -ForegroundColor Cyan
Start-Process $sandboxExe $wsbPath

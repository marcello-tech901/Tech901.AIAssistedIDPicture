<#
.SYNOPSIS
    Installs the Tech901 IdPhoto kiosk application on a target machine.

.DESCRIPTION
    Extracts (or copies) the published application to the install directory,
    creates a desktop shortcut, and optionally configures auto-start and Azure credentials.

.PARAMETER SourcePath
    Path to the ZIP package or a folder containing the published application.

.PARAMETER InstallDir
    Installation directory. Defaults to C:\Tech901\IdPhoto.

.PARAMETER AutoStart
    Create a shortcut in the Common Startup folder so the app launches on login.

.PARAMETER ConfigureAzure
    Prompt for Azure Speech and Face API credentials and store them as machine-level environment variables.

.EXAMPLE
    .\Install-Kiosk.ps1 -SourcePath .\Tech901-IdPhoto-1.0.0-win-x64.zip -AutoStart
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$SourcePath,

    [string]$InstallDir = 'C:\Tech901\IdPhoto',

    [switch]$AutoStart,

    [switch]$ConfigureAzure
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# Require elevation
$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = [Security.Principal.WindowsPrincipal]$identity
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Write-Error "This script must be run as Administrator."
    exit 1
}

$exeName = 'Tech901.IdPhoto.App.exe'

Write-Host "========================================" -ForegroundColor Cyan
Write-Host " Tech901 IdPhoto Kiosk Installer" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# ── Stop running instance ──────────────────────────────────────
$running = Get-Process -Name 'Tech901.IdPhoto.App' -ErrorAction SilentlyContinue
if ($running) {
    Write-Host "Stopping running instance..." -ForegroundColor Yellow
    $running | Stop-Process -Force
    Start-Sleep -Seconds 2
    Write-Host "  Stopped." -ForegroundColor Green
}

# ── Resolve source ─────────────────────────────────────────────
$SourcePath = Resolve-Path $SourcePath
$isZip = $SourcePath -like '*.zip'

# ── Install ────────────────────────────────────────────────────
Write-Host "Installing to $InstallDir..." -ForegroundColor Yellow

if (Test-Path $InstallDir) {
    Write-Host "  Removing previous installation..." -ForegroundColor Gray
    Remove-Item $InstallDir -Recurse -Force
}

New-Item -ItemType Directory -Path $InstallDir -Force | Out-Null

if ($isZip) {
    Expand-Archive -Path $SourcePath -DestinationPath $InstallDir -Force
} else {
    Copy-Item -Path "$SourcePath\*" -Destination $InstallDir -Recurse -Force
}

$exePath = Join-Path $InstallDir $exeName
if (-not (Test-Path $exePath)) {
    Write-Error "$exeName not found in the installed files. Verify the source package."
    exit 1
}

Write-Host "  Installed." -ForegroundColor Green

# ── Desktop shortcut ───────────────────────────────────────────
Write-Host "Creating desktop shortcut..." -ForegroundColor Yellow

$desktopPath = [Environment]::GetFolderPath('CommonDesktopDirectory')
$shortcutPath = Join-Path $desktopPath 'Tech901 IdPhoto.lnk'

$shell = New-Object -ComObject WScript.Shell
$shortcut = $shell.CreateShortcut($shortcutPath)
$shortcut.TargetPath = $exePath
$shortcut.WorkingDirectory = $InstallDir
$shortcut.Description = 'Tech901 AI-Assisted ID Photo Kiosk'
$shortcut.Save()
[System.Runtime.InteropServices.Marshal]::ReleaseComObject($shell) | Out-Null

Write-Host "  Shortcut created at $shortcutPath" -ForegroundColor Green

# ── Auto-start ─────────────────────────────────────────────────
if ($AutoStart) {
    Write-Host "Configuring auto-start..." -ForegroundColor Yellow

    $startupPath = [Environment]::GetFolderPath('CommonStartup')
    $startupShortcut = Join-Path $startupPath 'Tech901 IdPhoto.lnk'

    $shell = New-Object -ComObject WScript.Shell
    $shortcut = $shell.CreateShortcut($startupShortcut)
    $shortcut.TargetPath = $exePath
    $shortcut.WorkingDirectory = $InstallDir
    $shortcut.Description = 'Tech901 AI-Assisted ID Photo Kiosk (Auto-Start)'
    $shortcut.Save()
    [System.Runtime.InteropServices.Marshal]::ReleaseComObject($shell) | Out-Null

    Write-Host "  Auto-start shortcut created at $startupShortcut" -ForegroundColor Green
}

# ── Configure Azure credentials (machine-level env vars) ──────
if ($ConfigureAzure) {
    Write-Host ""
    Write-Host "Configuring Azure credentials as machine environment variables..." -ForegroundColor Yellow
    Write-Host "  Leave blank to skip a service." -ForegroundColor Gray

    $speechKey    = Read-Host -Prompt '  Azure Speech Key'
    $speechRegion = Read-Host -Prompt '  Azure Speech Region (e.g. eastus)'
    $faceKey      = Read-Host -Prompt '  Azure Face API Key'
    $faceEndpoint = Read-Host -Prompt '  Azure Face API Endpoint'

    $envVars = @{
        'Azure__Speech__Key'    = $speechKey
        'Azure__Speech__Region' = $speechRegion
        'Azure__Face__Key'      = $faceKey
        'Azure__Face__Endpoint' = $faceEndpoint
    }

    foreach ($kv in $envVars.GetEnumerator()) {
        if ($kv.Value) {
            [Environment]::SetEnvironmentVariable($kv.Key, $kv.Value, 'Machine')
            Write-Host "  $($kv.Key) set." -ForegroundColor Green
        }
    }

    Write-Host "  Environment variables take effect on next app launch." -ForegroundColor Gray
}

Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host " Installation complete!" -ForegroundColor Cyan
Write-Host " Location: $InstallDir" -ForegroundColor Cyan
Write-Host " Launch:   $exePath" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan

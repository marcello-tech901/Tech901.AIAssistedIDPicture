<#
.SYNOPSIS
    Creates a self-signed code-signing certificate for development.

.DESCRIPTION
    Generates an RSA 2048 / SHA256 code-signing certificate and exports it
    as a PFX file to certs/Tech901-IdPhoto-Dev.pfx.

.PARAMETER TrustCert
    Add the certificate to the Trusted Root CA store (suppresses SmartScreen).
    Requires elevation.

.PARAMETER CertPassword
    Password for the PFX file. If omitted, you will be prompted securely.

.EXAMPLE
    .\New-CodeSigningCert.ps1 -TrustCert
#>
[CmdletBinding()]
param(
    [switch]$TrustCert,
    [SecureString]$CertPassword
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

$repoRoot = Split-Path -Parent $PSScriptRoot
$certsDir = Join-Path $repoRoot 'certs'
$pfxPath  = Join-Path $certsDir 'Tech901-IdPhoto-Dev.pfx'

if (-not (Test-Path $certsDir)) {
    New-Item -ItemType Directory -Path $certsDir -Force | Out-Null
}

if (Test-Path $pfxPath) {
    Write-Warning "Certificate already exists at $pfxPath. Delete it first to regenerate."
    exit 0
}

# Prompt for password if not provided
if (-not $CertPassword) {
    $CertPassword = Read-Host -Prompt 'Enter PFX password' -AsSecureString
}

Write-Host "Creating self-signed code-signing certificate..." -ForegroundColor Cyan

$cert = New-SelfSignedCertificate `
    -Type CodeSigningCert `
    -Subject "CN=Tech901 IdPhoto Dev, O=Tech901" `
    -FriendlyName "Tech901 IdPhoto Development Signing" `
    -KeyAlgorithm RSA `
    -KeyLength 2048 `
    -HashAlgorithm SHA256 `
    -CertStoreLocation Cert:\CurrentUser\My `
    -NotAfter (Get-Date).AddYears(3)

Write-Host "Certificate created: $($cert.Thumbprint)" -ForegroundColor Green

# Export to PFX
Export-PfxCertificate -Cert $cert -FilePath $pfxPath -Password $CertPassword | Out-Null
Write-Host "Exported to $pfxPath" -ForegroundColor Green

# Optionally trust the certificate
if ($TrustCert) {
    Write-Host "Adding certificate to Trusted Root CA store..." -ForegroundColor Cyan

    $rootStore = [System.Security.Cryptography.X509Certificates.X509Store]::new(
        [System.Security.Cryptography.X509Certificates.StoreName]::Root,
        [System.Security.Cryptography.X509Certificates.StoreLocation]::LocalMachine
    )
    $rootStore.Open([System.Security.Cryptography.X509Certificates.OpenFlags]::ReadWrite)
    $rootStore.Add($cert)
    $rootStore.Close()

    Write-Host "Certificate trusted on this machine." -ForegroundColor Green
}

Write-Host ""
Write-Host "Thumbprint: $($cert.Thumbprint)" -ForegroundColor Yellow
Write-Host "Use with:   .\infra\publish.ps1 -CertPath '$pfxPath'" -ForegroundColor Yellow

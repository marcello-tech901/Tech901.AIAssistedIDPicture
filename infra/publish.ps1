<#
.SYNOPSIS
    Builds, tests, signs, and packages the Tech901 IdPhoto kiosk application.

.DESCRIPTION
    Six-step publish pipeline:
      1. Clean
      2. Build (Release)
      3. Test (skippable)
      4. Publish (self-contained win-x64)
      5. Sign (Authenticode, skippable)
      6. Package (ZIP)

.PARAMETER Version
    Semantic version for the build (e.g. 1.0.0). Defaults to VersionPrefix in Directory.Build.props.

.PARAMETER CertPath
    Path to a PFX file for code signing.

.PARAMETER CertPassword
    Password for the PFX file. If omitted and CertPath is provided, you will be prompted.

.PARAMETER CertThumbprint
    Thumbprint of a certificate in the Windows certificate store.

.PARAMETER SkipTests
    Skip the test step.

.PARAMETER SkipSign
    Skip the code-signing step.

.EXAMPLE
    .\publish.ps1 -Version 1.0.0 -CertPath .\certs\Tech901-IdPhoto-Dev.pfx
    .\publish.ps1 -Version 1.2.0 -CertThumbprint ABC123 -SkipTests
#>
[CmdletBinding()]
param(
    [string]$Version,
    [string]$CertPath,
    [SecureString]$CertPassword,
    [string]$CertThumbprint,
    [switch]$SkipTests,
    [switch]$SkipSign
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot   = Split-Path -Parent $PSScriptRoot
$appProject = Join-Path $repoRoot 'src\Tech901.IdPhoto.App\Tech901.IdPhoto.App.csproj'
$publishDir = Join-Path $repoRoot 'publish'

# Resolve version
if (-not $Version) {
    [xml]$buildProps = Get-Content (Join-Path $repoRoot 'Directory.Build.props')
    $Version = $buildProps.Project.PropertyGroup.VersionPrefix
    if (-not $Version) {
        Write-Error "No -Version specified and no VersionPrefix found in Directory.Build.props."
        exit 1
    }
}

$zipName = "Tech901-IdPhoto-$Version-win-x64.zip"
$zipPath = Join-Path $repoRoot $zipName

Write-Host "========================================" -ForegroundColor Cyan
Write-Host " Tech901 IdPhoto Publish Pipeline v$Version" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# ── Step 1: Clean ──────────────────────────────────────────────
Write-Host "[1/6] Cleaning..." -ForegroundColor Yellow
dotnet clean (Join-Path $repoRoot 'Tech901.AIAssistedIDPicture.sln') -c Release --nologo -v quiet
if (Test-Path $publishDir) { Remove-Item $publishDir -Recurse -Force }
Write-Host "  Clean complete." -ForegroundColor Green

# ── Step 2: Build ──────────────────────────────────────────────
Write-Host "[2/6] Building Release..." -ForegroundColor Yellow
$buildArgs = @(
    'build'
    (Join-Path $repoRoot 'Tech901.AIAssistedIDPicture.sln')
    '-c', 'Release'
    '--nologo'
)
if ($Version) { $buildArgs += "/p:VersionPrefix=$Version" }
& dotnet @buildArgs
if ($LASTEXITCODE -ne 0) { Write-Error "Build failed."; exit 1 }
Write-Host "  Build complete." -ForegroundColor Green

# ── Step 3: Test ───────────────────────────────────────────────
if ($SkipTests) {
    Write-Host "[3/6] Tests skipped." -ForegroundColor DarkYellow
} else {
    Write-Host "[3/6] Running tests..." -ForegroundColor Yellow
    dotnet test (Join-Path $repoRoot 'Tech901.AIAssistedIDPicture.sln') -c Release --no-build --nologo
    if ($LASTEXITCODE -ne 0) { Write-Error "Tests failed."; exit 1 }
    Write-Host "  Tests passed." -ForegroundColor Green
}

# ── Step 4: Publish ────────────────────────────────────────────
Write-Host "[4/6] Publishing self-contained win-x64..." -ForegroundColor Yellow
$publishArgs = @(
    'publish'
    $appProject
    '-c', 'Release'
    '-r', 'win-x64'
    '--self-contained', 'true'
    '-o', $publishDir
    '--nologo'
)
if ($Version) { $publishArgs += "/p:VersionPrefix=$Version" }
& dotnet @publishArgs
if ($LASTEXITCODE -ne 0) { Write-Error "Publish failed."; exit 1 }
Write-Host "  Published to $publishDir" -ForegroundColor Green

# ── Step 5: Sign ───────────────────────────────────────────────
if ($SkipSign) {
    Write-Host "[5/6] Signing skipped." -ForegroundColor DarkYellow
} else {
    if (-not $CertPath -and -not $CertThumbprint) {
        Write-Host "[5/6] Signing skipped (no certificate provided)." -ForegroundColor DarkYellow
    } else {
        Write-Host "[5/6] Signing assemblies..." -ForegroundColor Yellow

        # Find signtool.exe
        $signtool = Get-ChildItem "C:\Program Files (x86)\Windows Kits\10\bin\*\x64\signtool.exe" -ErrorAction SilentlyContinue |
            Sort-Object { $_.Directory.Name } -Descending |
            Select-Object -First 1
        if (-not $signtool) {
            Write-Error "signtool.exe not found. Install Windows SDK or specify path."
            exit 1
        }

        # Collect Tech901 assemblies to sign
        $assemblies = Get-ChildItem $publishDir -Include 'Tech901.*.exe', 'Tech901.*.dll' -Recurse

        if ($assemblies.Count -eq 0) {
            Write-Warning "No Tech901.* assemblies found to sign."
        } else {
            $signArgs = @('sign', '/fd', 'SHA256', '/tr', 'http://timestamp.digicert.com', '/td', 'SHA256')

            if ($CertPath) {
                $CertPath = Resolve-Path $CertPath
                if (-not $CertPassword) {
                    $CertPassword = Read-Host -Prompt 'Enter PFX password' -AsSecureString
                }
                $plainPassword = [Runtime.InteropServices.Marshal]::PtrToStringAuto(
                    [Runtime.InteropServices.Marshal]::SecureStringToBSTR($CertPassword)
                )
                $signArgs += @('/f', $CertPath, '/p', $plainPassword)
            } elseif ($CertThumbprint) {
                $signArgs += @('/sha1', $CertThumbprint)
            }

            foreach ($asm in $assemblies) {
                Write-Host "  Signing $($asm.Name)..." -ForegroundColor Gray
                & $signtool.FullName @signArgs $asm.FullName 2>&1 | Out-Null
                if ($LASTEXITCODE -ne 0) {
                    Write-Error "Failed to sign $($asm.Name)."
                    exit 1
                }
            }
            Write-Host "  Signed $($assemblies.Count) assemblies." -ForegroundColor Green
        }
    }
}

# ── Step 6: Package ────────────────────────────────────────────
Write-Host "[6/6] Creating ZIP package..." -ForegroundColor Yellow
if (Test-Path $zipPath) { Remove-Item $zipPath -Force }
Compress-Archive -Path "$publishDir\*" -DestinationPath $zipPath -CompressionLevel Optimal
$sizeMB = [math]::Round((Get-Item $zipPath).Length / 1MB, 1)
Write-Host "  Package: $zipPath ($sizeMB MB)" -ForegroundColor Green

Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host " Publish complete: v$Version" -ForegroundColor Cyan
Write-Host " ZIP: $zipName" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan

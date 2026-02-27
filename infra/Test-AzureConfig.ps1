<#
.SYNOPSIS
    Checks whether Azure environment variables are configured for the ID Photo kiosk app.
.EXAMPLE
    .\infra\Test-AzureConfig.ps1
#>

$vars = @(
    @{ Name = 'Azure__Speech__Key';    Section = 'Speech' }
    @{ Name = 'Azure__Speech__Region'; Section = 'Speech' }
    @{ Name = 'Azure__Face__Key';      Section = 'Face'   }
    @{ Name = 'Azure__Face__Endpoint'; Section = 'Face'   }
)

$allGood = $true

foreach ($v in $vars) {
    $val = [Environment]::GetEnvironmentVariable($v.Name, 'Machine')
    $scope = 'Machine'
    if (-not $val) {
        $val = [Environment]::GetEnvironmentVariable($v.Name, 'User')
        $scope = 'User'
    }
    if (-not $val) {
        $val = [Environment]::GetEnvironmentVariable($v.Name, 'Process')
        $scope = 'Process'
    }

    if ($val) {
        $masked = $val.Substring(0, [Math]::Min(4, $val.Length)) + '****'
        Write-Host "[OK]   $($v.Name) = $masked  ($scope)" -ForegroundColor Green
    } else {
        Write-Host "[MISSING] $($v.Name)" -ForegroundColor Red
        $allGood = $false
    }
}

Write-Host ""
if ($allGood) {
    Write-Host "All Azure environment variables are set." -ForegroundColor Green
} else {
    Write-Host "Missing variables. Set them with (elevated):" -ForegroundColor Yellow
    Write-Host '  [Environment]::SetEnvironmentVariable("Azure__Speech__Key",    "<key>",      "Machine")'
    Write-Host '  [Environment]::SetEnvironmentVariable("Azure__Speech__Region", "eastus",     "Machine")'
    Write-Host '  [Environment]::SetEnvironmentVariable("Azure__Face__Key",      "<key>",      "Machine")'
    Write-Host '  [Environment]::SetEnvironmentVariable("Azure__Face__Endpoint", "<endpoint>", "Machine")'
    Write-Host ""
    Write-Host "Then restart the app (or log off/on) for changes to take effect."
}

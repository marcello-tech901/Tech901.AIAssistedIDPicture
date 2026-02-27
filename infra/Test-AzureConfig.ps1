<#
.SYNOPSIS
    Checks whether Azure environment variables are configured for the ID Photo kiosk app.

.DESCRIPTION
    Verifies that the four required Azure Cognitive Services environment variables
    are set on the target machine. These variables provide credentials for the
    Speech Service (TTS/STT) and Face API (face landmark detection).

    Environment variable naming convention:
      Double-underscore (__) is the .NET configuration section separator for
      environment variables. At runtime, .NET's configuration system translates
      Azure__Speech__Key into the hierarchical path Azure:Speech:Key, which maps
      to the AzureSpeechOptions and AzureFaceOptions classes in the Infrastructure
      project's Configuration folder.

    Configuration precedence (last wins):
      1. appsettings.json           — Checked into source control, no secrets.
      2. .NET User Secrets          — Dev-machine only (ID: Tech901.IdPhoto.App).
                                      Populated automatically by infra/deploy.ps1.
      3. Environment Variables      — Used on kiosk machines. Set at the Machine
                                      scope so they survive reboots and user logoffs.

    On a development machine, you typically do NOT need environment variables because
    deploy.ps1 stores credentials in User Secrets. This script is primarily for
    verifying kiosk machine configuration after running Install-Kiosk.ps1.

    Credential masking:
      Values are masked in output (first 4 characters shown, remainder replaced
      with ****) so credentials are not exposed in screenshots or logs.

.NOTES
    Run from an elevated (Administrator) PowerShell prompt if checking Machine-scope
    variables. The script checks Machine → User → Process scope in that order
    and reports which scope the variable was found in.

    To set variables on a kiosk machine, either:
      - Run Install-Kiosk.ps1 -ConfigureAzure (recommended), or
      - Set them manually with [Environment]::SetEnvironmentVariable(name, value, "Machine")

    After setting Machine-scope variables, restart the application (or log off and
    back on) for the new values to take effect in the process environment.

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

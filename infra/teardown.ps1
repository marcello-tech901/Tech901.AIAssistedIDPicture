<#
.SYNOPSIS
    Tears down Azure infrastructure for the Tech901 AI-Assisted ID Picture project.
.PARAMETER Env
    Target environment name (default: dev).
#>

param(
    [string]$Env = 'dev'
)

$ErrorActionPreference = 'Stop'

$resourceGroup = "rg-tech901-idphoto-$Env"
$projectId     = 'Tech901.IdPhoto.App'

try {
    Write-Host "=== Tearing down Tech901 ID Photo infrastructure ($Env) ===" -ForegroundColor Yellow

    # Confirmation prompt
    $confirm = Read-Host "This will DELETE resource group '$resourceGroup' and all its resources. Continue? (y/N)"
    if ($confirm -notin @('y', 'Y', 'yes', 'Yes')) {
        Write-Host "Teardown cancelled." -ForegroundColor Cyan
        exit 0
    }

    # 1. Capture cognitive services account names before deleting the group
    Write-Host "Discovering Cognitive Services accounts in '$resourceGroup'..."
    $accounts = az cognitiveservices account list `
        --resource-group $resourceGroup `
        --output json 2>$null | ConvertFrom-Json

    # 2. Delete resource group
    Write-Host "Deleting resource group '$resourceGroup'..."
    az group delete --name $resourceGroup --yes --no-wait false --output none
    if ($LASTEXITCODE -ne 0) { throw "Failed to delete resource group." }
    Write-Host "Resource group deleted."

    # 3. Purge soft-deleted cognitive services accounts
    if ($accounts) {
        Write-Host "Purging soft-deleted Cognitive Services accounts..."
        foreach ($acct in $accounts) {
            $acctName = $acct.name
            $acctLocation = $acct.location
            Write-Host "  Purging '$acctName' in '$acctLocation'..."
            az cognitiveservices account purge `
                --name $acctName `
                --resource-group $resourceGroup `
                --location $acctLocation `
                --output none 2>$null
        }
        Write-Host "Purge complete."
    }

    # 4. Clear .NET User Secrets
    Write-Host "Clearing .NET User Secrets for project '$projectId'..."
    dotnet user-secrets clear --id $projectId

    Write-Host ""
    Write-Host "=== Teardown complete ===" -ForegroundColor Green
}
catch {
    Write-Host "ERROR: $_" -ForegroundColor Red
    exit 1
}

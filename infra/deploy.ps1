#Requires -Modules Az.Accounts, Az.Resources, Az.CognitiveServices

<#
.SYNOPSIS
    Deploys Azure infrastructure for the Tech901 AI-Assisted ID Picture project.
.PARAMETER Env
    Target environment name (default: dev).
.PARAMETER Location
    Azure region for all resources (default: eastus).
#>

param(
    [string]$Env = 'dev',
    [string]$Location = 'eastus'
)

$ErrorActionPreference = 'Stop'

$resourceGroup = "rg-tech901-idphoto-$Env"
$projectId     = 'Tech901.IdPhoto.App'

try {
    Write-Host "=== Deploying Tech901 ID Photo infrastructure ($Env) ===" -ForegroundColor Cyan

    # 1. Create resource group
    Write-Host "Creating resource group '$resourceGroup' in '$Location'..."
    az group create --name $resourceGroup --location $Location --output none
    if ($LASTEXITCODE -ne 0) { throw "Failed to create resource group." }

    # 2. Deploy Bicep template
    Write-Host "Deploying Bicep template..."
    $deployment = az deployment group create `
        --resource-group $resourceGroup `
        --template-file "$PSScriptRoot/main.bicep" `
        --parameters "$PSScriptRoot/parameters.json" `
        --parameters env=$Env location=$Location `
        --output json | ConvertFrom-Json

    if ($LASTEXITCODE -ne 0) { throw "Bicep deployment failed." }

    # 3. Retrieve outputs
    $speechKey    = $deployment.properties.outputs.speechKey.value
    $speechRegion = $deployment.properties.outputs.speechRegion.value
    $faceKey      = $deployment.properties.outputs.faceKey.value
    $faceEndpoint = $deployment.properties.outputs.faceEndpoint.value

    # 4. Store secrets in .NET User Secrets
    Write-Host "Storing secrets in .NET User Secrets (project: $projectId)..."
    dotnet user-secrets set "Azure:Speech:Key"    "$speechKey"    --id $projectId
    dotnet user-secrets set "Azure:Speech:Region"  "$speechRegion" --id $projectId
    dotnet user-secrets set "Azure:Face:Key"       "$faceKey"      --id $projectId
    dotnet user-secrets set "Azure:Face:Endpoint"  "$faceEndpoint" --id $projectId

    Write-Host ""
    Write-Host "=== Deployment complete ===" -ForegroundColor Green
    Write-Host "Resource Group : $resourceGroup"
    Write-Host "Speech Region  : $speechRegion"
    Write-Host "Face Endpoint  : $faceEndpoint"
    Write-Host "Secrets stored in .NET User Secrets for project '$projectId'."
}
catch {
    Write-Host "ERROR: $_" -ForegroundColor Red
    exit 1
}

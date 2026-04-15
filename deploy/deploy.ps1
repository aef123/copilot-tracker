[CmdletBinding()]
param(
    [string]$ResourceGroupName = "rg-copilot-tracker",
    [string]$ParameterFile = "main.bicepparam"
)

$ErrorActionPreference = "Stop"

Write-Host "Validating Bicep template..." -ForegroundColor Yellow
az deployment group validate `
    --resource-group $ResourceGroupName `
    --template-file "$PSScriptRoot\main.bicep" `
    --parameters "$PSScriptRoot\$ParameterFile"

if ($LASTEXITCODE -ne 0) {
    Write-Error "Validation failed"
    exit 1
}

Write-Host "Deploying infrastructure..." -ForegroundColor Yellow
$result = az deployment group create `
    --resource-group $ResourceGroupName `
    --template-file "$PSScriptRoot\main.bicep" `
    --parameters "$PSScriptRoot\$ParameterFile" `
    --output json | ConvertFrom-Json

Write-Host "`nDeployment complete!" -ForegroundColor Green
Write-Host "App URL: $($result.properties.outputs.appServiceUrl.value)"
Write-Host "Cosmos Endpoint: $($result.properties.outputs.cosmosEndpoint.value)"
Write-Host "UAMI Client ID: $($result.properties.outputs.managedIdentityClientId.value)"

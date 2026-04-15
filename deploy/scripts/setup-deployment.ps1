<#
.SYNOPSIS
    Creates the deployment infrastructure for Copilot Tracker.

.DESCRIPTION
    One-time setup script that creates:
    - Azure resource group
    - Entra app registration for GitHub Actions deployment
    - Contributor role assignment on the resource group
    - OIDC federated credentials for GitHub Actions (main branch + PRs)

    After running, you'll need to set three GitHub repository variables:
    - AZURE_CLIENT_ID
    - AZURE_TENANT_ID
    - AZURE_SUBSCRIPTION_ID

.PARAMETER ResourceGroupName
    Name of the Azure resource group. Default: rg-copilot-tracker

.PARAMETER Location
    Azure region. Default: eastus2

.PARAMETER GitHubRepo
    GitHub repository in owner/repo format. Default: aef123/copilot-tracker

.PARAMETER AppDisplayName
    Display name for the deployment service principal. Default: copilot-tracker-github-deploy

.EXAMPLE
    .\setup-deployment.ps1
    .\setup-deployment.ps1 -ResourceGroupName "my-rg" -GitHubRepo "myorg/my-tracker"
#>

[CmdletBinding()]
param(
    [string]$ResourceGroupName = "rg-copilot-tracker",
    [string]$Location = "eastus2",
    [string]$GitHubRepo = "aef123/copilot-tracker",
    [string]$AppDisplayName = "copilot-tracker-github-deploy"
)

$ErrorActionPreference = "Stop"

Write-Host "`n=== Copilot Tracker: Deployment Infrastructure Setup ===" -ForegroundColor Cyan

# Verify az CLI is logged in
Write-Host "`nVerifying Azure CLI login..." -ForegroundColor Yellow
$account = az account show --output json 2>$null | ConvertFrom-Json
if (-not $account) {
    Write-Error "Not logged into Azure CLI. Run 'az login' first."
    exit 1
}

$subscriptionId = $account.id
$tenantId = $account.tenantId
Write-Host "  Subscription: $($account.name) ($subscriptionId)"
Write-Host "  Tenant: $tenantId"

# Create resource group
Write-Host "`nCreating resource group '$ResourceGroupName' in $Location..." -ForegroundColor Yellow
az group create --name $ResourceGroupName --location $Location --output none
Write-Host "  Done." -ForegroundColor Green

# Check if app registration already exists
Write-Host "`nChecking for existing app registration '$AppDisplayName'..." -ForegroundColor Yellow
$existingApp = az ad app list --display-name $AppDisplayName --output json | ConvertFrom-Json
if ($existingApp -and $existingApp.Count -gt 0) {
    $appId = $existingApp[0].appId
    $appObjectId = $existingApp[0].id
    Write-Host "  Found existing app: $appId" -ForegroundColor Yellow
} else {
    Write-Host "  Creating app registration..." -ForegroundColor Yellow
    $newApp = az ad app create --display-name $AppDisplayName --output json | ConvertFrom-Json
    $appId = $newApp.appId
    $appObjectId = $newApp.id
    Write-Host "  Created: $appId" -ForegroundColor Green
}

# Ensure service principal exists
Write-Host "`nEnsuring service principal exists..." -ForegroundColor Yellow
$sp = az ad sp list --filter "appId eq '$appId'" --output json | ConvertFrom-Json
if (-not $sp -or $sp.Count -eq 0) {
    az ad sp create --id $appId --output none
    Write-Host "  Created service principal." -ForegroundColor Green
} else {
    Write-Host "  Service principal already exists." -ForegroundColor Green
}

# Assign Contributor role on resource group
Write-Host "`nAssigning Contributor role on $ResourceGroupName..." -ForegroundColor Yellow
$roleCheck = az role assignment list `
    --assignee $appId `
    --role "Contributor" `
    --scope "/subscriptions/$subscriptionId/resourceGroups/$ResourceGroupName" `
    --output json | ConvertFrom-Json

if ($roleCheck -and $roleCheck.Count -gt 0) {
    Write-Host "  Role already assigned." -ForegroundColor Green
} else {
    az role assignment create `
        --assignee $appId `
        --role "Contributor" `
        --scope "/subscriptions/$subscriptionId/resourceGroups/$ResourceGroupName" `
        --output none
    Write-Host "  Done." -ForegroundColor Green
}

# Create OIDC federated credentials
Write-Host "`nConfiguring OIDC federated credentials..." -ForegroundColor Yellow

$credentials = @(
    @{
        name     = "github-main-branch"
        subject  = "repo:${GitHubRepo}:ref:refs/heads/main"
        description = "GitHub Actions: push to main"
    },
    @{
        name     = "github-pull-request"
        subject  = "repo:${GitHubRepo}:pull_request"
        description = "GitHub Actions: pull requests"
    }
)

foreach ($cred in $credentials) {
    $existing = az ad app federated-credential list --id $appObjectId --output json | ConvertFrom-Json
    $found = $existing | Where-Object { $_.name -eq $cred.name }

    if ($found) {
        Write-Host "  '$($cred.name)' already exists, skipping." -ForegroundColor Yellow
    } else {
        # Write to temp file to avoid PowerShell JSON quoting issues
        $body = @{
            name        = $cred.name
            issuer      = "https://token.actions.githubusercontent.com"
            subject     = $cred.subject
            description = $cred.description
            audiences   = @("api://AzureADTokenExchange")
        } | ConvertTo-Json -Compress

        $tempFile = [System.IO.Path]::GetTempFileName()
        $body | Set-Content -Path $tempFile -Encoding utf8

        az ad app federated-credential create --id $appObjectId --parameters "@$tempFile" --output none
        Remove-Item $tempFile -Force
        Write-Host "  Created '$($cred.name)'." -ForegroundColor Green
    }
}

# Summary
Write-Host "`n=== Setup Complete ===" -ForegroundColor Cyan
Write-Host ""
Write-Host "Add these as GitHub repository variables" -ForegroundColor Yellow
Write-Host "(Settings > Secrets and variables > Actions > Variables tab):" -ForegroundColor Yellow
Write-Host ""
Write-Host "  AZURE_CLIENT_ID        = $appId"
Write-Host "  AZURE_TENANT_ID        = $tenantId"
Write-Host "  AZURE_SUBSCRIPTION_ID  = $subscriptionId"
Write-Host ""
Write-Host "Deployment SP Object ID: $appObjectId" -ForegroundColor Gray
Write-Host ""

<#
.SYNOPSIS
    Creates the Entra app registration for the Copilot Tracker API.

.DESCRIPTION
    One-time setup script that creates:
    - Entra app registration with an API scope (CopilotTracker.ReadWrite)
    - Service principal so tokens can be issued
    - SPA redirect URIs for the dashboard (localhost dev + production)

    This is the app that validates user tokens at the API/MCP layer.
    The Copilot CLI and Dashboard both authenticate against this app.

.PARAMETER AppDisplayName
    Display name for the app. Default: Copilot Tracker

.PARAMETER AppServiceName
    Name of the App Service (used for production redirect URI). Default: copilot-tracker

.PARAMETER DevPort
    Local dev server port for redirect URI. Default: 5173

.EXAMPLE
    .\setup-app-registration.ps1
    .\setup-app-registration.ps1 -AppDisplayName "My Tracker" -AppServiceName "my-tracker-app"
#>

[CmdletBinding()]
param(
    [string]$AppDisplayName = "Copilot Tracker",
    [string]$AppServiceName = "copilot-tracker",
    [int]$DevPort = 5173
)

$ErrorActionPreference = "Stop"

Write-Host "`n=== Copilot Tracker: App Registration Setup ===" -ForegroundColor Cyan

# Verify az CLI is logged in
Write-Host "`nVerifying Azure CLI login..." -ForegroundColor Yellow
$account = az account show --output json 2>$null | ConvertFrom-Json
if (-not $account) {
    Write-Error "Not logged into Azure CLI. Run 'az login' first."
    exit 1
}

$tenantId = $account.tenantId
Write-Host "  Tenant: $tenantId"

# Check if app already exists
Write-Host "`nChecking for existing app registration '$AppDisplayName'..." -ForegroundColor Yellow
$existingApp = az ad app list --display-name $AppDisplayName --output json | ConvertFrom-Json

# Filter exact match (az ad app list does substring matching)
$exactMatch = $existingApp | Where-Object { $_.displayName -eq $AppDisplayName }

if ($exactMatch) {
    $appId = $exactMatch.appId
    $appObjectId = $exactMatch.id
    Write-Host "  Found existing app: $appId" -ForegroundColor Yellow
    Write-Host "  To recreate, delete it first: az ad app delete --id $appObjectId" -ForegroundColor Gray
} else {
    Write-Host "  Creating app registration..." -ForegroundColor Yellow

    # Create the app with SPA redirect URIs
    $redirectUris = @(
        "http://localhost:$DevPort",
        "http://localhost:$DevPort/auth/callback",
        "https://$AppServiceName.azurewebsites.net",
        "https://$AppServiceName.azurewebsites.net/auth/callback"
    )

    $spaConfig = @{
        redirectUris = $redirectUris
    } | ConvertTo-Json -Compress

    # Build the full app manifest
    $appManifest = @{
        displayName    = $AppDisplayName
        signInAudience = "AzureADMyOrg"
        spa            = @{
            redirectUris = $redirectUris
        }
    } | ConvertTo-Json -Depth 3

    $tempFile = [System.IO.Path]::GetTempFileName()
    $appManifest | Set-Content -Path $tempFile -Encoding utf8

    $newApp = az rest --method POST `
        --uri "https://graph.microsoft.com/v1.0/applications" `
        --headers "Content-Type=application/json" `
        --body "@$tempFile" `
        --output json | ConvertFrom-Json

    Remove-Item $tempFile -Force

    $appId = $newApp.appId
    $appObjectId = $newApp.id
    Write-Host "  Created: $appId (object: $appObjectId)" -ForegroundColor Green
}

# Set the identifier URI (api://<appId>)
Write-Host "`nSetting identifier URI..." -ForegroundColor Yellow
$identifierUri = "api://$appId"

$uriBody = @{
    identifierUris = @($identifierUri)
} | ConvertTo-Json -Compress

$tempFile = [System.IO.Path]::GetTempFileName()
$uriBody | Set-Content -Path $tempFile -Encoding utf8

az rest --method PATCH `
    --uri "https://graph.microsoft.com/v1.0/applications/$appObjectId" `
    --headers "Content-Type=application/json" `
    --body "@$tempFile" `
    --output none 2>$null

Remove-Item $tempFile -Force
Write-Host "  Set to: $identifierUri" -ForegroundColor Green

# Add API scope: CopilotTracker.ReadWrite
Write-Host "`nAdding API scope 'CopilotTracker.ReadWrite'..." -ForegroundColor Yellow

$scopeId = [guid]::NewGuid().ToString()
$apiBody = @{
    api = @{
        oauth2PermissionScopes = @(
            @{
                id                      = $scopeId
                adminConsentDescription = "Allow the application to read and write Copilot Tracker sessions and tasks on behalf of the signed-in user."
                adminConsentDisplayName = "Read and write Copilot Tracker data"
                userConsentDescription  = "Allow Copilot Tracker to manage your sessions and tasks."
                userConsentDisplayName  = "Read and write your Copilot Tracker data"
                isEnabled               = $true
                type                    = "User"
                value                   = "CopilotTracker.ReadWrite"
            }
        )
    }
} | ConvertTo-Json -Depth 4

$tempFile = [System.IO.Path]::GetTempFileName()
$apiBody | Set-Content -Path $tempFile -Encoding utf8

az rest --method PATCH `
    --uri "https://graph.microsoft.com/v1.0/applications/$appObjectId" `
    --headers "Content-Type=application/json" `
    --body "@$tempFile" `
    --output none 2>$null

Remove-Item $tempFile -Force
Write-Host "  Done. Scope ID: $scopeId" -ForegroundColor Green

# Pre-authorize the app to consent to its own scope (for first-party clients)
Write-Host "`nPre-authorizing app for its own scope..." -ForegroundColor Yellow

$preAuthBody = @{
    api = @{
        preAuthorizedApplications = @(
            @{
                appId                = $appId
                delegatedPermissionIds = @($scopeId)
            }
        )
    }
} | ConvertTo-Json -Depth 4

$tempFile = [System.IO.Path]::GetTempFileName()
$preAuthBody | Set-Content -Path $tempFile -Encoding utf8

az rest --method PATCH `
    --uri "https://graph.microsoft.com/v1.0/applications/$appObjectId" `
    --headers "Content-Type=application/json" `
    --body "@$tempFile" `
    --output none 2>$null

Remove-Item $tempFile -Force
Write-Host "  Done." -ForegroundColor Green

# Ensure service principal exists
Write-Host "`nEnsuring service principal exists..." -ForegroundColor Yellow
$sp = az ad sp list --filter "appId eq '$appId'" --output json | ConvertFrom-Json
if (-not $sp -or $sp.Count -eq 0) {
    az ad sp create --id $appId --output none
    Write-Host "  Created service principal." -ForegroundColor Green
} else {
    Write-Host "  Service principal already exists." -ForegroundColor Green
}

# Summary
Write-Host "`n=== App Registration Complete ===" -ForegroundColor Cyan
Write-Host ""
Write-Host "App Registration Details:" -ForegroundColor Yellow
Write-Host "  Display Name    : $AppDisplayName"
Write-Host "  Application ID  : $appId"
Write-Host "  Object ID       : $appObjectId"
Write-Host "  Identifier URI  : $identifierUri"
Write-Host "  Scope           : $identifierUri/CopilotTracker.ReadWrite"
Write-Host "  Tenant          : $tenantId"
Write-Host ""
Write-Host "SPA Redirect URIs:" -ForegroundColor Yellow
Write-Host "  http://localhost:$DevPort"
Write-Host "  http://localhost:$DevPort/auth/callback"
Write-Host "  https://$AppServiceName.azurewebsites.net"
Write-Host "  https://$AppServiceName.azurewebsites.net/auth/callback"
Write-Host ""
Write-Host "For the CLI to get tokens:" -ForegroundColor Yellow
Write-Host "  az account get-access-token --resource $identifierUri --query accessToken -o tsv"
Write-Host ""
Write-Host "Save these values for appsettings.json:" -ForegroundColor Yellow
Write-Host "  AzureAd:TenantId  = $tenantId"
Write-Host "  AzureAd:ClientId  = $appId"
Write-Host "  AzureAd:Audience  = $identifierUri"
Write-Host ""

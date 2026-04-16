---
name: bootstrap
description: "Provision all Azure infrastructure for Copilot Session Tracker. Creates resource group, Entra app registration, Cosmos DB, App Service, and configures GitHub Actions deployment."
argument-hint: "[subscription-id] [resource-group] [region]"
compatibility: "Windows only. Requires Azure CLI (az), PowerShell 7+, and Owner or Contributor + User Access Administrator permissions."
metadata:
  author: Copilot Session Tracker
  version: 2.0.0
  category: setup
---

# Bootstrap: Provision Azure Infrastructure

This skill provisions all Azure infrastructure for the Copilot Session Tracker. It creates everything needed to run the backend: resource group, Entra app registration, Cosmos DB, App Service, managed identity, and GitHub Actions deployment credentials.

**Platform: Windows only.** Requires Azure CLI and PowerShell 7+.

**Permissions needed:**
- Owner or Contributor + User Access Administrator on the Azure subscription
- Permission to create Entra app registrations in your tenant

## Step 0: Gather Parameters (MANDATORY — DO NOT SKIP)

**STOP. You MUST ask the user for each parameter before proceeding. Do NOT guess or use defaults without explicit user confirmation.**

### 0a. Azure Subscription

Run this to show available subscriptions:

```powershell
az account list --query "[].{Name:name, Id:id}" -o table
```

Display the results and ask the user: **"Which subscription should I use? Please provide the name or ID."**

You MUST wait for the user to respond. Store as `$subscriptionId`.

### 0b. Resource Group Name

Ask the user: **"What resource group name should I use?"**

This is required. There is no default. Store as `$rgName`.

### 0c. Azure Region

Ask the user: **"What Azure region? (e.g. eastus2, westus2, centralus)"**

This is required. There is no default. Store as `$region`.

### 0d. App Service Name

Ask the user: **"What App Service name should I use? This becomes the URL: `<name>.azurewebsites.net`. Must be globally unique."**

This is required. There is no default. Store as `$appName`.

### 0e. Cosmos DB Account Name

Ask the user: **"What Cosmos DB account name should I use? Must be globally unique across Azure."**

This is required. There is no default. Store as `$cosmosAccountName`.

### 0f. GitHub Repository

Ask the user: **"What is the GitHub repository for CI/CD? (format: owner/repo)"**

This is required. There is no default. Store as `$githubRepo`.

### Confirmation Gate

Display all collected parameters and ask: **"I'm about to create Azure resources with these settings. This will incur Azure costs. Proceed?"**

```
Subscription:       <subscription-id>
Resource Group:     <rg-name>
Region:             <region>
App Service:        <app-name>.azurewebsites.net
Cosmos DB:          <cosmos-name>
GitHub Repo:        <owner/repo>
```

**Do NOT proceed until the user explicitly confirms.**

## Step 1: Set Subscription and Create Resource Group

```powershell
az account set --subscription $subscriptionId
Write-Output "✅ Subscription set to $subscriptionId"

$rgExists = az group show --name $rgName 2>$null
if (-not $rgExists) {
    az group create --name $rgName --location $region --output none
    Write-Output "✅ Created resource group '$rgName' in '$region'"
} else {
    Write-Output "✅ Resource group '$rgName' already exists"
}
```

## Step 2: Create Entra App Registration

This creates the API app registration with the CopilotTracker.ReadWrite scope, SPA redirect URIs, and pre-authorizes the Azure CLI.

```powershell
$appDisplayName = "Copilot Tracker"
$devPort = 5173

# Check for existing app
$existingApp = az ad app list --display-name $appDisplayName --output json | ConvertFrom-Json
$exactMatch = $existingApp | Where-Object { $_.displayName -eq $appDisplayName }

if ($exactMatch) {
    $appId = $exactMatch.appId
    $appObjectId = $exactMatch.id
    Write-Output "✅ Found existing app registration: $appId"
} else {
    $redirectUris = @(
        "http://localhost:$devPort",
        "http://localhost:$devPort/auth/callback",
        "https://$appName.azurewebsites.net",
        "https://$appName.azurewebsites.net/auth/callback"
    )

    $appManifest = @{
        displayName    = $appDisplayName
        signInAudience = "AzureADMyOrg"
        spa            = @{ redirectUris = $redirectUris }
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
    Write-Output "✅ Created app registration: $appId"
}

# Set identifier URI
$identifierUri = "api://$appId"
$uriBody = @{ identifierUris = @($identifierUri) } | ConvertTo-Json -Compress
$tempFile = [System.IO.Path]::GetTempFileName()
$uriBody | Set-Content -Path $tempFile -Encoding utf8
az rest --method PATCH --uri "https://graph.microsoft.com/v1.0/applications/$appObjectId" --headers "Content-Type=application/json" --body "@$tempFile" --output none 2>$null
Remove-Item $tempFile -Force
Write-Output "✅ Identifier URI: $identifierUri"

# Add API scope
$scopeId = [guid]::NewGuid().ToString()
$apiBody = @{
    api = @{
        oauth2PermissionScopes = @(@{
            id = $scopeId
            adminConsentDescription = "Allow the application to read and write Copilot Tracker sessions and tasks on behalf of the signed-in user."
            adminConsentDisplayName = "Read and write Copilot Tracker data"
            userConsentDescription  = "Allow Copilot Tracker to manage your sessions and tasks."
            userConsentDisplayName  = "Read and write your Copilot Tracker data"
            isEnabled = $true; type = "User"; value = "CopilotTracker.ReadWrite"
        })
    }
} | ConvertTo-Json -Depth 4
$tempFile = [System.IO.Path]::GetTempFileName()
$apiBody | Set-Content -Path $tempFile -Encoding utf8
az rest --method PATCH --uri "https://graph.microsoft.com/v1.0/applications/$appObjectId" --headers "Content-Type=application/json" --body "@$tempFile" --output none 2>$null
Remove-Item $tempFile -Force
Write-Output "✅ API scope: $identifierUri/CopilotTracker.ReadWrite"

# Pre-authorize app + Azure CLI
$azureCliAppId = "04b07795-8ddb-461a-bbee-02f9e1bf7b46"
$preAuthBody = @{
    api = @{
        preAuthorizedApplications = @(
            @{ appId = $appId; delegatedPermissionIds = @($scopeId) },
            @{ appId = $azureCliAppId; delegatedPermissionIds = @($scopeId) }
        )
    }
} | ConvertTo-Json -Depth 4
$tempFile = [System.IO.Path]::GetTempFileName()
$preAuthBody | Set-Content -Path $tempFile -Encoding utf8
az rest --method PATCH --uri "https://graph.microsoft.com/v1.0/applications/$appObjectId" --headers "Content-Type=application/json" --body "@$tempFile" --output none 2>$null
Remove-Item $tempFile -Force
Write-Output "✅ Pre-authorized Azure CLI for scope"

# Ensure service principal
$sp = az ad sp list --filter "appId eq '$appId'" --output json | ConvertFrom-Json
if (-not $sp -or $sp.Count -eq 0) {
    az ad sp create --id $appId --output none
}
Write-Output "✅ Service principal ready"

$tenantId = (az account show --query tenantId -o tsv)
```

## Step 3: Deploy Infrastructure via Bicep

Deploy the Cosmos DB, App Service, Managed Identity, and RBAC assignments.

```powershell
# Create a temporary bicep parameters file with the user's values
$paramsContent = @"
using 'main.bicep'

param appName = '$appName'
param cosmosAccountName = '$cosmosAccountName'
param location = '$region'
param appServicePlanSku = 'F1'
param tenantId = '$tenantId'
param apiClientId = '$appId'
"@

# Find the deploy directory (relative to plugin or repo)
$deployDir = $null
$repoDeployDir = Join-Path $PWD "deploy"
if (Test-Path $repoDeployDir) {
    $deployDir = $repoDeployDir
} else {
    Write-Error "❌ Cannot find deploy/ directory. Run this from the copilot-tracker repo root."
    return
}

$paramsFile = Join-Path $deployDir "bootstrap.bicepparam"
$paramsContent | Set-Content -Path $paramsFile -Encoding utf8

Write-Output "Validating Bicep template..."
az deployment group validate `
    --resource-group $rgName `
    --template-file (Join-Path $deployDir "main.bicep") `
    --parameters $paramsFile `
    --output none

if ($LASTEXITCODE -ne 0) {
    Remove-Item $paramsFile -Force
    Write-Error "❌ Bicep validation failed"
    return
}

Write-Output "Deploying infrastructure (this may take several minutes)..."
$result = az deployment group create `
    --resource-group $rgName `
    --template-file (Join-Path $deployDir "main.bicep") `
    --parameters $paramsFile `
    --output json | ConvertFrom-Json

Remove-Item $paramsFile -Force

if ($LASTEXITCODE -ne 0) {
    Write-Error "❌ Deployment failed"
    return
}

$appUrl = $result.properties.outputs.appServiceUrl.value
$cosmosEndpoint = $result.properties.outputs.cosmosEndpoint.value
$uamiClientId = $result.properties.outputs.managedIdentityClientId.value
Write-Output "✅ Infrastructure deployed"
Write-Output "  App URL:        $appUrl"
Write-Output "  Cosmos Endpoint: $cosmosEndpoint"
Write-Output "  UAMI Client ID: $uamiClientId"
```

## Step 4: Set Up GitHub Actions Deployment

Create a deployment service principal with OIDC federated credentials for GitHub Actions.

```powershell
$deploySpName = "$appName-github-deploy"

$existingDeploySp = az ad app list --display-name $deploySpName --output json | ConvertFrom-Json
if ($existingDeploySp -and $existingDeploySp.Count -gt 0) {
    $deployAppId = $existingDeploySp[0].appId
    $deployAppObjectId = $existingDeploySp[0].id
    Write-Output "✅ Found existing deployment SP: $deployAppId"
} else {
    $newDeployApp = az ad app create --display-name $deploySpName --output json | ConvertFrom-Json
    $deployAppId = $newDeployApp.appId
    $deployAppObjectId = $newDeployApp.id
    Write-Output "✅ Created deployment SP: $deployAppId"
}

# Ensure service principal
$deploySp = az ad sp list --filter "appId eq '$deployAppId'" --output json | ConvertFrom-Json
if (-not $deploySp -or $deploySp.Count -eq 0) {
    az ad sp create --id $deployAppId --output none
}

# Assign Contributor role
az role assignment create `
    --assignee $deployAppId `
    --role "Contributor" `
    --scope "/subscriptions/$subscriptionId/resourceGroups/$rgName" `
    --output none 2>$null
Write-Output "✅ Contributor role assigned on $rgName"

# Create OIDC federated credentials
$credentials = @(
    @{ name = "github-main-branch"; subject = "repo:${githubRepo}:ref:refs/heads/main"; description = "Push to main" },
    @{ name = "github-pull-request"; subject = "repo:${githubRepo}:pull_request"; description = "Pull requests" }
)

foreach ($cred in $credentials) {
    $existing = az ad app federated-credential list --id $deployAppObjectId --output json | ConvertFrom-Json
    $found = $existing | Where-Object { $_.name -eq $cred.name }
    if (-not $found) {
        $body = @{
            name = $cred.name; issuer = "https://token.actions.githubusercontent.com"
            subject = $cred.subject; description = $cred.description; audiences = @("api://AzureADTokenExchange")
        } | ConvertTo-Json -Compress
        $tempFile = [System.IO.Path]::GetTempFileName()
        $body | Set-Content -Path $tempFile -Encoding utf8
        az ad app federated-credential create --id $deployAppObjectId --parameters "@$tempFile" --output none
        Remove-Item $tempFile -Force
        Write-Output "✅ Created OIDC credential: $($cred.name)"
    } else {
        Write-Output "✅ OIDC credential '$($cred.name)' already exists"
    }
}
```

## Step 5: Output Summary

```powershell
Write-Output @"

✅ Bootstrap Complete!

=== Azure Resources ===
Subscription:     $subscriptionId
Resource Group:   $rgName
Region:           $region
App Service:      $appUrl
Cosmos DB:        $cosmosEndpoint
UAMI Client ID:   $uamiClientId

=== Entra App Registration ===
App Name:         Copilot Tracker
Application ID:   $appId
Identifier URI:   api://$appId
Tenant ID:        $tenantId

=== GitHub Actions (set as repository variables) ===
AZURE_CLIENT_ID       = $deployAppId
AZURE_TENANT_ID       = $tenantId
AZURE_SUBSCRIPTION_ID = $subscriptionId

=== Next Steps ===
1. Set the three GitHub repository variables listed above
   (Settings > Secrets and variables > Actions > Variables)
2. Push to main to trigger the first CD deployment
3. Run the 'initialize-machine' skill on each machine with:
   Server URL:  $appUrl
   Tenant ID:   $tenantId
   Resource ID: api://$appId
"@
```

## Error Recovery

This skill is safe to re-run. Every step checks for existing resources before creating them. If a step fails, fix the underlying issue and run the skill again.

## Important Notes

- **Idempotent.** Re-running won't duplicate resources.
- **Costs.** The F1 (free) App Service plan and serverless Cosmos DB minimize costs, but Azure will still bill for usage.
- **RBAC-only.** Cosmos DB local auth (keys) is fully disabled. Only managed identity access works.
- **No secrets stored.** OIDC federated credentials mean no passwords or secrets in GitHub.
- **Azure CLI pre-authorized.** The Azure CLI app ID is pre-authorized so `az account get-access-token --resource` works for the PowerShell module.

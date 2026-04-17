---
name: deploy
description: "Deploy Copilot Session Tracker to Azure without GitHub. Uses pre-compiled binaries packaged in the plugin and Azure CLI for direct deployment."
argument-hint: "[subscription-id] [resource-group] [region]"
compatibility: "Windows only. Requires Azure CLI (az) and PowerShell 7+."
metadata:
  author: Copilot Session Tracker
  version: 1.0.0
  category: deployment
---

# Deploy: Direct Azure Deployment (No GitHub Required)

This skill deploys the Copilot Session Tracker directly to Azure from the user's machine. It uses pre-compiled binaries packaged in this plugin and the Azure CLI for infrastructure provisioning and app deployment. No GitHub repository or GitHub Actions are involved.

**Platform: Windows only.** Requires Azure CLI and PowerShell 7+.

**How it differs from `bootstrap`:** The `bootstrap` skill provisions infrastructure and sets up GitHub Actions for CI/CD. This skill skips GitHub entirely. It deploys pre-compiled binaries straight to Azure using `az webapp deploy`.

**Permissions needed:**
- Contributor (or Owner) on the Azure subscription
- Permission to create Entra app registrations in your tenant

## Phase 1: Prerequisites Check

Verify the user's environment before doing anything else.

```powershell
# Check Azure CLI
$azVersion = az version 2>$null | ConvertFrom-Json
if (-not $azVersion) {
    Write-Error "❌ Azure CLI is not installed. Install it from https://aka.ms/installazurecli"
    return
}
Write-Output "✅ Azure CLI $($azVersion.'azure-cli')"

# Check logged in
$account = az account show 2>$null | ConvertFrom-Json
if (-not $account) {
    Write-Output "⚠️ Not logged into Azure. Running 'az login'..."
    az login
    $account = az account show | ConvertFrom-Json
}
Write-Output "✅ Logged in as $($account.user.name)"

# Check PowerShell version
if ($PSVersionTable.PSVersion.Major -lt 7) {
    Write-Error "❌ PowerShell 7+ is required. Current version: $($PSVersionTable.PSVersion)"
    return
}
Write-Output "✅ PowerShell $($PSVersionTable.PSVersion)"
```

If any check fails, stop and tell the user how to fix it.

## Phase 2: Entra App Registration

The API needs an Entra app registration for authentication. Ask the user:

> **Do you already have an Entra app registration for Copilot Tracker?** (For example, if you previously ran the `bootstrap` skill.) If so, provide the Application (client) ID and we'll skip this step. If not, I can create one for you or walk you through it.

### Option A: User already has one

Collect the **Application (client) ID** and skip to Phase 3.

### Option B: Create one automatically

Run the following to create the app registration, expose the API scope, and pre-authorize the Azure CLI:

```powershell
$appDisplayName = "Copilot Tracker"

# Check for existing app
$existingApp = az ad app list --display-name $appDisplayName --output json | ConvertFrom-Json
$exactMatch = $existingApp | Where-Object { $_.displayName -eq $appDisplayName }

if ($exactMatch) {
    $apiClientId = $exactMatch.appId
    $appObjectId = $exactMatch.id
    Write-Output "✅ Found existing app registration: $apiClientId"
} else {
    $appManifest = @{
        displayName    = $appDisplayName
        signInAudience = "AzureADMyOrg"
        spa            = @{ redirectUris = @(
            "http://localhost:5173",
            "http://localhost:5173/auth/callback"
        )}
    } | ConvertTo-Json -Depth 3

    $tempFile = [System.IO.Path]::GetTempFileName()
    $appManifest | Set-Content -Path $tempFile -Encoding utf8

    $newApp = az rest --method POST `
        --uri "https://graph.microsoft.com/v1.0/applications" `
        --headers "Content-Type=application/json" `
        --body "@$tempFile" `
        --output json | ConvertFrom-Json

    Remove-Item $tempFile -Force
    $apiClientId = $newApp.appId
    $appObjectId = $newApp.id
    Write-Output "✅ Created app registration: $apiClientId"
}

# Set identifier URI
$identifierUri = "api://$apiClientId"
$uriBody = @{ identifierUris = @($identifierUri) } | ConvertTo-Json -Compress
$tempFile = [System.IO.Path]::GetTempFileName()
$uriBody | Set-Content -Path $tempFile -Encoding utf8
az rest --method PATCH --uri "https://graph.microsoft.com/v1.0/applications/$appObjectId" `
    --headers "Content-Type=application/json" --body "@$tempFile" --output none 2>$null
Remove-Item $tempFile -Force
Write-Output "✅ Identifier URI: $identifierUri"

# Add OAuth2 scope
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
az rest --method PATCH --uri "https://graph.microsoft.com/v1.0/applications/$appObjectId" `
    --headers "Content-Type=application/json" --body "@$tempFile" --output none 2>$null
Remove-Item $tempFile -Force
Write-Output "✅ API scope: $identifierUri/CopilotTracker.ReadWrite"

# Pre-authorize the app itself and the Azure CLI
$azureCliAppId = "04b07795-8ddb-461a-bbee-02f9e1bf7b46"
$preAuthBody = @{
    api = @{
        preAuthorizedApplications = @(
            @{ appId = $apiClientId; delegatedPermissionIds = @($scopeId) },
            @{ appId = $azureCliAppId; delegatedPermissionIds = @($scopeId) }
        )
    }
} | ConvertTo-Json -Depth 4
$tempFile = [System.IO.Path]::GetTempFileName()
$preAuthBody | Set-Content -Path $tempFile -Encoding utf8
az rest --method PATCH --uri "https://graph.microsoft.com/v1.0/applications/$appObjectId" `
    --headers "Content-Type=application/json" --body "@$tempFile" --output none 2>$null
Remove-Item $tempFile -Force
Write-Output "✅ Pre-authorized Azure CLI for scope"

# Ensure service principal exists
$sp = az ad sp list --filter "appId eq '$apiClientId'" --output json | ConvertFrom-Json
if (-not $sp -or $sp.Count -eq 0) {
    az ad sp create --id $apiClientId --output none
}
Write-Output "✅ Service principal ready"
```

### Option C: Manual creation (portal walkthrough)

If the user prefers to create the app manually in the Azure Portal:

1. Go to **Azure Portal > Entra ID > App registrations > New registration**
2. Name: "Copilot Tracker" (or preferred name)
3. Supported account types: **Single tenant**
4. After creation:
   - Note the **Application (client) ID**
   - Go to **Expose an API** > Set Application ID URI to `api://<clientId>`
   - Add a scope: `CopilotTracker.ReadWrite` (admin and user consent)
   - Go to **Authentication** > Add SPA platform with redirect URIs:
     - `http://localhost:5173` and `http://localhost:5173/auth/callback` (dev)
     - `https://<your-app-name>.azurewebsites.net` and `https://<your-app-name>.azurewebsites.net/auth/callback` (prod, add after deployment)
   - Under **Authorized client applications**, pre-authorize:
     - The app's own client ID
     - `04b07795-8ddb-461a-bbee-02f9e1bf7b46` (Azure CLI)
5. Create a service principal: `az ad sp create --id <clientId>`

## Phase 3: Collect Parameters (MANDATORY, DO NOT SKIP)

**STOP. You MUST ask the user for each parameter below. Do NOT guess or use defaults without explicit user confirmation.**

### 3a. Azure Subscription

Run this to show available subscriptions:

```powershell
az account list --query "[].{Name:name, Id:id}" -o table
```

Display the results and ask: **"Which subscription should I use? Please provide the name or ID."**

Wait for the user to respond. Store as `$subscriptionId`.

### 3b. Resource Group Name

Ask: **"What resource group name should I use?"**

Required. No default. Store as `$rgName`.

### 3c. Azure Region

Ask: **"What Azure region? (e.g., eastus2, westus2, centralus)"**

Required. No default. Store as `$region`.

### 3d. App Service Name

Ask: **"What App Service name should I use? This becomes `<name>.azurewebsites.net` and must be globally unique."**

Required. No default. Store as `$appName`.

### 3e. Cosmos DB Account Name

Ask: **"What Cosmos DB account name should I use? Must be globally unique across Azure."**

Required. No default. Store as `$cosmosAccountName`.

### 3f. App Service Plan SKU

Ask: **"What App Service Plan SKU? Default is F1 (free tier). Other options: B1, S1, P1v2."**

Default: `F1`. Store as `$sku`.

### 3g. Entra Tenant ID

Pull automatically (confirm with user):

```powershell
$tenantId = (az account show --query tenantId -o tsv)
Write-Output "Tenant ID: $tenantId"
```

Ask: **"Is this the correct tenant? If not, provide the right tenant ID."**

### 3h. API Client ID

If the app registration was created in Phase 2, this is already stored as `$apiClientId`. Otherwise, ask: **"What is the Application (client) ID of your Entra app registration?"**

### Confirmation Gate

Display all collected parameters and ask for confirmation:

```
Subscription:       <subscription-id>
Resource Group:     <rg-name>
Region:             <region>
App Service:        <app-name>.azurewebsites.net
Cosmos DB:          <cosmos-name>
SKU:                <sku>
Tenant ID:          <tenant-id>
API Client ID:      <api-client-id>
```

**"I'm about to create Azure resources with these settings. This will incur Azure costs. Proceed?"**

**Do NOT proceed until the user explicitly confirms.**

## Phase 4: Deploy Infrastructure

The Bicep template is located at `infra/main.bicep` relative to this skill's directory. It creates:
- User-Assigned Managed Identity (for Cosmos DB RBAC)
- Cosmos DB account (serverless, RBAC-only) with containers: sessions, tasks, taskLogs, prompts, promptLogs
- App Service Plan (Linux)
- App Service (.NET 10) configured with the UAMI and Entra auth settings

```powershell
# Set subscription
az account set --subscription $subscriptionId

# Create resource group (idempotent)
$rgExists = az group show --name $rgName 2>$null
if (-not $rgExists) {
    az group create --name $rgName --location $region --output none
    Write-Output "✅ Created resource group '$rgName' in '$region'"
} else {
    Write-Output "✅ Resource group '$rgName' already exists"
}

# Deploy Bicep
# The template is at infra/main.bicep relative to this skill's directory.
# The agent should resolve the full path based on where this skill lives.
Write-Output "Deploying infrastructure (this may take several minutes)..."
$result = az deployment group create `
    --resource-group $rgName `
    --template-file "<skill-dir>/infra/main.bicep" `
    --parameters appName=$appName `
                 cosmosAccountName=$cosmosAccountName `
                 location=$region `
                 appServicePlanSku=$sku `
                 tenantId=$tenantId `
                 apiClientId=$apiClientId `
    --query "properties.outputs" -o json | ConvertFrom-Json

if ($LASTEXITCODE -ne 0) {
    Write-Error "❌ Infrastructure deployment failed. Check the error above and re-run."
    return
}

$appUrl = $result.appServiceUrl.value
$cosmosEndpoint = $result.cosmosEndpoint.value
$uamiClientId = $result.managedIdentityClientId.value

Write-Output "✅ Infrastructure deployed"
Write-Output "  App URL:         $appUrl"
Write-Output "  Cosmos Endpoint: $cosmosEndpoint"
Write-Output "  UAMI Client ID:  $uamiClientId"
```

**Note for the executing agent:** Replace `<skill-dir>` with the actual resolved path to this skill's directory (the directory containing this SKILL.md). The Bicep file is at `infra/main.bicep` within that directory.

## Phase 5: Deploy Application Binaries

The compiled server binaries are located in the `binaries/` subdirectory of this skill's directory. These are pre-compiled .NET binaries ready for deployment.

```powershell
# Path to the binaries directory (relative to this skill's directory)
$binariesDir = "<skill-dir>/binaries"

# Verify binaries exist
if (-not (Test-Path $binariesDir)) {
    Write-Error "❌ Binaries directory not found at $binariesDir. The plugin may not include pre-compiled binaries yet."
    Write-Error "   Build the server with: dotnet publish src/CopilotTracker.Api -c Release -o <skill-dir>/binaries"
    return
}

# Create zip for deployment
$zipPath = Join-Path $env:TEMP "copilot-tracker-deploy.zip"
if (Test-Path $zipPath) { Remove-Item $zipPath -Force }
Compress-Archive -Path "$binariesDir/*" -DestinationPath $zipPath -Force

Write-Output "Deploying application binaries..."
az webapp deploy `
    --resource-group $rgName `
    --name $appName `
    --src-path $zipPath `
    --type zip

if ($LASTEXITCODE -ne 0) {
    Write-Error "❌ Binary deployment failed. Check the error above and re-run."
    return
}

# Clean up zip
Remove-Item $zipPath -Force
Write-Output "✅ Application binaries deployed"
```

**Note for the executing agent:** Replace `<skill-dir>` with the actual path to this skill's directory. If the `binaries/` directory doesn't exist yet, tell the user they need to build the server first with `dotnet publish` and place the output in the `binaries/` subdirectory.

## Phase 6: Add Production Redirect URIs

After deployment, update the Entra app registration to include the production URLs as SPA redirect URIs.

```powershell
$prodUrl = "https://$appName.azurewebsites.net"

# Get current redirect URIs
$app = az ad app show --id $apiClientId --output json | ConvertFrom-Json
$currentUris = @($app.spa.redirectUris)

# Add production URIs (deduplicated)
$newUris = @("$prodUrl", "$prodUrl/auth/callback")
$allUris = ($currentUris + $newUris) | Select-Object -Unique

$body = @{ spa = @{ redirectUris = @($allUris) } } | ConvertTo-Json -Depth 5
$tempFile = [System.IO.Path]::GetTempFileName()
$body | Set-Content -Path $tempFile -Encoding utf8
az rest --method PATCH `
    --uri "https://graph.microsoft.com/v1.0/applications/$($app.id)" `
    --headers "Content-Type=application/json" `
    --body "@$tempFile" --output none
Remove-Item $tempFile -Force

Write-Output "✅ Production redirect URIs added:"
foreach ($uri in $newUris) { Write-Output "   $uri" }
```

## Phase 7: Output Summary

Display the final summary:

```powershell
Write-Output @"

✅ Deployment Complete!

=== Azure Resources ===
Subscription:     $subscriptionId
Resource Group:   $rgName
Region:           $region
App Service:      $appUrl
Cosmos DB:        $cosmosEndpoint
UAMI Client ID:   $uamiClientId

=== Entra App ===
Application ID:   $apiClientId
Identifier URI:   api://$apiClientId
Tenant ID:        $tenantId

=== Next Steps ===
1. Run the 'initialize-machine' skill to configure this machine for session tracking:
   Server URL:  $appUrl
   Tenant ID:   $tenantId
   Resource ID: api://$apiClientId
2. The dashboard is available at $appUrl
3. To update the server binaries later, re-run this skill or use 'az webapp deploy' directly.
"@
```

## Error Recovery

This skill is **idempotent**. Every step checks for existing resources before creating them. If any step fails:

1. **Read the error message.** Azure CLI errors are usually descriptive.
2. **Fix the underlying issue** (permissions, naming conflicts, quota limits, etc.).
3. **Re-run the skill.** It picks up where it left off because existing resources are detected and reused.

Common failure scenarios:
- **"Name already taken"**: App Service and Cosmos DB names are globally unique. Choose a different name.
- **"Insufficient permissions"**: You need Contributor on the subscription and permission to create Entra app registrations.
- **"Quota exceeded"**: Free tier (F1) is limited to one per subscription. Use B1 or higher.
- **"Bicep template not found"**: Make sure the `infra/main.bicep` file exists in this skill's directory. If the plugin was installed without the infrastructure template, copy it from the copilot-tracker repo's `deploy/` directory.
- **"Binaries directory not found"**: The pre-compiled binaries must be in the `binaries/` subdirectory. Build with `dotnet publish src/CopilotTracker.Api -c Release -o <skill-dir>/binaries`.

## Important Notes

- **No GitHub required.** This skill deploys directly from the user's machine. No repository, no Actions, no OIDC credentials.
- **Idempotent.** Safe to re-run. Existing resources are detected and reused.
- **Costs.** F1 (free) App Service and serverless Cosmos DB minimize costs, but Azure will still bill for usage beyond free tier limits.
- **RBAC-only.** Cosmos DB local auth (keys) is fully disabled. Only managed identity access works.
- **Azure CLI pre-authorized.** The Azure CLI app ID (`04b07795-8ddb-461a-bbee-02f9e1bf7b46`) is pre-authorized so `az account get-access-token --resource api://<clientId>` works for the PowerShell module.
- **Binaries directory.** The `binaries/` subdirectory of this skill contains the pre-compiled .NET server. To update it, rebuild with `dotnet publish` and replace the contents.
- **Infrastructure template.** The `infra/main.bicep` file in this skill's directory is the Bicep template for all Azure resources. It's the same template used by the `bootstrap` skill but packaged locally so no repo checkout is needed.

---
name: update-server
description: "Update the Copilot Session Tracker server binaries on an existing Azure App Service deployment. Use after updating the plugin to deploy new server code."
argument-hint: "[app-name] [resource-group]"
compatibility: "Windows only. Requires Azure CLI (az)."
metadata:
  author: Copilot Session Tracker
  version: 1.0.0
  category: deployment
---

# Update Server Skill

Deploys updated server binaries to an existing Azure App Service. This is a quick update operation, not a full provisioning flow. If you need to change infrastructure settings, use the `deploy` skill instead.

## Step 0: Prerequisites

Before starting, verify:

1. **Azure CLI** is installed and the user is logged in:

```powershell
az account show --query "name" -o tsv
```

2. **Binaries directory** exists at `binaries/` relative to this skill's directory (`${CLAUDE_PLUGIN_ROOT}/skills/update-server/binaries/`). If it doesn't exist, the plugin package may be incomplete.

## Step 1: Collect Parameters

Ask the user for:

1. **App Service Name** — the name of the existing App Service (e.g., `copilot-tracker`)
2. **Resource Group** — the Azure resource group containing the App Service

If the user has a config file at `~/.copilot/copilot-tracker-config.json`, suggest reading the `serverUrl` from it to derive the app name. For example:

```powershell
$config = Get-Content "$env:USERPROFILE\.copilot\copilot-tracker-config.json" | ConvertFrom-Json
# The serverUrl typically looks like https://<app-name>.azurewebsites.net
# Extract the app name from it
```

## Step 2: Verify App Service Exists

```powershell
az webapp show --name $appName --resource-group $rgName --query "state" -o tsv
```

If the App Service doesn't exist, tell the user to run the `deploy` skill first for initial setup. Do not proceed.

## Step 3: Deploy Updated Binaries

```powershell
# Create zip from the binaries directory
$skillDir = "${CLAUDE_PLUGIN_ROOT}/skills/update-server"
$zipPath = Join-Path $env:TEMP "copilot-tracker-update.zip"
Compress-Archive -Path "$skillDir/binaries/*" -DestinationPath $zipPath -Force

# Deploy to App Service
az webapp deploy --resource-group $rgName --name $appName --src-path $zipPath --type zip --clean true
```

The `--clean true` flag ensures old files are removed before deploying new ones. This is important so stale assemblies don't linger.

## Step 4: Verify Deployment

```powershell
# Wait for the app to restart
Start-Sleep -Seconds 15

# Check health endpoint
$appUrl = (az webapp show --name $appName --resource-group $rgName --query "defaultHostName" -o tsv)
try {
    $health = Invoke-RestMethod -Uri "https://$appUrl/api/health" -TimeoutSec 30
    # Report success with health check details
} catch {
    # The app may still be starting up after deployment
    # Suggest waiting 30-60 seconds and retrying manually
}
```

## Step 5: Summary

Report to the user:

- Whether the deployment succeeded or failed
- The app URL (e.g., `https://<app-name>.azurewebsites.net`)
- Note that the dashboard and API are now running the updated version
- If the health check failed, suggest waiting and retrying: `Invoke-RestMethod -Uri "https://<app-url>/api/health"`

## Important Notes

- This skill only updates the application binaries. Infrastructure (Cosmos DB, App Service Plan, etc.) is NOT modified.
- The binaries are pre-compiled and packaged in the plugin at `binaries/` relative to this skill.
- If the user needs to change infrastructure settings (SKU, region, Cosmos DB config), they should use the `deploy` skill.
- If deployment fails with a 404 or "not found" error, the App Service may not exist yet. Direct the user to the `deploy` skill.

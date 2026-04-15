# Setup Scripts

One-time scripts to provision the Entra and Azure infrastructure for Copilot Tracker. Run these in order when setting up a new environment.

## Prerequisites

- [Azure CLI](https://learn.microsoft.com/en-us/cli/azure/install-azure-cli) installed and logged in (`az login`)
- Sufficient permissions: Owner or Contributor + User Access Administrator on the target subscription
- Permissions to create Entra app registrations in your tenant

## Scripts

### 1. `setup-deployment.ps1`

Creates the GitHub Actions deployment infrastructure:
- Resource group
- Deployment service principal with Contributor role
- OIDC federated credentials for GitHub Actions

```powershell
# Use defaults
.\setup-deployment.ps1

# Or customize
.\setup-deployment.ps1 -ResourceGroupName "my-rg" -Location "westus2" -GitHubRepo "myorg/my-tracker"
```

After running, set these **GitHub repository variables** (Settings > Secrets and variables > Actions > Variables):
- `AZURE_CLIENT_ID`
- `AZURE_TENANT_ID`
- `AZURE_SUBSCRIPTION_ID`

### 2. `setup-app-registration.ps1`

Creates the Entra app registration for the API/MCP server:
- App registration with `CopilotTracker.ReadWrite` scope
- SPA redirect URIs for dashboard (localhost + production)
- Service principal for token issuance

```powershell
# Use defaults
.\setup-app-registration.ps1

# Or customize
.\setup-app-registration.ps1 -AppDisplayName "My Tracker" -AppServiceName "my-tracker-app"
```

Save the output values. You'll need the Application ID and Tenant ID for `appsettings.json`.

## What Gets Created

| Resource | Created By | Purpose |
|---|---|---|
| Resource group | `setup-deployment.ps1` | Contains all Azure resources |
| Deployment SP | `setup-deployment.ps1` | GitHub Actions deploys to Azure |
| OIDC credentials | `setup-deployment.ps1` | Passwordless auth from GitHub |
| API app registration | `setup-app-registration.ps1` | Validates user tokens at API/MCP layer |
| API scope | `setup-app-registration.ps1` | `CopilotTracker.ReadWrite` delegated permission |

## Infrastructure (Bicep)

The remaining Azure resources (App Service, Cosmos DB, UAMI, etc.) are provisioned via Bicep templates in `deploy/` and deployed automatically by the CD pipeline. See the main architecture docs for details.

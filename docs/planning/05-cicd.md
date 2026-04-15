# CI/CD Pipeline

## Overview

Two GitHub Actions workflows handle everything. CI runs on pull requests to validate the build and tests. CD runs on merge to main and deploys to Azure. That's it.

The critical design choice here: zero secrets stored anywhere. No passwords, no client secrets, no connection strings in GitHub Secrets. OIDC federated credentials give the workflows passwordless authentication to Azure. Managed identity handles everything at runtime. If you're storing secrets for Azure deployments in 2025, you're doing it wrong.

## CI Workflow

**File:** `.github/workflows/ci.yml`
**Trigger:** Pull requests targeting `main`
**Runner:** `ubuntu-latest`

The CI workflow validates that the code builds, tests pass, and the dashboard compiles. It doesn't touch Azure at all. Tests use mocks and the Cosmos DB emulator, so there's no need for credentials.

```yaml
name: CI
on:
  pull_request:
    branches: [main]

jobs:
  build-and-test:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4

      - name: Setup .NET 10
        uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '10.0.x'

      - name: Setup Node 22
        uses: actions/setup-node@v4
        with:
          node-version: '22'

      - name: Restore .NET dependencies
        run: dotnet restore

      - name: Build
        run: dotnet build --no-restore

      - name: Run tests
        run: dotnet test --no-build

      - name: Install dashboard dependencies
        working-directory: dashboard
        run: npm ci

      - name: Build dashboard
        working-directory: dashboard
        run: npm run build

      - name: Run dashboard tests
        working-directory: dashboard
        run: npm test
```

Steps in order:

1. **Setup .NET 10 and Node 22.** Both are needed because the server is .NET and the dashboard is a separate Node/TypeScript project.
2. **dotnet restore, build, test.** Standard .NET pipeline. Tests include both unit and integration tests. Integration tests use mocks or the Cosmos DB emulator, not a live Azure account.
3. **Dashboard: npm ci, npm run build, npm test.** Builds the React dashboard and runs its test suite.

No Azure login step. No credentials. If CI needs Azure access, something's wrong with the test design.

## CD Workflow

**File:** `.github/workflows/cd.yml`
**Trigger:** Push to `main` (which means a PR was merged)
**Runner:** `ubuntu-latest`

The CD workflow builds everything, bundles the dashboard into the server's wwwroot, publishes, deploys to Azure, and runs a smoke test.

```yaml
name: CD
on:
  push:
    branches: [main]

permissions:
  id-token: write
  contents: read

jobs:
  deploy:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4

      - name: Setup .NET 10
        uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '10.0.x'

      - name: Setup Node 22
        uses: actions/setup-node@v4
        with:
          node-version: '22'

      - name: Build dashboard
        working-directory: dashboard
        run: |
          npm ci
          npm run build

      - name: Copy dashboard to wwwroot
        run: cp -r dashboard/dist/* src/CopilotTracker.Server/wwwroot/

      - name: Publish .NET app
        run: dotnet publish src/CopilotTracker.Server -c Release -o ./publish

      - name: Azure Login (OIDC)
        uses: azure/login@v2
        with:
          client-id: ${{ vars.AZURE_CLIENT_ID }}
          tenant-id: ${{ vars.AZURE_TENANT_ID }}
          subscription-id: ${{ vars.AZURE_SUBSCRIPTION_ID }}

      - name: Deploy to Azure App Service
        uses: azure/webapps-deploy@v3
        with:
          app-name: copilot-tracker
          package: ./publish

      - name: Smoke test
        run: |
          sleep 30
          curl --fail --retry 5 --retry-delay 10 \
            https://copilot-tracker.azurewebsites.net/health
```

Key details:

- **permissions: id-token: write** is required for OIDC. The workflow requests a token from GitHub's OIDC provider, and Azure validates it against the federated credential. No secrets involved.
- **Dashboard bundling.** The dashboard gets built as static files, then copied into the .NET project's wwwroot folder. The published artifact is a single self-contained deployment.
- **Post-deploy smoke test.** A simple curl against the health endpoint. If the app doesn't respond, the workflow fails and you know immediately.

## OIDC Authentication

OIDC (OpenID Connect) federated credentials let GitHub Actions authenticate to Azure without any stored secrets. Here's how it works:

1. The workflow requests a token from GitHub's OIDC provider.
2. Azure AD validates that token against a pre-configured federated credential.
3. If the token's claims match (repo, branch, event type), Azure grants access.
4. The workflow gets a short-lived Azure token. No passwords ever existed.

### Deployment Service Principal

- **Name:** `copilot-tracker-github-deploy`
- **App ID:** `cadc7e50-2c30-4bad-ac9d-06a9afce0575`

### Federated Credentials

Two federated credentials are configured on this service principal:

| Credential Name | Subject Filter | Purpose |
|----------------|---------------|---------|
| `github-main-branch` | `repo:aef123/copilot-tracker:ref:refs/heads/main` | CD workflow deploys on push to main |
| `github-pull-request` | `repo:aef123/copilot-tracker:pull_request` | CI workflow (if it ever needs Azure access) |

The `github-main-branch` credential is what the CD workflow uses. The `github-pull-request` credential exists as a safety net. CI doesn't need Azure today, but if it ever does (like running integration tests against a staging Cosmos account), the credential is already there.

## Deployment Config Strategy

Everything falls into one of three buckets: committed to the repo, set as GitHub variables, or configured by Bicep during provisioning.

### In the Repo (Non-Secret)

**File:** `deploy/main.bicepparam`

This file contains deployment parameters that aren't sensitive:

- App name
- Resource group
- Region
- SKU/pricing tier
- Cosmos DB account name
- Database name

These are just names and configuration choices. Committing them is fine and makes the deployment reproducible.

### In GitHub Repository Variables

Set via **Settings > Secrets and variables > Actions > Variables**:

| Variable | Value |
|----------|-------|
| `AZURE_CLIENT_ID` | `cadc7e50-2c30-4bad-ac9d-06a9afce0575` |
| `AZURE_TENANT_ID` | `5df6d88f-0d78-491b-9617-8b43a209ba73` |
| `AZURE_SUBSCRIPTION_ID` | `e2cd7f27-ec00-4807-8bea-64d3cd24b72a` |

These are **variables**, not secrets. Subscription IDs, tenant IDs, and client IDs aren't sensitive. They're environment-specific identifiers that the OIDC flow needs. Storing them as variables (not secrets) means they show up in logs, which is fine and makes debugging easier.

### No Passwords. Anywhere.

OIDC plus managed identity eliminates every secret from the pipeline:

- **GitHub to Azure:** OIDC federated credentials. No client secret.
- **App Service to Cosmos DB:** User-assigned managed identity with RBAC. No connection string with keys.
- **App Service configuration:** Set by Bicep during provisioning. No manual secret management.

## What's a Secret vs Config

| Item | Where It Lives | Why There |
|------|---------------|-----------|
| Azure subscription/tenant/client IDs | GitHub repository variables | Not secret, just environment-specific identifiers |
| Resource group, region, SKU | `deploy/main.bicepparam` | Non-sensitive config, version-controlled for reproducibility |
| Cosmos endpoint, UAMI client ID | App Service settings (set by Bicep) | Configured automatically during infrastructure provisioning |
| Passwords/client secrets | **Nowhere** | OIDC + managed identity eliminates them entirely |

## One-Time Setup Steps

These steps run once to bootstrap the entire deployment pipeline. After this, everything is automated.

### 1. Create Deployment Infrastructure

```powershell
./deploy/scripts/setup-deployment.ps1
```

This script creates:
- The resource group
- The deployment service principal (`copilot-tracker-github-deploy`)
- Both OIDC federated credentials (main branch + pull request)

### 2. Create API App Registration

```powershell
./deploy/scripts/setup-app-registration.ps1
```

Creates the app registration for the API. This is separate from the deployment SP because they serve different purposes.

### 3. Set GitHub Repository Variables

Go to the repository settings and add these three variables:

- `AZURE_CLIENT_ID` = `cadc7e50-2c30-4bad-ac9d-06a9afce0575`
- `AZURE_TENANT_ID` = `5df6d88f-0d78-491b-9617-8b43a209ba73`
- `AZURE_SUBSCRIPTION_ID` = `e2cd7f27-ec00-4807-8bea-64d3cd24b72a`

### 4. Run Bicep Deployment

```powershell
az deployment group create \
  --resource-group copilot-tracker \
  --template-file deploy/main.bicep \
  --parameters deploy/main.bicepparam
```

This provisions all Azure infrastructure: App Service Plan, App Service, Cosmos DB account with database and containers, user-assigned managed identity, RBAC role assignments, and App Service configuration.

After this, push to main and the CD workflow handles everything.

## Infrastructure as Code

All Azure infrastructure is defined in Bicep templates under the `deploy/` folder.

Bicep manages:
- **App Service Plan** (hosting plan and SKU)
- **App Service** (the web app itself)
- **Cosmos DB account, database, and containers** (serverless, RBAC-only)
- **User-Assigned Managed Identity** (for App Service to Cosmos DB auth)
- **RBAC role assignments** (granting the UAMI access to Cosmos DB)
- **App Service configuration** (app settings, connection info)

The first deployment creates everything from scratch. Subsequent deployments are incremental. Bicep figures out what changed and only updates what's necessary. You don't need to track state files or worry about drift. It's declarative: the template describes what should exist, and Azure makes it so.

## Branch Strategy

Simple and straightforward:

- **`main`** is the deployment branch. Merging to main triggers a production deployment. Don't merge broken code.
- **Feature branches** are where work happens. Push a branch, open a PR, CI runs automatically.
- **PR merge triggers CD.** There's no manual deployment step. If CI passes and the PR is approved, merging it deploys it.

There's no staging environment right now. This is a single-user personal tool, so the complexity of multi-environment deployments isn't justified yet. If that changes, Azure App Service deployment slots can be added later without reworking the pipeline. Just add a slot, deploy to it first, run smoke tests, then swap.

## Related Docs

- [Architecture Overview](00-architecture.md) - System design and component relationships
- [Test Plan](04-test-plan.md) - Testing strategy referenced by the CI workflow

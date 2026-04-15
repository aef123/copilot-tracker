# Azure Resources

Provisioned resource names, endpoints, and identifiers. Updated after infrastructure changes.

## Subscription

- **Name:** Personal
- **ID:** `e2cd7f27-ec00-4807-8bea-64d3cd24b72a`
- **Tenant ID:** `5df6d88f-0d78-491b-9617-8b43a209ba73`

## Resource Group

- **Name:** `rg-copilot-tracker`
- **Region:** `eastus2`
- **Subscription:** `e2cd7f27-ec00-4807-8bea-64d3cd24b72a`

## Deployment Service Principal (GitHub Actions)

- **Display Name:** `copilot-tracker-github-deploy`
- **Application (client) ID:** `cadc7e50-2c30-4bad-ac9d-06a9afce0575`
- **Object ID (app):** `20257c7a-455f-4d9f-ba13-6acb87429f31`
- **Role:** Contributor on `rg-copilot-tracker`
- **OIDC Federated Credentials:**
  - `github-main-branch`: `repo:aef123/copilot-tracker:ref:refs/heads/main`
  - `github-pull-request`: `repo:aef123/copilot-tracker:pull_request`

## Cosmos DB (Not Yet Provisioned in this RG)

Will be created via Bicep. Planned:
- **Account Name:** TBD
- **Database:** `CopilotTracker`
- **Containers:** `sessions` (/machineId), `tasks` (/queueName), `taskLogs` (/taskId)
- **Capacity:** Serverless
- **Auth:** RBAC-only (local auth disabled)

### Previous Cosmos DB (in ai-marketplace setup, different subscription)

- **Account Name:** `afaust-copilot-tracker-db`
- **Endpoint:** `https://afaust-copilot-tracker-db.documents.azure.com:443/`
- **Resource Group:** `afaust-copilot-tracker`
- **Subscription:** `ff63ef21-51fb-4ae9-a73f-7ce2f34f2d40`
- Not used by this project. Remains for the ai-marketplace plugin.

## App Service (Not Yet Provisioned)

Will be created via Bicep in Phase 4. Planned:
- **App Name:** TBD (e.g., `copilot-tracker`)
- **Plan:** B1 (or free tier for dev)
- **Region:** `eastus2`
- **Managed Identity:** User-assigned (UAMI), granted Cosmos DB Built-in Data Contributor role

## Entra App Registration (API)

- **Display Name:** `Copilot Tracker`
- **Application (client) ID:** `4c8148f5-c913-40c5-863f-1c019821eac4`
- **Object ID:** `c762394e-8f0e-404c-a94d-942698249ee7`
- **Identifier URI:** `api://4c8148f5-c913-40c5-863f-1c019821eac4`
- **Scope:** `api://4c8148f5-c913-40c5-863f-1c019821eac4/CopilotTracker.ReadWrite`
- **Sign-in audience:** AzureADMyOrg (single tenant)
- **SPA Redirect URIs:**
  - `http://localhost:5173`
  - `http://localhost:5173/auth/callback`
  - `https://copilot-tracker.azurewebsites.net`
  - `https://copilot-tracker.azurewebsites.net/auth/callback`
- **Used by:** Dashboard (MSAL.js), CLI (`az account get-access-token`)

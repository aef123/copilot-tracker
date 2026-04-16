# Copilot Tracker -- Project Context

This file is the "start here" for any new Copilot session. It describes what's actually built and true right now.

## What This Is

A system to track Copilot CLI sessions and tasks across multiple machines. Azure-hosted backend with REST API (for CLI and dashboard), and a React SPA dashboard. Cosmos DB (serverless, RBAC-only) for storage.

## Repository

- **Repo:** `github.com/aef123/copilot-tracker`
- **Local path:** `c:\git\copilot-tracker`
- **Branch:** `main`

## Current State

**Core implementation complete.** All backend and frontend code is built and tested. Not yet deployed to Azure.

### What's Built

- **.NET 10 solution** with 3 projects (Server, Core, Cosmos) + 3 test projects
- **REST API** at `/api/*` with controllers for sessions (GET + POST), tasks (GET + POST), health
- **Service layer**: SessionService, TaskService, TaskLogService, HealthService (30s cache)
- **Cosmos repositories**: partition-key-aware, behind interfaces (ISessionRepository, ITaskRepository, ITaskLogRepository)
- **Entra auth**: Microsoft.Identity.Web bearer token validation
- **Stale session cleanup**: BackgroundService on configurable timer
- **React dashboard**: Vite + TypeScript + MSAL.js auth + typed API client + 5 components (HealthDashboard, SessionList, SessionDetail, TaskDetail, Layout)
- **PowerShell module**: CopilotTracker.psm1 talks to REST API (not Cosmos directly)
- **CI/CD**: GitHub Actions workflows (ci.yml for PRs, cd.yml for deploy on merge)
- **Bicep IaC**: App Service, Cosmos DB (serverless, RBAC-only), UAMI, role assignments
- **Setup scripts**: setup-deployment.ps1, setup-app-registration.ps1

### Test Coverage

- 36 .NET tests (16 core service, 7 cosmos, 13 server)
- 22 dashboard tests (4 auth, 12 API client, 6 component)
- 58 total, all passing

## Azure Resources

See `context/azure-resources.md` for details.

- Resource group, deployment SP, OIDC credentials: provisioned
- App registration for API: provisioned (4c8148f5-c913-40c5-863f-1c019821eac4)
- Cosmos DB, App Service, UAMI: defined in Bicep, not yet deployed

## What's Next

1. Deploy infrastructure via Bicep (`deploy/deploy.ps1`)
2. Push to main to trigger CD pipeline
3. Verify end-to-end: CLI -> REST API -> Cosmos, Dashboard -> API -> Cosmos
4. Post-deploy smoke tests

See `docs/planning/phase-status.md` for detailed progress.

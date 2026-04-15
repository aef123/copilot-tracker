# Phase Status

Tracks progress across all implementation phases. Updated after each work item completes.

## Phase 0: Documentation Scaffolding

| Item | Status | Notes |
|------|--------|-------|
| Create `docs/planning/` structure | Done | |
| Create `context/` structure | Done | |
| Write `00-architecture.md` | Done | |
| Write `phase-status.md` | Done | This file |
| Write `decisions.md` | Done | Seeded with initial decisions |
| Write `context/CONTEXT.md` | Done | |
| Write `context/conventions.md` | Done | |
| Write `context/azure-resources.md` | Done | |
| Write `04-test-plan.md` | Done | Comprehensive test plan covering all layers |
| Remaining planning docs (01-03, 05-06) | Pending | Written as each topic is implemented |

## Phase 1: Foundation (Spike + Core)

| Item | Status | Notes |
|------|--------|-------|
| Entra app registration | Pending | Single registration, expose API scope |
| Scaffold .NET solution (3 projects) | Pending | Server, Core, Cosmos |
| Core models + repository interfaces | Pending | |
| Cosmos repository layer | Pending | SDK v3 + DefaultAzureCredential |
| MCP server spike (initialize-session, heartbeat) | Pending | Riskiest path, validate first |
| Write `01-auth-model.md` | Pending | |
| Write `02-data-model.md` | Pending | |

## Phase 2: Full MCP + API

| Item | Status | Notes |
|------|--------|-------|
| Remaining MCP tools | Pending | complete-session, set-task, add-log, get-session |
| REST API controllers | Pending | Sessions, Tasks, Health |
| Service layer | Pending | SessionService, TaskService, HealthService |
| Stale session cleanup background service | Pending | IHostedService on timer |
| Health aggregation with caching | Pending | |
| Write `03-api-design.md` | Pending | |

## Phase 3: Dashboard Refactor

| Item | Status | Notes |
|------|--------|-------|
| MSAL.js auth integration | Pending | Replace token pasting |
| Typed API client | Pending | Replaces direct Cosmos calls |
| Update components | Pending | |
| Dashboard builds into wwwroot/ | Pending | |
| Write `06-dashboard.md` | Pending | |

## Phase 4: CI/CD + Deployment

| Item | Status | Notes |
|------|--------|-------|
| Bicep templates | Pending | App Service, Cosmos DB, UAMI, RBAC |
| CI workflow (ci.yml) | Pending | On PR: build + test |
| CD workflow (cd.yml) | Pending | On merge to main: build + deploy |
| Deployment service principal + OIDC | Pending | Federated credential, no secrets |
| Simplify PowerShell module | Pending | Talks to MCP, not Cosmos |
| Update plugin skills | Pending | |
| Write `04-test-plan.md`, `05-cicd.md` | Pending | |

## Phase 5: Polish

| Item | Status | Notes |
|------|--------|-------|
| Update plugin.json | Pending | |
| Final docs pass | Pending | All planning + context docs |
| End-to-end testing across machines | Pending | |
| Update `context/conventions.md` | Pending | Patterns that emerged |

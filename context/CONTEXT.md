# Copilot Tracker -- Project Context

This file is the "start here" for any new Copilot session. It describes what's actually built and true right now.

## What This Is

A system to track Copilot CLI sessions and tasks across multiple machines. Azure-hosted backend with MCP server (for CLI), REST API (for dashboard), and a React SPA dashboard. Cosmos DB (serverless, RBAC-only) for storage.

## Repository

- **Repo:** `github.com/aef123/copilot-tracker`
- **Local path:** `c:\git\copilot-tracker`
- **Branch:** `main`

## Current State

**Nothing is built yet.** This is a fresh repo with only README.md and these docs/context files. The architecture is planned and documented in `docs/planning/`.

### What Exists (from the original version in ai-marketplace)

The original `ai-marketplace/plugins/copilot-session-tracker` has:
- PowerShell module that talks directly to Cosmos DB via REST
- React dashboard that talks directly to Cosmos DB from the browser
- Bootstrap and initialize-machine skills for Azure provisioning
- Working Cosmos DB backend (serverless, RBAC-only, already provisioned)

### What We're Building

A new architecture with a proper backend server sitting between all clients and the database:
- .NET 10 ASP.NET Core server hosting MCP + REST API + SPA
- Shared service + repository layer with Cosmos DB behind interfaces
- UAMI for server-to-Cosmos auth, Entra user tokens at the edge
- GitHub Actions CI/CD (OIDC, no secrets)
- See `docs/planning/00-architecture.md` for the full design

## Azure Resources (Already Provisioned)

See `context/azure-resources.md` for details. The Cosmos DB backend from the original version is still active and will be reused.

## What's Next

Phase 0 (documentation scaffolding) is complete. Next is Phase 1: Foundation (auth spike + core scaffolding). The MCP auth spike is the riskiest path and should be validated first.

See `docs/planning/phase-status.md` for detailed progress.

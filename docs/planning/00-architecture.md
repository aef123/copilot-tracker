# Architecture Overview

## Problem

The original Copilot Session Tracker (in `ai-marketplace/plugins/copilot-session-tracker`) has the PowerShell module and the React dashboard both talking directly to Cosmos DB via REST. This means:
- Every client needs AAD tokens scoped to Cosmos DB
- No abstraction over the database (locked to Cosmos REST API quirks)
- The dashboard requires manual token pasting (no real auth flow)
- No shared business logic between CLI and dashboard paths

## New Architecture

A single C# (.NET 10) ASP.NET Core application hosting three concerns in one process:

1. **MCP Server** (streamable HTTP at `/mcp`) -- Copilot CLI manages sessions and tasks through MCP tools
2. **REST API** (`/api/*`) -- Dashboard calls conventional endpoints
3. **Static SPA** (fallback) -- React dashboard served from `wwwroot/`

All three share a common service + repository layer. Cosmos DB access is behind interfaces so the database can be swapped later.

```
┌──────────────┐     ┌──────────────┐
│  Copilot CLI │     │   Dashboard  │
│  (PowerShell)│     │  (React SPA) │
└──────┬───────┘     └──────┬───────┘
       │ MCP                │ REST API
       │ (streamable HTTP)  │ (/api/*)
       └────────┬───────────┘
                │
    ┌───────────▼──────────────┐
    │   ASP.NET Core Host      │
    │  ┌─────┐  ┌──────┐      │
    │  │ MCP │  │ API  │      │
    │  │Tools│  │Ctrls │      │
    │  └──┬──┘  └──┬───┘      │
    │     └────┬───┘           │
    │    ┌─────▼──────┐        │
    │    │  Services   │        │
    │    └─────┬──────┘        │
    │    ┌─────▼──────┐        │
    │    │ Repos (I*)  │        │
    │    └─────┬──────┘        │
    └──────────┼───────────────┘
               │ UAMI
    ┌──────────▼───────────────┐
    │     Azure Cosmos DB      │
    │  (serverless, RBAC-only) │
    └──────────────────────────┘
```

## Hosting

**Azure App Service** (B1 or free tier for dev). Simpler than Container Apps for a single .NET app, always-on avoids cold-start issues for MCP, and background services (stale cleanup) run reliably.

Container Apps remains an option if we later need scale-to-zero or container-native features.

## Project Structure

```
copilot-tracker/
├── src/
│   ├── CopilotTracker.Server/           # ASP.NET Core host
│   │   ├── Program.cs                   # DI, auth, MCP, static files, routing
│   │   ├── Controllers/                 # REST API controllers
│   │   ├── Mcp/                         # MCP tool definitions
│   │   ├── Auth/                        # Token validation, user extraction
│   │   ├── BackgroundServices/          # Stale session cleanup (IHostedService)
│   │   └── wwwroot/                     # Dashboard build output
│   ├── CopilotTracker.Core/             # Shared: models, interfaces, services
│   │   ├── Models/
│   │   ├── Interfaces/
│   │   └── Services/
│   └── CopilotTracker.Cosmos/           # Cosmos DB repository implementations
├── tests/
│   ├── CopilotTracker.Core.Tests/       # Unit tests (xUnit + Moq)
│   ├── CopilotTracker.Cosmos.Tests/     # Integration tests (Cosmos Emulator)
│   └── CopilotTracker.Server.Tests/     # API + MCP integration tests
├── dashboard/                            # React SPA (Vite + TypeScript)
├── skills/                               # Copilot CLI plugin skills (use MCP, not Cosmos)
├── docs/planning/                        # Architecture, design, progress docs
├── context/                              # AI session continuity context
├── deploy/                               # Bicep templates + deployment scripts
├── .github/workflows/                    # CI (PRs) + CD (merge to main)
└── CopilotTracker.sln
```

## Key Design Decisions

See [decisions.md](decisions.md) for the full ADR log. Highlights:

1. **Repos are storage-oriented.** They know about partition keys and Cosmos SDK types internally, but expose clean interfaces.
2. **Services orchestrate business logic.** Controllers and MCP tools call services, not repos directly.
3. **Eventual consistency for task + log writes.** If the log write fails, the task update still stands.
4. **Health aggregates are cached.** HealthService runs a cross-partition query on a timer and caches the result.
5. **Stale session cleanup is a background service**, not an MCP tool. Runs on a timer inside the host process.

## Related Docs

- [Auth Model](01-auth-model.md)
- [Data Model](02-data-model.md)
- [API Design](03-api-design.md)
- [Test Plan](04-test-plan.md)
- [CI/CD](05-cicd.md)
- [Dashboard](06-dashboard.md)
- [Phase Status](phase-status.md)
- [Decisions](decisions.md)

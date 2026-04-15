# Copilot Session Tracker

A .NET server with a React dashboard that tracks Copilot CLI sessions, tasks, and progress in real time.

## Architecture

```
Copilot CLI  →  PowerShell module  →  .NET Server  →  Cosmos DB
                (auth + MCP calls)     (MCP + REST)    (storage)
                                            ↑
                                       Dashboard
                                      (React SPA)
```

- **MCP endpoint** (`/mcp`) — handles writes via JSON-RPC tool calls (initialize-session, heartbeat, complete-session, set-task, add-log, get-session). Requires Entra ID bearer token.
- **REST API** (`/api/*`) — handles reads (list sessions, tasks, logs, health check)
- **Dashboard** — React/Vite SPA served from the same App Service, authenticates via MSAL redirect flow
- **PowerShell module** — bridges the Copilot CLI to the server. Handles Entra token acquisition via `az account get-access-token` and wraps JSON-RPC calls. **The CLI cannot call the MCP endpoint directly** because it requires Entra bearer auth that the CLI's built-in MCP support doesn't handle.

## Quick Start

### 1. Prerequisites

- Azure CLI (`az`) installed and authenticated
- PowerShell 7+
- Access to the Entra tenant where the tracker is registered

### 2. One-time Azure login

Log into the Entra tenant where your tracker is registered:

```powershell
az login --tenant <your-tenant-id>
```

### 3. Initialize your machine

Run the `initialize-machine` skill from the Copilot CLI:

```
> initialize machine for copilot session tracker
```

This copies the PowerShell module to `~/.copilot/copilot-tracker/` and adds tracking directives to your `copilot-instructions.md`. See [plugins/copilot-session-tracker/](plugins/copilot-session-tracker/) for details.

After initialization, every Copilot CLI session will automatically:
- Register itself with the tracker on startup
- Send heartbeats every 60 seconds
- Report tasks and their outcomes
- Clean up on exit

## Configuration

All settings are stored in `~/.copilot/copilot-tracker-config.json`, created by the `initialize-machine` skill. There are no defaults — you must run the skill to configure:

```json
{
  "serverUrl": "https://your-tracker.azurewebsites.net",
  "tenantId": "your-entra-tenant-id",
  "resourceId": "api://your-app-registration-client-id"
}
```

The module reads this file on every session startup, so changes take effect immediately without restarting your shell.

## Dashboard

Access at: **https://copilot-tracker.azurewebsites.net**

Authenticates via Microsoft Entra ID (MSAL redirect flow). Shows active/completed/stale sessions, tasks, and real-time health metrics.

## MCP Tools (server-side)

These tools are exposed on the `/mcp` endpoint. The PowerShell module wraps them
with authentication. They are **not directly callable** by the Copilot CLI because
the endpoint requires Entra bearer tokens.

| Tool | Description |
|------|-------------|
| `initialize-session` | Register a new session |
| `heartbeat` | Update session heartbeat |
| `complete-session` | Mark session completed |
| `get-session` | Get session details |
| `set-task` | Create or update a task |
| `add-log` | Add a log entry to a task |

## REST API Endpoints

| Method | Path | Auth | Description |
|--------|------|------|-------------|
| GET | `/api/sessions` | Bearer | List sessions (query: machineId, status, since) |
| GET | `/api/sessions/{machineId}/{id}` | Bearer | Get a specific session |
| GET | `/api/tasks` | Bearer | List tasks (query: queueName, status) |
| GET | `/api/tasks/{queueName}/{id}` | Bearer | Get a specific task |
| GET | `/api/tasks/{queueName}/{id}/logs` | Bearer | Get task logs |
| GET | `/api/health` | None | Health check |

## PowerShell Module

| Function | Description |
|----------|-------------|
| `Initialize-TrackerConnection` | Set the server URL and machine ID |
| `Start-TrackerSession` | Create a new session and start the heartbeat |
| `Send-TrackerHeartbeat` | Manually send a heartbeat (normally automatic) |
| `Complete-TrackerSession` | Mark session completed, stop heartbeat |
| `Get-TrackerSession` | Fetch session details via REST API |
| `Set-TrackerTask` | Create or update a task |
| `Add-TrackerLog` | Append a log entry to a task |

## Project Structure

```
src/
  CopilotTracker.Server/     .NET web server (MCP + REST + SPA host)
  CopilotTracker.Core/       Domain models, services, interfaces
  CopilotTracker.Cosmos/     Cosmos DB repository implementations
dashboard/                   React/Vite SPA
plugins/
  copilot-session-tracker/   Installable Copilot CLI plugin
    shared/                  PowerShell module + startup script (canonical source)
    skills/initialize-machine/  Machine setup skill (SKILL.md)
    templates/               copilot-instructions.md snippet template
deploy/                      Bicep IaC + setup scripts
tests/                       .NET + PowerShell tests
```

# Copilot Tracker Skills

PowerShell module and startup scripts for integrating Copilot CLI with the Copilot Session Tracker.

## Architecture

The Copilot Tracker has a .NET server at its center:

- **MCP endpoint** (`/mcp`) handles write operations via JSON-RPC tool calls
  (initialize-session, heartbeat, complete-session, set-task, add-log)
- **REST API** (`/api/*`) handles read-only queries
  (list sessions, get session, list tasks, get task logs)
- **Auth**: Entra ID bearer tokens obtained via `az account get-access-token`

The PowerShell module calls the MCP endpoint for writes and the REST API for reads.
All Cosmos DB access happens server-side; the module never talks to Cosmos directly.

## Prerequisites

- Azure CLI (`az login` for authentication)
- PowerShell 7+

## Configuration

The server URL is resolved in this order:

1. Explicit `-BaseUrl` parameter
2. `COPILOT_TRACKER_URL` environment variable
3. Default: `https://copilot-tracker.azurewebsites.net`

For local development, set the env var or pass the parameter:

```powershell
$env:COPILOT_TRACKER_URL = "http://localhost:5000"
```

## Files

| File | Purpose |
|------|---------|
| `shared/CopilotTracker.psm1` | Main PowerShell module (all exported functions) |
| `shared/Start-TrackerSession.ps1` | Startup script called from `copilot-instructions.md` |

## Usage

The module is loaded automatically by Copilot CLI via the custom instructions
in `.github/copilot-instructions.md`. For manual or scripted usage:

```powershell
Import-Module ./skills/shared/CopilotTracker.psm1

# Connect (uses env var or default if no parameter)
Initialize-TrackerConnection -BaseUrl "http://localhost:5000"

# Start a session
$sid = Start-TrackerSession -Repo "https://github.com/me/myrepo" -Branch "main"

# Report a task
$tid = Set-TrackerTask -Title "Building feature" -Status "started"
Set-TrackerTask -TaskId $tid -Title "Building feature" -Status "done" -Result "All tests pass"

# Add a progress log
Add-TrackerLog -TaskId $tid -LogType "progress" -Message "Step 2 of 3 complete"

# Read session details (uses REST API)
Get-TrackerSession -SessionId $sid

# End the session
Complete-TrackerSession -Summary "Feature complete"
```

## Exported Functions

| Function | Description |
|----------|-------------|
| `Initialize-TrackerConnection` | Set the server URL and machine ID |
| `Start-TrackerSession` | Create a new session and start the heartbeat job |
| `Send-TrackerHeartbeat` | Manually send a heartbeat (normally automatic) |
| `Complete-TrackerSession` | Mark the session as completed and stop the heartbeat |
| `Get-TrackerSession` | Fetch session details via REST API |
| `Set-TrackerTask` | Create or update a task |
| `Add-TrackerLog` | Append a log entry to a task |

## MCP Tools (server-side)

These are the tools exposed on the `/mcp` endpoint. The PowerShell module wraps them,
but Copilot CLI can also call them directly as MCP tool calls:

| Tool | Description |
|------|-------------|
| `initialize-session` | Register a new session |
| `heartbeat` | Update session heartbeat |
| `complete-session` | Mark session completed |
| `get-session` | Get session details |
| `set-task` | Create or update a task |
| `add-log` | Add a log entry to a task |

## REST API Endpoints (read-only)

| Method | Path | Description |
|--------|------|-------------|
| GET | `/api/sessions` | List sessions (query: machineId, status, since) |
| GET | `/api/sessions/{machineId}/{id}` | Get a specific session |
| GET | `/api/tasks` | List tasks (query: queueName, status) |
| GET | `/api/tasks/{queueName}/{id}` | Get a specific task |
| GET | `/api/tasks/{queueName}/{id}/logs` | Get task logs |
| GET | `/api/health` | Health check (no auth required) |

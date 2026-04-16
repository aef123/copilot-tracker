# Change 2: Hooks-Based Tracking Migration

## Problem

The current tracking approach requires the Copilot agent to actively call PowerShell scripts to start sessions, report tasks, and send heartbeats. This means:
- Every copilot-instructions.md must include tracking directives
- The agent must spend context on tracking calls
- Sessions and tasks can be missed if the agent doesn't follow instructions
- The tracking behavior depends on agent compliance

## Approach

Switch to Copilot CLI hooks, which fire automatically at key lifecycle events. The hooks call PowerShell scripts that POST to the server. This removes all agent awareness of tracking. The copilot-instructions.md tracking snippet becomes unnecessary.

### Hooks We'll Use

| Hook | Maps To | Description |
|------|---------|-------------|
| `sessionStart` | Session.Initialize | New session created |
| `sessionEnd` | Session.Complete | Session ended |
| `userPromptSubmitted` | Prompt.Create | New prompt record (formerly Task) |
| `agentStop` | Prompt.Complete | Agent finished processing prompt |
| `subagentStart` | PromptLog.Create | Sub-agent spawned |
| `subagentStop` | PromptLog.Create | Sub-agent finished |
| `notification` | PromptLog.Create | UI notification fired |

### Naming Changes

| Old Name | New Name | Reason |
|----------|----------|--------|
| TrackerTask | Prompt | Hooks capture user prompts, not arbitrary tasks |
| TaskLog | PromptLog | Logs tied to prompts |
| TaskService | PromptService | Service rename |
| TaskLogService | PromptLogService | Service rename |
| TasksController | PromptsController | Controller rename |
| /api/tasks | /api/prompts | API route rename |
| tasks (Cosmos container) | prompts | Container rename |
| taskLogs (Cosmos container) | promptLogs | Container rename |
| Set-TrackerTask | Removed | Hooks replace this |
| Add-TrackerLog | Removed | Hooks replace this |

### Prompt ID Assignment

Hooks don't provide a prompt/task ID. The server handles this:

1. **`userPromptSubmitted`**: Server creates a new Prompt with a server-generated GUID. Status = "started".
2. **`agentStop`**: Server finds the most recent active (started, not completed) Prompt for the session. Marks it as "done". If no active prompt exists, this is a no-op (or log a warning).
3. **`subagentStart` / `subagentStop` / `notification`**: Server finds the most recent active Prompt for the session. Creates a PromptLog tied to that Prompt.
   - If no active prompt exists (prompt already completed), create a new Prompt with title = "MISSED START", tie the log to it.
   - Subsequent orphaned events for the same session continue to use this "MISSED START" prompt until a new `userPromptSubmitted` creates a real prompt.

## Database Changes

### New Cosmos Containers

**Container: `prompts`**
- Partition Key: `/sessionId`
- Rationale: All prompt lookups are session-scoped ("find most recent prompt for session X")

**Container: `promptLogs`**
- Partition Key: `/promptId`
- Rationale: All log lookups are prompt-scoped

### Prompt Model

```csharp
public class Prompt
{
    public string Id { get; set; }           // Server-generated GUID
    public string SessionId { get; set; }    // Partition key, from hook sessionId
    public string PromptText { get; set; }   // From userPromptSubmitted.prompt
    public string Cwd { get; set; }          // Working directory from hook
    public string Status { get; set; }       // "started" | "done"
    public string? Result { get; set; }      // Optional completion info
    public string UserId { get; set; }       // From bearer token
    public string CreatedBy { get; set; }    // Display name from token
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
}
```

### PromptLog Model

```csharp
public class PromptLog
{
    public string Id { get; set; }           // Auto-generated GUID
    public string PromptId { get; set; }     // Partition key
    public string SessionId { get; set; }    // For cross-reference
    public string LogType { get; set; }      // "subagent_start" | "subagent_stop" | "notification" | "agent_stop"
    public string Message { get; set; }      // Descriptive message (agent name, notification text, etc.)
    public string? AgentName { get; set; }   // For subagent events
    public string? NotificationType { get; set; }  // For notification events
    public DateTime Timestamp { get; set; }
}
```

### Bicep Changes

**File:** `deploy/main.bicep`

Add two new containers to the Cosmos database:
```bicep
{
  name: 'prompts'
  partitionKeyPath: '/sessionId'
}
{
  name: 'promptLogs'
  partitionKeyPath: '/promptId'
}
```

Remove (or keep for backward compat) the old `tasks` and `taskLogs` containers. Recommendation: keep them in Bicep for now, remove in a future cleanup.

## Server Changes

### New: HooksController

**File:** `src/CopilotTracker.Server/Controllers/HooksController.cs`

```
POST /api/hooks/{hookType}
```

Single endpoint that accepts any hook payload. Routes to appropriate service based on hookType:

- `sessionStart` → SessionService.InitializeSessionAsync
- `sessionEnd` → SessionService.CompleteSessionAsync
- `userPromptSubmitted` → PromptService.CreatePromptAsync
- `agentStop` → PromptService.CompleteActivePromptAsync
- `subagentStart` → PromptService.LogSubagentStartAsync
- `subagentStop` → PromptService.LogSubagentStopAsync
- `notification` → PromptService.LogNotificationAsync

Request body is the raw hook JSON payload. The controller deserializes based on hookType.

### Hook Payload DTOs

```csharp
public class SessionStartHook
{
    public string SessionId { get; set; }
    public long Timestamp { get; set; }
    public string Cwd { get; set; }
    public string Source { get; set; }
    public string? InitialPrompt { get; set; }
}

public class SessionEndHook
{
    public string SessionId { get; set; }
    public long Timestamp { get; set; }
    public string Cwd { get; set; }
    public string Reason { get; set; }
}

public class UserPromptSubmittedHook
{
    public string SessionId { get; set; }
    public long Timestamp { get; set; }
    public string Cwd { get; set; }
    public string Prompt { get; set; }
}

public class AgentStopHook
{
    public string SessionId { get; set; }
    public long Timestamp { get; set; }
    public string Cwd { get; set; }
    public string? TranscriptPath { get; set; }
    public string StopReason { get; set; }
}

public class SubagentStartHook
{
    public string SessionId { get; set; }
    public long Timestamp { get; set; }
    public string Cwd { get; set; }
    public string AgentName { get; set; }
    public string? AgentDisplayName { get; set; }
    public string? AgentDescription { get; set; }
}

public class SubagentStopHook
{
    public string SessionId { get; set; }
    public long Timestamp { get; set; }
    public string Cwd { get; set; }
    public string AgentName { get; set; }
    public string? AgentDisplayName { get; set; }
    public string StopReason { get; set; }
}

public class NotificationHook
{
    public string SessionId { get; set; }
    public long Timestamp { get; set; }
    public string Cwd { get; set; }
    public string? HookEventName { get; set; }
    public string Message { get; set; }
    public string? Title { get; set; }
    public string? NotificationType { get; set; }
}
```

### Session Service Changes

The existing `SessionService` needs adaptation for hooks:

- `InitializeSessionAsync` currently takes `machineId, repository, branch`. Hooks provide `sessionId, cwd, source, initialPrompt`.
  - Derive machineId from `Environment.MachineName` (sent by hook script) or from the session payload
  - The hook script will add `machineName` to the payload before posting
  - Repository and branch can be derived from CWD (git remote/branch) by the hook script

- `CompleteSessionAsync` currently takes `machineId, sessionId, summary`. Hooks provide `sessionId, reason`.
  - Need to look up session by sessionId (may need cross-partition query or include machineId in hook payload)
  - **Decision:** Hook scripts will include `machineName` in every payload to avoid cross-partition queries

### PromptService (New)

**File:** `src/CopilotTracker.Core/Services/PromptService.cs`

Key methods:

```csharp
// Create a new prompt from userPromptSubmitted hook
Task<Prompt> CreatePromptAsync(string sessionId, string promptText, string cwd, string userId, string createdBy);

// Complete the most recent active prompt for a session (from agentStop)
Task<Prompt?> CompleteActivePromptAsync(string sessionId);

// Find or create the active prompt for a session (for subagent/notification events)
// If no active prompt, creates a "MISSED START" placeholder
Task<Prompt> GetOrCreateActivePromptAsync(string sessionId, string userId, string createdBy);

// Read methods for dashboard
Task<Prompt?> GetAsync(string sessionId, string id);
Task<PagedResult<Prompt>> ListAsync(string? sessionId, string? status, DateTime? since, string? continuationToken, int pageSize);
Task<IReadOnlyList<Prompt>> GetBySessionAsync(string sessionId);
```

### PromptLogService (New)

**File:** `src/CopilotTracker.Core/Services/PromptLogService.cs`

```csharp
Task<PromptLog> AddLogAsync(string promptId, string sessionId, string logType, string message, string? agentName, string? notificationType);
Task<IReadOnlyList<PromptLog>> GetLogsAsync(string promptId);
Task<PagedResult<PromptLog>> GetLogsPagedAsync(string promptId, string? continuationToken, int pageSize);
```

### Cosmos Repositories (New)

**CosmosPromptRepository** (container: prompts, partition key: /sessionId)
- Create, Get, Update, GetBySession, List, GetActiveBySession (status="started", order by createdAt desc, take 1)

**CosmosPromptLogRepository** (container: promptLogs, partition key: /promptId)
- Create, GetByPrompt, GetByPromptPaged

### REST Controller for Dashboard

**File:** `src/CopilotTracker.Server/Controllers/PromptsController.cs`

```
GET /api/prompts                         → List prompts with filters
GET /api/prompts/{sessionId}/{id}        → Get specific prompt
GET /api/prompts/{sessionId}/{id}/logs   → Get prompt logs
```

Note: Partition key is sessionId (not queueName as before). Routes reflect this.

### MCP Tools and Old Controllers

- Remove or deprecate `TasksController` and old MCP task tools
- Remove old `TrackerTools.cs` MCP methods for set-task and add-log
- Keep MCP session tools temporarily (sessionStart/sessionEnd hooks replace these too, but MCP can remain as backup)

### Health Service Update

Update `HealthService` to count prompts instead of tasks:
- Total prompts, active prompts (instead of total tasks, active tasks)

## Hook Scripts

### Universal Hook Handler

**File:** `plugins/copilot-session-tracker/shared/Invoke-TrackerHook.ps1`

A single PowerShell script used by all hooks. The hook type is passed as a parameter.

```powershell
param(
    [Parameter(Mandatory=$true)]
    [string]$HookType
)

$ErrorActionPreference = "Stop"

try {
    # Read stdin (hook payload)
    $inputJson = [Console]::In.ReadToEnd()
    $payload = $inputJson | ConvertFrom-Json

    # Load config
    $configPath = Join-Path $env:USERPROFILE ".copilot\copilot-tracker-config.json"
    if (-not (Test-Path $configPath)) { exit 0 }
    $config = Get-Content $configPath -Raw | ConvertFrom-Json

    # Acquire token (cached)
    $token = Get-TrackerToken -Config $config

    # Enrich payload with machine info
    $payload | Add-Member -NotePropertyName "machineName" -NotePropertyValue $env:COMPUTERNAME -Force

    # For sessionStart: add git info
    if ($HookType -eq "sessionStart") {
        try {
            $repo = git -C $payload.cwd remote get-url origin 2>$null
            $branch = git -C $payload.cwd branch --show-current 2>$null
            $payload | Add-Member -NotePropertyName "repository" -NotePropertyValue $repo -Force
            $payload | Add-Member -NotePropertyName "branch" -NotePropertyValue $branch -Force
        } catch { }
    }

    # POST to server
    $body = $payload | ConvertTo-Json -Depth 10
    $headers = @{
        "Authorization" = "Bearer $token"
        "Content-Type"  = "application/json"
    }
    $uri = "$($config.serverUrl)/api/hooks/$HookType"
    Invoke-RestMethod -Uri $uri -Method POST -Headers $headers -Body $body -TimeoutSec 15

    exit 0
} catch {
    # Best-effort: never block the agent
    $errPath = Join-Path $env:USERPROFILE ".copilot\copilot-tracker\hook-errors.log"
    Add-Content -Path $errPath -Value "$(Get-Date): [$HookType] $($_.Exception.Message)" -ErrorAction SilentlyContinue
    exit 0
}
```

### Token Acquisition Helper

**File:** `plugins/copilot-session-tracker/shared/Get-TrackerToken.ps1`

Separate script (dot-sourced) for token acquisition with caching:

```powershell
function Get-TrackerToken {
    param([object]$Config)

    $cachePath = Join-Path $env:USERPROFILE ".copilot\copilot-tracker\.token-cache.json"

    # Check cache
    if (Test-Path $cachePath) {
        $cache = Get-Content $cachePath -Raw | ConvertFrom-Json
        if ([datetime]$cache.expiresOn -gt (Get-Date).AddMinutes(5)) {
            return $cache.accessToken
        }
    }

    if ($Config.authMode -eq "certificate") {
        # Certificate-based token acquisition (see Change 1 plan)
        $token = Get-CertificateToken -Config $Config
    } else {
        # Azure CLI token
        $tokenResponse = az account get-access-token --resource $Config.resourceId --query "{accessToken:accessToken,expiresOn:expiresOn}" -o json | ConvertFrom-Json
        $token = $tokenResponse.accessToken
        $expiresOn = $tokenResponse.expiresOn
    }

    # Cache token
    @{ accessToken = $token; expiresOn = $expiresOn } | ConvertTo-Json | Set-Content $cachePath -Force

    return $token
}
```

### hooks.json Generation

The initialize-machine skill generates `~/.copilot/hooks/hooks.json`:

```json
{
  "version": 1,
  "hooks": {
    "sessionStart": [
      {
        "type": "command",
        "powershell": "powershell -ExecutionPolicy Bypass -File \"C:\\Users\\<user>\\.copilot\\copilot-tracker\\Invoke-TrackerHook.ps1\" -HookType sessionStart",
        "timeoutSec": 15
      }
    ],
    "sessionEnd": [
      {
        "type": "command",
        "powershell": "powershell -ExecutionPolicy Bypass -File \"C:\\Users\\<user>\\.copilot\\copilot-tracker\\Invoke-TrackerHook.ps1\" -HookType sessionEnd",
        "timeoutSec": 15
      }
    ],
    "userPromptSubmitted": [
      {
        "type": "command",
        "powershell": "powershell -ExecutionPolicy Bypass -File \"C:\\Users\\<user>\\.copilot\\copilot-tracker\\Invoke-TrackerHook.ps1\" -HookType userPromptSubmitted",
        "timeoutSec": 15
      }
    ],
    "agentStop": [
      {
        "type": "command",
        "powershell": "powershell -ExecutionPolicy Bypass -File \"C:\\Users\\<user>\\.copilot\\copilot-tracker\\Invoke-TrackerHook.ps1\" -HookType agentStop",
        "timeoutSec": 15
      }
    ],
    "subagentStart": [
      {
        "type": "command",
        "powershell": "powershell -ExecutionPolicy Bypass -File \"C:\\Users\\<user>\\.copilot\\copilot-tracker\\Invoke-TrackerHook.ps1\" -HookType subagentStart",
        "timeoutSec": 15
      }
    ],
    "subagentStop": [
      {
        "type": "command",
        "powershell": "powershell -ExecutionPolicy Bypass -File \"C:\\Users\\<user>\\.copilot\\copilot-tracker\\Invoke-TrackerHook.ps1\" -HookType subagentStop",
        "timeoutSec": 15
      }
    ],
    "notification": [
      {
        "type": "command",
        "powershell": "powershell -ExecutionPolicy Bypass -File \"C:\\Users\\<user>\\.copilot\\copilot-tracker\\Invoke-TrackerHook.ps1\" -HookType notification",
        "timeoutSec": 15
      }
    ]
  }
}
```

## Dashboard Changes

### Type Renames

**File:** `dashboard/src/api/types.ts`
- `TrackerTask` → `Prompt`
- `TaskLog` → `PromptLog`
- Update all field names accordingly

**File:** `dashboard/src/api/tasksApi.ts` → `dashboard/src/api/promptsApi.ts`
- `listTasks()` → `listPrompts()` using `/api/prompts`
- `getTask()` → `getPrompt()` using `/api/prompts/{sessionId}/{id}`
- `getTaskLogs()` → `getPromptLogs()` using `/api/prompts/{sessionId}/{id}/logs`

### Component Renames

- `TaskDetail.tsx` → `PromptDetail.tsx` (update all references)
- `SessionDetail.tsx` → update task references to prompt
- `HealthDashboard.tsx` → update task metrics to prompt metrics
- `App.tsx` → update routes: `/tasks/:queueName/:id` → `/prompts/:sessionId/:id`

### Health Summary Update

`HealthSummary` type: rename `totalTasks` → `totalPrompts`, `activeTasks` → `activePrompts`

## Plugin Changes

### Files to Remove/Replace

| File | Action |
|------|--------|
| `shared/CopilotTracker.psm1` | Replace with simplified auth-only module |
| `shared/Start-TrackerSession.ps1` | Remove (hooks replace this) |
| `templates/copilot-instructions-snippet.md` | Rewrite (remove tracking directives) |

### Files to Add

| File | Purpose |
|------|---------|
| `shared/Invoke-TrackerHook.ps1` | Universal hook handler script |
| `shared/Get-TrackerToken.ps1` | Token acquisition with caching |

### Initialize-Machine Skill Update

The skill now sets up hooks instead of the PowerShell module:

1. Gather parameters (serverUrl, tenantId, resourceId, authMode, etc.)
2. Verify prerequisites
3. Install hook scripts to `~/.copilot/copilot-tracker/`
   - `Invoke-TrackerHook.ps1`
   - `Get-TrackerToken.ps1`
4. Write config at `~/.copilot/copilot-tracker-config.json`
5. Generate `~/.copilot/hooks/hooks.json` (merge with existing if present)
6. Update copilot-instructions.md (remove old tracking snippet, no new snippet needed)
7. Verify: Test a hook call to the server

## Heartbeat Strategy

The current approach uses a background PowerShell job for heartbeats. With hooks, there's no explicit heartbeat mechanism. Options:

**Option A: agentStop as implicit heartbeat.** Each agentStop event updates the session's lastHeartbeat. The stale cleanup threshold is increased.

**Option B: postToolUse as heartbeat.** Every tool use triggers a heartbeat. High volume but very reliable.

**Option C: Keep a lightweight heartbeat in hooks.** The `agentStop` hook updates lastHeartbeat. The `userPromptSubmitted` hook also updates it. Between the two, any active session gets heartbeats every few seconds to minutes.

**Decision: Option C.** The hooks controller updates `LastHeartbeat` on every hook event for the session. This is a simple update and ensures sessions aren't marked stale during active use.

## Test Plan

### Server Unit Tests

#### HooksController Tests
1. POST /api/hooks/sessionStart → creates session, returns 200
2. POST /api/hooks/sessionEnd → completes session, returns 200
3. POST /api/hooks/userPromptSubmitted → creates prompt, returns 200
4. POST /api/hooks/agentStop → completes active prompt, returns 200
5. POST /api/hooks/agentStop with no active prompt → returns 200 (no-op)
6. POST /api/hooks/subagentStart → creates prompt log, returns 200
7. POST /api/hooks/subagentStart with no active prompt → creates MISSED START prompt + log
8. POST /api/hooks/subagentStop → creates prompt log, returns 200
9. POST /api/hooks/notification → creates prompt log, returns 200
10. POST /api/hooks/unknown → returns 400
11. POST /api/hooks/* without auth → returns 401

#### PromptService Tests
1. CreatePromptAsync → generates GUID, sets status=started
2. CompleteActivePromptAsync → finds most recent started prompt, sets status=done
3. CompleteActivePromptAsync with no active prompt → returns null
4. GetOrCreateActivePromptAsync with active prompt → returns it
5. GetOrCreateActivePromptAsync with no active prompt → creates MISSED START
6. GetOrCreateActivePromptAsync called twice with no active → reuses same MISSED START
7. Multiple prompts for session → CompleteActivePromptAsync targets the most recent
8. ListAsync with filters → correct filtering
9. GetBySessionAsync → returns all prompts for session

#### PromptLogService Tests
1. AddLogAsync → creates log with correct fields
2. GetLogsAsync → returns logs ordered by timestamp
3. GetLogsPagedAsync → pagination works

#### HealthService Tests (Updated)
1. Health returns prompt counts instead of task counts

### Cosmos Repository Tests

1. CosmosPromptRepository CRUD operations
2. CosmosPromptRepository.GetActiveBySession → correct partition query
3. CosmosPromptLogRepository CRUD operations

### Dashboard Tests

1. PromptDetail component renders prompt data
2. SessionDetail shows prompts instead of tasks
3. HealthDashboard shows prompt metrics
4. API client calls correct endpoints

### PowerShell Tests

1. Invoke-TrackerHook reads stdin and posts to server
2. Invoke-TrackerHook handles auth failure gracefully
3. Invoke-TrackerHook enriches payload with machineName
4. Get-TrackerToken returns cached token when valid
5. Get-TrackerToken refreshes expired token
6. hooks.json generation produces valid structure

### Integration/E2E Tests

1. Deploy to Azure via CI/CD → smoke test passes
2. Hook script fires on sessionStart → session appears in dashboard
3. Hook script fires on userPromptSubmitted → prompt appears under session
4. Hook script fires on agentStop → prompt marked as done
5. Full lifecycle: sessionStart → prompt → agentStop → sessionEnd
6. Orphan scenario: subagentStart without prompt → MISSED START created

## Implementation Order

1. **Bicep: Add new containers** → commit + push → CI/CD deploys
2. **Server: Models + Repos** → Prompt, PromptLog, Cosmos repositories
3. **Server: Services** → PromptService, PromptLogService
4. **Server: HooksController** → hook endpoint + DTOs
5. **Server: Update existing** → UserContext, HealthService, auth config
6. **Server: PromptsController** → dashboard read endpoint
7. **Tests: All server tests** → unit + integration
8. **Commit + push** → CI/CD deploys server changes
9. **Dashboard: Rename** → types, API, components, routes
10. **Dashboard tests** → verify renames
11. **Plugin: Hook scripts** → Invoke-TrackerHook, Get-TrackerToken
12. **Plugin: Initialize-machine** → hooks setup
13. **Plugin: Cleanup** → remove old scripts, update templates
14. **PowerShell tests** → hook script tests
15. **Commit + push** → final CI/CD deploy
16. **E2E testing** → verify hooks work live
17. **Marketplace sync** → update ai-marketplace repo

## Risks and Mitigations

| Risk | Mitigation |
|------|-----------|
| Hook timeout (30s default) | Set timeoutSec=15, use token cache |
| Cosmos container creation fails | Test Bicep deployment first |
| Session lookup by ID requires machineId | Hook script includes machineName in payload |
| Multiple rapid prompts cause race condition | Server uses hook timestamp ordering, single-partition query |
| MISSED START prompts accumulate | One MISSED START per session until next real prompt |
| Old tasks container data orphaned | Keep old containers + APIs during transition |
| hooks.json conflicts with other hooks | JSON merge: preserve existing, replace tracker entries only |

---

## Review Feedback Incorporated

### RF-1: Hook Ordering and Timestamps

Hooks fire in separate processes and can arrive out of order. The server must:
- Store the hook's `timestamp` field (millisecond Unix timestamp) on Prompt records
- When finding "most recent active prompt," order by hook timestamp, not server CreatedAt
- Make writes idempotent (duplicate hook delivery should not create duplicate records)
- Use sessionId + hook timestamp as a natural ordering key

### RF-2: Deterministic MISSED START

Instead of creating a new MISSED START prompt for each orphaned event:
- Per session, at most **one** MISSED START prompt exists at a time
- When an orphaned event arrives: look for existing MISSED START prompt (status=started) first
- Only create a new MISSED START if none exists
- When a real `userPromptSubmitted` arrives, the MISSED START naturally becomes historical
- This prevents concurrent subagent events from creating multiple MISSED START prompts

### RF-3: Heartbeat Strategy - Add postToolUse

Add `postToolUse` as a heartbeat-only hook. It fires on every tool execution, providing frequent session heartbeat updates. The hook script only sends a lightweight heartbeat (session touch), not a full log entry. This prevents sessions from going stale during long tool runs.

Updated hooks list:
- sessionStart, sessionEnd, userPromptSubmitted, agentStop, subagentStart, subagentStop, notification (full processing)
- postToolUse (heartbeat only - just touches session lastHeartbeat)

### RF-4: Additive Deployment Strategy

Deployment MUST be additive:
1. **Phase 1 deploy:** Add new containers, new endpoints, new controllers. Keep ALL old APIs working.
2. **Phase 2 deploy:** Dashboard switches to new endpoints.
3. **Phase 3 deploy:** Plugin/hooks rollout.
4. **Phase 4 (future):** Remove deprecated old APIs.

Old routes (`/api/tasks`) and old health fields (`totalTasks`, `activeTasks`) remain functional until explicitly removed.

### RF-5: Session Resume/Startup Handling

`sessionStart` fires for `source: "new"`, `"resume"`, and `"startup"`. The server must:
- Use the hook's `sessionId` as the Session.Id (Copilot assigns the GUID)
- For `source=new`: Create new session (current behavior)
- For `source=resume|startup`: If session exists, reopen it (set status back to active, update heartbeat). If not found, create new.
- Do NOT stale existing sessions on resume.

### RF-6: hooks.json Merge Strategy

When initialize-machine generates hooks.json:
1. Read existing `~/.copilot/hooks/hooks.json` if present
2. Parse as JSON, preserve all non-tracker hook entries
3. For each tracker hook type: replace (not append) the tracker entry, identified by the script path containing `copilot-tracker`
4. Validate: all keys are camelCase, no duplicates
5. Write back atomically (temp file + rename)
6. Re-running initialize-machine is idempotent

### RF-7: notification Snake-Case Fields

The notification hook uses `hook_event_name`, `notification_type` (snake_case). The DTO must use `[JsonPropertyName("hook_event_name")]` etc. to bind correctly with System.Text.Json.

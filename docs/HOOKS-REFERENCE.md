# GitHub Copilot CLI Hooks Reference (Undocumented)

> **Last updated:** 2026-04-16
> **Method:** Empirical testing via schema dump hooks + `strings.exe` memory analysis of the Copilot CLI process.
> **Copilot CLI version:** 1.0.29

## Overview

GitHub Copilot CLI supports a hooks system that executes custom shell commands at key points during agent execution. The official documentation covers 6 hook types, but through memory dump analysis and runtime testing, we confirmed **13 hook types** are actually supported, with 7 being completely undocumented.

## Hook Configuration

### File Location

User-level hooks are loaded from:

```
~/.copilot/hooks/hooks.json
```

On Windows:

```
C:\Users\<username>\.copilot\hooks\hooks.json
```

Repository-level hooks go in `.github/hooks/hooks.json` on the default branch.

### File Format

```json
{
  "version": 1,
  "hooks": {
    "<hookType>": [
      {
        "type": "command",
        "powershell": "powershell -ExecutionPolicy Bypass -File \"C:\\path\\to\\script.ps1\"",
        "bash": "./path/to/script.sh",
        "comment": "Optional description",
        "timeoutSec": 10
      }
    ]
  }
}
```

### Important Notes

- Hook type keys are **camelCase only**. PascalCase keys cause the entire hooks.json to fail to load.
- At least one of `bash`, `powershell`, or `command` must be specified per hook entry.
- Use **absolute paths** to scripts. Relative paths resolve from the session CWD, not the hooks directory.
- Default timeout is 30 seconds. Configurable via `timeoutSec`.
- Input JSON is passed via **stdin**. Scripts read it with `[Console]::In.ReadToEnd()` (PowerShell) or `cat` (bash).
- Multiple hooks of the same type execute in array order.
- Unknown hook type keys are silently ignored (not an error, just never fired).

---

## Confirmed Hook Types (13 Total)

### Legend

| Status | Meaning |
|--------|---------|
| 📗 Documented | In official GitHub docs |
| 📙 Undocumented | Not in official docs, confirmed working via testing |

---

## 1. `sessionStart` 📗

Fires when a new session begins or an existing session is resumed.

**When:** Once per session, at the very beginning.

### Input Schema

```json
{
  "sessionId": "cdcfa3e5-7191-4064-a6c6-d4895b01f888",
  "timestamp": 1776373391304,
  "cwd": "d:\\scratch\\hooks",
  "source": "new",
  "initialPrompt": "test"
}
```

### Fields

| Field | Type | Description |
|-------|------|-------------|
| `sessionId` | `string` | Unique GUID for the session (undocumented field) |
| `timestamp` | `number` | Unix timestamp in milliseconds |
| `cwd` | `string` | Current working directory |
| `source` | `string` | How the session started: `"new"`, `"resume"`, or `"startup"` |
| `initialPrompt` | `string` | The user's first prompt text |

### Output

Ignored.

---

## 2. `sessionEnd` 📗

Fires when the session terminates.

**When:** Once per session, at the very end.

### Input Schema

```json
{
  "sessionId": "cdcfa3e5-7191-4064-a6c6-d4895b01f888",
  "timestamp": 1776373549871,
  "cwd": "d:\\scratch\\hooks",
  "reason": "user_exit"
}
```

### Fields

| Field | Type | Description |
|-------|------|-------------|
| `sessionId` | `string` | Session GUID |
| `timestamp` | `number` | Unix timestamp in milliseconds |
| `cwd` | `string` | Current working directory |
| `reason` | `string` | Why the session ended: `"user_exit"`, `"complete"`, `"error"`, `"abort"`, `"timeout"` |

### Output

Ignored.

---

## 3. `userPromptSubmitted` 📗

Fires when the user submits a prompt.

**When:** Once per user message, before the agent processes it.

### Input Schema

```json
{
  "sessionId": "cdcfa3e5-7191-4064-a6c6-d4895b01f888",
  "timestamp": 1776373389933,
  "cwd": "d:\\scratch\\hooks",
  "prompt": "test"
}
```

### Fields

| Field | Type | Description |
|-------|------|-------------|
| `sessionId` | `string` | Session GUID |
| `timestamp` | `number` | Unix timestamp in milliseconds |
| `cwd` | `string` | Current working directory |
| `prompt` | `string` | The exact text the user submitted |

### Output

Ignored.

---

## 4. `preToolUse` 📗

Fires before the agent executes any tool. Can approve or deny the tool execution.

**When:** Before every tool call (powershell, view, edit, create, grep, glob, task, sql, ask_user, report_intent, read_agent, fetch_copilot_cli_documentation, MCP tools, etc.).

### Input Schema

```json
{
  "sessionId": "cdcfa3e5-7191-4064-a6c6-d4895b01f888",
  "timestamp": 1776373399264,
  "cwd": "d:\\scratch\\hooks",
  "toolName": "powershell",
  "toolArgs": "{\"command\": \"npm test\", \"description\": \"Run tests\"}"
}
```

### Fields

| Field | Type | Description |
|-------|------|-------------|
| `sessionId` | `string` | Session GUID |
| `timestamp` | `number` | Unix timestamp in milliseconds |
| `cwd` | `string` | Current working directory |
| `toolName` | `string` | Name of the tool being invoked |
| `toolArgs` | `string` | **JSON string** containing the tool's arguments (note: this is a string, not an object) |

### Observed Tool Names

`powershell`, `view`, `edit`, `create`, `glob`, `grep`, `sql`, `task`, `ask_user`, `report_intent`, `read_agent`, `fetch_copilot_cli_documentation`, and MCP tool names like `ev2-get_service_info`.

### Output (Optional)

```json
{
  "permissionDecision": "deny",
  "permissionDecisionReason": "Dangerous command blocked"
}
```

| Field | Values | Description |
|-------|--------|-------------|
| `permissionDecision` | `"allow"`, `"deny"`, `"ask"` | Only `"deny"` is currently enforced |
| `permissionDecisionReason` | `string` | Human-readable explanation |

---

## 5. `postToolUse` 📗

Fires after a tool completes successfully.

**When:** After every successful tool execution.

### Input Schema

```json
{
  "sessionId": "cdcfa3e5-7191-4064-a6c6-d4895b01f888",
  "timestamp": 1776373401684,
  "cwd": "d:\\scratch\\hooks",
  "toolName": "report_intent",
  "toolArgs": {
    "intent": "Starting session tracker"
  },
  "toolResult": {
    "resultType": "success",
    "textResultForLlm": "Intent logged"
  }
}
```

### Fields

| Field | Type | Description |
|-------|------|-------------|
| `sessionId` | `string` | Session GUID |
| `timestamp` | `number` | Unix timestamp in milliseconds |
| `cwd` | `string` | Current working directory |
| `toolName` | `string` | Name of the tool that executed |
| `toolArgs` | `object` | Tool arguments as a **parsed object** (unlike `preToolUse` which sends a JSON string) |
| `toolResult` | `object` | Result object (see below) |
| `toolResult.resultType` | `string` | `"success"` or `"failure"` |
| `toolResult.textResultForLlm` | `string` | The result text shown to the agent |

### Output

Ignored.

---

## 6. `errorOccurred` 📗

Fires when an error occurs during agent execution.

**When:** On unhandled errors during the session. (Confirmed loaded via memory dump but no sample captured during testing.)

### Input Schema (from documentation)

```json
{
  "timestamp": 1704614800000,
  "cwd": "/path/to/project",
  "error": {
    "message": "Network timeout",
    "name": "TimeoutError",
    "stack": "TimeoutError: Network timeout\n    at ..."
  }
}
```

### Fields

| Field | Type | Description |
|-------|------|-------------|
| `sessionId` | `string` | Session GUID (likely present based on all other hooks) |
| `timestamp` | `number` | Unix timestamp in milliseconds |
| `cwd` | `string` | Current working directory |
| `error.message` | `string` | Error message |
| `error.name` | `string` | Error type/name |
| `error.stack` | `string` | Stack trace (if available) |

### Output

Ignored.

---

## 7. `postToolUseFailure` 📙

Fires when a tool call fails (as opposed to `postToolUse` which fires on success).

**When:** After a tool execution that results in an error.

### Input Schema

```json
{
  "sessionId": "48fd0be1-18f8-459a-83c7-47370786d559",
  "timestamp": 1776374841547,
  "cwd": "d:\\scratch\\hooks",
  "toolName": "ev2-get_service_info",
  "toolArgs": {
    "serviceId": "this-is-not-a-real-guid-just-testing-errors"
  },
  "error": "MCP server 'ev2': An error occurred invoking 'get_service_info'."
}
```

### Fields

| Field | Type | Description |
|-------|------|-------------|
| `sessionId` | `string` | Session GUID |
| `timestamp` | `number` | Unix timestamp in milliseconds |
| `cwd` | `string` | Current working directory |
| `toolName` | `string` | Name of the tool that failed |
| `toolArgs` | `object` | Tool arguments as a **parsed object** |
| `error` | `string` | Error message describing the failure |

### Output

Ignored.

---

## 8. `permissionRequest` 📙

Fires when a tool requires user permission (e.g., running a shell command that hasn't been pre-approved).

**When:** When a permission dialog is about to be shown to the user.

### Input Schema

```json
{
  "hookName": "permissionRequest",
  "sessionId": "cdcfa3e5-7191-4064-a6c6-d4895b01f888",
  "timestamp": 1776373401974,
  "cwd": "d:\\scratch\\hooks",
  "toolName": "powershell",
  "toolInput": {
    "command": "& \"$env:USERPROFILE\\.copilot\\copilot-tracker\\Start-TrackerSession.ps1\" -WorkingDirectory $PWD"
  },
  "permissionSuggestions": []
}
```

### Fields

| Field | Type | Description |
|-------|------|-------------|
| `hookName` | `string` | Always `"permissionRequest"` (unique: this is the only hook that includes its own name) |
| `sessionId` | `string` | Session GUID |
| `timestamp` | `number` | Unix timestamp in milliseconds |
| `cwd` | `string` | Current working directory |
| `toolName` | `string` | The tool requesting permission |
| `toolInput` | `object` | The tool's input arguments as a **parsed object** (note: key is `toolInput`, not `toolArgs`) |
| `permissionSuggestions` | `array` | Suggested permission rules (observed as empty array) |

### Output

Unknown. May support permission decisions similar to `preToolUse`.

---

## 9. `notification` 📙

Fires when Copilot sends a notification to the user (permission prompts, agent status changes, elicitation dialogs).

**When:** On various UI notification events.

### Input Schema

```json
{
  "sessionId": "cdcfa3e5-7191-4064-a6c6-d4895b01f888",
  "timestamp": 1776373403364,
  "cwd": "d:\\scratch\\hooks",
  "hook_event_name": "Notification",
  "message": "Run command: & \"$env:USERPROFILE\\.copilot\\...\"",
  "title": "Permission needed",
  "notification_type": "permission_prompt"
}
```

### Fields

| Field | Type | Description |
|-------|------|-------------|
| `sessionId` | `string` | Session GUID |
| `timestamp` | `number` | Unix timestamp in milliseconds |
| `cwd` | `string` | Current working directory |
| `hook_event_name` | `string` | Always `"Notification"` (PascalCase, interestingly) |
| `message` | `string` | The notification message text |
| `title` | `string` | Notification title |
| `notification_type` | `string` | Type of notification (see below) |

### Observed `notification_type` Values

| Value | Description |
|-------|-------------|
| `permission_prompt` | A tool is asking for user permission |
| `elicitation_dialog` | The agent is asking the user a question (e.g., via `ask_user`) |
| `agent_idle` | A background sub-agent has finished and is idle |

### Output

Ignored.

---

## 10. `subagentStart` 📙

Fires when a sub-agent (rubber-duck, explore, task, general-purpose, etc.) is spawned.

**When:** When the agent launches a sub-agent via the `task` tool.

### Input Schema

```json
{
  "sessionId": "48fd0be1-18f8-459a-83c7-47370786d559",
  "timestamp": 1776374246549,
  "cwd": "d:\\scratch\\hooks",
  "transcriptPath": "",
  "agentName": "rubber-duck",
  "agentDisplayName": "Rubber Duck Agent",
  "agentDescription": "A constructive critic for proposals, designs, implementations, or tests..."
}
```

### Fields

| Field | Type | Description |
|-------|------|-------------|
| `sessionId` | `string` | Session GUID |
| `timestamp` | `number` | Unix timestamp in milliseconds |
| `cwd` | `string` | Current working directory |
| `transcriptPath` | `string` | Path to the agent's transcript (may be empty) |
| `agentName` | `string` | Internal agent identifier (e.g., `"rubber-duck"`, `"explore"`, `"general-purpose"`) |
| `agentDisplayName` | `string` | Human-readable agent name |
| `agentDescription` | `string` | Full description of the agent's purpose |

### Output

Ignored.

---

## 11. `subagentStop` 📙

Fires when a sub-agent finishes execution.

**When:** When a sub-agent completes its task.

### Input Schema

```json
{
  "timestamp": 1776374278858,
  "cwd": "d:\\scratch\\hooks",
  "sessionId": "48fd0be1-18f8-459a-83c7-47370786d559",
  "transcriptPath": "",
  "agentName": "rubber-duck",
  "agentDisplayName": "Rubber Duck Agent",
  "stopReason": "end_turn"
}
```

### Fields

| Field | Type | Description |
|-------|------|-------------|
| `sessionId` | `string` | Session GUID |
| `timestamp` | `number` | Unix timestamp in milliseconds |
| `cwd` | `string` | Current working directory |
| `transcriptPath` | `string` | Path to transcript (may be empty) |
| `agentName` | `string` | Internal agent identifier |
| `agentDisplayName` | `string` | Human-readable agent name |
| `stopReason` | `string` | Why the agent stopped: `"end_turn"` |

### Output

Ignored.

---

## 12. `agentStop` 📙

Fires when the **main agent** finishes a turn (distinct from `subagentStop` which is for child agents).

**When:** After the main agent completes responding to a user prompt.

### Input Schema

```json
{
  "timestamp": 1776373419371,
  "cwd": "d:\\scratch\\hooks",
  "sessionId": "cdcfa3e5-7191-4064-a6c6-d4895b01f888",
  "transcriptPath": "C:\\Users\\afaust\\.copilot\\session-state\\cdcfa3e5-...\\events.jsonl",
  "stopReason": "end_turn"
}
```

### Fields

| Field | Type | Description |
|-------|------|-------------|
| `sessionId` | `string` | Session GUID |
| `timestamp` | `number` | Unix timestamp in milliseconds |
| `cwd` | `string` | Current working directory |
| `transcriptPath` | `string` | Path to the session's events.jsonl transcript |
| `stopReason` | `string` | Why the agent stopped: `"end_turn"` |

### Output

Ignored.

---

## 13. `preCompact` 📙

Fires before context compaction (when the conversation is too long and needs to be summarized).

**When:** Before the agent compacts/summarizes its context window.

Confirmed loaded via memory dump analysis. No sample captured during testing (compaction didn't occur in test sessions).

### Expected Fields

| Field | Type | Description |
|-------|------|-------------|
| `sessionId` | `string` | Session GUID |
| `timestamp` | `number` | Unix timestamp in milliseconds |
| `cwd` | `string` | Current working directory |

### Output

Unknown.

---

## Hook Types That Were NOT Loaded

The following hook types were registered in `hooks.json` but Copilot did **not** load them (confirmed via `strings.exe` memory analysis). These are Claude Code hook types that Copilot does not currently support:

| Hook Key | Claude Code Equivalent | Status |
|----------|----------------------|--------|
| `stop` | `Stop` | Not loaded |
| `stopFailure` | `StopFailure` | Not loaded |
| `permissionDenied` | `PermissionDenied` | Not loaded |
| `taskCreated` | `TaskCreated` | Not loaded |
| `taskCompleted` | `TaskCompleted` | Not loaded |
| `teammateIdle` | `TeammateIdle` | Not loaded |
| `instructionsLoaded` | `InstructionsLoaded` | Not loaded |
| `configChange` | `ConfigChange` | Not loaded |
| `cwdChanged` | `CwdChanged` | Not loaded |
| `fileChanged` | `FileChanged` | Not loaded |
| `worktreeCreate` | `WorktreeCreate` | Not loaded |
| `worktreeRemove` | `WorktreeRemove` | Not loaded |
| `postCompact` | `PostCompact` | Not loaded |
| `elicitation` | `Elicitation` | Not loaded |
| `elicitationResult` | `ElicitationResult` | Not loaded |
| `userPromptSubmit` | `UserPromptSubmit` | Not loaded |

---

## Key Differences from Official Documentation

1. **`sessionId` field** — Present in every hook input but not mentioned in official docs.
2. **`postToolUseFailure`** — Completely undocumented. Provides the error message when a tool fails.
3. **`permissionRequest`** — Undocumented. Fires before permission dialogs. Has unique fields (`hookName`, `toolInput` instead of `toolArgs`, `permissionSuggestions`).
4. **`notification`** — Undocumented. Fires on UI notifications with `notification_type` discriminator.
5. **`subagentStart` / `subagentStop`** — Undocumented. Provide agent metadata including name, display name, and description.
6. **`agentStop`** — Undocumented. Fires when the main agent completes a turn. Includes `transcriptPath` pointing to the session's events.jsonl.
7. **`preCompact`** — Undocumented. Fires before context compaction.
8. **`preToolUse.toolArgs` is a JSON string**, but **`postToolUse.toolArgs` is a parsed object** — inconsistent serialization.
9. **Duplicate hook keys with different casing break hooks.json** — Having both `sessionStart` and `SessionStart` in the same file caused Copilot to reject the entire file. PascalCase keys in isolation were not tested, so it's unclear whether Copilot rejects PascalCase specifically or just duplicate keys with casing differences.

---

## Schema Quirks and Gotchas

### `toolArgs` serialization inconsistency

`preToolUse` sends `toolArgs` as a **JSON-encoded string**:
```json
"toolArgs": "{\"command\": \"npm test\"}"
```

`postToolUse` and `postToolUseFailure` send `toolArgs` as a **parsed object**:
```json
"toolArgs": { "command": "npm test" }
```

Your scripts need to handle both formats.

### `permissionRequest` uses `toolInput`, not `toolArgs`

Unlike all other tool-related hooks, `permissionRequest` sends the tool arguments under the key `toolInput` (as a parsed object), not `toolArgs`.

### `notification.hook_event_name` is PascalCase

The `hook_event_name` field in notification payloads uses PascalCase (`"Notification"`) even though the hook key itself is camelCase (`notification`).

---

## Dump Hook Script

The PowerShell script used to capture these schemas:

```powershell
param(
    [Parameter(Mandatory=$true)]
    [string]$HookType
)

$ErrorActionPreference = "Stop"
$outputDir = "C:\scratch\hooks"

try {
    if (-not (Test-Path $outputDir)) {
        New-Item -ItemType Directory -Path $outputDir -Force | Out-Null
    }

    $inputJson = [Console]::In.ReadToEnd()
    $timestamp = Get-Date -Format "yyyyMMdd_HHmmss_fff"
    $fileName = "${HookType}_${timestamp}.json"
    $outputPath = Join-Path $outputDir $fileName

    try {
        $parsed = $inputJson | ConvertFrom-Json
        $pretty = $parsed | ConvertTo-Json -Depth 20
        Set-Content -Path $outputPath -Value $pretty -Encoding UTF8
    } catch {
        Set-Content -Path $outputPath -Value $inputJson -Encoding UTF8
    }

    exit 0
} catch {
    $errPath = Join-Path $outputDir "hook-errors.log"
    Add-Content -Path $errPath -Value "$(Get-Date): [$HookType] $($_.Exception.Message)"
    exit 0
}
```

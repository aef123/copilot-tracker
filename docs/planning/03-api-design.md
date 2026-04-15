# API Design

## Overview

This document covers every endpoint and tool the server exposes. The REST API powers the dashboard. The MCP tools power the Copilot CLI. Both share the same service layer underneath, so the behavior is identical regardless of which surface you're calling.

All endpoints require Entra ID bearer tokens. All responses are JSON. Timestamps are ISO 8601 UTC. Errors follow RFC 7807 Problem Details.

## REST API

The REST API lives at `/api/*`. Routes are partition-key-aware, meaning the primary "get by ID" endpoints include the partition key in the URL path. This lets the server do efficient Cosmos DB point reads instead of cross-partition queries.

### Sessions

#### List Sessions

```
GET /api/sessions
```

Query parameters:

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| machineId | string | No | Filter to a specific machine |
| status | string | No | Filter by session status (e.g., `active`, `completed`, `stale`) |
| since | string (ISO 8601) | No | Only return sessions updated after this timestamp |

Returns a paginated list of sessions. See [Pagination](#pagination) for how continuation works.

#### Get Session

```
GET /api/sessions/{machineId}/{id}
```

Point read using both the partition key (`machineId`) and the document ID. This is the fastest read path in Cosmos DB. Returns 404 if the session doesn't exist.

### Tasks

#### List Tasks

```
GET /api/tasks
```

Query parameters:

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| queueName | string | No | Filter to a specific queue |
| status | string | No | Filter by task status (e.g., `started`, `done`, `failed`) |

Returns a paginated list of tasks. See [Pagination](#pagination) for how continuation works.

#### Get Task

```
GET /api/tasks/{queueName}/{id}
```

Point read using the partition key (`queueName`) and the document ID. Returns 404 if the task doesn't exist.

#### Get Task Logs

```
GET /api/tasks/{queueName}/{id}/logs
```

Returns all log entries associated with the task. Logs are ordered by timestamp ascending. This endpoint is also paginated.

### Health

```
GET /api/health
```

Returns aggregated counts (active sessions, recent tasks, etc.). The response is cached server-side with a 30-second TTL so it doesn't hammer Cosmos on every dashboard refresh.

## MCP Tools

The MCP server runs at `/mcp` using streamable HTTP transport. The Copilot CLI connects here to manage sessions and report tasks. These are the tools it exposes:

### initialize-session

Registers a new session with the tracker.

**Parameters:**

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| machineId | string | Yes | Identifier for the machine running the session |
| workingDirectory | string | Yes | The directory the session started in |
| repository | string | No | Repository URL, if known |
| branch | string | No | Git branch, if known |

**Behavior:** Creates a new session document in Cosmos DB. Returns the session ID that the client uses for all subsequent calls. If a session already exists for this machine with status `active`, the server can return the existing session rather than creating a duplicate.

### heartbeat

Updates the session's last-seen timestamp.

**Parameters:**

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| sessionId | string | Yes | The session to heartbeat |
| machineId | string | Yes | Partition key for the session |

**Behavior:** Updates the `lastHeartbeat` field on the session document. This is a point write. If the session doesn't exist or is already completed, it returns an error.

### complete-session

Marks a session as completed.

**Parameters:**

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| sessionId | string | Yes | The session to complete |
| machineId | string | Yes | Partition key for the session |

**Behavior:** Sets the session status to `completed` and records the completion timestamp. Idempotent. Calling this on an already-completed session is a no-op, not an error.

### set-task

Creates or updates a task.

**Parameters:**

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| taskId | string | No | If omitted, a new task is created and the ID is returned |
| sessionId | string | Yes | The session this task belongs to |
| queueName | string | Yes | Partition key for the task |
| title | string | Yes | Short description of what the task is doing |
| status | string | Yes | One of: `started`, `done`, `failed` |
| result | string | No | Outcome summary (for `done` status) |
| errorMessage | string | No | What went wrong (for `failed` status) |
| source | string | No | Where the task came from (e.g., `queue`, `prompt`) |

**Behavior:** If `taskId` is provided, updates the existing task. If omitted, creates a new one and returns the generated ID. After updating/creating the task document, the service writes a status-change log entry on a best-effort basis. If the log write fails, the task update still stands. This matches the eventual consistency behavior from the existing PowerShell module.

### add-log

Adds a log entry to a task.

**Parameters:**

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| taskId | string | Yes | The task to log against |
| queueName | string | Yes | Partition key for the task |
| logType | string | Yes | One of: `status_change`, `progress`, `output`, `error`, `heartbeat` |
| message | string | Yes | The log content |

**Behavior:** Appends a timestamped log entry to the task's log container. Returns the log entry ID.

### get-session

Reads the current state of a session.

**Parameters:**

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| sessionId | string | Yes | The session to read |
| machineId | string | Yes | Partition key for the session |

**Behavior:** Point read of the session document. Returns the full session state including status, timestamps, and metadata.

### Stale Session Cleanup

This is NOT an MCP tool. Stale session cleanup runs as a hosted background service (`IHostedService`) on a timer inside the server process. It queries for sessions that haven't sent a heartbeat within the staleness threshold and marks them accordingly. There's no client-facing surface for this.

## Service Layer

Controllers and MCP tool handlers don't talk to repositories directly. They go through the service layer, which owns all the business logic.

| Service | Methods |
|---------|---------|
| SessionService | `InitializeSession`, `Heartbeat`, `CompleteSession`, `GetSession`, `ListSessions`, `CleanupStale` |
| TaskService | `SetTask`, `GetTask`, `ListTasks` |
| TaskLogService | `AddLog`, `GetLogsForTask` |
| HealthService | `GetHealth` (30s TTL cache) |

The `CleanupStale` method on SessionService is called by the background service, not by any endpoint. `GetHealth` uses a simple time-based cache so the dashboard can poll aggressively without creating load on Cosmos.

### Eventual Consistency for Task + Log Writes

When `SetTask` runs, it updates the task document first, then writes a log entry. If the log write fails, the task update still stands. This is intentional. The PowerShell module already behaves this way, and changing it would mean a single flaky log write could block task status updates. That's a worse outcome than a missing log entry.

## Error Handling

All errors use the RFC 7807 Problem Details format:

```json
{
  "type": "https://tools.ietf.org/html/rfc7807",
  "title": "Not Found",
  "status": 404,
  "detail": "Session with ID 'abc-123' was not found in partition 'machine-42'."
}
```

Standard status codes:

| Code | When |
|------|------|
| 400 | Validation errors (missing required fields, bad format) |
| 401 | Missing or invalid bearer token |
| 403 | Token is valid but doesn't have the required scope |
| 404 | Resource not found |
| 409 | Conflict (e.g., trying to create a session that already exists in a non-idempotent way) |
| 500 | Server error (Cosmos DB unreachable, unhandled exception) |

## Pagination

All list endpoints use continuation tokens, not page numbers. This is a direct consequence of using Cosmos DB, which doesn't support offset-based paging efficiently.

**How it works:**

1. Client calls the list endpoint with no continuation token. The server returns the first page of results plus a `continuationToken` field.
2. To get the next page, the client passes that token back via the `continuationToken` query parameter (or a request header, either works).
3. When there are no more pages, `continuationToken` is `null`.

**Response shape for list endpoints:**

```json
{
  "items": [ ... ],
  "continuationToken": "eyJjb250aW51YXRpb24iOiAiLi4uIn0="
}
```

The token is opaque to the client. Don't parse it, don't store it long-term, and don't assume anything about its format. It could change between server versions.

## Auth Requirements

Every endpoint (REST and MCP) requires a valid Entra ID bearer token in the `Authorization` header.

- **401 Unauthorized** if the token is missing, expired, or can't be validated.
- **403 Forbidden** if the token is valid but doesn't carry the required scope.
- User identity is extracted from the token's claims and used to populate audit fields (who created or modified a resource).

The auth model is documented in detail in [01-auth-model.md](01-auth-model.md).

## Related Docs

- [00-architecture.md](00-architecture.md) - Overall system architecture and component diagram
- [01-auth-model.md](01-auth-model.md) - Entra ID auth configuration, scopes, and token flow
- [02-data-model.md](02-data-model.md) - Cosmos DB containers, partition keys, and document schemas

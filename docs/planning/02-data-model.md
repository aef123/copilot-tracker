# Data Model

## Overview

This document defines the Cosmos DB data model for the Copilot Session Tracker. There are three containers that mirror the existing PowerShell module's data model: sessions, tasks, and task logs. The design priorities are simple partition key alignment with real query patterns, serverless cost efficiency, and clean repository abstractions that don't leak Cosmos SDK types into the rest of the codebase.

If you're looking for the API surface that sits on top of this data model, see [03-api-design.md](03-api-design.md). For the broader system architecture, see [00-architecture.md](00-architecture.md).

## Cosmos DB Configuration

| Setting | Value |
|---------|-------|
| Account | Created via Bicep in `rg-copilot-tracker` |
| Region | East US 2 |
| Database | `CopilotTracker` |
| Capacity Mode | Serverless |
| Authentication | RBAC-only (local auth and keys disabled) |

Serverless is the right fit here. Session tracking is bursty, not constant. There's no reason to pay for provisioned throughput when traffic is a handful of developers running Copilot sessions throughout the day. Serverless bills per-request and caps at 1 TB storage, both of which are well within our needs.

RBAC-only means there are no connection strings or account keys floating around. All access goes through Entra ID. The Bicep template disables local authentication entirely, so even if someone digs up an endpoint URL, they can't do anything without a valid token.

## Containers

### sessions

| Property | Value |
|----------|-------|
| Container Name | `sessions` |
| Partition Key | `/machineId` |
| Purpose | Tracks the lifecycle of each Copilot CLI session |

Every time a Copilot CLI session starts, a document lands here. The document gets updated with heartbeats while the session is alive, and gets a final status update when the session ends (or gets marked stale if the heartbeats stop).

### tasks

| Property | Value |
|----------|-------|
| Container Name | `tasks` |
| Partition Key | `/queueName` |
| Purpose | Tracks individual units of work within sessions |

A task represents something the user asked Copilot to do, or something pulled from a work queue. Tasks have a clear start/end lifecycle and carry a result or error message when they complete.

### taskLogs

| Property | Value |
|----------|-------|
| Container Name | `taskLogs` |
| Partition Key | `/taskId` |
| Purpose | Detailed log entries for individual tasks |

Logs are append-only entries tied to a specific task. They capture progress updates, status changes, output, errors, and heartbeats. You'll always query these in the context of a single task, which is why `taskId` is the partition key.

## Document Schemas

### Session

```json
{
  "id": "a1b2c3d4-e5f6-7890-abcd-ef1234567890",
  "machineId": "DESKTOP-ABC123",
  "repository": "https://github.com/aef123/copilot-tracker",
  "branch": "main",
  "status": "active",
  "createdAt": "2025-01-15T10:30:00Z",
  "updatedAt": "2025-01-15T11:45:00Z",
  "lastHeartbeat": "2025-01-15T11:44:30Z",
  "completedAt": null,
  "summary": "Implemented JWT auth module and wrote tests",
  "userId": "00000000-0000-0000-0000-000000000001",
  "createdBy": "Alex Fisher"
}
```

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `id` | string (GUID) | Yes | Unique session identifier |
| `machineId` | string | Yes | Machine identifier. Partition key. |
| `repository` | string | No | Git repository URL |
| `branch` | string | No | Git branch name |
| `status` | string | Yes | One of: `active`, `completed`, `stale` |
| `createdAt` | string (ISO 8601) | Yes | When the session started |
| `updatedAt` | string (ISO 8601) | Yes | Last modification timestamp |
| `lastHeartbeat` | string (ISO 8601) | Yes | Most recent heartbeat timestamp |
| `completedAt` | string (ISO 8601) | No | When the session ended. Null while active. |
| `summary` | string | No | Human-readable session summary |
| `userId` | string (GUID) | Yes | Entra object ID of the user |
| `createdBy` | string | Yes | Display name of the user |

**Status transitions:**

- `active` is the initial state. Set on creation.
- `active` → `completed` when the session ends normally.
- `active` → `stale` when heartbeats stop arriving (cleanup process handles this).

### Task

```json
{
  "id": "f1e2d3c4-b5a6-7890-fedc-ba0987654321",
  "sessionId": "a1b2c3d4-e5f6-7890-abcd-ef1234567890",
  "queueName": "default",
  "title": "Implementing JWT auth module",
  "status": "done",
  "result": "Auth module complete, 12 tests passing",
  "errorMessage": null,
  "source": "prompt",
  "createdAt": "2025-01-15T10:35:00Z",
  "updatedAt": "2025-01-15T10:52:00Z",
  "userId": "00000000-0000-0000-0000-000000000001",
  "createdBy": "Alex Fisher"
}
```

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `id` | string (GUID) | Yes | Unique task identifier |
| `sessionId` | string (GUID) | Yes | Parent session reference |
| `queueName` | string | Yes | Task queue name. Partition key. |
| `title` | string | Yes | Brief description of the task |
| `status` | string | Yes | One of: `started`, `done`, `failed` |
| `result` | string | No | Outcome description. Set when status is `done`. |
| `errorMessage` | string | No | Error details. Set when status is `failed`. |
| `source` | string | Yes | One of: `prompt` (user-initiated) or `queue` (from work queue) |
| `createdAt` | string (ISO 8601) | Yes | When the task was created |
| `updatedAt` | string (ISO 8601) | Yes | Last modification timestamp |
| `userId` | string (GUID) | Yes | Entra object ID of the user |
| `createdBy` | string | Yes | Display name of the user |

**Status transitions:**

- `started` is the initial state.
- `started` → `done` on successful completion.
- `started` → `failed` on error.

Tasks don't go backwards. Once they're `done` or `failed`, that's final.

### TaskLog

```json
{
  "id": "11223344-5566-7788-99aa-bbccddeeff00",
  "taskId": "f1e2d3c4-b5a6-7890-fedc-ba0987654321",
  "logType": "progress",
  "message": "Completed unit tests, moving to integration tests",
  "timestamp": "2025-01-15T10:42:00Z"
}
```

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `id` | string (GUID) | Yes | Unique log entry identifier |
| `taskId` | string (GUID) | Yes | Parent task reference. Partition key. |
| `logType` | string | Yes | One of: `status_change`, `progress`, `output`, `error`, `heartbeat` |
| `message` | string | Yes | Log content |
| `timestamp` | string (ISO 8601) | Yes | When the log entry was created |

TaskLog documents are append-only. You write them and read them. You don't update or delete them.

## Indexing Strategy

Cosmos DB auto-indexes every property by default, and that's fine for most of our query patterns. We add composite indexes where we know we'll be filtering and sorting on multiple fields together.

### Composite Indexes

**sessions container:**

```json
{
  "compositeIndexes": [
    [
      { "path": "/status", "order": "ascending" },
      { "path": "/lastHeartbeat", "order": "ascending" }
    ]
  ]
}
```

This supports the stale session cleanup query: "find all sessions where status is `active` and lastHeartbeat is older than X." Without this composite index, Cosmos would need to do a cross-partition fan-out with post-filtering, which is slow and expensive on serverless.

**tasks container:**

```json
{
  "compositeIndexes": [
    [
      { "path": "/status", "order": "ascending" },
      { "path": "/createdAt", "order": "descending" }
    ]
  ]
}
```

This supports the active task listing query: "find all tasks where status is `started`, ordered by creation time." Same rationale as above.

### Default Indexing

Everything else uses the default Cosmos indexing policy. All properties are automatically indexed with range indexes, which covers our point reads, equality filters, and range queries without any extra configuration.

## Partition Key Design

The partition key choices aren't arbitrary. They're driven by how the data actually gets queried.

### sessions → `/machineId`

Almost every session query is scoped to a single machine. "What sessions are running on this machine?" "Clean up stale sessions on this machine." "Show me the history for this machine." Partitioning by `machineId` means these queries hit a single partition, which is as fast and cheap as it gets in Cosmos.

The one exception is cross-machine reporting ("show me all sessions for this user"). That's a cross-partition query, but it's infrequent and acceptable for a reporting scenario.

### tasks → `/queueName`

Tasks are grouped and queried by queue. "What's in the default queue?" "Show me all pending tasks for queue X." The queue-based partitioning keeps these queries efficient. For tasks created from user prompts (not queued), the `queueName` defaults to `"default"`.

### taskLogs → `/taskId`

Logs are always viewed in the context of a single task. You never query logs across tasks without knowing which task you're looking at first. This makes `taskId` the natural partition key since every log query is a single-partition read.

### Alignment with Existing Model

These partition key choices match the data access patterns already established in the PowerShell module. The module groups sessions by machine, tasks by queue, and logs by task. We're not inventing new patterns here. We're formalizing what already works.

## Repository Interfaces

The data access layer uses a repository pattern to keep Cosmos SDK types out of the API layer. The interfaces live in the core project; the implementations live in `CopilotTracker.Cosmos`.

### ISessionRepository

```csharp
public interface ISessionRepository
{
    Task<Session> CreateAsync(Session session);
    Task<Session?> GetAsync(string id, string machineId);
    Task<Session> UpdateAsync(Session session);
    Task<IReadOnlyList<Session>> GetActiveByMachineAsync(string machineId);
    Task<PagedResult<Session>> GetStaleSessionsAsync(
        DateTime heartbeatBefore, string? continuationToken = null, int pageSize = 50);
}
```

### ITaskRepository

```csharp
public interface ITaskRepository
{
    Task<TrackerTask> CreateAsync(TrackerTask task);
    Task<TrackerTask?> GetAsync(string id, string queueName);
    Task<TrackerTask> UpdateAsync(TrackerTask task);
    Task<PagedResult<TrackerTask>> GetBySessionAsync(
        string sessionId, string? continuationToken = null, int pageSize = 50);
    Task<PagedResult<TrackerTask>> GetByQueueAsync(
        string queueName, string? status = null,
        string? continuationToken = null, int pageSize = 50);
}
```

### ITaskLogRepository

```csharp
public interface ITaskLogRepository
{
    Task<TaskLog> CreateAsync(TaskLog log);
    Task<IReadOnlyList<TaskLog>> GetByTaskAsync(string taskId);
    Task<PagedResult<TaskLog>> GetByTaskPagedAsync(
        string taskId, string? continuationToken = null, int pageSize = 100);
}
```

### Design Notes

- **No Cosmos types leak out.** The interfaces use domain types (`Session`, `TrackerTask`, `TaskLog`) and a generic `PagedResult<T>` wrapper. Callers never see `FeedIterator`, `ItemResponse`, or any other SDK type.
- **Continuation token support.** Paginated methods accept an opaque continuation token string and return it in `PagedResult<T>`. This maps directly to Cosmos DB's continuation token mechanism but doesn't expose the implementation.
- **Async all the way down.** Every method returns `Task<T>`. There are no synchronous alternatives. Cosmos DB operations are inherently async, and we don't hide that.
- **The `TrackerTask` name** avoids collision with `System.Threading.Tasks.Task`. It's a small naming annoyance, but it prevents a much bigger ambiguity problem.

## Related Docs

- [00-architecture.md](00-architecture.md) for the overall system architecture
- [03-api-design.md](03-api-design.md) for the API endpoints that use these repositories
- [decisions.md](decisions.md) for architectural decision records

# Test Plan

## Why This Exists

A build that compiles is not a build that works. A unit test that passes does not mean the feature works end-to-end. This test plan exists to catch the gap between "the code compiles" and "the system actually does what it's supposed to do."

Every feature we build must be validated at multiple layers before it's considered done. The CI pipeline enforces this. A PR can't merge if tests fail, and the test suite covers enough ground that a passing CI run gives real confidence.

## Test Philosophy

1. **Unit tests prove the logic is correct.** Given these inputs, this service produces these outputs. Repos are mocked.
2. **Integration tests prove the pieces connect.** HTTP request hits the controller, flows through the service, hits the repo (mocked or real emulator), and the response comes back correctly shaped with the right status code.
3. **Functional tests prove the system works for a real user.** A browser loads the dashboard, a user clicks things, data appears correctly. The MCP client sends tool calls, sessions and tasks actually persist.
4. **Post-deployment health checks prove it works in production.** The deployed app responds, auth works, Cosmos is reachable.

**The rule: nothing is "done" until it has tests at the appropriate layers, and those tests run in CI.**

---

## Layer 1: Unit Tests

### Backend: `CopilotTracker.Core.Tests` (xUnit + Moq)

Framework: xUnit. Mocking: Moq. Assertions: FluentAssertions.

Services are the primary target. Every public method on every service gets tested. Repos are mocked via their interfaces, so these tests run fast with zero external dependencies.

#### SessionService

| Test | What it validates |
|------|-------------------|
| `InitializeSession_CreatesSession_WithCorrectFields` | machineId, userId, status=active, startTime set, endTime null, lastHeartbeat=startTime |
| `InitializeSession_StampsCallerIdentity` | Entra OID and display name written to createdBy/userId fields from the auth context |
| `InitializeSession_ReturnsSessionId` | Returns the generated session ID string |
| `InitializeSession_CallsRepoUpsert` | Verifies the repo's Upsert method was called exactly once with the correct document |
| `CompleteSession_SetsStatusAndEndTime` | status=completed, endTime set to current UTC |
| `CompleteSession_ClearsCurrentTask` | currentTaskId and currentTaskTitle set to null |
| `CompleteSession_NonexistentSession_Throws` | Throws NotFoundException when session ID doesn't exist |
| `CompleteSession_AlreadyCompleted_Throws` | Throws InvalidOperationException for non-active session |
| `Heartbeat_UpdatesTimestamp` | lastHeartbeat updated to current UTC |
| `Heartbeat_InactiveSession_Throws` | Throws InvalidOperationException when session.status != active |
| `Heartbeat_NonexistentSession_Throws` | Throws NotFoundException |
| `GetSession_ReturnsSession` | Returns the session from the repo |
| `GetSession_NotFound_Throws` | Throws NotFoundException |

#### TaskService

| Test | What it validates |
|------|-------------------|
| `CreateTask_GeneratesId` | New task gets a GUID id |
| `CreateTask_SetsAllFields` | queueName, title, status, createdAt, createdBy, machineId, sessionId all populated |
| `CreateTask_StartsTask_SetsStartedAt` | When status is "started", startedAt is set |
| `CreateTask_QueuedTask_StartedAtNull` | When status is "queued", startedAt is null |
| `CreateTask_CallsRepoUpsert` | Repo Upsert called with correct document |
| `CreateTask_WritesStatusChangeLog` | AddLog called with logType=status_change |
| `CreateTask_LogFailure_DoesNotRollback` | If AddLog throws, the task is still persisted (eventual consistency) |
| `UpdateTask_ChangesStatus` | Existing task status updated |
| `UpdateTask_SetsCompletedAt_OnDone` | completedAt set when status changes to done |
| `UpdateTask_SetsCompletedAt_OnFailed` | completedAt set when status changes to failed |
| `UpdateTask_SetsStartedAt_OnStarted` | startedAt set when status changes to started (if not already set) |
| `UpdateTask_PreservesStartedAt` | startedAt not overwritten if already set |
| `UpdateTask_SetsResult` | result field populated on done |
| `UpdateTask_SetsError` | error field populated on failed |
| `UpdateTask_UpdatesSessionCurrentTask` | session.currentTaskId and currentTaskTitle updated for active task |
| `UpdateTask_ClearsSessionCurrentTask_OnDone` | session.currentTaskId nulled when task completes |
| `UpdateTask_ClearsSessionCurrentTask_OnFailed` | session.currentTaskId nulled when task fails |
| `UpdateTask_SessionUpdateFailure_DoesNotRollback` | Task persists even if session update fails |
| `UpdateTask_NonexistentTask_Throws` | NotFoundException |
| `AddLog_CreatesLogWithCorrectFields` | id, taskId, sessionId, timestamp, type, message all set |
| `AddLog_AcceptsAllLogTypes` | status_change, progress, output, error, heartbeat all valid |
| `AddLog_InvalidLogType_Throws` | Rejects unknown log types |

#### HealthService

| Test | What it validates |
|------|-------------------|
| `GetHealth_ReturnsCounts` | Active sessions, tasks by status (queued, in_progress, done, failed) |
| `GetHealth_IdentifiesStaleSessions` | Sessions with lastHeartbeat > 5 min ago flagged |
| `GetHealth_IdentifiesStuckTasks` | Tasks in_progress for > 30 min flagged |
| `GetHealth_CachesResult` | Second call within TTL returns same object (no repo calls) |
| `GetHealth_RefreshesExpiredCache` | Call after TTL triggers fresh repo queries |
| `GetHealth_HandlesEmptyData` | Returns zeros, not nulls or errors, when no data exists |

#### StaleSessionCleanupService

| Test | What it validates |
|------|-------------------|
| `Execute_MarksStaleSessionsDisconnected` | Sessions active with lastHeartbeat > threshold get status=disconnected, endTime set |
| `Execute_IgnoresCompletedSessions` | Already completed/disconnected sessions untouched |
| `Execute_IgnoresRecentSessions` | Sessions with recent heartbeat untouched |
| `Execute_HandlesEmptyResult` | No stale sessions = no writes, no errors |
| `Execute_ContinuesOnPartialFailure` | If one session update fails, remaining sessions still processed |

#### Model Tests

| Test | What it validates |
|------|-------------------|
| `Session_RequiredFields` | id, machineId, status, startTime cannot be null |
| `Session_ValidStatuses` | Only active, completed, disconnected are valid |
| `TrackerTask_RequiredFields` | id, queueName, title, status cannot be null |
| `TrackerTask_ValidStatuses` | Only queued, started, in_progress, done, failed are valid |
| `TaskLog_RequiredFields` | id, taskId, timestamp, type, message cannot be null |
| `TaskLog_ValidLogTypes` | Only status_change, progress, output, error, heartbeat are valid |

### Dashboard: `dashboard/__tests__/` (Vitest + React Testing Library + msw)

#### Component Tests (`__tests__/components/`)

Every component tested in isolation with mocked data. msw intercepts API calls.

| Component | Test | What it validates |
|-----------|------|-------------------|
| `SessionList` | Renders session rows | Each session appears with machine, status, time |
| `SessionList` | Status badges correct | active=green, completed=gray, disconnected=red |
| `SessionList` | Filter by status | Selecting "active" shows only active sessions |
| `SessionList` | Filter by machine | Selecting a machine shows only that machine's sessions |
| `SessionList` | Click navigates to detail | Clicking a row pushes correct route with machineId and id |
| `SessionList` | Loading state | Shows spinner/skeleton while fetching |
| `SessionList` | Error state | Shows error message when API returns 500 |
| `SessionList` | Empty state | Shows "No sessions found" when list is empty |
| `SessionDetail` | Renders all fields | Session ID, machine, user, status, times, working directory, repo, branch |
| `SessionDetail` | Shows task list | Tasks associated with this session rendered |
| `SessionDetail` | Loading state | Spinner while fetching |
| `SessionDetail` | Not found state | 404 from API shows "Session not found" |
| `TaskList` | Renders task rows | Each task appears with title, status, time, session |
| `TaskList` | Status tabs filter | Clicking "Done" shows only done tasks |
| `TaskList` | All tab shows everything | Default tab shows all statuses |
| `TaskList` | Click navigates to detail | Correct route with queueName and id |
| `TaskList` | Empty state per tab | "No failed tasks" when failed tab is empty |
| `TaskDetail` | Renders task fields | Title, status, result, error, timestamps, session link |
| `TaskDetail` | Shows log entries | Logs rendered in chronological order |
| `TaskDetail` | Empty logs | "No logs" message, not a crash |
| `TaskDetail` | Error field shown on failed | Error message displayed when status=failed |
| `TaskDetail` | Result field shown on done | Result displayed when status=done |
| `HealthPanel` | Renders all counts | Active sessions, queued, in progress, done, failed counts |
| `HealthPanel` | Stale alert visible | Alert shown when stale session count > 0 |
| `HealthPanel` | Stuck alert visible | Alert shown when stuck task count > 0 |
| `HealthPanel` | Zero state | All zeros renders cleanly, no alerts |
| `AuthBar` | Logged out state | Shows login button |
| `AuthBar` | Logged in state | Shows user name and logout button |
| `Layout` | Navigation renders | All nav links present |
| `Layout` | Active route highlighted | Current page's nav link styled differently |

#### API Client Tests (`__tests__/api/`)

Test the typed API client functions in isolation. msw mocks the HTTP layer.

| Test | What it validates |
|------|-------------------|
| `fetchSessions` builds correct URL | Query params for machineId, status, since appended correctly |
| `fetchSessions` handles continuation token | Token passed in header, new token extracted from response |
| `fetchSessions` returns typed array | Response parsed into Session[] correctly |
| `fetchSessionDetail` builds correct URL | `/api/sessions/{machineId}/{id}` path constructed |
| `fetchSessionDetail` returns typed object | Response parsed into Session |
| `fetchSessionDetail` throws on 404 | NotFoundError thrown |
| `fetchTasks` builds correct URL | Query params for queueName, status |
| `fetchTaskDetail` builds correct URL | `/api/tasks/{queueName}/{id}` |
| `fetchTaskLogs` builds correct URL | `/api/tasks/{queueName}/{id}/logs` |
| `fetchHealth` returns typed object | HealthSummary parsed correctly |
| `401 response triggers re-auth` | MSAL acquireTokenSilent called, request retried |
| `401 after retry shows login` | If silent acquisition fails, user redirected to login |
| `500 response throws ApiError` | Error includes status code and message from response body |
| `Network failure throws NetworkError` | Fetch rejection produces descriptive error |
| `Auth header attached` | Every request includes `Authorization: Bearer <token>` |

#### Auth Tests (`__tests__/auth/`)

| Test | What it validates |
|------|-------------------|
| `AuthProvider initializes MSAL` | MSAL PublicClientApplication created with correct config |
| `useAuth hook returns login/logout/token` | Hook exposes expected interface |
| `Login redirects to Entra` | loginRedirect called with correct scopes |
| `Logout clears session` | logoutRedirect called, state cleared |
| `Token acquisition uses correct scope` | `api://<app-id>/CopilotTracker.ReadWrite` requested |
| `Silent token acquisition works` | Returns cached token when available |
| `Silent failure triggers interactive` | Falls back to popup/redirect when silent fails |

---

## Layer 2: Integration Tests

### Backend API: `CopilotTracker.Server.Tests` (xUnit + WebApplicationFactory)

These spin up the real ASP.NET Core HTTP pipeline. The full request flows through middleware, auth, controllers, services, and repos. Repos are backed by in-memory implementations (not mocks), so we're testing the real wiring, not just the happy path through a mock.

**Why in-memory repos instead of mocks:** Mocks verify calls were made. In-memory repos verify the data actually round-trips. If the service writes a session and then reads it back, the in-memory repo confirms the data is consistent. This catches serialization bugs, missing fields, and incorrect IDs that mocks would miss.

#### Auth Integration

| Test | What it validates |
|------|-------------------|
| `NoToken_Returns401` | Any API endpoint without Authorization header returns 401 |
| `InvalidToken_Returns401` | Malformed or expired JWT returns 401 |
| `WrongAudience_Returns401` | Token for different app returns 401 |
| `ValidToken_ExtractsUserClaims` | Controller can read user OID and display name from claims |
| `MissingScope_Returns403` | Valid token without required scope returns 403 |
| `ValidToken_WithScope_Returns200` | Full valid token passes through to controller |

#### Sessions API

| Test | What it validates |
|------|-------------------|
| `GET /api/sessions` returns 200 with list | Response is JSON array, content-type correct |
| `GET /api/sessions` returns empty list when no data | 200 with empty array, not 404 |
| `GET /api/sessions?machineId=X` filters by machine | Only sessions for that machine returned |
| `GET /api/sessions?status=active` filters by status | Only active sessions returned |
| `GET /api/sessions?since=<ISO>` filters by time | Only sessions after timestamp returned |
| `GET /api/sessions` multiple filters combine | machineId + status applied together (AND) |
| `GET /api/sessions` returns continuation token | When more results exist, response header includes token |
| `GET /api/sessions` continuation token fetches next page | Second request with token returns next batch |
| `GET /api/sessions/{machineId}/{id}` returns session | 200 with correct session object |
| `GET /api/sessions/{machineId}/{id}` wrong machine returns 404 | Partition key mismatch = not found |
| `GET /api/sessions/{machineId}/{id}` nonexistent returns 404 | Unknown ID = not found |
| `GET /api/sessions/{machineId}/{id}` response shape | All expected fields present with correct types |

#### Tasks API

| Test | What it validates |
|------|-------------------|
| `GET /api/tasks` returns 200 with list | JSON array of tasks |
| `GET /api/tasks?queueName=X` filters by queue | Only tasks in that queue |
| `GET /api/tasks?status=done` filters by status | Only done tasks |
| `GET /api/tasks` pagination works | Continuation token flow works correctly |
| `GET /api/tasks/{queueName}/{id}` returns task | 200 with correct task |
| `GET /api/tasks/{queueName}/{id}` nonexistent returns 404 | |
| `GET /api/tasks/{queueName}/{id}/logs` returns logs | JSON array sorted by timestamp ascending |
| `GET /api/tasks/{queueName}/{id}/logs` empty logs | 200 with empty array |
| `GET /api/tasks/{queueName}/{id}/logs` nonexistent task | 404 (not 200 with empty array, if the task itself doesn't exist) |

#### Health API

| Test | What it validates |
|------|-------------------|
| `GET /api/health` returns 200 | Response is JSON object |
| `GET /api/health` response shape | Contains: activeSessions, tasks (queued, inProgress, done, failed), alerts (staleSessions, stuckTasks) |
| `GET /api/health` counts match seeded data | Seed 3 active sessions, 2 done tasks: counts should be 3 and 2 |
| `GET /api/health` stale alert triggers | Seed a session with old heartbeat: staleSessions > 0 |
| `GET /api/health` stuck alert triggers | Seed an in_progress task with old startedAt: stuckTasks > 0 |

#### MCP Tools

These test the MCP tool invocations over the streamable HTTP transport. The test client sends MCP tool call requests to `/mcp` and validates responses.

| Test | What it validates |
|------|-------------------|
| `initialize-session` returns sessionId | Tool response contains a valid session ID |
| `initialize-session` persists session | After calling, GET /api/sessions returns the new session |
| `initialize-session` sets correct fields | machineId, userId, status=active all match input |
| `heartbeat` updates timestamp | After calling, session's lastHeartbeat is newer |
| `heartbeat` invalid session returns error | Tool response indicates failure for unknown session |
| `complete-session` marks completed | Session status = completed, endTime set |
| `complete-session` already completed returns error | Can't complete twice |
| `set-task` creates new task | Task persisted, returns taskId |
| `set-task` updates existing task | Status and fields updated |
| `set-task` done clears session current task | Session's currentTaskId nulled |
| `add-log` creates log entry | Log persisted with correct taskId |
| `add-log` invalid task returns error | Unknown taskId handled gracefully |
| **Full lifecycle test** | initialize-session -> set-task(started) -> add-log -> set-task(done) -> complete-session: verify all state transitions |

**The lifecycle test is critical.** It's the single most important integration test because it validates the entire MCP workflow that the CLI will actually use. If this test passes, the core MCP path works.

### Cosmos DB Repos: `CopilotTracker.Cosmos.Tests` (xUnit + Cosmos DB Emulator)

These test the actual Cosmos SDK code against the Cosmos DB Emulator running in Docker. They validate partition key routing, query construction, upsert behavior, and pagination.

**These tests catch:** wrong partition key paths, incorrect query SQL, serialization mismatches between C# models and Cosmos documents, pagination token handling bugs, and missing index configurations.

#### CosmosSessionRepository

| Test | What it validates |
|------|-------------------|
| `Upsert_CreateNew` | New session created, can be read back with all fields correct |
| `Upsert_UpdateExisting` | Existing session updated without creating duplicate |
| `Get_CorrectPartitionKey` | Point read with machineId returns the session |
| `Get_WrongPartitionKey` | Point read with wrong machineId returns not found |
| `Get_NonexistentId` | Unknown ID returns not found |
| `List_All` | Returns all sessions across partitions |
| `List_ByMachineId` | Returns only sessions for that machine (single-partition) |
| `List_ByStatus` | Cross-partition query filters by status correctly |
| `List_BySince` | Filters sessions created after timestamp |
| `List_CombinedFilters` | machineId + status applied together |
| `List_Pagination` | Continuation token returns next page, no duplicates, no missing items |
| `List_EmptyResult` | Returns empty list, not error |

#### CosmosTaskRepository

| Test | What it validates |
|------|-------------------|
| `Upsert_CreateNew` | New task round-trips correctly |
| `Upsert_UpdateExisting` | Updates without duplicating |
| `Get_CorrectPartitionKey` | Point read with queueName works |
| `Get_WrongPartitionKey` | Wrong queueName returns not found |
| `List_ByQueueName` | Single-partition filter |
| `List_ByStatus` | Cross-partition status filter |
| `List_BySessionId` | Tasks for a specific session |
| `List_Pagination` | Continuation token flow |

#### CosmosTaskLogRepository

| Test | What it validates |
|------|-------------------|
| `Create_RoundTrips` | Log doc created and readable |
| `GetByTaskId` | Returns all logs for a task (single-partition) |
| `GetByTaskId_OrderedByTimestamp` | Logs returned in chronological order |
| `GetByTaskId_NoLogs` | Empty list, not error |
| `GetByTaskId_MultipleTasksIsolated` | Logs for task A don't appear in task B query |

---

## Layer 3: Functional Tests

### Dashboard E2E: `dashboard/__tests__/e2e/` (Playwright)

Full stack tests. A real browser talks to a real .NET server. The server uses in-memory repos seeded with known test data. Auth is handled by a test token (server configured to accept a test JWT issuer in test mode).

**These tests catch:** routing bugs, JavaScript runtime errors, missing API calls, broken component rendering, auth flow failures, and UX regressions that unit tests can never find.

#### Test Data Seeding

Standard seed data used across all E2E tests:

```
Machines: DESKTOP-ABC, LAPTOP-XYZ
Sessions:
  - session-1: DESKTOP-ABC, active, recent heartbeat, currentTask=task-1
  - session-2: DESKTOP-ABC, completed, has endTime
  - session-3: LAPTOP-XYZ, active, stale heartbeat (>5 min old)
  - session-4: LAPTOP-XYZ, disconnected

Tasks:
  - task-1: default queue, started, sessionId=session-1, "Building feature X"
  - task-2: default queue, done, sessionId=session-2, result="12 tests passing"
  - task-3: default queue, failed, sessionId=session-2, error="Compile error"
  - task-4: default queue, in_progress, sessionId=session-3, startedAt >30min ago (stuck)
  - task-5: default queue, queued (no session yet)

Logs (for task-2):
  - log-1: status_change, "started: Building feature X"
  - log-2: progress, "Completed unit tests"
  - log-3: status_change, "done: Building feature X"
```

This seed data covers: active, completed, disconnected, stale sessions; queued, started, in_progress, done, failed tasks; a stuck task; and a task with multiple log entries.

#### Session Scenarios

| Scenario | Steps | Expected |
|----------|-------|----------|
| View session list | Navigate to /sessions | All 4 sessions visible with correct status badges |
| Filter active sessions | Click "Active" filter | session-1 and session-3 shown, session-2 and session-4 hidden |
| Filter by machine | Select DESKTOP-ABC | session-1 and session-2 shown, session-3 and session-4 hidden |
| View session detail | Click session-1 | Detail page shows all fields, task-1 listed |
| Session detail shows tasks | Click session-2 | task-2 and task-3 listed (both belong to session-2) |
| Session not found | Navigate to /sessions/FAKE/fake-id | "Session not found" message |

#### Task Scenarios

| Scenario | Steps | Expected |
|----------|-------|----------|
| View task list | Navigate to /tasks | All 5 tasks visible |
| Filter by status tab | Click "Failed" tab | Only task-3 shown |
| Filter by done | Click "Done" tab | Only task-2 shown |
| View task detail | Click task-2 | Detail shows title, status=done, result="12 tests passing" |
| Task detail shows logs | (continued from above) | 3 log entries in chronological order |
| Failed task shows error | Click task-3 | Error field shows "Compile error" |
| Queued task has no session | Click task-5 | No session link displayed |
| Task not found | Navigate to /tasks/default/fake-id | "Task not found" message |

#### Health Panel Scenarios

| Scenario | Steps | Expected |
|----------|-------|----------|
| Health renders counts | Navigate to /dashboard (or wherever health lives) | activeSessions=2, queued=1, inProgress=1, done=1, failed=1 |
| Stale session alert | (from seed data) | Alert: "1 stale session" (session-3) |
| Stuck task alert | (from seed data) | Alert: "1 stuck task" (task-4) |

#### Auth Scenarios

| Scenario | Steps | Expected |
|----------|-------|----------|
| Unauthenticated access | Navigate to any page without token | Redirected to login / login prompt shown |
| Login flow | Click login | MSAL redirect initiated (mocked in test) |
| Post-login data loads | Complete mock login | Dashboard loads with session data |
| Logout | Click logout | Returned to login screen, API calls stop |
| Token expiry | Wait for token TTL (or mock expiry) | User prompted to re-authenticate |

#### Error Scenarios

| Scenario | Steps | Expected |
|----------|-------|----------|
| API server error | Mock /api/sessions to return 500 | Error message displayed, app doesn't crash |
| Network timeout | Mock fetch to reject | "Connection error" displayed |
| Empty data | Seed with no sessions/tasks | "No sessions found" / "No tasks found" messages |
| Slow response | Mock 3-second delay | Loading indicator visible, then data renders |

### MCP Functional Tests: `tests/CopilotTracker.Server.Tests/Mcp/McpFunctionalTests.cs`

These simulate the actual Copilot CLI workflow. A real MCP client connects to the real server over streamable HTTP and executes the full session lifecycle.

| Scenario | Steps | Expected |
|----------|-------|----------|
| Full session lifecycle | initialize-session -> heartbeat -> set-task(started) -> add-log(progress) -> set-task(done) -> complete-session | Each step succeeds. After completion: session=completed, task=done, logs exist. Verify via API endpoints. |
| Multiple sessions | Initialize 2 sessions on different "machines" | Both visible in GET /api/sessions. Filtered correctly by machineId. |
| Task with failure | set-task(started) -> set-task(failed, error="msg") | Task status=failed, error field populated, session currentTask cleared |
| Heartbeat keeps session alive | Initialize, heartbeat 3 times | lastHeartbeat increases each time |
| Stale cleanup runs | Initialize session, wait for cleanup timer | Session with old heartbeat marked disconnected |

**The full session lifecycle test is the single most important functional test.** It validates the entire workflow that the PowerShell module will execute in production.

---

## Layer 4: Post-Deployment Health Checks

These run after CD deploys to App Service. They validate the deployed app is functioning, not just running.

### Smoke Tests (run in CD pipeline after deploy step)

| Check | How | Expected |
|-------|-----|----------|
| App is reachable | `curl https://<app>.azurewebsites.net/api/health` | 200 response |
| Auth is enforced | `curl https://<app>.azurewebsites.net/api/sessions` (no token) | 401 |
| Cosmos connectivity | `/api/health` response includes valid counts (not error) | Health JSON with numeric counts |
| Dashboard loads | `curl https://<app>.azurewebsites.net/` | 200, HTML contains expected `<title>` |
| MCP endpoint exists | `curl -X POST https://<app>.azurewebsites.net/mcp` (no body) | 400 or 405 (not 404, proving the route exists) |

### CD Pipeline Integration

```yaml
# In cd.yml, after deploy step:
- name: Post-deployment smoke tests
  run: |
    APP_URL="https://copilot-tracker.azurewebsites.net"

    # Health endpoint reachable
    STATUS=$(curl -s -o /dev/null -w "%{http_code}" "$APP_URL/api/health")
    if [ "$STATUS" -ne 200 ]; then echo "Health check failed: $STATUS"; exit 1; fi

    # Auth enforced
    STATUS=$(curl -s -o /dev/null -w "%{http_code}" "$APP_URL/api/sessions")
    if [ "$STATUS" -ne 401 ]; then echo "Auth check failed: $STATUS"; exit 1; fi

    # Dashboard loads
    STATUS=$(curl -s -o /dev/null -w "%{http_code}" "$APP_URL/")
    if [ "$STATUS" -ne 200 ]; then echo "Dashboard check failed: $STATUS"; exit 1; fi

    echo "All smoke tests passed"
```

If smoke tests fail, the pipeline fails. This prevents a "deploy succeeded but nothing works" situation.

---

## What Runs Where (Summary)

| Test suite | Layer | CI (PRs)? | CI (merge)? | Post-deploy? | External deps? |
|-----------|-------|-----------|-------------|-------------|----------------|
| Core.Tests (unit) | Unit | Yes | Yes | No | None |
| Dashboard component tests (Vitest) | Unit | Yes | Yes | No | None |
| Dashboard API client tests (msw) | Unit | Yes | Yes | No | None |
| Dashboard auth tests | Unit | Yes | Yes | No | None |
| Server.Tests (WebApplicationFactory) | Integration | Yes | Yes | No | None (in-memory repos) |
| Cosmos.Tests (emulator) | Integration | Yes | Yes | No | Cosmos Emulator (Docker) |
| Dashboard E2E (Playwright) | Functional | Yes | Yes | No | .NET server + browser |
| MCP functional tests | Functional | Yes | Yes | No | .NET server |
| Post-deploy smoke tests | Smoke | No | No | Yes | Deployed App Service |

**Total: Every test suite runs in CI on every PR.** No exceptions. The Cosmos emulator runs as a Docker service container in GitHub Actions. Playwright browsers are installed in CI. The functional tests use the same `WebApplicationFactory` but with a real MCP client, so they don't need external infrastructure.

Post-deploy smoke tests run only after CD completes. They validate the real deployed environment.

---

## CI Workflow Structure

```yaml
# ci.yml - runs on every PR
name: CI
on:
  pull_request:
    branches: [main]

jobs:
  backend-tests:
    runs-on: ubuntu-latest
    services:
      cosmosdb:
        image: mcr.microsoft.com/cosmosdb/linux/azure-cosmos-emulator:latest
        ports: ['8081:8081', '10250-10255:10250-10255']
        options: >-
          --memory 3g --cpus 2
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '10.0.x'
      - run: dotnet restore
      - run: dotnet build --no-restore
      - run: dotnet test tests/CopilotTracker.Core.Tests --no-build --logger trx
      - run: dotnet test tests/CopilotTracker.Server.Tests --no-build --logger trx
      - run: dotnet test tests/CopilotTracker.Cosmos.Tests --no-build --logger trx
        env:
          COSMOS_EMULATOR_ENDPOINT: https://localhost:8081
      - uses: actions/upload-artifact@v4
        if: always()
        with:
          name: backend-test-results
          path: '**/*.trx'

  dashboard-tests:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-node@v4
        with:
          node-version: '22'
      - run: cd dashboard && npm ci
      - run: cd dashboard && npm run test -- --reporter=junit --outputFile=test-results.xml
      - run: cd dashboard && npx playwright install --with-deps
      - run: cd dashboard && npm run test:e2e
      - uses: actions/upload-artifact@v4
        if: always()
        with:
          name: dashboard-test-results
          path: |
            dashboard/test-results.xml
            dashboard/playwright-report/

  # Both jobs must pass for the PR check to succeed
```

---

## Test Coverage Expectations

Not chasing a coverage percentage for its own sake. But these are the minimums:

| Area | Expectation |
|------|-------------|
| Service methods | 100% of public methods tested |
| Repository interface methods | 100% tested via Cosmos emulator |
| API endpoints | 100% of routes tested (happy path + error cases) |
| MCP tools | 100% of tools tested (happy path + error cases) |
| Dashboard components | Every component rendered at least once with data, loading, error, and empty states |
| API client functions | Every function tested for URL construction, response parsing, and error handling |
| Auth flows | Login, logout, token acquisition, expiry all tested |
| E2E scenarios | Every user-facing workflow (session list -> detail, task list -> detail, health panel) |

---

## When Is a Feature "Done"?

A feature is done when ALL of the following are true:

1. The code compiles (obviously)
2. Unit tests pass for the new/changed service methods
3. Integration tests pass for the new/changed API endpoints or MCP tools
4. If it touches the dashboard: component tests pass AND E2E scenario passes
5. If it touches Cosmos: emulator-based repo test passes
6. All existing tests still pass (no regressions)
7. CI pipeline is green

**A feature that compiles but has no tests is not done. A feature with passing unit tests but no integration test is not done. The CI pipeline is the definition of done.**

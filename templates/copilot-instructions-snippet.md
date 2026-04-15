<!-- BEGIN COPILOT SESSION TRACKER -->

# Copilot Session Tracker

You are integrated with the Copilot Session Tracker. Follow these instructions precisely.

## Session Startup (REQUIRED — FIRST ACTION)

You MUST run this script at the very start of every interaction, before doing anything else:

```powershell
& "$env:USERPROFILE\.copilot\copilot-tracker\Start-TrackerSession.ps1" -WorkingDirectory $PWD -Repo "<repo-url-if-known>" -Branch "<branch-if-known>"
```

If the repository URL or branch aren't known, omit those parameters — they're optional.

This single script handles everything automatically:
- Registers your session with the tracking backend
- Starts a background heartbeat (every 60 seconds)
- Registers an exit handler to complete the session on shutdown

If this fails, log a warning and continue with the user's request. Never let tracker errors block the user's work.

**You do NOT need to manually call heartbeat or session-end functions. They are automated.**

## Task Reporting (REQUIRED for non-trivial work)

Whenever the user gives you a prompt that will take more than a few seconds to complete (building, testing, multi-step implementation, research, refactoring, etc.), you MUST report what you're working on. Every piece of work is a "task," whether it came from the user typing a prompt or from the work queue.

**When you start working on something:**
```powershell
$taskId = Set-TrackerTask -Title "Brief description" -Status "started"
```

Save the returned `$taskId`. You'll need it to report completion.

**When you finish successfully:**
```powershell
Set-TrackerTask -TaskId $taskId -Title "Brief description" -Status "done" -Result "One-line outcome"
```

**When something fails:**
```powershell
Set-TrackerTask -TaskId $taskId -Title "Brief description" -Status "failed" -ErrorMessage "What went wrong"
```

**For queued tasks** (pre-registered in the work queue), pass the existing TaskId and QueueName:
```powershell
Set-TrackerTask -TaskId "<queue-task-id>" -QueueName "<queue>" -Title "Task title" -Status "started" -Source "queue"
```

Examples:
- `$taskId = Set-TrackerTask -Title "Implementing JWT auth module" -Status "started"`
- `Set-TrackerTask -TaskId $taskId -Title "Implementing JWT auth module" -Status "done" -Result "Auth module complete, 12 tests passing"`
- `$taskId = Set-TrackerTask -Title "Running test suite" -Status "started"`
- `Set-TrackerTask -TaskId $taskId -Title "Running test suite" -Status "failed" -ErrorMessage "3 compiler errors in auth.cs"`

Use your judgment. Don't report trivial single-command actions. Do report anything that takes real time or has a meaningful outcome.

For detailed progress updates on long-running tasks:
```powershell
Add-TrackerLog -TaskId $taskId -LogType "progress" -Message "Completed unit tests, moving to integration tests"
```

Valid log types: `status_change`, `progress`, `output`, `error`, `heartbeat`.

## Error Handling

If the tracker module fails at any point (network error, token issue, server unreachable, etc.), you MUST NOT let it block the user's actual work. Log a warning and continue. Session tracking is best-effort, not mission-critical. The user's request always takes priority over tracker telemetry.

<!-- END COPILOT SESSION TRACKER -->

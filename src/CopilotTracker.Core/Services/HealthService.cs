namespace CopilotTracker.Core.Services;

using CopilotTracker.Core.Interfaces;
using CopilotTracker.Core.Models;

public class HealthService
{
    private readonly ISessionRepository _sessions;
    private readonly ITaskRepository _tasks;
    private readonly IPromptRepository _prompts;

    private HealthSummary? _cached;
    private DateTime _cachedAt = DateTime.MinValue;
    private readonly TimeSpan _cacheTtl = TimeSpan.FromSeconds(30);
    private readonly SemaphoreSlim _lock = new(1, 1);

    public HealthService(ISessionRepository sessions, ITaskRepository tasks, IPromptRepository prompts)
    {
        _sessions = sessions;
        _tasks = tasks;
        _prompts = prompts;
    }

    public virtual async Task<HealthSummary> GetHealthAsync()
    {
        if (_cached != null && DateTime.UtcNow - _cachedAt < _cacheTtl)
            return _cached;

        await _lock.WaitAsync();
        try
        {
            // Double-check after acquiring lock
            if (_cached != null && DateTime.UtcNow - _cachedAt < _cacheTtl)
                return _cached;

            var activePage = await _sessions.ListAsync(status: SessionStatus.Active, pageSize: 1);
            var completedPage = await _sessions.ListAsync(status: SessionStatus.Closed, pageSize: 1);
            var stalePage = await _sessions.ListAsync(status: SessionStatus.Stale, pageSize: 1);
            var allTasksPage = await _tasks.ListAsync(pageSize: 1);
            var activeTasksPage = await _tasks.ListAsync(status: Models.TaskStatus.Started, pageSize: 1);

            // Count by paging through all results
            int activeSessions = await CountAllAsync(
                token => _sessions.ListAsync(status: SessionStatus.Active, continuationToken: token));
            int completedSessions = await CountAllAsync(
                token => _sessions.ListAsync(status: SessionStatus.Closed, continuationToken: token));
            int staleSessions = await CountAllAsync(
                token => _sessions.ListAsync(status: SessionStatus.Stale, continuationToken: token));
            int totalTasks = await CountAllTasksAsync(
                token => _tasks.ListAsync(continuationToken: token));
            int activeTasks = await CountAllTasksAsync(
                token => _tasks.ListAsync(status: Models.TaskStatus.Started, continuationToken: token));

            int totalPrompts = await CountAllPromptsAsync(
                token => _prompts.ListAsync(continuationToken: token));
            int activePrompts = await CountAllPromptsAsync(
                token => _prompts.ListAsync(status: "started", continuationToken: token));

            _cached = new HealthSummary
            {
                ActiveSessions = activeSessions,
                CompletedSessions = completedSessions,
                StaleSessions = staleSessions,
                TotalTasks = totalTasks,
                ActiveTasks = activeTasks,
                TotalPrompts = totalPrompts,
                ActivePrompts = activePrompts,
                Timestamp = DateTime.UtcNow
            };
            _cachedAt = DateTime.UtcNow;

            return _cached;
        }
        finally
        {
            _lock.Release();
        }
    }

    private static async Task<int> CountAllAsync(
        Func<string?, Task<PagedResult<Session>>> query)
    {
        int count = 0;
        string? token = null;
        do
        {
            var page = await query(token);
            count += page.Items.Count;
            token = page.ContinuationToken;
        } while (token != null);
        return count;
    }

    private static async Task<int> CountAllTasksAsync(
        Func<string?, Task<PagedResult<TrackerTask>>> query)
    {
        int count = 0;
        string? token = null;
        do
        {
            var page = await query(token);
            count += page.Items.Count;
            token = page.ContinuationToken;
        } while (token != null);
        return count;
    }

    private static async Task<int> CountAllPromptsAsync(
        Func<string?, Task<PagedResult<Prompt>>> query)
    {
        int count = 0;
        string? token = null;
        do
        {
            var page = await query(token);
            count += page.Items.Count;
            token = page.ContinuationToken;
        } while (token != null);
        return count;
    }
}

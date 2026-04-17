namespace CopilotTracker.Core.Services;

using CopilotTracker.Core.Interfaces;
using CopilotTracker.Core.Models;
using Microsoft.Extensions.Logging;

public class SessionService
{
    private readonly ISessionRepository _sessions;
    private readonly IPromptRepository _prompts;
    private readonly ILogger<SessionService> _logger;

    public SessionService(ISessionRepository sessions, IPromptRepository prompts, ILogger<SessionService> logger)
    {
        _sessions = sessions;
        _prompts = prompts;
        _logger = logger;
    }

    public virtual async Task<Session> InitializeSessionAsync(
        string machineId, string? repository, string? branch, string userId, string createdBy)
    {
        // Clean up stale sessions on this machine before creating a new one
        var activeSessions = await _sessions.GetActiveByMachineAsync(machineId);
        foreach (var stale in activeSessions)
        {
            stale.Status = SessionStatus.Stale;
            stale.UpdatedAt = DateTime.UtcNow;
            await _sessions.UpdateAsync(stale);
            _logger.LogInformation("Marked session {SessionId} as stale during initialization", stale.Id);
        }

        var session = new Session
        {
            MachineId = machineId,
            Repository = repository,
            Branch = branch,
            Status = SessionStatus.Active,
            UserId = userId,
            CreatedBy = createdBy,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            LastHeartbeat = DateTime.UtcNow
        };

        return await _sessions.CreateAsync(session);
    }

    public virtual async Task<Session> HeartbeatAsync(string id, string machineId)
    {
        var session = await _sessions.GetAsync(id, machineId)
            ?? throw new InvalidOperationException($"Session '{id}' not found.");

        if (session.Status != SessionStatus.Active)
            throw new InvalidOperationException($"Session '{id}' is not active (status: {session.Status}).");

        session.LastHeartbeat = DateTime.UtcNow;
        session.UpdatedAt = DateTime.UtcNow;
        return await _sessions.UpdateAsync(session);
    }

    public virtual async Task<Session> CompleteSessionAsync(string id, string machineId, string? summary)
    {
        var session = await _sessions.GetAsync(id, machineId)
            ?? throw new InvalidOperationException($"Session '{id}' not found.");

        if (session.Status != SessionStatus.Active)
            throw new InvalidOperationException($"Session '{id}' is not active (status: {session.Status}).");

        session.Status = SessionStatus.Completed;
        session.CompletedAt = DateTime.UtcNow;
        session.Summary = summary;
        session.UpdatedAt = DateTime.UtcNow;
        return await _sessions.UpdateAsync(session);
    }

    public virtual async Task<Session?> GetAsync(string id, string machineId)
    {
        return await _sessions.GetAsync(id, machineId);
    }

    public virtual async Task<PagedResult<Session>> ListAsync(
        string? machineId, string? status, string? tool, DateTime? since, string? continuationToken, int pageSize)
    {
        return await _sessions.ListAsync(machineId, status, tool, since, continuationToken, pageSize);
    }

    public virtual async Task<Session> InitializeFromHookAsync(
        string hookSessionId, string machineName, string? repository, string? branch,
        string source, string? initialPrompt, string userId, string createdBy,
        string? tool = null)
    {
        // For "resume" or "startup": check if session already exists
        if (source == "resume" || source == "startup")
        {
            var existing = await _sessions.GetAsync(hookSessionId, machineName);
            if (existing != null)
            {
                existing.Status = SessionStatus.Active;
                existing.Tool = tool ?? existing.Tool;
                existing.LastHeartbeat = DateTime.UtcNow;
                existing.UpdatedAt = DateTime.UtcNow;
                await _sessions.UpdateAsync(existing);
                return existing;
            }
        }

        // For "new" or if session not found: stale any existing active sessions for this machine
        var activeSessions = await _sessions.GetActiveByMachineAsync(machineName);
        foreach (var active in activeSessions)
        {
            active.Status = SessionStatus.Stale;
            active.UpdatedAt = DateTime.UtcNow;
            await _sessions.UpdateAsync(active);
        }

        var session = new Session
        {
            Id = hookSessionId,
            MachineId = machineName,
            Repository = repository,
            Branch = branch,
            Tool = tool,
            Status = SessionStatus.Active,
            UserId = userId,
            CreatedBy = createdBy,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            LastHeartbeat = DateTime.UtcNow
        };

        await _sessions.CreateAsync(session);
        return session;
    }

    public virtual async Task TouchSessionAsync(string sessionId, string machineId)
    {
        try
        {
            var session = await _sessions.GetAsync(sessionId, machineId);
            if (session != null && session.Status == SessionStatus.Active)
            {
                session.LastHeartbeat = DateTime.UtcNow;
                session.UpdatedAt = DateTime.UtcNow;
                await _sessions.UpdateAsync(session);
            }
        }
        catch (Exception)
        {
            // Best-effort heartbeat
        }
    }

    public async Task<int> CleanupStaleSessionsAsync(TimeSpan staleThreshold)
    {
        var cutoff = DateTime.UtcNow - staleThreshold;
        int totalCleaned = 0;
        string? token = null;

        do
        {
            var page = await _sessions.GetStaleSessionsAsync(cutoff, token);

            foreach (var session in page.Items)
            {
                // Check if the session has recent prompt activity before marking stale
                if (await HasRecentActivityAsync(session.Id, cutoff))
                {
                    // Session has recent activity, refresh its heartbeat instead
                    session.LastHeartbeat = DateTime.UtcNow;
                    session.UpdatedAt = DateTime.UtcNow;
                    await _sessions.UpdateAsync(session);
                    _logger.LogDebug(
                        "Session {SessionId} has recent prompt activity, refreshing heartbeat",
                        session.Id);
                    continue;
                }

                session.Status = SessionStatus.Stale;
                session.UpdatedAt = DateTime.UtcNow;
                await _sessions.UpdateAsync(session);
                totalCleaned++;
            }

            token = page.ContinuationToken;
        } while (token != null);

        if (totalCleaned > 0)
            _logger.LogInformation("Cleaned up {Count} stale sessions", totalCleaned);

        return totalCleaned;
    }

    private async Task<bool> HasRecentActivityAsync(string sessionId, DateTime cutoff)
    {
        var prompts = await _prompts.GetBySessionAsync(sessionId);

        // If any prompt is still active (started), the session is definitely not stale
        if (prompts.Any(p => p.Status == "started"))
            return true;

        // If any prompt was created or updated after the cutoff, session is active
        if (prompts.Any(p => p.UpdatedAt > cutoff || p.CreatedAt > cutoff))
            return true;

        return false;
    }
}

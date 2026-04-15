namespace CopilotTracker.Core.Services;

using CopilotTracker.Core.Interfaces;
using CopilotTracker.Core.Models;
using Microsoft.Extensions.Logging;

public class SessionService
{
    private readonly ISessionRepository _sessions;
    private readonly ITaskLogRepository _logs;
    private readonly ILogger<SessionService> _logger;

    public SessionService(ISessionRepository sessions, ITaskLogRepository logs, ILogger<SessionService> logger)
    {
        _sessions = sessions;
        _logs = logs;
        _logger = logger;
    }

    public async Task<Session> InitializeSessionAsync(
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

    public async Task<Session> HeartbeatAsync(string id, string machineId)
    {
        var session = await _sessions.GetAsync(id, machineId)
            ?? throw new InvalidOperationException($"Session '{id}' not found.");

        if (session.Status != SessionStatus.Active)
            throw new InvalidOperationException($"Session '{id}' is not active (status: {session.Status}).");

        session.LastHeartbeat = DateTime.UtcNow;
        session.UpdatedAt = DateTime.UtcNow;
        return await _sessions.UpdateAsync(session);
    }

    public async Task<Session> CompleteSessionAsync(string id, string machineId, string? summary)
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
        string? machineId, string? status, DateTime? since, string? continuationToken, int pageSize)
    {
        return await _sessions.ListAsync(machineId, status, since, continuationToken, pageSize);
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
}

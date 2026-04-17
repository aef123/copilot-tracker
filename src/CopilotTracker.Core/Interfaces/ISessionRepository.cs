namespace CopilotTracker.Core.Interfaces;

using CopilotTracker.Core.Models;

public interface ISessionRepository
{
    Task<Session> CreateAsync(Session session);
    Task<Session?> GetAsync(string id, string machineId);
    Task<Session> UpdateAsync(Session session);
    Task<IReadOnlyList<Session>> GetActiveByMachineAsync(string machineId);
    Task<PagedResult<Session>> ListAsync(string? machineId = null, string? status = null, string? tool = null, DateTime? since = null, string? continuationToken = null, int pageSize = 50, string? statusGroup = null);
    Task<PagedResult<Session>> GetStaleSessionsAsync(DateTime heartbeatBefore, string? continuationToken = null, int pageSize = 50);
}

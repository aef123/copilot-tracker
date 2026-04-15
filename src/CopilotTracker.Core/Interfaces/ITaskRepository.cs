namespace CopilotTracker.Core.Interfaces;

using CopilotTracker.Core.Models;

public interface ITaskRepository
{
    Task<TrackerTask> CreateAsync(TrackerTask task);
    Task<TrackerTask?> GetAsync(string id, string queueName);
    Task<TrackerTask> UpdateAsync(TrackerTask task);
    Task<PagedResult<TrackerTask>> GetBySessionAsync(string sessionId, string? continuationToken = null, int pageSize = 50);
    Task<PagedResult<TrackerTask>> GetByQueueAsync(string queueName, string? status = null, string? continuationToken = null, int pageSize = 50);
    Task<PagedResult<TrackerTask>> ListAsync(string? queueName = null, string? status = null, string? continuationToken = null, int pageSize = 50);
}

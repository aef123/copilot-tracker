namespace CopilotTracker.Core.Interfaces;

using CopilotTracker.Core.Models;

public interface ITaskLogRepository
{
    Task<TaskLog> CreateAsync(TaskLog log);
    Task<IReadOnlyList<TaskLog>> GetByTaskAsync(string taskId);
    Task<PagedResult<TaskLog>> GetByTaskPagedAsync(string taskId, string? continuationToken = null, int pageSize = 100);
}

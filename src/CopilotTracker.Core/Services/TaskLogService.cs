namespace CopilotTracker.Core.Services;

using CopilotTracker.Core.Interfaces;
using CopilotTracker.Core.Models;

public class TaskLogService
{
    private readonly ITaskLogRepository _logs;

    public TaskLogService(ITaskLogRepository logs)
    {
        _logs = logs;
    }

    public async Task<TaskLog> AddLogAsync(string taskId, string logType, string message)
    {
        var log = new TaskLog
        {
            TaskId = taskId,
            LogType = logType,
            Message = message,
            Timestamp = DateTime.UtcNow
        };
        return await _logs.CreateAsync(log);
    }

    public async Task<IReadOnlyList<TaskLog>> GetLogsAsync(string taskId)
    {
        return await _logs.GetByTaskAsync(taskId);
    }

    public virtual async Task<PagedResult<TaskLog>> GetLogsPagedAsync(
        string taskId, string? continuationToken, int pageSize)
    {
        return await _logs.GetByTaskPagedAsync(taskId, continuationToken, pageSize);
    }
}

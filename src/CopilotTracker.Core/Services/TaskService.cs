namespace CopilotTracker.Core.Services;

using CopilotTracker.Core.Interfaces;
using CopilotTracker.Core.Models;
using Microsoft.Extensions.Logging;

public class TaskService
{
    private readonly ITaskRepository _tasks;
    private readonly ITaskLogRepository _logs;
    private readonly ILogger<TaskService> _logger;

    public TaskService(ITaskRepository tasks, ITaskLogRepository logs, ILogger<TaskService> logger)
    {
        _tasks = tasks;
        _logs = logs;
        _logger = logger;
    }

    public async Task<TrackerTask> SetTaskAsync(
        string? taskId, string sessionId, string queueName, string title,
        string status, string? result, string? errorMessage, string source,
        string userId, string createdBy)
    {
        TrackerTask? existing = null;
        if (taskId != null)
        {
            existing = await _tasks.GetAsync(taskId, queueName);
        }

        TrackerTask task;
        string logMessage;

        if (existing != null)
        {
            var oldStatus = existing.Status;
            existing.Title = title;
            existing.Status = status;
            existing.Result = result;
            existing.ErrorMessage = errorMessage;
            existing.UpdatedAt = DateTime.UtcNow;

            task = await _tasks.UpdateAsync(existing);
            logMessage = $"Task status changed from '{oldStatus}' to '{status}'";
        }
        else
        {
            task = new TrackerTask
            {
                Id = taskId ?? Guid.NewGuid().ToString(),
                SessionId = sessionId,
                QueueName = queueName,
                Title = title,
                Status = status,
                Result = result,
                ErrorMessage = errorMessage,
                Source = source,
                UserId = userId,
                CreatedBy = createdBy,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            task = await _tasks.CreateAsync(task);
            logMessage = $"Task created with status '{status}'";
        }

        // Best-effort log write
        try
        {
            await _logs.CreateAsync(new TaskLog
            {
                TaskId = task.Id,
                LogType = LogTypes.StatusChange,
                Message = logMessage,
                Timestamp = DateTime.UtcNow
            });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to write task log for task {TaskId}", task.Id);
        }

        return task;
    }

    public virtual async Task<TrackerTask?> GetAsync(string id, string queueName)
    {
        return await _tasks.GetAsync(id, queueName);
    }

    public virtual async Task<PagedResult<TrackerTask>> ListAsync(
        string? queueName, string? status, string? continuationToken, int pageSize)
    {
        return await _tasks.ListAsync(queueName, status, continuationToken, pageSize);
    }

    public async Task<PagedResult<TrackerTask>> GetBySessionAsync(
        string sessionId, string? continuationToken, int pageSize)
    {
        return await _tasks.GetBySessionAsync(sessionId, continuationToken, pageSize);
    }
}

namespace CopilotTracker.Core.Services;

using CopilotTracker.Core.Interfaces;
using CopilotTracker.Core.Models;
using Microsoft.Extensions.Logging;

public class PromptLogService
{
    private readonly IPromptLogRepository _logs;
    private readonly ILogger<PromptLogService> _logger;

    public PromptLogService(IPromptLogRepository logs, ILogger<PromptLogService> logger)
    {
        _logs = logs;
        _logger = logger;
    }

    public virtual async Task<PromptLog> AddLogAsync(
        string promptId, string sessionId, string logType, string message,
        string? agentName, string? notificationType, long hookTimestamp)
    {
        var log = new PromptLog
        {
            Id = Guid.NewGuid().ToString(),
            PromptId = promptId,
            SessionId = sessionId,
            LogType = logType,
            Message = message,
            AgentName = agentName,
            NotificationType = notificationType,
            HookTimestamp = hookTimestamp,
            Timestamp = DateTime.UtcNow
        };
        return await _logs.CreateAsync(log);
    }

    public async Task<IReadOnlyList<PromptLog>> GetLogsAsync(string promptId)
    {
        return await _logs.GetByPromptAsync(promptId);
    }

    public virtual async Task<PagedResult<PromptLog>> GetLogsPagedAsync(
        string promptId, string? continuationToken, int pageSize)
    {
        return await _logs.GetByPromptPagedAsync(promptId, continuationToken, pageSize);
    }
}

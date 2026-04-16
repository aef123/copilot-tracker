namespace CopilotTracker.Core.Models;

public class PromptLog
{
    public string Id { get; set; } = string.Empty;
    public string PromptId { get; set; } = string.Empty;  // Partition key
    public string SessionId { get; set; } = string.Empty;
    public string LogType { get; set; } = string.Empty;  // "subagent_start" | "subagent_stop" | "notification" | "agent_stop"
    public string Message { get; set; } = string.Empty;
    public string? AgentName { get; set; }
    public string? NotificationType { get; set; }
    public long HookTimestamp { get; set; }  // Unix ms from hook
    public DateTime Timestamp { get; set; }
}

namespace CopilotTracker.Core.Models;

public class TaskLog
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string TaskId { get; set; } = string.Empty;
    public string LogType { get; set; } = LogTypes.Progress;
    public string Message { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}

public static class LogTypes
{
    public const string StatusChange = "status_change";
    public const string Progress = "progress";
    public const string Output = "output";
    public const string Error = "error";
    public const string Heartbeat = "heartbeat";
}

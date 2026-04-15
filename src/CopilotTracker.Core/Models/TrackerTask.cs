namespace CopilotTracker.Core.Models;

public class TrackerTask
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string SessionId { get; set; } = string.Empty;
    public string QueueName { get; set; } = "default";
    public string Title { get; set; } = string.Empty;
    public string Status { get; set; } = TaskStatus.Started;
    public string? Result { get; set; }
    public string? ErrorMessage { get; set; }
    public string Source { get; set; } = TaskSource.Prompt;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public string UserId { get; set; } = string.Empty;
    public string CreatedBy { get; set; } = string.Empty;
}

public static class TaskStatus
{
    public const string Started = "started";
    public const string Done = "done";
    public const string Failed = "failed";
}

public static class TaskSource
{
    public const string Prompt = "prompt";
    public const string Queue = "queue";
}

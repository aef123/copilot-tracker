namespace CopilotTracker.Core.Models;

public class Prompt
{
    public string Id { get; set; } = string.Empty;
    public string SessionId { get; set; } = string.Empty;  // Partition key
    public string PromptText { get; set; } = string.Empty;
    public string? Title { get; set; }
    public string? Cwd { get; set; }
    public string Status { get; set; } = "started";  // "started" | "done"
    public string? Result { get; set; }
    public long HookTimestamp { get; set; }  // Unix ms from hook, used for ordering
    public string UserId { get; set; } = string.Empty;
    public string CreatedBy { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
}

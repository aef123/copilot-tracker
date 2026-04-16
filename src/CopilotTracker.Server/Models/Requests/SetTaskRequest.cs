namespace CopilotTracker.Server.Models.Requests;

public class SetTaskRequest
{
    public required string SessionId { get; set; }
    public required string Title { get; set; }
    public required string Status { get; set; }
    public string? TaskId { get; set; }
    public string QueueName { get; set; } = "default";
    public string? Result { get; set; }
    public string? ErrorMessage { get; set; }
    public string Source { get; set; } = "prompt";
}

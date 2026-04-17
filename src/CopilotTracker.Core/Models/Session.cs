namespace CopilotTracker.Core.Models;

public class Session
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string MachineId { get; set; } = string.Empty;
    public string? Repository { get; set; }
    public string? Branch { get; set; }
    public string? Tool { get; set; }
    public string Status { get; set; } = SessionStatus.Active;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public DateTime LastHeartbeat { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }
    public string? Summary { get; set; }
    public string UserId { get; set; } = string.Empty;
    public string CreatedBy { get; set; } = string.Empty;
}

public static class SessionStatus
{
    public const string Active = "active";
    public const string Completed = "completed";
    public const string Stale = "stale";
}

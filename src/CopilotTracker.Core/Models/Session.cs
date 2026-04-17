namespace CopilotTracker.Core.Models;

public class Session
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string MachineId { get; set; } = string.Empty;
    public string? Repository { get; set; }
    public string? Branch { get; set; }
    public string? Title { get; set; }
    public string? Tool { get; set; }
    public string Status { get; set; } = SessionStatus.Active;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public DateTime LastHeartbeat { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }
    public string? Summary { get; set; }
    public string UserId { get; set; } = string.Empty;
    public string CreatedBy { get; set; } = string.Empty;

    /// <summary>
    /// Enrichment-only field set by the list endpoint. Null when read from Cosmos directly.
    /// Cosmos ignores null values on write, so this won't pollute stored documents.
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("hasActivePrompt")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public bool? HasActivePrompt { get; set; }
}

public static class SessionStatus
{
    public const string Active = "active";
    public const string Closed = "closed";
    public const string Idle = "idle";
    public const string Stale = "stale";

    [Obsolete("Use Closed instead")]
    public const string Completed = "closed";
}

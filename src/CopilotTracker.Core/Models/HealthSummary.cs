namespace CopilotTracker.Core.Models;

public class HealthSummary
{
    public int ActiveSessions { get; set; }
    public int CompletedSessions { get; set; }
    public int StaleSessions { get; set; }
    public int TotalTasks { get; set; }
    public int ActiveTasks { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}

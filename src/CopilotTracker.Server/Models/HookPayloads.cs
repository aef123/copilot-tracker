using System.Text.Json.Serialization;

namespace CopilotTracker.Server.Models;

public class SessionStartHook
{
    public string SessionId { get; set; } = string.Empty;
    public long Timestamp { get; set; }
    public string Cwd { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;  // "new", "resume", "startup"
    public string? InitialPrompt { get; set; }
    // Added by hook script before posting:
    public string? MachineName { get; set; }
    public string? Repository { get; set; }
    public string? Branch { get; set; }
    public string? Tool { get; set; }
}

public class SessionEndHook
{
    public string SessionId { get; set; } = string.Empty;
    public long Timestamp { get; set; }
    public string Cwd { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public string? MachineName { get; set; }
}

public class UserPromptSubmittedHook
{
    public string SessionId { get; set; } = string.Empty;
    public long Timestamp { get; set; }
    public string Cwd { get; set; } = string.Empty;
    public string Prompt { get; set; } = string.Empty;
    public string? MachineName { get; set; }
}

public class AgentStopHook
{
    public string SessionId { get; set; } = string.Empty;
    public long Timestamp { get; set; }
    public string Cwd { get; set; } = string.Empty;
    public string? TranscriptPath { get; set; }
    public string StopReason { get; set; } = string.Empty;
    public string? MachineName { get; set; }
}

public class SubagentStartHook
{
    public string SessionId { get; set; } = string.Empty;
    public long Timestamp { get; set; }
    public string Cwd { get; set; } = string.Empty;
    public string? TranscriptPath { get; set; }
    public string AgentName { get; set; } = string.Empty;
    public string? AgentDisplayName { get; set; }
    public string? AgentDescription { get; set; }
    public string? MachineName { get; set; }
}

public class SubagentStopHook
{
    public string SessionId { get; set; } = string.Empty;
    public long Timestamp { get; set; }
    public string Cwd { get; set; } = string.Empty;
    public string? TranscriptPath { get; set; }
    public string AgentName { get; set; } = string.Empty;
    public string? AgentDisplayName { get; set; }
    public string StopReason { get; set; } = string.Empty;
    public string? MachineName { get; set; }
}

public class NotificationHook
{
    public string SessionId { get; set; } = string.Empty;
    public long Timestamp { get; set; }
    public string Cwd { get; set; } = string.Empty;

    [JsonPropertyName("hook_event_name")]
    public string? HookEventName { get; set; }

    public string Message { get; set; } = string.Empty;
    public string? Title { get; set; }

    [JsonPropertyName("notification_type")]
    public string? NotificationType { get; set; }

    public string? MachineName { get; set; }
}

// Lightweight heartbeat - only updates session lastHeartbeat
public class PostToolUseHeartbeatHook
{
    public string SessionId { get; set; } = string.Empty;
    public long Timestamp { get; set; }
    public string? MachineName { get; set; }
}

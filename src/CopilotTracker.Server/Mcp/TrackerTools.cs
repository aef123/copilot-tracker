namespace CopilotTracker.Server.Mcp;

using System.ComponentModel;
using CopilotTracker.Core.Services;
using CopilotTracker.Server.Auth;
using ModelContextProtocol.Server;

[McpServerToolType]
public class TrackerTools
{
    [McpServerTool(Name = "initialize-session"), Description("Register a new Copilot session")]
    public static async Task<object> InitializeSession(
        SessionService sessionService,
        IHttpContextAccessor httpContextAccessor,
        [Description("Machine identifier")] string machineId,
        [Description("Repository URL")] string? repository = null,
        [Description("Git branch name")] string? branch = null)
    {
        var (userId, createdBy) = GetUserInfo(httpContextAccessor);
        var session = await sessionService.InitializeSessionAsync(machineId, repository, branch, userId, createdBy);
        return session;
    }

    [McpServerTool(Name = "heartbeat"), Description("Update session heartbeat")]
    public static async Task<object> Heartbeat(
        SessionService sessionService,
        [Description("Session ID")] string sessionId,
        [Description("Machine identifier")] string machineId)
    {
        var session = await sessionService.HeartbeatAsync(sessionId, machineId);
        return session;
    }

    [McpServerTool(Name = "complete-session"), Description("Mark a session as completed")]
    public static async Task<object> CompleteSession(
        SessionService sessionService,
        [Description("Session ID")] string sessionId,
        [Description("Machine identifier")] string machineId,
        [Description("Session summary")] string? summary = null)
    {
        var session = await sessionService.CompleteSessionAsync(sessionId, machineId, summary);
        return session;
    }

    [McpServerTool(Name = "set-task"), Description("Create or update a task")]
    public static async Task<object> SetTask(
        TaskService taskService,
        IHttpContextAccessor httpContextAccessor,
        [Description("Session ID")] string sessionId,
        [Description("Task title")] string title,
        [Description("Task status (started, done, failed)")] string status,
        [Description("Existing task ID for updates")] string? taskId = null,
        [Description("Queue name")] string queueName = "default",
        [Description("Task result summary")] string? result = null,
        [Description("Error message if failed")] string? errorMessage = null,
        [Description("Task source (prompt or queue)")] string source = "prompt")
    {
        var (userId, createdBy) = GetUserInfo(httpContextAccessor);
        var task = await taskService.SetTaskAsync(
            taskId, sessionId, queueName, title, status, result, errorMessage, source, userId, createdBy);
        return task;
    }

    [McpServerTool(Name = "add-log"), Description("Add a log entry to a task")]
    public static async Task<object> AddLog(
        TaskLogService taskLogService,
        [Description("Task ID")] string taskId,
        [Description("Log type (status_change, progress, output, error, heartbeat)")] string logType,
        [Description("Log message")] string message)
    {
        var log = await taskLogService.AddLogAsync(taskId, logType, message);
        return log;
    }

    [McpServerTool(Name = "get-session"), Description("Get session details")]
    public static async Task<object> GetSession(
        SessionService sessionService,
        [Description("Session ID")] string sessionId,
        [Description("Machine identifier")] string machineId)
    {
        var session = await sessionService.GetAsync(sessionId, machineId);
        return session is null
            ? new { error = $"Session '{sessionId}' not found" }
            : session;
    }

    private static (string userId, string createdBy) GetUserInfo(IHttpContextAccessor httpContextAccessor)
    {
        var user = httpContextAccessor.HttpContext?.User;
        if (user?.Identity?.IsAuthenticated != true)
            return ("anonymous", "anonymous");

        return (UserContext.GetUserId(user), UserContext.GetDisplayName(user));
    }
}

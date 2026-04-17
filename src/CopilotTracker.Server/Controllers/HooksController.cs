namespace CopilotTracker.Server.Controllers;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using CopilotTracker.Core.Services;
using CopilotTracker.Server.Auth;
using CopilotTracker.Server.Models;

[ApiController]
[Route("api/hooks")]
[Authorize(Policy = "TrackerAccess")]
public class HooksController : ControllerBase
{
    private readonly SessionService _sessionService;
    private readonly PromptService _promptService;
    private readonly PromptLogService _promptLogService;
    private readonly ILogger<HooksController> _logger;

    public HooksController(
        SessionService sessionService,
        PromptService promptService,
        PromptLogService promptLogService,
        ILogger<HooksController> logger)
    {
        _sessionService = sessionService;
        _promptService = promptService;
        _promptLogService = promptLogService;
        _logger = logger;
    }

    private (string userId, string createdBy) GetUserInfo()
    {
        var userId = UserContext.GetUserId(User);
        var createdBy = UserContext.GetDisplayName(User);
        return (userId, createdBy);
    }

    [HttpPost("sessionStart")]
    public async Task<IActionResult> SessionStart([FromBody] SessionStartHook hook)
    {
        var (userId, createdBy) = GetUserInfo();
        var session = await _sessionService.InitializeFromHookAsync(
            hook.SessionId, hook.MachineName ?? Environment.MachineName,
            hook.Repository, hook.Branch, hook.Source, hook.InitialPrompt,
            userId, createdBy, hook.Tool, hook.Title);
        return Ok(new { sessionId = session.Id });
    }

    [HttpPost("sessionEnd")]
    public async Task<IActionResult> SessionEnd([FromBody] SessionEndHook hook)
    {
        var machineId = hook.MachineName ?? Environment.MachineName;
        await _sessionService.CompleteSessionAsync(hook.SessionId, machineId, hook.Reason);
        return Ok();
    }

    [HttpPost("userPromptSubmitted")]
    public async Task<IActionResult> UserPromptSubmitted([FromBody] UserPromptSubmittedHook hook)
    {
        var (userId, createdBy) = GetUserInfo();
        if (hook.MachineName != null)
            await _sessionService.TouchSessionAsync(hook.SessionId, hook.MachineName);

        if (!string.IsNullOrEmpty(hook.Title) && hook.MachineName != null)
            await _sessionService.UpdateSessionTitleAsync(hook.SessionId, hook.MachineName, hook.Title);

        var prompt = await _promptService.CreatePromptAsync(
            hook.SessionId, hook.Prompt, hook.Cwd, hook.Timestamp, userId, createdBy);
        return Ok(new { promptId = prompt.Id });
    }

    [HttpPost("agentStop")]
    public async Task<IActionResult> AgentStop([FromBody] AgentStopHook hook)
    {
        if (hook.MachineName != null)
            await _sessionService.TouchSessionAsync(hook.SessionId, hook.MachineName);

        var prompt = await _promptService.CompleteActivePromptAsync(hook.SessionId, hook.Timestamp);
        return Ok(new { promptId = prompt?.Id, completed = prompt != null });
    }

    [HttpPost("subagentStart")]
    public async Task<IActionResult> SubagentStart([FromBody] SubagentStartHook hook)
    {
        var (userId, createdBy) = GetUserInfo();
        if (hook.MachineName != null)
            await _sessionService.TouchSessionAsync(hook.SessionId, hook.MachineName);

        var prompt = await _promptService.GetOrCreateActivePromptAsync(hook.SessionId, userId, createdBy);
        var message = $"Sub-agent started: {hook.AgentDisplayName ?? hook.AgentName}";
        if (!string.IsNullOrEmpty(hook.AgentDescription))
            message += $" - {hook.AgentDescription}";

        await _promptLogService.AddLogAsync(
            prompt.Id, hook.SessionId, "subagent_start", message,
            hook.AgentName, null, hook.Timestamp);
        return Ok();
    }

    [HttpPost("subagentStop")]
    public async Task<IActionResult> SubagentStop([FromBody] SubagentStopHook hook)
    {
        var (userId, createdBy) = GetUserInfo();
        if (hook.MachineName != null)
            await _sessionService.TouchSessionAsync(hook.SessionId, hook.MachineName);

        var prompt = await _promptService.GetOrCreateActivePromptAsync(hook.SessionId, userId, createdBy);
        var message = $"Sub-agent stopped: {hook.AgentDisplayName ?? hook.AgentName} (reason: {hook.StopReason})";

        await _promptLogService.AddLogAsync(
            prompt.Id, hook.SessionId, "subagent_stop", message,
            hook.AgentName, null, hook.Timestamp);
        return Ok();
    }

    [HttpPost("notification")]
    public async Task<IActionResult> Notification([FromBody] NotificationHook hook)
    {
        var (userId, createdBy) = GetUserInfo();
        if (hook.MachineName != null)
            await _sessionService.TouchSessionAsync(hook.SessionId, hook.MachineName);

        var prompt = await _promptService.GetOrCreateActivePromptAsync(hook.SessionId, userId, createdBy);
        var message = hook.Message;
        if (!string.IsNullOrEmpty(hook.Title))
            message = $"[{hook.Title}] {message}";

        await _promptLogService.AddLogAsync(
            prompt.Id, hook.SessionId, "notification", message,
            null, hook.NotificationType, hook.Timestamp);
        return Ok();
    }

    [HttpPost("postToolUse")]
    public async Task<IActionResult> PostToolUseHeartbeat([FromBody] PostToolUseHeartbeatHook hook)
    {
        if (hook.MachineName != null)
            await _sessionService.TouchSessionAsync(hook.SessionId, hook.MachineName);
        return Ok();
    }
}

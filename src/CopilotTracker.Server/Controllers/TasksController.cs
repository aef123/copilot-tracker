namespace CopilotTracker.Server.Controllers;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using CopilotTracker.Core.Services;
using CopilotTracker.Server.Auth;
using CopilotTracker.Server.Models.Requests;

[ApiController]
[Route("api/tasks")]
[Authorize(Policy = "TrackerAccess")]
public class TasksController : ControllerBase
{
    private readonly TaskService _taskService;
    private readonly TaskLogService _logService;

    public TasksController(TaskService taskService, TaskLogService logService)
    {
        _taskService = taskService;
        _logService = logService;
    }

    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] string? queueName,
        [FromQuery] string? status,
        [FromQuery] string? continuationToken,
        [FromQuery] int pageSize = 50)
    {
        var result = await _taskService.ListAsync(queueName, status, continuationToken, pageSize);
        return Ok(result);
    }

    [HttpGet("{queueName}/{id}")]
    public async Task<IActionResult> Get(string queueName, string id)
    {
        var task = await _taskService.GetAsync(id, queueName);
        if (task == null) return NotFound();
        return Ok(task);
    }

    [HttpGet("{queueName}/{id}/logs")]
    public async Task<IActionResult> GetLogs(
        string queueName, string id,
        [FromQuery] string? continuationToken,
        [FromQuery] int pageSize = 100)
    {
        var task = await _taskService.GetAsync(id, queueName);
        if (task == null) return NotFound();

        var logs = await _logService.GetLogsPagedAsync(id, continuationToken, pageSize);
        return Ok(logs);
    }

    [HttpPost]
    public async Task<IActionResult> SetTask([FromBody] SetTaskRequest request)
    {
        var (userId, createdBy) = GetUserInfo();
        var task = await _taskService.SetTaskAsync(
            request.TaskId, request.SessionId, request.QueueName, request.Title,
            request.Status, request.Result, request.ErrorMessage, request.Source,
            userId, createdBy);
        return Ok(task);
    }

    [HttpPost("{queueName}/{id}/logs")]
    public async Task<IActionResult> AddLog(
        string queueName, string id, [FromBody] AddLogRequest request)
    {
        var task = await _taskService.GetAsync(id, queueName);
        if (task == null) return NotFound(new { error = $"Task '{id}' not found in queue '{queueName}'" });

        var log = await _logService.AddLogAsync(id, request.LogType, request.Message);
        return Ok(log);
    }

    private (string userId, string createdBy) GetUserInfo()
    {
        if (User.Identity?.IsAuthenticated != true)
            return ("anonymous", "anonymous");

        return (UserContext.GetUserId(User), UserContext.GetDisplayName(User));
    }
}

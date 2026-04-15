namespace CopilotTracker.Server.Controllers;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using CopilotTracker.Core.Services;

[ApiController]
[Route("api/tasks")]
[Authorize]
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
}

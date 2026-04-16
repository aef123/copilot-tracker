namespace CopilotTracker.Server.Controllers;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using CopilotTracker.Core.Services;

[ApiController]
[Route("api/prompts")]
[Authorize(Policy = "TrackerAccess")]
public class PromptsController : ControllerBase
{
    private readonly PromptService _promptService;
    private readonly PromptLogService _logService;

    public PromptsController(PromptService promptService, PromptLogService logService)
    {
        _promptService = promptService;
        _logService = logService;
    }

    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] string? sessionId,
        [FromQuery] string? status,
        [FromQuery] DateTime? since,
        [FromQuery] string? continuationToken,
        [FromQuery] int pageSize = 50)
    {
        var result = await _promptService.ListAsync(sessionId, status, since, continuationToken, pageSize);
        return Ok(result);
    }

    [HttpGet("{sessionId}/{id}")]
    public async Task<IActionResult> Get(string sessionId, string id)
    {
        var prompt = await _promptService.GetAsync(sessionId, id);
        if (prompt == null) return NotFound();
        return Ok(prompt);
    }

    [HttpGet("{sessionId}/{id}/logs")]
    public async Task<IActionResult> GetLogs(
        string sessionId, string id,
        [FromQuery] string? continuationToken,
        [FromQuery] int pageSize = 100)
    {
        var prompt = await _promptService.GetAsync(sessionId, id);
        if (prompt == null) return NotFound();

        var logs = await _logService.GetLogsPagedAsync(id, continuationToken, pageSize);
        return Ok(logs);
    }
}

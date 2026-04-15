namespace CopilotTracker.Server.Controllers;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using CopilotTracker.Core.Services;

[ApiController]
[Route("api/sessions")]
[Authorize]
public class SessionsController : ControllerBase
{
    private readonly SessionService _sessionService;

    public SessionsController(SessionService sessionService)
    {
        _sessionService = sessionService;
    }

    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] string? machineId,
        [FromQuery] string? status,
        [FromQuery] DateTime? since,
        [FromQuery] string? continuationToken,
        [FromQuery] int pageSize = 50)
    {
        var result = await _sessionService.ListAsync(machineId, status, since, continuationToken, pageSize);
        return Ok(result);
    }

    [HttpGet("{machineId}/{id}")]
    public async Task<IActionResult> Get(string machineId, string id)
    {
        var session = await _sessionService.GetAsync(id, machineId);
        if (session == null) return NotFound();
        return Ok(session);
    }
}

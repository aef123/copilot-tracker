namespace CopilotTracker.Server.Controllers;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using CopilotTracker.Core.Services;
using CopilotTracker.Server.Auth;
using CopilotTracker.Server.Models.Requests;

[ApiController]
[Route("api/sessions")]
[Authorize(Policy = "TrackerAccess")]
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
        [FromQuery] string? tool,
        [FromQuery] DateTime? since,
        [FromQuery] string? continuationToken,
        [FromQuery] int pageSize = 50)
    {
        var result = await _sessionService.ListAsync(machineId, status, tool, since, continuationToken, pageSize);
        return Ok(result);
    }

    [HttpGet("{machineId}/{id}")]
    public async Task<IActionResult> Get(string machineId, string id)
    {
        var session = await _sessionService.GetAsync(id, machineId);
        if (session == null) return NotFound();
        return Ok(session);
    }

    [HttpPost]
    public async Task<IActionResult> Initialize([FromBody] InitializeSessionRequest request)
    {
        var (userId, createdBy) = GetUserInfo();
        var session = await _sessionService.InitializeSessionAsync(
            request.MachineId, request.Repository, request.Branch, userId, createdBy);
        return CreatedAtAction(nameof(Get), new { machineId = session.MachineId, id = session.Id }, session);
    }

    [HttpPost("{machineId}/{id}/heartbeat")]
    public async Task<IActionResult> Heartbeat(string machineId, string id)
    {
        try
        {
            var session = await _sessionService.HeartbeatAsync(id, machineId);
            return Ok(session);
        }
        catch (InvalidOperationException ex)
        {
            return NotFound(new { error = ex.Message });
        }
    }

    [HttpPost("{machineId}/{id}/complete")]
    public async Task<IActionResult> Complete(
        string machineId, string id, [FromBody] CompleteSessionRequest? request = null)
    {
        try
        {
            var session = await _sessionService.CompleteSessionAsync(id, machineId, request?.Summary);
            return Ok(session);
        }
        catch (InvalidOperationException ex)
        {
            return ex.Message.Contains("not found", StringComparison.OrdinalIgnoreCase)
                ? NotFound(new { error = ex.Message })
                : Conflict(new { error = ex.Message });
        }
    }

    private (string userId, string createdBy) GetUserInfo()
    {
        if (User.Identity?.IsAuthenticated != true)
            return ("anonymous", "anonymous");

        return (UserContext.GetUserId(User), UserContext.GetDisplayName(User));
    }
}

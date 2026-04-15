namespace CopilotTracker.Server.Controllers;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using CopilotTracker.Core.Services;

[ApiController]
[Route("api/health")]
public class HealthController : ControllerBase
{
    private readonly HealthService _healthService;

    public HealthController(HealthService healthService)
    {
        _healthService = healthService;
    }

    [HttpGet]
    [AllowAnonymous]
    public async Task<IActionResult> Get()
    {
        var health = await _healthService.GetHealthAsync();
        return Ok(health);
    }
}

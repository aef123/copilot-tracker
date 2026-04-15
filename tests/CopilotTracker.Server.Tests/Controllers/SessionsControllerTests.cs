namespace CopilotTracker.Server.Tests.Controllers;

using CopilotTracker.Core.Models;
using CopilotTracker.Core.Services;
using CopilotTracker.Server.Controllers;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Moq;

public class SessionsControllerTests
{
    private readonly Mock<SessionService> _sessionService;
    private readonly SessionsController _controller;

    public SessionsControllerTests()
    {
        _sessionService = new Mock<SessionService>(
            Mock.Of<Core.Interfaces.ISessionRepository>(),
            Mock.Of<Core.Interfaces.ITaskLogRepository>(),
            Mock.Of<Microsoft.Extensions.Logging.ILogger<SessionService>>());

        _controller = new SessionsController(_sessionService.Object);
    }

    [Fact]
    public async Task List_Returns200WithPagedResults()
    {
        var pagedResult = new PagedResult<Session>
        {
            Items = [new Session { Id = "s1", MachineId = "m1" }],
            ContinuationToken = null
        };
        _sessionService
            .Setup(s => s.ListAsync(null, null, null, null, 50))
            .ReturnsAsync(pagedResult);

        var result = await _controller.List(null, null, null, null, 50);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        ok.Value.Should().BeEquivalentTo(pagedResult);
    }

    [Fact]
    public async Task Get_Returns200WithSession()
    {
        var session = new Session { Id = "s1", MachineId = "m1" };
        _sessionService
            .Setup(s => s.GetAsync("s1", "m1"))
            .ReturnsAsync(session);

        var result = await _controller.Get("m1", "s1");

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        ok.Value.Should().BeEquivalentTo(session);
    }

    [Fact]
    public async Task Get_Returns404WhenNotFound()
    {
        _sessionService
            .Setup(s => s.GetAsync("missing", "m1"))
            .ReturnsAsync((Session?)null);

        var result = await _controller.Get("m1", "missing");

        result.Should().BeOfType<NotFoundResult>();
    }
}

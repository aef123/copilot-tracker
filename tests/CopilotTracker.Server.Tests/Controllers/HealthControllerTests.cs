namespace CopilotTracker.Server.Tests.Controllers;

using CopilotTracker.Core.Models;
using CopilotTracker.Core.Services;
using CopilotTracker.Server.Controllers;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Moq;

public class HealthControllerTests
{
    private readonly Mock<HealthService> _healthService;
    private readonly HealthController _controller;

    public HealthControllerTests()
    {
        _healthService = new Mock<HealthService>(
            Mock.Of<Core.Interfaces.ISessionRepository>(),
            Mock.Of<Core.Interfaces.ITaskRepository>());

        _controller = new HealthController(_healthService.Object);
    }

    [Fact]
    public async Task Get_Returns200WithHealthSummary()
    {
        var summary = new HealthSummary
        {
            ActiveSessions = 3,
            CompletedSessions = 10,
            StaleSessions = 1,
            TotalTasks = 25,
            ActiveTasks = 2
        };
        _healthService
            .Setup(s => s.GetHealthAsync())
            .ReturnsAsync(summary);

        var result = await _controller.Get();

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        ok.Value.Should().BeEquivalentTo(summary);
    }
}

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

    // --- Edge case tests ---

    [Fact]
    public async Task Get_ReturnsAllZeros_WhenNoActivity()
    {
        var summary = new HealthSummary
        {
            ActiveSessions = 0,
            CompletedSessions = 0,
            StaleSessions = 0,
            TotalTasks = 0,
            ActiveTasks = 0
        };
        _healthService
            .Setup(s => s.GetHealthAsync())
            .ReturnsAsync(summary);

        var result = await _controller.Get();

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var health = ok.Value.Should().BeOfType<HealthSummary>().Subject;
        health.ActiveSessions.Should().Be(0);
        health.CompletedSessions.Should().Be(0);
        health.StaleSessions.Should().Be(0);
        health.TotalTasks.Should().Be(0);
        health.ActiveTasks.Should().Be(0);
    }

    [Fact]
    public async Task Get_ResponseShape_ContainsTimestamp()
    {
        var summary = new HealthSummary
        {
            ActiveSessions = 1,
            CompletedSessions = 2,
            StaleSessions = 0,
            TotalTasks = 5,
            ActiveTasks = 1,
            Timestamp = new DateTime(2024, 6, 15, 12, 0, 0, DateTimeKind.Utc)
        };
        _healthService
            .Setup(s => s.GetHealthAsync())
            .ReturnsAsync(summary);

        var result = await _controller.Get();

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var health = ok.Value.Should().BeOfType<HealthSummary>().Subject;
        health.Timestamp.Should().Be(new DateTime(2024, 6, 15, 12, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public async Task Get_ServiceThrows_Returns500WithErrorDetails()
    {
        _healthService
            .Setup(s => s.GetHealthAsync())
            .ThrowsAsync(new InvalidOperationException("Cosmos unavailable"));

        var result = await _controller.Get();

        var statusResult = result.Should().BeOfType<ObjectResult>().Subject;
        statusResult.StatusCode.Should().Be(500);
    }

    [Fact]
    public async Task Get_ResponseShape_MatchesAllHealthSummaryProperties()
    {
        var summary = new HealthSummary
        {
            ActiveSessions = 10,
            CompletedSessions = 100,
            StaleSessions = 5,
            TotalTasks = 200,
            ActiveTasks = 15,
            Timestamp = DateTime.UtcNow
        };
        _healthService
            .Setup(s => s.GetHealthAsync())
            .ReturnsAsync(summary);

        var result = await _controller.Get();

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        ok.Value.Should().BeEquivalentTo(summary, options => options
            .Including(h => h.ActiveSessions)
            .Including(h => h.CompletedSessions)
            .Including(h => h.StaleSessions)
            .Including(h => h.TotalTasks)
            .Including(h => h.ActiveTasks)
            .Including(h => h.Timestamp));
    }

    [Fact]
    public async Task Get_LargeCounts_HandledCorrectly()
    {
        var summary = new HealthSummary
        {
            ActiveSessions = 999999,
            CompletedSessions = int.MaxValue,
            StaleSessions = 0,
            TotalTasks = 1000000,
            ActiveTasks = 500000
        };
        _healthService
            .Setup(s => s.GetHealthAsync())
            .ReturnsAsync(summary);

        var result = await _controller.Get();

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var health = ok.Value.Should().BeOfType<HealthSummary>().Subject;
        health.ActiveSessions.Should().Be(999999);
        health.CompletedSessions.Should().Be(int.MaxValue);
        health.TotalTasks.Should().Be(1000000);
    }
}

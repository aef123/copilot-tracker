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

    // --- Edge case tests ---

    [Fact]
    public async Task List_WithNonexistentMachineId_ReturnsEmptyResults()
    {
        var empty = new PagedResult<Session> { Items = [], ContinuationToken = null };
        _sessionService
            .Setup(s => s.ListAsync("nonexistent-machine", null, null, null, 50))
            .ReturnsAsync(empty);

        var result = await _controller.List("nonexistent-machine", null, null, null, 50);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var paged = ok.Value.Should().BeOfType<PagedResult<Session>>().Subject;
        paged.Items.Should().BeEmpty();
        paged.HasMore.Should().BeFalse();
    }

    [Fact]
    public async Task List_WithStatusFilter_PassesFilterToService()
    {
        var pagedResult = new PagedResult<Session> { Items = [], ContinuationToken = null };
        _sessionService
            .Setup(s => s.ListAsync(null, "active", null, null, 50))
            .ReturnsAsync(pagedResult);

        var result = await _controller.List(null, "active", null, null, 50);

        result.Should().BeOfType<OkObjectResult>();
        _sessionService.Verify(s => s.ListAsync(null, "active", null, null, 50), Times.Once);
    }

    [Fact]
    public async Task List_WithSinceFilter_PassesDateToService()
    {
        var since = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var pagedResult = new PagedResult<Session> { Items = [], ContinuationToken = null };
        _sessionService
            .Setup(s => s.ListAsync(null, null, since, null, 50))
            .ReturnsAsync(pagedResult);

        var result = await _controller.List(null, null, since, null, 50);

        result.Should().BeOfType<OkObjectResult>();
        _sessionService.Verify(s => s.ListAsync(null, null, since, null, 50), Times.Once);
    }

    [Fact]
    public async Task List_WithContinuationToken_PassesTokenToService()
    {
        var pagedResult = new PagedResult<Session> { Items = [], ContinuationToken = null };
        _sessionService
            .Setup(s => s.ListAsync(null, null, null, "token123", 50))
            .ReturnsAsync(pagedResult);

        var result = await _controller.List(null, null, null, "token123", 50);

        result.Should().BeOfType<OkObjectResult>();
        _sessionService.Verify(s => s.ListAsync(null, null, null, "token123", 50), Times.Once);
    }

    [Fact]
    public async Task List_WithCustomPageSize_PassesPageSizeToService()
    {
        var pagedResult = new PagedResult<Session> { Items = [], ContinuationToken = null };
        _sessionService
            .Setup(s => s.ListAsync(null, null, null, null, 10))
            .ReturnsAsync(pagedResult);

        var result = await _controller.List(null, null, null, null, 10);

        result.Should().BeOfType<OkObjectResult>();
        _sessionService.Verify(s => s.ListAsync(null, null, null, null, 10), Times.Once);
    }

    [Fact]
    public async Task List_WithLargePageSize_PassesValueThrough()
    {
        var pagedResult = new PagedResult<Session> { Items = [], ContinuationToken = null };
        _sessionService
            .Setup(s => s.ListAsync(null, null, null, null, 10000))
            .ReturnsAsync(pagedResult);

        var result = await _controller.List(null, null, null, null, 10000);

        result.Should().BeOfType<OkObjectResult>();
        _sessionService.Verify(s => s.ListAsync(null, null, null, null, 10000), Times.Once);
    }

    [Fact]
    public async Task List_WithMultiplePages_ReturnsContinuationToken()
    {
        var pagedResult = new PagedResult<Session>
        {
            Items = [new Session { Id = "s1", MachineId = "m1" }],
            ContinuationToken = "next-page-token"
        };
        _sessionService
            .Setup(s => s.ListAsync(null, null, null, null, 1))
            .ReturnsAsync(pagedResult);

        var result = await _controller.List(null, null, null, null, 1);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var paged = ok.Value.Should().BeOfType<PagedResult<Session>>().Subject;
        paged.HasMore.Should().BeTrue();
        paged.ContinuationToken.Should().Be("next-page-token");
    }

    [Fact]
    public async Task List_ServiceThrows_ExceptionPropagates()
    {
        _sessionService
            .Setup(s => s.ListAsync(null, null, null, null, 50))
            .ThrowsAsync(new InvalidOperationException("Database unavailable"));

        Func<Task> act = () => _controller.List(null, null, null, null, 50);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Database unavailable");
    }

    [Fact]
    public async Task Get_ServiceThrows_ExceptionPropagates()
    {
        _sessionService
            .Setup(s => s.GetAsync("s1", "m1"))
            .ThrowsAsync(new InvalidOperationException("Cosmos error"));

        Func<Task> act = () => _controller.Get("m1", "s1");

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Cosmos error");
    }

    [Fact]
    public async Task Get_WithEmptyStringIds_DelegatesToService()
    {
        _sessionService
            .Setup(s => s.GetAsync("", ""))
            .ReturnsAsync((Session?)null);

        var result = await _controller.Get("", "");

        result.Should().BeOfType<NotFoundResult>();
    }

    [Fact]
    public async Task List_AllFiltersApplied_PassesAllToService()
    {
        var since = new DateTime(2024, 6, 1, 0, 0, 0, DateTimeKind.Utc);
        var pagedResult = new PagedResult<Session> { Items = [], ContinuationToken = null };
        _sessionService
            .Setup(s => s.ListAsync("m1", "active", since, "tok", 25))
            .ReturnsAsync(pagedResult);

        var result = await _controller.List("m1", "active", since, "tok", 25);

        result.Should().BeOfType<OkObjectResult>();
        _sessionService.Verify(s => s.ListAsync("m1", "active", since, "tok", 25), Times.Once);
    }
}

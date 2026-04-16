namespace CopilotTracker.Server.Tests.Controllers;

using System.Security.Claims;
using CopilotTracker.Core.Models;
using CopilotTracker.Core.Services;
using CopilotTracker.Server.Controllers;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;

public class PromptsControllerTests
{
    private readonly Mock<PromptService> _promptService;
    private readonly Mock<PromptLogService> _logService;
    private readonly PromptsController _controller;

    public PromptsControllerTests()
    {
        _promptService = new Mock<PromptService>(
            Mock.Of<Core.Interfaces.IPromptRepository>(),
            Mock.Of<Microsoft.Extensions.Logging.ILogger<PromptService>>());

        _logService = new Mock<PromptLogService>(
            Mock.Of<Core.Interfaces.IPromptLogRepository>(),
            Mock.Of<Microsoft.Extensions.Logging.ILogger<PromptLogService>>());

        _controller = new PromptsController(_promptService.Object, _logService.Object);
        _controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                [
                    new Claim("oid", "test-user-id"),
                    new Claim("name", "Test User"),
                ], "Test"))
            }
        };
    }

    // --- List tests ---

    [Fact]
    public async Task List_ReturnsOk_WithResults()
    {
        var paged = new PagedResult<Prompt>
        {
            Items = [new Prompt { Id = "p1", SessionId = "s1" }],
            ContinuationToken = null
        };
        _promptService
            .Setup(s => s.ListAsync(null, null, null, null, 50))
            .ReturnsAsync(paged);

        var result = await _controller.List(null, null, null, null, 50);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        ok.Value.Should().BeEquivalentTo(paged);
    }

    [Fact]
    public async Task List_ReturnsOk_WithFilters()
    {
        var since = new DateTime(2024, 6, 1, 0, 0, 0, DateTimeKind.Utc);
        var paged = new PagedResult<Prompt> { Items = [], ContinuationToken = null };
        _promptService
            .Setup(s => s.ListAsync("s1", "started", since, null, 50))
            .ReturnsAsync(paged);

        var result = await _controller.List("s1", "started", since, null, 50);

        result.Should().BeOfType<OkObjectResult>();
        _promptService.Verify(s => s.ListAsync("s1", "started", since, null, 50), Times.Once);
    }

    [Fact]
    public async Task List_PassesPaginationParams()
    {
        var paged = new PagedResult<Prompt>
        {
            Items = [new Prompt()],
            ContinuationToken = "next"
        };
        _promptService
            .Setup(s => s.ListAsync(null, null, null, "token", 10))
            .ReturnsAsync(paged);

        var result = await _controller.List(null, null, null, "token", 10);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var page = ok.Value.Should().BeOfType<PagedResult<Prompt>>().Subject;
        page.HasMore.Should().BeTrue();
        _promptService.Verify(s => s.ListAsync(null, null, null, "token", 10), Times.Once);
    }

    // --- Get tests ---

    [Fact]
    public async Task Get_ReturnsOk_WhenFound()
    {
        var prompt = new Prompt { Id = "p1", SessionId = "s1", PromptText = "test" };
        _promptService
            .Setup(s => s.GetAsync("s1", "p1"))
            .ReturnsAsync(prompt);

        var result = await _controller.Get("s1", "p1");

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        ok.Value.Should().BeEquivalentTo(prompt);
    }

    [Fact]
    public async Task Get_ReturnsNotFound_WhenMissing()
    {
        _promptService
            .Setup(s => s.GetAsync("s1", "missing"))
            .ReturnsAsync((Prompt?)null);

        var result = await _controller.Get("s1", "missing");

        result.Should().BeOfType<NotFoundResult>();
    }

    // --- GetLogs tests ---

    [Fact]
    public async Task GetLogs_ReturnsOk_WithResults()
    {
        var prompt = new Prompt { Id = "p1", SessionId = "s1" };
        _promptService
            .Setup(s => s.GetAsync("s1", "p1"))
            .ReturnsAsync(prompt);

        var logs = new PagedResult<PromptLog>
        {
            Items = [new PromptLog { Id = "l1", PromptId = "p1", Message = "agent started" }],
            ContinuationToken = null
        };
        _logService
            .Setup(s => s.GetLogsPagedAsync("p1", null, 100))
            .ReturnsAsync(logs);

        var result = await _controller.GetLogs("s1", "p1", null, 100);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        ok.Value.Should().BeEquivalentTo(logs);
    }

    [Fact]
    public async Task GetLogs_ReturnsNotFound_WhenPromptMissing()
    {
        _promptService
            .Setup(s => s.GetAsync("s1", "missing"))
            .ReturnsAsync((Prompt?)null);

        var result = await _controller.GetLogs("s1", "missing", null, 100);

        result.Should().BeOfType<NotFoundResult>();
        _logService.Verify(s => s.GetLogsPagedAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<int>()), Times.Never);
    }
}

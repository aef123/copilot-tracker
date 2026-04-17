namespace CopilotTracker.Server.Tests.Controllers;

using System.Security.Claims;
using CopilotTracker.Core.Models;
using CopilotTracker.Core.Services;
using CopilotTracker.Server.Controllers;
using CopilotTracker.Server.Models;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;

public class HooksControllerTests
{
    private readonly Mock<SessionService> _sessionService;
    private readonly Mock<PromptService> _promptService;
    private readonly Mock<PromptLogService> _promptLogService;
    private readonly HooksController _controller;

    public HooksControllerTests()
    {
        _sessionService = new Mock<SessionService>(
            Mock.Of<Core.Interfaces.ISessionRepository>(),
            Mock.Of<Core.Interfaces.IPromptRepository>(),
            Mock.Of<Microsoft.Extensions.Logging.ILogger<SessionService>>());

        _promptService = new Mock<PromptService>(
            Mock.Of<Core.Interfaces.IPromptRepository>(),
            Mock.Of<Microsoft.Extensions.Logging.ILogger<PromptService>>());

        _promptLogService = new Mock<PromptLogService>(
            Mock.Of<Core.Interfaces.IPromptLogRepository>(),
            Mock.Of<Microsoft.Extensions.Logging.ILogger<PromptLogService>>());

        _controller = new HooksController(
            _sessionService.Object, _promptService.Object,
            _promptLogService.Object,
            Mock.Of<Microsoft.Extensions.Logging.ILogger<HooksController>>());

        var claims = new[]
        {
            new Claim("oid", "test-oid"),
            new Claim("name", "Test User"),
            new Claim("scp", "CopilotTracker.ReadWrite")
        };
        _controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Bearer"))
            }
        };
    }

    // --- SessionStart ---

    [Fact]
    public async Task SessionStart_CreatesSession_Returns200()
    {
        var session = new Session { Id = "hook-session-1", MachineId = "machine1" };
        _sessionService
            .Setup(s => s.InitializeFromHookAsync(
                "hook-session-1", "machine1", "repo", "main", "new", null, "test-oid", "Test User"))
            .ReturnsAsync(session);

        var hook = new SessionStartHook
        {
            SessionId = "hook-session-1", MachineName = "machine1",
            Repository = "repo", Branch = "main", Source = "new"
        };
        var result = await _controller.SessionStart(hook);

        result.Should().BeOfType<OkObjectResult>();
        _sessionService.Verify(s => s.InitializeFromHookAsync(
            "hook-session-1", "machine1", "repo", "main", "new", null, "test-oid", "Test User"), Times.Once);
    }

    // --- SessionEnd ---

    [Fact]
    public async Task SessionEnd_CompletesSession_Returns200()
    {
        _sessionService
            .Setup(s => s.CompleteSessionAsync("s1", "machine1", "user_exit"))
            .Returns(Task.FromResult(new Session { Id = "s1" }));

        var hook = new SessionEndHook
        {
            SessionId = "s1", MachineName = "machine1", Reason = "user_exit"
        };
        var result = await _controller.SessionEnd(hook);

        result.Should().BeOfType<OkResult>();
        _sessionService.Verify(s => s.CompleteSessionAsync("s1", "machine1", "user_exit"), Times.Once);
    }

    // --- UserPromptSubmitted ---

    [Fact]
    public async Task UserPromptSubmitted_CreatesPrompt_Returns200()
    {
        var prompt = new Prompt { Id = "p1", SessionId = "s1", PromptText = "Fix the bug" };
        _promptService
            .Setup(s => s.CreatePromptAsync("s1", "Fix the bug", "/cwd", 123, "test-oid", "Test User"))
            .ReturnsAsync(prompt);

        var hook = new UserPromptSubmittedHook
        {
            SessionId = "s1", Prompt = "Fix the bug", Cwd = "/cwd",
            Timestamp = 123, MachineName = "m1"
        };
        var result = await _controller.UserPromptSubmitted(hook);

        result.Should().BeOfType<OkObjectResult>();
        _promptService.Verify(s => s.CreatePromptAsync(
            "s1", "Fix the bug", "/cwd", 123, "test-oid", "Test User"), Times.Once);
    }

    [Fact]
    public async Task UserPromptSubmitted_TouchesSession()
    {
        var prompt = new Prompt { Id = "p1", SessionId = "s1" };
        _promptService
            .Setup(s => s.CreatePromptAsync(It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string?>(), It.IsAny<long>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(prompt);

        var hook = new UserPromptSubmittedHook
        {
            SessionId = "s1", Prompt = "test", MachineName = "m1", Timestamp = 123
        };
        await _controller.UserPromptSubmitted(hook);

        _sessionService.Verify(s => s.TouchSessionAsync("s1", "m1"), Times.Once);
    }

    // --- AgentStop ---

    [Fact]
    public async Task AgentStop_CompletesActivePrompt_Returns200()
    {
        var prompt = new Prompt { Id = "p1", SessionId = "s1", Status = "done" };
        _promptService
            .Setup(s => s.CompleteActivePromptAsync("s1", 999))
            .ReturnsAsync(prompt);

        var hook = new AgentStopHook
        {
            SessionId = "s1", Timestamp = 999, MachineName = "m1", StopReason = "done"
        };
        var result = await _controller.AgentStop(hook);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        // Response should include completed=true
        ok.Value.Should().BeEquivalentTo(new { promptId = "p1", completed = true });
    }

    [Fact]
    public async Task AgentStop_NoActivePrompt_Returns200_WithCompletedFalse()
    {
        _promptService
            .Setup(s => s.CompleteActivePromptAsync("s1", 999))
            .ReturnsAsync((Prompt?)null);

        var hook = new AgentStopHook
        {
            SessionId = "s1", Timestamp = 999, MachineName = "m1", StopReason = "done"
        };
        var result = await _controller.AgentStop(hook);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        ok.Value.Should().BeEquivalentTo(new { promptId = (string?)null, completed = false });
    }

    // --- SubagentStart ---

    [Fact]
    public async Task SubagentStart_CreatesLog_Returns200()
    {
        var prompt = new Prompt { Id = "p1", SessionId = "s1" };
        _promptService
            .Setup(s => s.GetOrCreateActivePromptAsync("s1", "test-oid", "Test User"))
            .ReturnsAsync(prompt);

        var hook = new SubagentStartHook
        {
            SessionId = "s1", AgentName = "explorer", AgentDisplayName = "Explorer Agent",
            Timestamp = 555, MachineName = "m1"
        };
        var result = await _controller.SubagentStart(hook);

        result.Should().BeOfType<OkResult>();
        _promptService.Verify(s => s.GetOrCreateActivePromptAsync("s1", "test-oid", "Test User"), Times.Once);
        _promptLogService.Verify(s => s.AddLogAsync(
            "p1", "s1", "subagent_start", It.Is<string>(m => m.Contains("Explorer Agent")),
            "explorer", null, 555), Times.Once);
    }

    [Fact]
    public async Task SubagentStart_NoActivePrompt_CreatesMissedStart()
    {
        var missedPrompt = new Prompt
        {
            Id = "missed-p1", SessionId = "s1", PromptText = "MISSED START", Status = "started"
        };
        _promptService
            .Setup(s => s.GetOrCreateActivePromptAsync("s1", "test-oid", "Test User"))
            .ReturnsAsync(missedPrompt);

        var hook = new SubagentStartHook
        {
            SessionId = "s1", AgentName = "task", Timestamp = 100, MachineName = "m1"
        };
        await _controller.SubagentStart(hook);

        // The log should be attached to the missed-start prompt
        _promptLogService.Verify(s => s.AddLogAsync(
            "missed-p1", "s1", "subagent_start", It.IsAny<string>(),
            "task", null, 100), Times.Once);
    }

    // --- SubagentStop ---

    [Fact]
    public async Task SubagentStop_CreatesLog_Returns200()
    {
        var prompt = new Prompt { Id = "p1", SessionId = "s1" };
        _promptService
            .Setup(s => s.GetOrCreateActivePromptAsync("s1", "test-oid", "Test User"))
            .ReturnsAsync(prompt);

        var hook = new SubagentStopHook
        {
            SessionId = "s1", AgentName = "explorer", AgentDisplayName = "Explorer Agent",
            StopReason = "completed", Timestamp = 600, MachineName = "m1"
        };
        var result = await _controller.SubagentStop(hook);

        result.Should().BeOfType<OkResult>();
        _promptLogService.Verify(s => s.AddLogAsync(
            "p1", "s1", "subagent_stop",
            It.Is<string>(m => m.Contains("Explorer Agent") && m.Contains("completed")),
            "explorer", null, 600), Times.Once);
    }

    // --- Notification ---

    [Fact]
    public async Task Notification_CreatesLog_Returns200_WithTitleInMessage()
    {
        var prompt = new Prompt { Id = "p1", SessionId = "s1" };
        _promptService
            .Setup(s => s.GetOrCreateActivePromptAsync("s1", "test-oid", "Test User"))
            .ReturnsAsync(prompt);

        var hook = new NotificationHook
        {
            SessionId = "s1", Title = "Build Status", Message = "Build succeeded",
            NotificationType = "info", Timestamp = 700, MachineName = "m1"
        };
        var result = await _controller.Notification(hook);

        result.Should().BeOfType<OkResult>();
        _promptLogService.Verify(s => s.AddLogAsync(
            "p1", "s1", "notification",
            It.Is<string>(m => m.Contains("[Build Status]") && m.Contains("Build succeeded")),
            null, "info", 700), Times.Once);
    }

    // --- PostToolUse ---

    [Fact]
    public async Task PostToolUse_TouchesSession_Returns200()
    {
        var hook = new PostToolUseHeartbeatHook
        {
            SessionId = "s1", MachineName = "m1", Timestamp = 800
        };
        var result = await _controller.PostToolUseHeartbeat(hook);

        result.Should().BeOfType<OkResult>();
        _sessionService.Verify(s => s.TouchSessionAsync("s1", "m1"), Times.Once);
    }

    // --- Tool field tests ---

    [Fact]
    public async Task SessionStart_WithTool_PassesToolToService()
    {
        var session = new Session { Id = "tool-session", MachineId = "machine1" };
        _sessionService
            .Setup(s => s.InitializeFromHookAsync(
                "tool-session", "machine1", "repo", "main", "new", null, "test-oid", "Test User", "claude"))
            .ReturnsAsync(session);

        var hook = new SessionStartHook
        {
            SessionId = "tool-session", MachineName = "machine1",
            Repository = "repo", Branch = "main", Source = "new", Tool = "claude"
        };
        var result = await _controller.SessionStart(hook);

        result.Should().BeOfType<OkObjectResult>();
        _sessionService.Verify(s => s.InitializeFromHookAsync(
            "tool-session", "machine1", "repo", "main", "new", null, "test-oid", "Test User", "claude"), Times.Once);
    }

    [Fact]
    public async Task SessionStart_WithoutTool_PassesNullToolToService()
    {
        var session = new Session { Id = "no-tool-session", MachineId = "machine1" };
        _sessionService
            .Setup(s => s.InitializeFromHookAsync(
                "no-tool-session", "machine1", "repo", "main", "new", null, "test-oid", "Test User", null))
            .ReturnsAsync(session);

        var hook = new SessionStartHook
        {
            SessionId = "no-tool-session", MachineName = "machine1",
            Repository = "repo", Branch = "main", Source = "new"
        };
        var result = await _controller.SessionStart(hook);

        result.Should().BeOfType<OkObjectResult>();
        _sessionService.Verify(s => s.InitializeFromHookAsync(
            "no-tool-session", "machine1", "repo", "main", "new", null, "test-oid", "Test User", null), Times.Once);
    }
}

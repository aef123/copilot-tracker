namespace CopilotTracker.Server.Tests.Mcp;

using System.Security.Claims;
using CopilotTracker.Core.Models;
using CopilotTracker.Core.Services;
using CopilotTracker.Server.Mcp;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Moq;

public class TrackerToolsTests
{
    private readonly Mock<SessionService> _sessionService;
    private readonly Mock<TaskService> _taskService;
    private readonly Mock<TaskLogService> _taskLogService;
    private readonly Mock<IHttpContextAccessor> _httpContextAccessor;

    public TrackerToolsTests()
    {
        _sessionService = new Mock<SessionService>(
            Mock.Of<Core.Interfaces.ISessionRepository>(),
            Mock.Of<Core.Interfaces.ITaskLogRepository>(),
            Mock.Of<Microsoft.Extensions.Logging.ILogger<SessionService>>());

        _taskService = new Mock<TaskService>(
            Mock.Of<Core.Interfaces.ITaskRepository>(),
            Mock.Of<Core.Interfaces.ITaskLogRepository>(),
            Mock.Of<Microsoft.Extensions.Logging.ILogger<TaskService>>());

        _taskLogService = new Mock<TaskLogService>(
            Mock.Of<Core.Interfaces.ITaskLogRepository>());

        _httpContextAccessor = new Mock<IHttpContextAccessor>();
        SetupAnonymousUser();
    }

    private void SetupAnonymousUser()
    {
        var context = new DefaultHttpContext();
        // No authenticated identity by default
        _httpContextAccessor.Setup(x => x.HttpContext).Returns(context);
    }

    private void SetupAuthenticatedUser(string userId = "user-123", string displayName = "Test User")
    {
        var claims = new[]
        {
            new Claim("oid", userId),
            new Claim("name", displayName)
        };
        var identity = new ClaimsIdentity(claims, "TestAuth");
        var principal = new ClaimsPrincipal(identity);
        var context = new DefaultHttpContext { User = principal };
        _httpContextAccessor.Setup(x => x.HttpContext).Returns(context);
    }

    // =====================================================================
    // InitializeSession
    // =====================================================================

    [Fact]
    public async Task InitializeSession_HappyPath_ReturnsSession()
    {
        var expected = new Session { Id = "s1", MachineId = "m1", Repository = "repo", Branch = "main" };
        _sessionService
            .Setup(s => s.InitializeSessionAsync("m1", "repo", "main", "anonymous", "anonymous"))
            .ReturnsAsync(expected);

        var result = await TrackerTools.InitializeSession(
            _sessionService.Object, _httpContextAccessor.Object, "m1", "repo", "main");

        result.Should().Be(expected);
    }

    [Fact]
    public async Task InitializeSession_WithAuthenticatedUser_PassesUserInfo()
    {
        SetupAuthenticatedUser("uid-42", "Jane Doe");
        var expected = new Session { Id = "s1", MachineId = "m1" };
        _sessionService
            .Setup(s => s.InitializeSessionAsync("m1", null, null, "uid-42", "Jane Doe"))
            .ReturnsAsync(expected);

        var result = await TrackerTools.InitializeSession(
            _sessionService.Object, _httpContextAccessor.Object, "m1");

        result.Should().Be(expected);
    }

    [Fact]
    public async Task InitializeSession_OptionalParamsNull_PassesNulls()
    {
        var expected = new Session { Id = "s1", MachineId = "m1" };
        _sessionService
            .Setup(s => s.InitializeSessionAsync("m1", null, null, "anonymous", "anonymous"))
            .ReturnsAsync(expected);

        var result = await TrackerTools.InitializeSession(
            _sessionService.Object, _httpContextAccessor.Object, "m1");

        result.Should().Be(expected);
        _sessionService.Verify(
            s => s.InitializeSessionAsync("m1", null, null, "anonymous", "anonymous"), Times.Once);
    }

    [Fact]
    public async Task InitializeSession_ServiceThrows_ExceptionPropagates()
    {
        _sessionService
            .Setup(s => s.InitializeSessionAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<string>()))
            .ThrowsAsync(new InvalidOperationException("DB error"));

        Func<Task> act = () => TrackerTools.InitializeSession(
            _sessionService.Object, _httpContextAccessor.Object, "m1");

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("DB error");
    }

    [Fact]
    public async Task InitializeSession_EmptyMachineId_PassesToService()
    {
        var expected = new Session { Id = "s1", MachineId = "" };
        _sessionService
            .Setup(s => s.InitializeSessionAsync("", null, null, "anonymous", "anonymous"))
            .ReturnsAsync(expected);

        var result = await TrackerTools.InitializeSession(
            _sessionService.Object, _httpContextAccessor.Object, "");

        result.Should().Be(expected);
    }

    // =====================================================================
    // Heartbeat
    // =====================================================================

    [Fact]
    public async Task Heartbeat_HappyPath_ReturnsUpdatedSession()
    {
        var expected = new Session { Id = "s1", MachineId = "m1", Status = SessionStatus.Active };
        _sessionService
            .Setup(s => s.HeartbeatAsync("s1", "m1"))
            .ReturnsAsync(expected);

        var result = await TrackerTools.Heartbeat(_sessionService.Object, "s1", "m1");

        result.Should().Be(expected);
    }

    [Fact]
    public async Task Heartbeat_SessionNotFound_ServiceThrows()
    {
        _sessionService
            .Setup(s => s.HeartbeatAsync("missing", "m1"))
            .ThrowsAsync(new InvalidOperationException("Session 'missing' not found."));

        Func<Task> act = () => TrackerTools.Heartbeat(_sessionService.Object, "missing", "m1");

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*not found*");
    }

    [Fact]
    public async Task Heartbeat_SessionNotActive_ServiceThrows()
    {
        _sessionService
            .Setup(s => s.HeartbeatAsync("s1", "m1"))
            .ThrowsAsync(new InvalidOperationException("Session 's1' is not active (status: completed)."));

        Func<Task> act = () => TrackerTools.Heartbeat(_sessionService.Object, "s1", "m1");

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*not active*");
    }

    [Fact]
    public async Task Heartbeat_EmptySessionId_DelegatesToService()
    {
        _sessionService
            .Setup(s => s.HeartbeatAsync("", "m1"))
            .ThrowsAsync(new InvalidOperationException("not found"));

        Func<Task> act = () => TrackerTools.Heartbeat(_sessionService.Object, "", "m1");

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    // =====================================================================
    // CompleteSession
    // =====================================================================

    [Fact]
    public async Task CompleteSession_HappyPath_ReturnsCompletedSession()
    {
        var expected = new Session { Id = "s1", MachineId = "m1", Status = SessionStatus.Completed, Summary = "Done" };
        _sessionService
            .Setup(s => s.CompleteSessionAsync("s1", "m1", "Done"))
            .ReturnsAsync(expected);

        var result = await TrackerTools.CompleteSession(_sessionService.Object, "s1", "m1", "Done");

        result.Should().Be(expected);
    }

    [Fact]
    public async Task CompleteSession_NullSummary_PassesNull()
    {
        var expected = new Session { Id = "s1", MachineId = "m1", Status = SessionStatus.Completed };
        _sessionService
            .Setup(s => s.CompleteSessionAsync("s1", "m1", null))
            .ReturnsAsync(expected);

        var result = await TrackerTools.CompleteSession(_sessionService.Object, "s1", "m1");

        result.Should().Be(expected);
        _sessionService.Verify(s => s.CompleteSessionAsync("s1", "m1", null), Times.Once);
    }

    [Fact]
    public async Task CompleteSession_SessionNotFound_ServiceThrows()
    {
        _sessionService
            .Setup(s => s.CompleteSessionAsync("missing", "m1", null))
            .ThrowsAsync(new InvalidOperationException("Session 'missing' not found."));

        Func<Task> act = () => TrackerTools.CompleteSession(_sessionService.Object, "missing", "m1");

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*not found*");
    }

    [Fact]
    public async Task CompleteSession_SessionNotActive_ServiceThrows()
    {
        _sessionService
            .Setup(s => s.CompleteSessionAsync("s1", "m1", null))
            .ThrowsAsync(new InvalidOperationException("Session 's1' is not active (status: completed)."));

        Func<Task> act = () => TrackerTools.CompleteSession(_sessionService.Object, "s1", "m1");

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*not active*");
    }

    // =====================================================================
    // SetTask
    // =====================================================================

    [Fact]
    public async Task SetTask_CreateNew_ReturnsTask()
    {
        var expected = new TrackerTask { Id = "t1", SessionId = "s1", Title = "Build", Status = "started" };
        _taskService
            .Setup(s => s.SetTaskAsync(null, "s1", "default", "Build", "started", null, null, "prompt", "anonymous", "anonymous"))
            .ReturnsAsync(expected);

        var result = await TrackerTools.SetTask(
            _taskService.Object, _httpContextAccessor.Object,
            "s1", "Build", "started");

        result.Should().Be(expected);
    }

    [Fact]
    public async Task SetTask_UpdateExisting_PassesTaskId()
    {
        var expected = new TrackerTask { Id = "t1", SessionId = "s1", Title = "Build", Status = "done", Result = "OK" };
        _taskService
            .Setup(s => s.SetTaskAsync("t1", "s1", "default", "Build", "done", "OK", null, "prompt", "anonymous", "anonymous"))
            .ReturnsAsync(expected);

        var result = await TrackerTools.SetTask(
            _taskService.Object, _httpContextAccessor.Object,
            "s1", "Build", "done", taskId: "t1", result: "OK");

        result.Should().Be(expected);
    }

    [Fact]
    public async Task SetTask_FailedWithError_PassesErrorMessage()
    {
        var expected = new TrackerTask { Id = "t1", Status = "failed", ErrorMessage = "Compile error" };
        _taskService
            .Setup(s => s.SetTaskAsync(null, "s1", "default", "Build", "failed", null, "Compile error", "prompt", "anonymous", "anonymous"))
            .ReturnsAsync(expected);

        var result = await TrackerTools.SetTask(
            _taskService.Object, _httpContextAccessor.Object,
            "s1", "Build", "failed", errorMessage: "Compile error");

        result.Should().Be(expected);
    }

    [Fact]
    public async Task SetTask_WithQueueSource_PassesCorrectly()
    {
        var expected = new TrackerTask { Id = "t1", QueueName = "deploy", Source = "queue" };
        _taskService
            .Setup(s => s.SetTaskAsync("t1", "s1", "deploy", "Deploy", "started", null, null, "queue", "anonymous", "anonymous"))
            .ReturnsAsync(expected);

        var result = await TrackerTools.SetTask(
            _taskService.Object, _httpContextAccessor.Object,
            "s1", "Deploy", "started", taskId: "t1", queueName: "deploy", source: "queue");

        result.Should().Be(expected);
    }

    [Fact]
    public async Task SetTask_WithAuthenticatedUser_PassesUserInfo()
    {
        SetupAuthenticatedUser("uid-99", "Bob");
        var expected = new TrackerTask { Id = "t1", UserId = "uid-99", CreatedBy = "Bob" };
        _taskService
            .Setup(s => s.SetTaskAsync(null, "s1", "default", "Test", "started", null, null, "prompt", "uid-99", "Bob"))
            .ReturnsAsync(expected);

        var result = await TrackerTools.SetTask(
            _taskService.Object, _httpContextAccessor.Object,
            "s1", "Test", "started");

        result.Should().Be(expected);
    }

    [Fact]
    public async Task SetTask_ServiceThrows_ExceptionPropagates()
    {
        _taskService
            .Setup(s => s.SetTaskAsync(It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ThrowsAsync(new InvalidOperationException("DB error"));

        Func<Task> act = () => TrackerTools.SetTask(
            _taskService.Object, _httpContextAccessor.Object,
            "s1", "Build", "started");

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("DB error");
    }

    [Fact]
    public async Task SetTask_AllOptionalParams_PassesAll()
    {
        var expected = new TrackerTask { Id = "t1" };
        _taskService
            .Setup(s => s.SetTaskAsync("t1", "s1", "myqueue", "Title", "done", "result-text", "err-text", "queue", "anonymous", "anonymous"))
            .ReturnsAsync(expected);

        var result = await TrackerTools.SetTask(
            _taskService.Object, _httpContextAccessor.Object,
            "s1", "Title", "done",
            taskId: "t1", queueName: "myqueue", result: "result-text", errorMessage: "err-text", source: "queue");

        result.Should().Be(expected);
        _taskService.Verify(s => s.SetTaskAsync("t1", "s1", "myqueue", "Title", "done", "result-text", "err-text", "queue", "anonymous", "anonymous"), Times.Once);
    }

    // =====================================================================
    // AddLog
    // =====================================================================

    [Fact]
    public async Task AddLog_HappyPath_ReturnsLog()
    {
        var expected = new TaskLog { Id = "l1", TaskId = "t1", LogType = "progress", Message = "50% done" };
        _taskLogService
            .Setup(s => s.AddLogAsync("t1", "progress", "50% done"))
            .ReturnsAsync(expected);

        var result = await TrackerTools.AddLog(_taskLogService.Object, "t1", "progress", "50% done");

        result.Should().Be(expected);
    }

    [Fact]
    public async Task AddLog_AllLogTypes_DelegatesToService()
    {
        foreach (var logType in new[] { "status_change", "progress", "output", "error", "heartbeat" })
        {
            var expected = new TaskLog { TaskId = "t1", LogType = logType, Message = "msg" };
            _taskLogService
                .Setup(s => s.AddLogAsync("t1", logType, "msg"))
                .ReturnsAsync(expected);

            var result = await TrackerTools.AddLog(_taskLogService.Object, "t1", logType, "msg");

            result.Should().Be(expected);
        }
    }

    [Fact]
    public async Task AddLog_ServiceThrows_ExceptionPropagates()
    {
        _taskLogService
            .Setup(s => s.AddLogAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ThrowsAsync(new InvalidOperationException("DB error"));

        Func<Task> act = () => TrackerTools.AddLog(_taskLogService.Object, "t1", "progress", "msg");

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("DB error");
    }

    [Fact]
    public async Task AddLog_EmptyStrings_DelegatesToService()
    {
        var expected = new TaskLog { TaskId = "", LogType = "", Message = "" };
        _taskLogService
            .Setup(s => s.AddLogAsync("", "", ""))
            .ReturnsAsync(expected);

        var result = await TrackerTools.AddLog(_taskLogService.Object, "", "", "");

        result.Should().Be(expected);
    }

    [Fact]
    public async Task AddLog_SpecialCharacters_DelegatesToService()
    {
        var message = "Line1\nLine2\tTabbed \"quoted\" <html>&amp;";
        var expected = new TaskLog { TaskId = "t1", LogType = "output", Message = message };
        _taskLogService
            .Setup(s => s.AddLogAsync("t1", "output", message))
            .ReturnsAsync(expected);

        var result = await TrackerTools.AddLog(_taskLogService.Object, "t1", "output", message);

        result.Should().Be(expected);
    }

    // =====================================================================
    // GetSession
    // =====================================================================

    [Fact]
    public async Task GetSession_Found_ReturnsSession()
    {
        var expected = new Session { Id = "s1", MachineId = "m1" };
        _sessionService
            .Setup(s => s.GetAsync("s1", "m1"))
            .ReturnsAsync(expected);

        var result = await TrackerTools.GetSession(_sessionService.Object, "s1", "m1");

        result.Should().Be(expected);
    }

    [Fact]
    public async Task GetSession_NotFound_ReturnsErrorObject()
    {
        _sessionService
            .Setup(s => s.GetAsync("missing", "m1"))
            .ReturnsAsync((Session?)null);

        var result = await TrackerTools.GetSession(_sessionService.Object, "missing", "m1");

        // Returns anonymous object with error property
        var error = result.GetType().GetProperty("error")?.GetValue(result) as string;
        error.Should().Be("Session 'missing' not found");
    }

    [Fact]
    public async Task GetSession_ServiceThrows_ExceptionPropagates()
    {
        _sessionService
            .Setup(s => s.GetAsync("s1", "m1"))
            .ThrowsAsync(new InvalidOperationException("DB error"));

        Func<Task> act = () => TrackerTools.GetSession(_sessionService.Object, "s1", "m1");

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("DB error");
    }

    [Fact]
    public async Task GetSession_EmptyIds_DelegatesToService()
    {
        _sessionService
            .Setup(s => s.GetAsync("", ""))
            .ReturnsAsync((Session?)null);

        var result = await TrackerTools.GetSession(_sessionService.Object, "", "");

        var error = result.GetType().GetProperty("error")?.GetValue(result) as string;
        error.Should().Contain("not found");
    }

    // =====================================================================
    // GetUserInfo (private, tested indirectly)
    // =====================================================================

    [Fact]
    public async Task InitializeSession_NullHttpContext_ReturnsAnonymous()
    {
        _httpContextAccessor.Setup(x => x.HttpContext).Returns((HttpContext?)null);
        var expected = new Session { Id = "s1", MachineId = "m1" };
        _sessionService
            .Setup(s => s.InitializeSessionAsync("m1", null, null, "anonymous", "anonymous"))
            .ReturnsAsync(expected);

        var result = await TrackerTools.InitializeSession(
            _sessionService.Object, _httpContextAccessor.Object, "m1");

        result.Should().Be(expected);
        _sessionService.Verify(
            s => s.InitializeSessionAsync("m1", null, null, "anonymous", "anonymous"), Times.Once);
    }

    [Fact]
    public async Task SetTask_NullHttpContext_ReturnsAnonymous()
    {
        _httpContextAccessor.Setup(x => x.HttpContext).Returns((HttpContext?)null);
        var expected = new TrackerTask { Id = "t1" };
        _taskService
            .Setup(s => s.SetTaskAsync(null, "s1", "default", "Test", "started", null, null, "prompt", "anonymous", "anonymous"))
            .ReturnsAsync(expected);

        var result = await TrackerTools.SetTask(
            _taskService.Object, _httpContextAccessor.Object,
            "s1", "Test", "started");

        result.Should().Be(expected);
    }

    [Fact]
    public async Task InitializeSession_LongStrings_DelegatesToService()
    {
        var longMachineId = new string('x', 1000);
        var longRepo = new string('r', 2000);
        var expected = new Session { Id = "s1", MachineId = longMachineId };
        _sessionService
            .Setup(s => s.InitializeSessionAsync(longMachineId, longRepo, null, "anonymous", "anonymous"))
            .ReturnsAsync(expected);

        var result = await TrackerTools.InitializeSession(
            _sessionService.Object, _httpContextAccessor.Object, longMachineId, longRepo);

        result.Should().Be(expected);
    }
}

namespace CopilotTracker.Server.Tests.Controllers;

using System.Security.Claims;
using CopilotTracker.Core.Models;
using CopilotTracker.Core.Services;
using CopilotTracker.Server.Controllers;
using CopilotTracker.Server.Models.Requests;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;

public class TasksControllerTests
{
    private readonly Mock<TaskService> _taskService;
    private readonly Mock<TaskLogService> _logService;
    private readonly TasksController _controller;

    public TasksControllerTests()
    {
        _taskService = new Mock<TaskService>(
            Mock.Of<Core.Interfaces.ITaskRepository>(),
            Mock.Of<Core.Interfaces.ITaskLogRepository>(),
            Mock.Of<Microsoft.Extensions.Logging.ILogger<TaskService>>());

        _logService = new Mock<TaskLogService>(
            Mock.Of<Core.Interfaces.ITaskLogRepository>());

        _controller = new TasksController(_taskService.Object, _logService.Object);
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

    [Fact]
    public async Task List_Returns200WithPagedResults()
    {
        var pagedResult = new PagedResult<TrackerTask>
        {
            Items = [new TrackerTask { Id = "t1", QueueName = "default" }],
            ContinuationToken = null
        };
        _taskService
            .Setup(s => s.ListAsync(null, null, null, 50))
            .ReturnsAsync(pagedResult);

        var result = await _controller.List(null, null, null, 50);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        ok.Value.Should().BeEquivalentTo(pagedResult);
    }

    [Fact]
    public async Task Get_Returns200WithTask()
    {
        var task = new TrackerTask { Id = "t1", QueueName = "default" };
        _taskService
            .Setup(s => s.GetAsync("t1", "default"))
            .ReturnsAsync(task);

        var result = await _controller.Get("default", "t1");

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        ok.Value.Should().BeEquivalentTo(task);
    }

    [Fact]
    public async Task Get_Returns404WhenNotFound()
    {
        _taskService
            .Setup(s => s.GetAsync("missing", "default"))
            .ReturnsAsync((TrackerTask?)null);

        var result = await _controller.Get("default", "missing");

        result.Should().BeOfType<NotFoundResult>();
    }

    [Fact]
    public async Task GetLogs_ReturnsLogsForExistingTask()
    {
        var task = new TrackerTask { Id = "t1", QueueName = "default" };
        _taskService
            .Setup(s => s.GetAsync("t1", "default"))
            .ReturnsAsync(task);

        var logs = new PagedResult<TaskLog>
        {
            Items = [new TaskLog { Id = "l1", TaskId = "t1", Message = "progress" }],
            ContinuationToken = null
        };
        _logService
            .Setup(s => s.GetLogsPagedAsync("t1", null, 100))
            .ReturnsAsync(logs);

        var result = await _controller.GetLogs("default", "t1", null, 100);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        ok.Value.Should().BeEquivalentTo(logs);
    }

    [Fact]
    public async Task GetLogs_Returns404WhenTaskNotFound()
    {
        _taskService
            .Setup(s => s.GetAsync("missing", "default"))
            .ReturnsAsync((TrackerTask?)null);

        var result = await _controller.GetLogs("default", "missing", null, 100);

        result.Should().BeOfType<NotFoundResult>();
    }

    // --- Edge case tests ---

    [Fact]
    public async Task List_WithQueueNameFilter_PassesFilterToService()
    {
        var pagedResult = new PagedResult<TrackerTask> { Items = [], ContinuationToken = null };
        _taskService
            .Setup(s => s.ListAsync("my-queue", null, null, 50))
            .ReturnsAsync(pagedResult);

        var result = await _controller.List("my-queue", null, null, 50);

        result.Should().BeOfType<OkObjectResult>();
        _taskService.Verify(s => s.ListAsync("my-queue", null, null, 50), Times.Once);
    }

    [Fact]
    public async Task List_WithStatusFilter_PassesFilterToService()
    {
        var pagedResult = new PagedResult<TrackerTask> { Items = [], ContinuationToken = null };
        _taskService
            .Setup(s => s.ListAsync(null, "started", null, 50))
            .ReturnsAsync(pagedResult);

        var result = await _controller.List(null, "started", null, 50);

        result.Should().BeOfType<OkObjectResult>();
        _taskService.Verify(s => s.ListAsync(null, "started", null, 50), Times.Once);
    }

    [Fact]
    public async Task List_WithInvalidStatusFilter_PassesThroughToService()
    {
        // Controller doesn't validate status values; it delegates to the service
        var pagedResult = new PagedResult<TrackerTask> { Items = [], ContinuationToken = null };
        _taskService
            .Setup(s => s.ListAsync(null, "bogus-status", null, 50))
            .ReturnsAsync(pagedResult);

        var result = await _controller.List(null, "bogus-status", null, 50);

        result.Should().BeOfType<OkObjectResult>();
        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var paged = ok.Value.Should().BeOfType<PagedResult<TrackerTask>>().Subject;
        paged.Items.Should().BeEmpty();
    }

    [Fact]
    public async Task List_WithContinuationToken_PassesTokenToService()
    {
        var pagedResult = new PagedResult<TrackerTask> { Items = [], ContinuationToken = null };
        _taskService
            .Setup(s => s.ListAsync(null, null, "page2-token", 50))
            .ReturnsAsync(pagedResult);

        var result = await _controller.List(null, null, "page2-token", 50);

        result.Should().BeOfType<OkObjectResult>();
        _taskService.Verify(s => s.ListAsync(null, null, "page2-token", 50), Times.Once);
    }

    [Fact]
    public async Task List_WithCustomPageSize_PassesPageSizeToService()
    {
        var pagedResult = new PagedResult<TrackerTask> { Items = [], ContinuationToken = null };
        _taskService
            .Setup(s => s.ListAsync(null, null, null, 5))
            .ReturnsAsync(pagedResult);

        var result = await _controller.List(null, null, null, 5);

        result.Should().BeOfType<OkObjectResult>();
        _taskService.Verify(s => s.ListAsync(null, null, null, 5), Times.Once);
    }

    [Fact]
    public async Task List_EmptyResults_Returns200WithEmptyItems()
    {
        var empty = new PagedResult<TrackerTask> { Items = [], ContinuationToken = null };
        _taskService
            .Setup(s => s.ListAsync(null, null, null, 50))
            .ReturnsAsync(empty);

        var result = await _controller.List(null, null, null, 50);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var paged = ok.Value.Should().BeOfType<PagedResult<TrackerTask>>().Subject;
        paged.Items.Should().BeEmpty();
        paged.HasMore.Should().BeFalse();
    }

    [Fact]
    public async Task List_WithMultiplePages_ReturnsContinuationToken()
    {
        var pagedResult = new PagedResult<TrackerTask>
        {
            Items = [new TrackerTask { Id = "t1", QueueName = "default" }],
            ContinuationToken = "next-page"
        };
        _taskService
            .Setup(s => s.ListAsync(null, null, null, 1))
            .ReturnsAsync(pagedResult);

        var result = await _controller.List(null, null, null, 1);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var paged = ok.Value.Should().BeOfType<PagedResult<TrackerTask>>().Subject;
        paged.HasMore.Should().BeTrue();
        paged.ContinuationToken.Should().Be("next-page");
    }

    [Fact]
    public async Task List_AllFiltersApplied_PassesAllToService()
    {
        var pagedResult = new PagedResult<TrackerTask> { Items = [], ContinuationToken = null };
        _taskService
            .Setup(s => s.ListAsync("q1", "done", "tok", 25))
            .ReturnsAsync(pagedResult);

        var result = await _controller.List("q1", "done", "tok", 25);

        result.Should().BeOfType<OkObjectResult>();
        _taskService.Verify(s => s.ListAsync("q1", "done", "tok", 25), Times.Once);
    }

    [Fact]
    public async Task List_ServiceThrows_ExceptionPropagates()
    {
        _taskService
            .Setup(s => s.ListAsync(null, null, null, 50))
            .ThrowsAsync(new InvalidOperationException("Database unavailable"));

        Func<Task> act = () => _controller.List(null, null, null, 50);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Database unavailable");
    }

    [Fact]
    public async Task Get_ServiceThrows_ExceptionPropagates()
    {
        _taskService
            .Setup(s => s.GetAsync("t1", "default"))
            .ThrowsAsync(new InvalidOperationException("Cosmos error"));

        Func<Task> act = () => _controller.Get("default", "t1");

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Cosmos error");
    }

    [Fact]
    public async Task Get_WithEmptyStringIds_DelegatesToService()
    {
        _taskService
            .Setup(s => s.GetAsync("", ""))
            .ReturnsAsync((TrackerTask?)null);

        var result = await _controller.Get("", "");

        result.Should().BeOfType<NotFoundResult>();
    }

    [Fact]
    public async Task GetLogs_WithContinuationToken_PassesTokenThrough()
    {
        var task = new TrackerTask { Id = "t1", QueueName = "default" };
        _taskService
            .Setup(s => s.GetAsync("t1", "default"))
            .ReturnsAsync(task);

        var logs = new PagedResult<TaskLog>
        {
            Items = [new TaskLog { Id = "l2", TaskId = "t1", Message = "more" }],
            ContinuationToken = null
        };
        _logService
            .Setup(s => s.GetLogsPagedAsync("t1", "log-token", 50))
            .ReturnsAsync(logs);

        var result = await _controller.GetLogs("default", "t1", "log-token", 50);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        ok.Value.Should().BeEquivalentTo(logs);
    }

    [Fact]
    public async Task GetLogs_WithCustomPageSize_PassesPageSizeThrough()
    {
        var task = new TrackerTask { Id = "t1", QueueName = "default" };
        _taskService
            .Setup(s => s.GetAsync("t1", "default"))
            .ReturnsAsync(task);

        var logs = new PagedResult<TaskLog> { Items = [], ContinuationToken = null };
        _logService
            .Setup(s => s.GetLogsPagedAsync("t1", null, 10))
            .ReturnsAsync(logs);

        var result = await _controller.GetLogs("default", "t1", null, 10);

        result.Should().BeOfType<OkObjectResult>();
        _logService.Verify(s => s.GetLogsPagedAsync("t1", null, 10), Times.Once);
    }

    [Fact]
    public async Task GetLogs_ServiceThrowsOnTaskLookup_ExceptionPropagates()
    {
        _taskService
            .Setup(s => s.GetAsync("t1", "default"))
            .ThrowsAsync(new InvalidOperationException("Cosmos error"));

        Func<Task> act = () => _controller.GetLogs("default", "t1", null, 100);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task GetLogs_LogServiceThrows_ExceptionPropagates()
    {
        var task = new TrackerTask { Id = "t1", QueueName = "default" };
        _taskService
            .Setup(s => s.GetAsync("t1", "default"))
            .ReturnsAsync(task);

        _logService
            .Setup(s => s.GetLogsPagedAsync("t1", null, 100))
            .ThrowsAsync(new InvalidOperationException("Log store error"));

        Func<Task> act = () => _controller.GetLogs("default", "t1", null, 100);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Log store error");
    }

    [Fact]
    public async Task GetLogs_EmptyLogs_ReturnsEmptyPagedResult()
    {
        var task = new TrackerTask { Id = "t1", QueueName = "default" };
        _taskService
            .Setup(s => s.GetAsync("t1", "default"))
            .ReturnsAsync(task);

        var logs = new PagedResult<TaskLog> { Items = [], ContinuationToken = null };
        _logService
            .Setup(s => s.GetLogsPagedAsync("t1", null, 100))
            .ReturnsAsync(logs);

        var result = await _controller.GetLogs("default", "t1", null, 100);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var paged = ok.Value.Should().BeOfType<PagedResult<TaskLog>>().Subject;
        paged.Items.Should().BeEmpty();
    }

    [Fact]
    public async Task GetLogs_DoesNotCallLogService_WhenTaskNotFound()
    {
        _taskService
            .Setup(s => s.GetAsync("missing", "default"))
            .ReturnsAsync((TrackerTask?)null);

        await _controller.GetLogs("default", "missing", null, 100);

        _logService.Verify(s => s.GetLogsPagedAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<int>()), Times.Never);
    }

    // --- POST endpoint tests ---

    [Fact]
    public async Task SetTask_ReturnsOkWithTask()
    {
        var task = new TrackerTask { Id = "t1", SessionId = "s1", QueueName = "default", Title = "Test" };
        _taskService
            .Setup(s => s.SetTaskAsync(null, "s1", "default", "Test", "started", null, null, "prompt", "test-user-id", "Test User"))
            .ReturnsAsync(task);

        var request = new SetTaskRequest { SessionId = "s1", Title = "Test", Status = "started" };
        var result = await _controller.SetTask(request);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        ok.Value.Should().BeEquivalentTo(task);
    }

    [Fact]
    public async Task SetTask_WithTaskId_PassesIdForUpdate()
    {
        var task = new TrackerTask { Id = "existing-t1", SessionId = "s1", Status = "done" };
        _taskService
            .Setup(s => s.SetTaskAsync("existing-t1", "s1", "default", "Updated", "done", "All good", null, "prompt", "test-user-id", "Test User"))
            .ReturnsAsync(task);

        var request = new SetTaskRequest
        {
            TaskId = "existing-t1", SessionId = "s1", Title = "Updated",
            Status = "done", Result = "All good"
        };
        var result = await _controller.SetTask(request);

        result.Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public async Task AddLog_ReturnsOkWithLog()
    {
        var task = new TrackerTask { Id = "t1", QueueName = "default" };
        _taskService.Setup(s => s.GetAsync("t1", "default")).ReturnsAsync(task);

        var log = new TaskLog { Id = "l1", TaskId = "t1", LogType = "progress", Message = "Working" };
        _logService.Setup(s => s.AddLogAsync("t1", "progress", "Working")).ReturnsAsync(log);

        var request = new AddLogRequest { LogType = "progress", Message = "Working" };
        var result = await _controller.AddLog("default", "t1", request);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        ok.Value.Should().BeEquivalentTo(log);
    }

    [Fact]
    public async Task AddLog_Returns404WhenTaskNotFound()
    {
        _taskService.Setup(s => s.GetAsync("missing", "default")).ReturnsAsync((TrackerTask?)null);

        var request = new AddLogRequest { LogType = "progress", Message = "Working" };
        var result = await _controller.AddLog("default", "missing", request);

        result.Should().BeOfType<NotFoundObjectResult>();
    }

    [Fact]
    public async Task AddLog_DoesNotCallLogService_WhenTaskNotFound()
    {
        _taskService.Setup(s => s.GetAsync("missing", "default")).ReturnsAsync((TrackerTask?)null);

        var request = new AddLogRequest { LogType = "progress", Message = "Working" };
        await _controller.AddLog("default", "missing", request);

        _logService.Verify(s => s.AddLogAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }
}

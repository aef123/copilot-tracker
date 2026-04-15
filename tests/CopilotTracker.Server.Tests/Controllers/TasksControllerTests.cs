namespace CopilotTracker.Server.Tests.Controllers;

using CopilotTracker.Core.Models;
using CopilotTracker.Core.Services;
using CopilotTracker.Server.Controllers;
using FluentAssertions;
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
}

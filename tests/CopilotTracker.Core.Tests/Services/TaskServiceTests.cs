namespace CopilotTracker.Core.Tests.Services;

using CopilotTracker.Core.Interfaces;
using CopilotTracker.Core.Models;
using CopilotTracker.Core.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

public class TaskServiceTests
{
    private readonly Mock<ITaskRepository> _taskRepo = new();
    private readonly Mock<ITaskLogRepository> _logRepo = new();
    private readonly TaskService _sut;

    public TaskServiceTests()
    {
        _sut = new TaskService(_taskRepo.Object, _logRepo.Object, NullLogger<TaskService>.Instance);
    }

    [Fact]
    public async Task SetTaskAsync_CreatesNewTask_WhenTaskIdIsNull()
    {
        _taskRepo.Setup(r => r.CreateAsync(It.IsAny<TrackerTask>()))
            .ReturnsAsync((TrackerTask t) => t);
        _logRepo.Setup(r => r.CreateAsync(It.IsAny<TaskLog>()))
            .ReturnsAsync((TaskLog l) => l);

        var result = await _sut.SetTaskAsync(
            null, "session-1", "default", "My Task",
            Models.TaskStatus.Started, null, null, TaskSource.Prompt,
            "user1", "copilot");

        result.SessionId.Should().Be("session-1");
        result.Title.Should().Be("My Task");
        result.Status.Should().Be(Models.TaskStatus.Started);
        result.Id.Should().NotBeNullOrEmpty();
        _taskRepo.Verify(r => r.CreateAsync(It.IsAny<TrackerTask>()), Times.Once);
    }

    [Fact]
    public async Task SetTaskAsync_UpdatesExistingTask()
    {
        var existing = new TrackerTask
        {
            Id = "t1",
            SessionId = "session-1",
            QueueName = "default",
            Title = "Old Title",
            Status = Models.TaskStatus.Started
        };
        _taskRepo.Setup(r => r.GetAsync("t1", "default")).ReturnsAsync(existing);
        _taskRepo.Setup(r => r.UpdateAsync(It.IsAny<TrackerTask>()))
            .ReturnsAsync((TrackerTask t) => t);
        _logRepo.Setup(r => r.CreateAsync(It.IsAny<TaskLog>()))
            .ReturnsAsync((TaskLog l) => l);

        var result = await _sut.SetTaskAsync(
            "t1", "session-1", "default", "Updated Title",
            Models.TaskStatus.Done, "Success", null, TaskSource.Prompt,
            "user1", "copilot");

        result.Title.Should().Be("Updated Title");
        result.Status.Should().Be(Models.TaskStatus.Done);
        result.Result.Should().Be("Success");
        _taskRepo.Verify(r => r.UpdateAsync(It.IsAny<TrackerTask>()), Times.Once);
        _taskRepo.Verify(r => r.CreateAsync(It.IsAny<TrackerTask>()), Times.Never);
    }

    [Fact]
    public async Task SetTaskAsync_WritesLogEntry()
    {
        _taskRepo.Setup(r => r.CreateAsync(It.IsAny<TrackerTask>()))
            .ReturnsAsync((TrackerTask t) => t);
        _logRepo.Setup(r => r.CreateAsync(It.IsAny<TaskLog>()))
            .ReturnsAsync((TaskLog l) => l);

        await _sut.SetTaskAsync(
            null, "session-1", "default", "Task",
            Models.TaskStatus.Started, null, null, TaskSource.Prompt,
            "user1", "copilot");

        _logRepo.Verify(r => r.CreateAsync(It.Is<TaskLog>(l =>
            l.LogType == LogTypes.StatusChange)), Times.Once);
    }

    [Fact]
    public async Task SetTaskAsync_DoesNotFail_WhenLogWriteThrows()
    {
        _taskRepo.Setup(r => r.CreateAsync(It.IsAny<TrackerTask>()))
            .ReturnsAsync((TrackerTask t) => t);
        _logRepo.Setup(r => r.CreateAsync(It.IsAny<TaskLog>()))
            .ThrowsAsync(new Exception("Cosmos DB error"));

        var act = () => _sut.SetTaskAsync(
            null, "session-1", "default", "Task",
            Models.TaskStatus.Started, null, null, TaskSource.Prompt,
            "user1", "copilot");

        var result = await act.Should().NotThrowAsync();
        result.Subject.Should().NotBeNull();
    }
}

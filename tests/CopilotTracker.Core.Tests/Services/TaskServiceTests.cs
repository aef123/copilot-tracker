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

    // --- Edge case and error path tests ---

    [Fact]
    public async Task SetTaskAsync_GeneratesId_WhenTaskIdIsNull()
    {
        _taskRepo.Setup(r => r.CreateAsync(It.IsAny<TrackerTask>()))
            .ReturnsAsync((TrackerTask t) => t);
        _logRepo.Setup(r => r.CreateAsync(It.IsAny<TaskLog>()))
            .ReturnsAsync((TaskLog l) => l);

        var result = await _sut.SetTaskAsync(
            null, "s1", "default", "Title",
            Models.TaskStatus.Started, null, null, TaskSource.Prompt,
            "user1", "copilot");

        result.Id.Should().NotBeNullOrEmpty();
        Guid.TryParse(result.Id, out _).Should().BeTrue("generated ID should be a valid GUID");
    }

    [Fact]
    public async Task SetTaskAsync_UsesProvidedTaskId_WhenNotNull_AndTaskNotFound()
    {
        _taskRepo.Setup(r => r.GetAsync("custom-id", "default")).ReturnsAsync((TrackerTask?)null);
        _taskRepo.Setup(r => r.CreateAsync(It.IsAny<TrackerTask>()))
            .ReturnsAsync((TrackerTask t) => t);
        _logRepo.Setup(r => r.CreateAsync(It.IsAny<TaskLog>()))
            .ReturnsAsync((TaskLog l) => l);

        var result = await _sut.SetTaskAsync(
            "custom-id", "s1", "default", "Title",
            Models.TaskStatus.Started, null, null, TaskSource.Prompt,
            "user1", "copilot");

        result.Id.Should().Be("custom-id");
    }

    [Fact]
    public async Task SetTaskAsync_UpdatesExistingTask_PreservesSessionId()
    {
        var existing = new TrackerTask
        {
            Id = "t1", SessionId = "original-session", QueueName = "q1",
            Title = "Old", Status = Models.TaskStatus.Started
        };
        _taskRepo.Setup(r => r.GetAsync("t1", "q1")).ReturnsAsync(existing);
        _taskRepo.Setup(r => r.UpdateAsync(It.IsAny<TrackerTask>()))
            .ReturnsAsync((TrackerTask t) => t);
        _logRepo.Setup(r => r.CreateAsync(It.IsAny<TaskLog>()))
            .ReturnsAsync((TaskLog l) => l);

        var result = await _sut.SetTaskAsync(
            "t1", "different-session", "q1", "New Title",
            Models.TaskStatus.Done, "result", null, TaskSource.Prompt,
            "user1", "copilot");

        // SessionId stays from the existing task, not overwritten
        result.SessionId.Should().Be("original-session");
    }

    [Fact]
    public async Task SetTaskAsync_SetsErrorMessage_OnFailedStatus()
    {
        _taskRepo.Setup(r => r.CreateAsync(It.IsAny<TrackerTask>()))
            .ReturnsAsync((TrackerTask t) => t);
        _logRepo.Setup(r => r.CreateAsync(It.IsAny<TaskLog>()))
            .ReturnsAsync((TaskLog l) => l);

        var result = await _sut.SetTaskAsync(
            null, "s1", "default", "Broken Task",
            Models.TaskStatus.Failed, null, "Something broke", TaskSource.Prompt,
            "user1", "copilot");

        result.Status.Should().Be(Models.TaskStatus.Failed);
        result.ErrorMessage.Should().Be("Something broke");
    }

    [Fact]
    public async Task SetTaskAsync_PropagatesRepoException_OnCreate()
    {
        _taskRepo.Setup(r => r.CreateAsync(It.IsAny<TrackerTask>()))
            .ThrowsAsync(new InvalidOperationException("Cosmos conflict"));

        var act = () => _sut.SetTaskAsync(
            null, "s1", "default", "Title",
            Models.TaskStatus.Started, null, null, TaskSource.Prompt,
            "user1", "copilot");

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Cosmos conflict*");
    }

    [Fact]
    public async Task SetTaskAsync_PropagatesRepoException_OnUpdate()
    {
        var existing = new TrackerTask
        {
            Id = "t1", SessionId = "s1", QueueName = "default",
            Title = "Task", Status = Models.TaskStatus.Started
        };
        _taskRepo.Setup(r => r.GetAsync("t1", "default")).ReturnsAsync(existing);
        _taskRepo.Setup(r => r.UpdateAsync(It.IsAny<TrackerTask>()))
            .ThrowsAsync(new Exception("Update failed"));

        var act = () => _sut.SetTaskAsync(
            "t1", "s1", "default", "New Title",
            Models.TaskStatus.Done, null, null, TaskSource.Prompt,
            "user1", "copilot");

        await act.Should().ThrowAsync<Exception>().WithMessage("*Update failed*");
    }

    [Fact]
    public async Task SetTaskAsync_LogMessageContainsOldAndNewStatus_OnUpdate()
    {
        var existing = new TrackerTask
        {
            Id = "t1", SessionId = "s1", QueueName = "default",
            Title = "Task", Status = Models.TaskStatus.Started
        };
        _taskRepo.Setup(r => r.GetAsync("t1", "default")).ReturnsAsync(existing);
        _taskRepo.Setup(r => r.UpdateAsync(It.IsAny<TrackerTask>()))
            .ReturnsAsync((TrackerTask t) => t);

        TaskLog? capturedLog = null;
        _logRepo.Setup(r => r.CreateAsync(It.IsAny<TaskLog>()))
            .Callback<TaskLog>(l => capturedLog = l)
            .ReturnsAsync((TaskLog l) => l);

        await _sut.SetTaskAsync(
            "t1", "s1", "default", "Task",
            Models.TaskStatus.Done, "result", null, TaskSource.Prompt,
            "user1", "copilot");

        capturedLog.Should().NotBeNull();
        capturedLog!.Message.Should().Contain("started").And.Contain("done");
    }

    [Fact]
    public async Task SetTaskAsync_LogMessageContainsCreated_OnNewTask()
    {
        _taskRepo.Setup(r => r.CreateAsync(It.IsAny<TrackerTask>()))
            .ReturnsAsync((TrackerTask t) => t);

        TaskLog? capturedLog = null;
        _logRepo.Setup(r => r.CreateAsync(It.IsAny<TaskLog>()))
            .Callback<TaskLog>(l => capturedLog = l)
            .ReturnsAsync((TaskLog l) => l);

        await _sut.SetTaskAsync(
            null, "s1", "default", "New Task",
            Models.TaskStatus.Started, null, null, TaskSource.Prompt,
            "user1", "copilot");

        capturedLog.Should().NotBeNull();
        capturedLog!.Message.Should().Contain("created");
    }

    [Fact]
    public async Task SetTaskAsync_WithQueueSource_SetsSource()
    {
        _taskRepo.Setup(r => r.CreateAsync(It.IsAny<TrackerTask>()))
            .ReturnsAsync((TrackerTask t) => t);
        _logRepo.Setup(r => r.CreateAsync(It.IsAny<TaskLog>()))
            .ReturnsAsync((TaskLog l) => l);

        var result = await _sut.SetTaskAsync(
            null, "s1", "work-queue", "Queued Task",
            Models.TaskStatus.Started, null, null, TaskSource.Queue,
            "user1", "copilot");

        result.Source.Should().Be(TaskSource.Queue);
        result.QueueName.Should().Be("work-queue");
    }

    [Fact]
    public async Task GetAsync_ReturnsNull_WhenTaskDoesNotExist()
    {
        _taskRepo.Setup(r => r.GetAsync("missing", "default")).ReturnsAsync((TrackerTask?)null);

        var result = await _sut.GetAsync("missing", "default");

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetAsync_ReturnsTask_WhenFound()
    {
        var task = new TrackerTask { Id = "t1", QueueName = "default" };
        _taskRepo.Setup(r => r.GetAsync("t1", "default")).ReturnsAsync(task);

        var result = await _sut.GetAsync("t1", "default");

        result.Should().BeSameAs(task);
    }

    [Fact]
    public async Task ListAsync_ReturnsEmptyPage_WhenNoTasks()
    {
        _taskRepo.Setup(r => r.ListAsync("default", null, null, 50))
            .ReturnsAsync(new PagedResult<TrackerTask> { Items = [], ContinuationToken = null });

        var result = await _sut.ListAsync("default", null, null, 50);

        result.Items.Should().BeEmpty();
        result.HasMore.Should().BeFalse();
    }

    [Fact]
    public async Task ListAsync_ForwardsFilterParameters()
    {
        _taskRepo.Setup(r => r.ListAsync("q1", Models.TaskStatus.Done, "token", 10))
            .ReturnsAsync(new PagedResult<TrackerTask>
            {
                Items = [new TrackerTask()],
                ContinuationToken = "next"
            });

        var result = await _sut.ListAsync("q1", Models.TaskStatus.Done, "token", 10);

        result.Items.Should().HaveCount(1);
        result.HasMore.Should().BeTrue();
    }

    [Fact]
    public async Task GetBySessionAsync_ReturnsTasksForSession()
    {
        _taskRepo.Setup(r => r.GetBySessionAsync("s1", null, 50))
            .ReturnsAsync(new PagedResult<TrackerTask>
            {
                Items = [new TrackerTask { SessionId = "s1" }, new TrackerTask { SessionId = "s1" }]
            });

        var result = await _sut.GetBySessionAsync("s1", null, 50);

        result.Items.Should().HaveCount(2);
    }

    [Fact]
    public async Task GetBySessionAsync_ReturnsEmptyPage_WhenNoTasksForSession()
    {
        _taskRepo.Setup(r => r.GetBySessionAsync("empty-session", null, 50))
            .ReturnsAsync(new PagedResult<TrackerTask> { Items = [] });

        var result = await _sut.GetBySessionAsync("empty-session", null, 50);

        result.Items.Should().BeEmpty();
    }
}

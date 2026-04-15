namespace CopilotTracker.Core.Tests.Services;

using CopilotTracker.Core.Interfaces;
using CopilotTracker.Core.Models;
using CopilotTracker.Core.Services;
using FluentAssertions;
using Moq;

public class TaskLogServiceTests
{
    private readonly Mock<ITaskLogRepository> _logRepo = new();
    private readonly TaskLogService _sut;

    public TaskLogServiceTests()
    {
        _sut = new TaskLogService(_logRepo.Object);
    }

    [Fact]
    public async Task AddLogAsync_CreatesLogWithCorrectProperties()
    {
        _logRepo.Setup(r => r.CreateAsync(It.IsAny<TaskLog>()))
            .ReturnsAsync((TaskLog l) => l);

        var before = DateTime.UtcNow;
        var result = await _sut.AddLogAsync("t1", LogTypes.Progress, "Step 1 complete");

        result.TaskId.Should().Be("t1");
        result.LogType.Should().Be(LogTypes.Progress);
        result.Message.Should().Be("Step 1 complete");
        result.Timestamp.Should().BeOnOrAfter(before);
        result.Id.Should().NotBeNullOrEmpty();
    }

    [Theory]
    [InlineData(LogTypes.StatusChange)]
    [InlineData(LogTypes.Progress)]
    [InlineData(LogTypes.Output)]
    [InlineData(LogTypes.Error)]
    [InlineData(LogTypes.Heartbeat)]
    public async Task AddLogAsync_AcceptsAllLogTypes(string logType)
    {
        _logRepo.Setup(r => r.CreateAsync(It.IsAny<TaskLog>()))
            .ReturnsAsync((TaskLog l) => l);

        var result = await _sut.AddLogAsync("t1", logType, "msg");

        result.LogType.Should().Be(logType);
    }

    [Fact]
    public async Task AddLogAsync_PropagatesRepoException()
    {
        _logRepo.Setup(r => r.CreateAsync(It.IsAny<TaskLog>()))
            .ThrowsAsync(new Exception("Not found"));

        var act = () => _sut.AddLogAsync("nonexistent-task", LogTypes.Progress, "msg");

        await act.Should().ThrowAsync<Exception>().WithMessage("*Not found*");
    }

    [Fact]
    public async Task AddLogAsync_WithEmptyMessage()
    {
        _logRepo.Setup(r => r.CreateAsync(It.IsAny<TaskLog>()))
            .ReturnsAsync((TaskLog l) => l);

        var result = await _sut.AddLogAsync("t1", LogTypes.Progress, "");

        result.Message.Should().BeEmpty();
    }

    [Fact]
    public async Task GetLogsAsync_ReturnsLogs()
    {
        var logs = new List<TaskLog>
        {
            new() { TaskId = "t1", LogType = LogTypes.StatusChange, Message = "created" },
            new() { TaskId = "t1", LogType = LogTypes.Progress, Message = "50%" }
        };
        _logRepo.Setup(r => r.GetByTaskAsync("t1")).ReturnsAsync(logs);

        var result = await _sut.GetLogsAsync("t1");

        result.Should().HaveCount(2);
    }

    [Fact]
    public async Task GetLogsAsync_ReturnsEmptyList_WhenNoLogs()
    {
        _logRepo.Setup(r => r.GetByTaskAsync("t1")).ReturnsAsync(new List<TaskLog>());

        var result = await _sut.GetLogsAsync("t1");

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetLogsPagedAsync_ReturnsPaginatedResults()
    {
        _logRepo.Setup(r => r.GetByTaskPagedAsync("t1", null, 10))
            .ReturnsAsync(new PagedResult<TaskLog>
            {
                Items = [new TaskLog { TaskId = "t1" }],
                ContinuationToken = "next-token"
            });

        var result = await _sut.GetLogsPagedAsync("t1", null, 10);

        result.Items.Should().HaveCount(1);
        result.HasMore.Should().BeTrue();
        result.ContinuationToken.Should().Be("next-token");
    }

    [Fact]
    public async Task GetLogsPagedAsync_ReturnsEmptyPage_WhenNoLogs()
    {
        _logRepo.Setup(r => r.GetByTaskPagedAsync("t1", null, 50))
            .ReturnsAsync(new PagedResult<TaskLog> { Items = [], ContinuationToken = null });

        var result = await _sut.GetLogsPagedAsync("t1", null, 50);

        result.Items.Should().BeEmpty();
        result.HasMore.Should().BeFalse();
    }

    [Fact]
    public async Task GetLogsPagedAsync_ForwardsContinuationToken()
    {
        _logRepo.Setup(r => r.GetByTaskPagedAsync("t1", "page2-token", 25))
            .ReturnsAsync(new PagedResult<TaskLog>
            {
                Items = [new TaskLog()],
                ContinuationToken = null
            });

        var result = await _sut.GetLogsPagedAsync("t1", "page2-token", 25);

        result.Items.Should().HaveCount(1);
        result.HasMore.Should().BeFalse();
    }

    [Fact]
    public async Task GetLogsAsync_PropagatesRepoException()
    {
        _logRepo.Setup(r => r.GetByTaskAsync("t1"))
            .ThrowsAsync(new Exception("Connection lost"));

        var act = () => _sut.GetLogsAsync("t1");

        await act.Should().ThrowAsync<Exception>().WithMessage("*Connection lost*");
    }
}

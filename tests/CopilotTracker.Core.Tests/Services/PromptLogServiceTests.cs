namespace CopilotTracker.Core.Tests.Services;

using CopilotTracker.Core.Interfaces;
using CopilotTracker.Core.Models;
using CopilotTracker.Core.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

public class PromptLogServiceTests
{
    private readonly Mock<IPromptLogRepository> _logRepo = new();
    private readonly PromptLogService _sut;

    public PromptLogServiceTests()
    {
        _sut = new PromptLogService(_logRepo.Object, NullLogger<PromptLogService>.Instance);
    }

    // --- AddLogAsync tests ---

    [Fact]
    public async Task AddLogAsync_CreatesLog_WithCorrectFieldsAndGuid()
    {
        _logRepo.Setup(r => r.CreateAsync(It.IsAny<PromptLog>()))
            .ReturnsAsync((PromptLog l) => l);

        var result = await _sut.AddLogAsync(
            "p1", "s1", "subagent_start", "Agent started: explorer",
            "explorer", null, 1700000000000);

        result.Id.Should().NotBeNullOrEmpty();
        Guid.TryParse(result.Id, out _).Should().BeTrue();
        result.PromptId.Should().Be("p1");
        result.SessionId.Should().Be("s1");
        result.LogType.Should().Be("subagent_start");
        result.Message.Should().Be("Agent started: explorer");
        result.AgentName.Should().Be("explorer");
        result.NotificationType.Should().BeNull();
        result.HookTimestamp.Should().Be(1700000000000);
    }

    [Fact]
    public async Task AddLogAsync_SetsTimestamp_ToUtcNow()
    {
        _logRepo.Setup(r => r.CreateAsync(It.IsAny<PromptLog>()))
            .ReturnsAsync((PromptLog l) => l);

        var before = DateTime.UtcNow;
        var result = await _sut.AddLogAsync(
            "p1", "s1", "notification", "Hello", null, "info", 123);

        result.Timestamp.Should().BeOnOrAfter(before);
        result.Timestamp.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(2));
    }

    // --- GetLogsAsync tests ---

    [Fact]
    public async Task GetLogsAsync_DelegatesToRepository()
    {
        var logs = new List<PromptLog>
        {
            new() { Id = "l1", PromptId = "p1", LogType = "subagent_start" },
            new() { Id = "l2", PromptId = "p1", LogType = "subagent_stop" }
        };
        _logRepo.Setup(r => r.GetByPromptAsync("p1")).ReturnsAsync(logs);

        var result = await _sut.GetLogsAsync("p1");

        result.Should().HaveCount(2);
    }

    // --- GetLogsPagedAsync tests ---

    [Fact]
    public async Task GetLogsPagedAsync_DelegatesToRepository()
    {
        var paged = new PagedResult<PromptLog>
        {
            Items = [new PromptLog { Id = "l1", PromptId = "p1" }],
            ContinuationToken = "next-token"
        };
        _logRepo.Setup(r => r.GetByPromptPagedAsync("p1", null, 25))
            .ReturnsAsync(paged);

        var result = await _sut.GetLogsPagedAsync("p1", null, 25);

        result.Items.Should().HaveCount(1);
        result.HasMore.Should().BeTrue();
        result.ContinuationToken.Should().Be("next-token");
    }
}

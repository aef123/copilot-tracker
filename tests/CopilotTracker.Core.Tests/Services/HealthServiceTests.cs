namespace CopilotTracker.Core.Tests.Services;

using CopilotTracker.Core.Interfaces;
using CopilotTracker.Core.Models;
using CopilotTracker.Core.Services;
using FluentAssertions;
using Moq;

public class HealthServiceTests
{
    private readonly Mock<ISessionRepository> _sessionRepo = new();
    private readonly Mock<ITaskRepository> _taskRepo = new();
    private readonly HealthService _sut;

    public HealthServiceTests()
    {
        _sut = new HealthService(_sessionRepo.Object, _taskRepo.Object);
    }

    private void SetupCounts(int active, int completed, int stale, int totalTasks, int activeTasks)
    {
        _sessionRepo.Setup(r => r.ListAsync(null, SessionStatus.Active, null, null, 50))
            .ReturnsAsync(new PagedResult<Session>
            {
                Items = Enumerable.Range(0, active).Select(_ => new Session()).ToList(),
                ContinuationToken = null
            });
        _sessionRepo.Setup(r => r.ListAsync(null, SessionStatus.Completed, null, null, 50))
            .ReturnsAsync(new PagedResult<Session>
            {
                Items = Enumerable.Range(0, completed).Select(_ => new Session()).ToList(),
                ContinuationToken = null
            });
        _sessionRepo.Setup(r => r.ListAsync(null, SessionStatus.Stale, null, null, 50))
            .ReturnsAsync(new PagedResult<Session>
            {
                Items = Enumerable.Range(0, stale).Select(_ => new Session()).ToList(),
                ContinuationToken = null
            });
        _taskRepo.Setup(r => r.ListAsync(null, null, null, 50))
            .ReturnsAsync(new PagedResult<TrackerTask>
            {
                Items = Enumerable.Range(0, totalTasks).Select(_ => new TrackerTask()).ToList(),
                ContinuationToken = null
            });
        _taskRepo.Setup(r => r.ListAsync(null, Models.TaskStatus.Started, null, 50))
            .ReturnsAsync(new PagedResult<TrackerTask>
            {
                Items = Enumerable.Range(0, activeTasks).Select(_ => new TrackerTask()).ToList(),
                ContinuationToken = null
            });
    }

    [Fact]
    public async Task GetHealthAsync_ReturnsCorrectCounts()
    {
        SetupCounts(active: 3, completed: 10, stale: 2, totalTasks: 15, activeTasks: 5);

        var result = await _sut.GetHealthAsync();

        result.ActiveSessions.Should().Be(3);
        result.CompletedSessions.Should().Be(10);
        result.StaleSessions.Should().Be(2);
        result.TotalTasks.Should().Be(15);
        result.ActiveTasks.Should().Be(5);
    }

    [Fact]
    public async Task GetHealthAsync_CachesResult()
    {
        SetupCounts(active: 1, completed: 0, stale: 0, totalTasks: 0, activeTasks: 0);

        var result1 = await _sut.GetHealthAsync();
        var result2 = await _sut.GetHealthAsync();

        result1.Should().BeSameAs(result2);
        // ListAsync for active sessions should only be called once due to caching
        _sessionRepo.Verify(r => r.ListAsync(null, SessionStatus.Active, null, null, 50), Times.Once);
    }

    [Fact]
    public async Task GetHealthAsync_RefreshesAfterTtl()
    {
        SetupCounts(active: 1, completed: 0, stale: 0, totalTasks: 0, activeTasks: 0);

        var result1 = await _sut.GetHealthAsync();

        // Force cache expiry by using reflection to set _cachedAt in the past
        var cachedAtField = typeof(HealthService).GetField("_cachedAt",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        cachedAtField.SetValue(_sut, DateTime.UtcNow.AddSeconds(-60));

        SetupCounts(active: 5, completed: 0, stale: 0, totalTasks: 0, activeTasks: 0);

        var result2 = await _sut.GetHealthAsync();

        result2.Should().NotBeSameAs(result1);
        result2.ActiveSessions.Should().Be(5);
    }
}

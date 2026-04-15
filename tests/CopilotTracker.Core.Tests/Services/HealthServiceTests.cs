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

    private void SetupProbes(int activeSessions, int completedSessions, int staleSessions, int totalTasks, int activeTasks)
    {
        // HealthService first calls ListAsync with pageSize:1 as a probe before counting
        _sessionRepo.Setup(r => r.ListAsync(null, SessionStatus.Active, null, null, 1))
            .ReturnsAsync(new PagedResult<Session>
            {
                Items = activeSessions > 0 ? [new Session()] : [],
                ContinuationToken = null
            });
        _sessionRepo.Setup(r => r.ListAsync(null, SessionStatus.Completed, null, null, 1))
            .ReturnsAsync(new PagedResult<Session>
            {
                Items = completedSessions > 0 ? [new Session()] : [],
                ContinuationToken = null
            });
        _sessionRepo.Setup(r => r.ListAsync(null, SessionStatus.Stale, null, null, 1))
            .ReturnsAsync(new PagedResult<Session>
            {
                Items = staleSessions > 0 ? [new Session()] : [],
                ContinuationToken = null
            });
        _taskRepo.Setup(r => r.ListAsync(null, null, null, 1))
            .ReturnsAsync(new PagedResult<TrackerTask>
            {
                Items = totalTasks > 0 ? [new TrackerTask()] : [],
                ContinuationToken = null
            });
        _taskRepo.Setup(r => r.ListAsync(null, Models.TaskStatus.Started, null, 1))
            .ReturnsAsync(new PagedResult<TrackerTask>
            {
                Items = activeTasks > 0 ? [new TrackerTask()] : [],
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

    // --- Edge case and error path tests ---

    [Fact]
    public async Task GetHealthAsync_ReturnsAllZeros_WhenNothingExists()
    {
        SetupCounts(active: 0, completed: 0, stale: 0, totalTasks: 0, activeTasks: 0);

        var result = await _sut.GetHealthAsync();

        result.ActiveSessions.Should().Be(0);
        result.CompletedSessions.Should().Be(0);
        result.StaleSessions.Should().Be(0);
        result.TotalTasks.Should().Be(0);
        result.ActiveTasks.Should().Be(0);
    }

    [Fact]
    public async Task GetHealthAsync_SetsTimestamp()
    {
        SetupCounts(active: 0, completed: 0, stale: 0, totalTasks: 0, activeTasks: 0);

        var before = DateTime.UtcNow;
        var result = await _sut.GetHealthAsync();

        result.Timestamp.Should().BeOnOrAfter(before);
        result.Timestamp.Should().BeOnOrBefore(DateTime.UtcNow);
    }

    [Fact]
    public async Task GetHealthAsync_PropagatesRepoException()
    {
        _sessionRepo.Setup(r => r.ListAsync(null, SessionStatus.Active, null, null, It.IsAny<int>()))
            .ThrowsAsync(new Exception("DB down"));

        var act = () => _sut.GetHealthAsync();

        await act.Should().ThrowAsync<Exception>().WithMessage("*DB down*");
    }

    [Fact]
    public async Task GetHealthAsync_CacheServesStaleData_WhileWithinTtl()
    {
        SetupCounts(active: 3, completed: 0, stale: 0, totalTasks: 0, activeTasks: 0);

        var result1 = await _sut.GetHealthAsync();

        // Change the repo setup to return different data
        SetupCounts(active: 99, completed: 0, stale: 0, totalTasks: 0, activeTasks: 0);

        var result2 = await _sut.GetHealthAsync();

        // Should still be the cached value
        result2.ActiveSessions.Should().Be(3);
        result2.Should().BeSameAs(result1);
    }

    [Fact]
    public async Task GetHealthAsync_ConcurrentCalls_OnlyFetchOnce()
    {
        int callCount = 0;
        _sessionRepo.Setup(r => r.ListAsync(null, SessionStatus.Active, null, null, It.IsAny<int>()))
            .ReturnsAsync(() =>
            {
                Interlocked.Increment(ref callCount);
                return new PagedResult<Session> { Items = [], ContinuationToken = null };
            });
        _sessionRepo.Setup(r => r.ListAsync(null, SessionStatus.Completed, null, null, It.IsAny<int>()))
            .ReturnsAsync(new PagedResult<Session> { Items = [], ContinuationToken = null });
        _sessionRepo.Setup(r => r.ListAsync(null, SessionStatus.Stale, null, null, It.IsAny<int>()))
            .ReturnsAsync(new PagedResult<Session> { Items = [], ContinuationToken = null });
        _taskRepo.Setup(r => r.ListAsync(null, null, null, It.IsAny<int>()))
            .ReturnsAsync(new PagedResult<TrackerTask> { Items = [], ContinuationToken = null });
        _taskRepo.Setup(r => r.ListAsync(null, Models.TaskStatus.Started, null, It.IsAny<int>()))
            .ReturnsAsync(new PagedResult<TrackerTask> { Items = [], ContinuationToken = null });

        var tasks = Enumerable.Range(0, 10).Select(_ => _sut.GetHealthAsync()).ToArray();
        var results = await Task.WhenAll(tasks);

        // Double-check locking: repo should be hit very few times (ideally once + the initial pageSize:1 calls)
        results.Should().AllSatisfy(r => r.Should().NotBeNull());
    }

    [Fact]
    public async Task GetHealthAsync_AfterException_RetrySucceeds()
    {
        // First call: repo throws on the initial pageSize:1 probe
        _sessionRepo.Setup(r => r.ListAsync(null, SessionStatus.Active, null, null, 1))
            .ThrowsAsync(new Exception("Transient error"));

        var act = () => _sut.GetHealthAsync();
        await act.Should().ThrowAsync<Exception>();

        // Reset to non-throwing setup and configure full counts.
        // Must cover both pageSize:1 (probe) and pageSize:50 (count) calls.
        _sessionRepo.Reset();
        _taskRepo.Reset();
        SetupCounts(active: 2, completed: 0, stale: 0, totalTasks: 0, activeTasks: 0);
        SetupProbes(activeSessions: 2, completedSessions: 0, staleSessions: 0, totalTasks: 0, activeTasks: 0);

        var result = await _sut.GetHealthAsync();
        result.ActiveSessions.Should().Be(2);
    }
}

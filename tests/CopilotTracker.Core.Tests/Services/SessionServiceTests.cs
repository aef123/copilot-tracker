namespace CopilotTracker.Core.Tests.Services;

using CopilotTracker.Core.Interfaces;
using CopilotTracker.Core.Models;
using CopilotTracker.Core.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

public class SessionServiceTests
{
    private readonly Mock<ISessionRepository> _sessionRepo = new();
    private readonly Mock<ITaskLogRepository> _logRepo = new();
    private readonly SessionService _sut;

    public SessionServiceTests()
    {
        _sut = new SessionService(_sessionRepo.Object, _logRepo.Object, NullLogger<SessionService>.Instance);
    }

    [Fact]
    public async Task InitializeSessionAsync_CreatesSessionWithCorrectProperties()
    {
        _sessionRepo.Setup(r => r.GetActiveByMachineAsync("machine-1"))
            .ReturnsAsync(new List<Session>());
        _sessionRepo.Setup(r => r.CreateAsync(It.IsAny<Session>()))
            .ReturnsAsync((Session s) => s);

        var result = await _sut.InitializeSessionAsync("machine-1", "repo/url", "main", "user1", "copilot");

        result.MachineId.Should().Be("machine-1");
        result.Repository.Should().Be("repo/url");
        result.Branch.Should().Be("main");
        result.Status.Should().Be(SessionStatus.Active);
        result.UserId.Should().Be("user1");
        result.CreatedBy.Should().Be("copilot");
        _sessionRepo.Verify(r => r.CreateAsync(It.IsAny<Session>()), Times.Once);
    }

    [Fact]
    public async Task InitializeSessionAsync_MarksExistingActiveSessionsAsStale()
    {
        var existing = new Session { Id = "old-session", MachineId = "machine-1", Status = SessionStatus.Active };
        _sessionRepo.Setup(r => r.GetActiveByMachineAsync("machine-1"))
            .ReturnsAsync(new List<Session> { existing });
        _sessionRepo.Setup(r => r.UpdateAsync(It.IsAny<Session>()))
            .ReturnsAsync((Session s) => s);
        _sessionRepo.Setup(r => r.CreateAsync(It.IsAny<Session>()))
            .ReturnsAsync((Session s) => s);

        await _sut.InitializeSessionAsync("machine-1", null, null, "user1", "copilot");

        _sessionRepo.Verify(r => r.UpdateAsync(It.Is<Session>(s =>
            s.Id == "old-session" && s.Status == SessionStatus.Stale)), Times.Once);
    }

    [Fact]
    public async Task HeartbeatAsync_UpdatesLastHeartbeat()
    {
        var session = new Session
        {
            Id = "s1",
            MachineId = "m1",
            Status = SessionStatus.Active,
            LastHeartbeat = DateTime.UtcNow.AddMinutes(-5)
        };
        _sessionRepo.Setup(r => r.GetAsync("s1", "m1")).ReturnsAsync(session);
        _sessionRepo.Setup(r => r.UpdateAsync(It.IsAny<Session>()))
            .ReturnsAsync((Session s) => s);

        var before = DateTime.UtcNow;
        var result = await _sut.HeartbeatAsync("s1", "m1");

        result.LastHeartbeat.Should().BeOnOrAfter(before);
        result.UpdatedAt.Should().BeOnOrAfter(before);
    }

    [Fact]
    public async Task HeartbeatAsync_ThrowsWhenSessionNotFound()
    {
        _sessionRepo.Setup(r => r.GetAsync("missing", "m1")).ReturnsAsync((Session?)null);

        var act = () => _sut.HeartbeatAsync("missing", "m1");

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*not found*");
    }

    [Fact]
    public async Task HeartbeatAsync_ThrowsWhenSessionNotActive()
    {
        var session = new Session { Id = "s1", MachineId = "m1", Status = SessionStatus.Completed };
        _sessionRepo.Setup(r => r.GetAsync("s1", "m1")).ReturnsAsync(session);

        var act = () => _sut.HeartbeatAsync("s1", "m1");

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*not active*");
    }

    [Fact]
    public async Task CompleteSessionAsync_SetsStatusAndCompletedAt()
    {
        var session = new Session { Id = "s1", MachineId = "m1", Status = SessionStatus.Active };
        _sessionRepo.Setup(r => r.GetAsync("s1", "m1")).ReturnsAsync(session);
        _sessionRepo.Setup(r => r.UpdateAsync(It.IsAny<Session>()))
            .ReturnsAsync((Session s) => s);

        var before = DateTime.UtcNow;
        var result = await _sut.CompleteSessionAsync("s1", "m1", "All done");

        result.Status.Should().Be(SessionStatus.Completed);
        result.CompletedAt.Should().NotBeNull();
        result.CompletedAt!.Value.Should().BeOnOrAfter(before);
        result.Summary.Should().Be("All done");
    }

    [Fact]
    public async Task CompleteSessionAsync_ThrowsWhenNotActive()
    {
        var session = new Session { Id = "s1", MachineId = "m1", Status = SessionStatus.Stale };
        _sessionRepo.Setup(r => r.GetAsync("s1", "m1")).ReturnsAsync(session);

        var act = () => _sut.CompleteSessionAsync("s1", "m1", null);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*not active*");
    }

    [Fact]
    public async Task CleanupStaleSessionsAsync_MarksOldSessionsAsStale()
    {
        var staleSessions = new List<Session>
        {
            new() { Id = "s1", MachineId = "m1", Status = SessionStatus.Active },
            new() { Id = "s2", MachineId = "m2", Status = SessionStatus.Active }
        };

        _sessionRepo.Setup(r => r.GetStaleSessionsAsync(It.IsAny<DateTime>(), null, 50))
            .ReturnsAsync(new PagedResult<Session> { Items = staleSessions, ContinuationToken = null });
        _sessionRepo.Setup(r => r.UpdateAsync(It.IsAny<Session>()))
            .ReturnsAsync((Session s) => s);

        var count = await _sut.CleanupStaleSessionsAsync(TimeSpan.FromMinutes(5));

        count.Should().Be(2);
        _sessionRepo.Verify(r => r.UpdateAsync(It.Is<Session>(s => s.Status == SessionStatus.Stale)), Times.Exactly(2));
    }

    [Fact]
    public async Task CleanupStaleSessionsAsync_ReturnsCorrectCount_AcrossPages()
    {
        var page1 = new PagedResult<Session>
        {
            Items = [new Session { Id = "s1", MachineId = "m1", Status = SessionStatus.Active }],
            ContinuationToken = "token1"
        };
        var page2 = new PagedResult<Session>
        {
            Items = [new Session { Id = "s2", MachineId = "m2", Status = SessionStatus.Active }],
            ContinuationToken = null
        };

        _sessionRepo.Setup(r => r.GetStaleSessionsAsync(It.IsAny<DateTime>(), null, 50))
            .ReturnsAsync(page1);
        _sessionRepo.Setup(r => r.GetStaleSessionsAsync(It.IsAny<DateTime>(), "token1", 50))
            .ReturnsAsync(page2);
        _sessionRepo.Setup(r => r.UpdateAsync(It.IsAny<Session>()))
            .ReturnsAsync((Session s) => s);

        var count = await _sut.CleanupStaleSessionsAsync(TimeSpan.FromMinutes(5));

        count.Should().Be(2);
    }
}

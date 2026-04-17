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
    private readonly Mock<IPromptRepository> _promptRepo = new();
    private readonly SessionService _sut;

    public SessionServiceTests()
    {
        _sut = new SessionService(_sessionRepo.Object, _promptRepo.Object, NullLogger<SessionService>.Instance);
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
        // No recent prompts, so sessions are truly stale
        _promptRepo.Setup(r => r.GetBySessionAsync(It.IsAny<string>()))
            .ReturnsAsync(new List<Prompt>());

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
        _promptRepo.Setup(r => r.GetBySessionAsync(It.IsAny<string>()))
            .ReturnsAsync(new List<Prompt>());

        var count = await _sut.CleanupStaleSessionsAsync(TimeSpan.FromMinutes(5));

        count.Should().Be(2);
    }

    // --- Edge case and error path tests ---

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task InitializeSessionAsync_PassesMachineIdToRepo_EvenIfNullOrEmpty(string? machineId)
    {
        // Service doesn't validate machineId; it delegates to repo. Verify it forwards as-is.
        _sessionRepo.Setup(r => r.GetActiveByMachineAsync(machineId!))
            .ReturnsAsync(new List<Session>());
        _sessionRepo.Setup(r => r.CreateAsync(It.IsAny<Session>()))
            .ReturnsAsync((Session s) => s);

        var result = await _sut.InitializeSessionAsync(machineId!, null, null, "user1", "copilot");

        result.MachineId.Should().Be(machineId);
    }

    [Fact]
    public async Task InitializeSessionAsync_PropagatesRepoException_OnDuplicateCreate()
    {
        _sessionRepo.Setup(r => r.GetActiveByMachineAsync("m1"))
            .ReturnsAsync(new List<Session>());
        _sessionRepo.Setup(r => r.CreateAsync(It.IsAny<Session>()))
            .ThrowsAsync(new InvalidOperationException("Conflict: duplicate id"));

        var act = () => _sut.InitializeSessionAsync("m1", null, null, "user1", "copilot");

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Conflict*");
    }

    [Fact]
    public async Task InitializeSessionAsync_HandlesMultipleStaleSessionsOnSameMachine()
    {
        var stale1 = new Session { Id = "old-1", MachineId = "m1", Status = SessionStatus.Active };
        var stale2 = new Session { Id = "old-2", MachineId = "m1", Status = SessionStatus.Active };
        _sessionRepo.Setup(r => r.GetActiveByMachineAsync("m1"))
            .ReturnsAsync(new List<Session> { stale1, stale2 });
        _sessionRepo.Setup(r => r.UpdateAsync(It.IsAny<Session>()))
            .ReturnsAsync((Session s) => s);
        _sessionRepo.Setup(r => r.CreateAsync(It.IsAny<Session>()))
            .ReturnsAsync((Session s) => s);

        await _sut.InitializeSessionAsync("m1", null, null, "user1", "copilot");

        _sessionRepo.Verify(r => r.UpdateAsync(It.Is<Session>(s => s.Status == SessionStatus.Stale)), Times.Exactly(2));
    }

    [Fact]
    public async Task InitializeSessionAsync_NoStaleSessionsToClean()
    {
        _sessionRepo.Setup(r => r.GetActiveByMachineAsync("m1"))
            .ReturnsAsync(new List<Session>());
        _sessionRepo.Setup(r => r.CreateAsync(It.IsAny<Session>()))
            .ReturnsAsync((Session s) => s);

        var result = await _sut.InitializeSessionAsync("m1", "repo", "main", "user1", "copilot");

        _sessionRepo.Verify(r => r.UpdateAsync(It.IsAny<Session>()), Times.Never);
        result.Status.Should().Be(SessionStatus.Active);
    }

    [Fact]
    public async Task HeartbeatAsync_ThrowsWhenSessionIsStale()
    {
        var session = new Session { Id = "s1", MachineId = "m1", Status = SessionStatus.Stale };
        _sessionRepo.Setup(r => r.GetAsync("s1", "m1")).ReturnsAsync(session);

        var act = () => _sut.HeartbeatAsync("s1", "m1");

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*not active*");
    }

    [Fact]
    public async Task HeartbeatAsync_PropagatesRepoException()
    {
        _sessionRepo.Setup(r => r.GetAsync("s1", "m1"))
            .ThrowsAsync(new Exception("Cosmos DB unavailable"));

        var act = () => _sut.HeartbeatAsync("s1", "m1");

        await act.Should().ThrowAsync<Exception>()
            .WithMessage("*Cosmos DB unavailable*");
    }

    [Fact]
    public async Task CompleteSessionAsync_ThrowsWhenSessionNotFound()
    {
        _sessionRepo.Setup(r => r.GetAsync("missing", "m1")).ReturnsAsync((Session?)null);

        var act = () => _sut.CompleteSessionAsync("missing", "m1", null);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*not found*");
    }

    [Fact]
    public async Task CompleteSessionAsync_ThrowsWhenAlreadyCompleted()
    {
        var session = new Session { Id = "s1", MachineId = "m1", Status = SessionStatus.Completed };
        _sessionRepo.Setup(r => r.GetAsync("s1", "m1")).ReturnsAsync(session);

        var act = () => _sut.CompleteSessionAsync("s1", "m1", "summary");

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*not active*");
    }

    [Fact]
    public async Task CompleteSessionAsync_SetsSummaryToNull_WhenNotProvided()
    {
        var session = new Session { Id = "s1", MachineId = "m1", Status = SessionStatus.Active };
        _sessionRepo.Setup(r => r.GetAsync("s1", "m1")).ReturnsAsync(session);
        _sessionRepo.Setup(r => r.UpdateAsync(It.IsAny<Session>()))
            .ReturnsAsync((Session s) => s);

        var result = await _sut.CompleteSessionAsync("s1", "m1", null);

        result.Summary.Should().BeNull();
        result.Status.Should().Be(SessionStatus.Completed);
    }

    [Fact]
    public async Task GetAsync_ReturnsNull_WhenSessionDoesNotExist()
    {
        _sessionRepo.Setup(r => r.GetAsync("missing", "m1")).ReturnsAsync((Session?)null);

        var result = await _sut.GetAsync("missing", "m1");

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetAsync_ReturnsSession_WhenFound()
    {
        var session = new Session { Id = "s1", MachineId = "m1" };
        _sessionRepo.Setup(r => r.GetAsync("s1", "m1")).ReturnsAsync(session);

        var result = await _sut.GetAsync("s1", "m1");

        result.Should().BeSameAs(session);
    }

    [Fact]
    public async Task ListAsync_ReturnsEmptyPage_WhenNoSessions()
    {
        _sessionRepo.Setup(r => r.ListAsync("m1", null, null, null, null, 50))
            .ReturnsAsync(new PagedResult<Session> { Items = [], ContinuationToken = null });

        var result = await _sut.ListAsync("m1", null, null, null, null, 50);

        result.Items.Should().BeEmpty();
        result.HasMore.Should().BeFalse();
    }

    [Fact]
    public async Task ListAsync_ForwardsPaginationParameters()
    {
        var since = DateTime.UtcNow.AddDays(-1);
        _sessionRepo.Setup(r => r.ListAsync("m1", SessionStatus.Active, null, since, "token", 10))
            .ReturnsAsync(new PagedResult<Session>
            {
                Items = [new Session()],
                ContinuationToken = "next"
            });

        var result = await _sut.ListAsync("m1", SessionStatus.Active, null, since, "token", 10);

        result.Items.Should().HaveCount(1);
        result.HasMore.Should().BeTrue();
    }

    [Fact]
    public async Task CleanupStaleSessionsAsync_ReturnsZero_WhenNoStaleSessions()
    {
        _sessionRepo.Setup(r => r.GetStaleSessionsAsync(It.IsAny<DateTime>(), null, 50))
            .ReturnsAsync(new PagedResult<Session> { Items = [], ContinuationToken = null });

        var count = await _sut.CleanupStaleSessionsAsync(TimeSpan.FromMinutes(5));

        count.Should().Be(0);
        _sessionRepo.Verify(r => r.UpdateAsync(It.IsAny<Session>()), Times.Never);
    }

    [Fact]
    public async Task CleanupStaleSessionsAsync_SkipsSessionWithActivePrompt()
    {
        var staleSessions = new List<Session>
        {
            new() { Id = "s1", MachineId = "m1", Status = SessionStatus.Active }
        };

        _sessionRepo.Setup(r => r.GetStaleSessionsAsync(It.IsAny<DateTime>(), null, 50))
            .ReturnsAsync(new PagedResult<Session> { Items = staleSessions, ContinuationToken = null });
        _sessionRepo.Setup(r => r.UpdateAsync(It.IsAny<Session>()))
            .ReturnsAsync((Session s) => s);
        // Session has an active prompt, so it should NOT be marked stale
        _promptRepo.Setup(r => r.GetBySessionAsync("s1"))
            .ReturnsAsync(new List<Prompt> { new() { Status = "started", CreatedAt = DateTime.UtcNow } });

        var count = await _sut.CleanupStaleSessionsAsync(TimeSpan.FromMinutes(5));

        count.Should().Be(0);
        // Session should have been refreshed, not marked stale
        _sessionRepo.Verify(r => r.UpdateAsync(It.Is<Session>(s => s.Status == SessionStatus.Active)), Times.Once);
    }

    [Fact]
    public async Task CleanupStaleSessionsAsync_SkipsSessionWithRecentPromptActivity()
    {
        var staleSessions = new List<Session>
        {
            new() { Id = "s1", MachineId = "m1", Status = SessionStatus.Active }
        };

        _sessionRepo.Setup(r => r.GetStaleSessionsAsync(It.IsAny<DateTime>(), null, 50))
            .ReturnsAsync(new PagedResult<Session> { Items = staleSessions, ContinuationToken = null });
        _sessionRepo.Setup(r => r.UpdateAsync(It.IsAny<Session>()))
            .ReturnsAsync((Session s) => s);
        // Prompt completed recently (within threshold)
        _promptRepo.Setup(r => r.GetBySessionAsync("s1"))
            .ReturnsAsync(new List<Prompt> { new() { Status = "done", UpdatedAt = DateTime.UtcNow.AddMinutes(-2) } });

        var count = await _sut.CleanupStaleSessionsAsync(TimeSpan.FromMinutes(5));

        count.Should().Be(0);
    }

    [Fact]
    public async Task CleanupStaleSessionsAsync_PropagatesRepoException()
    {
        _sessionRepo.Setup(r => r.GetStaleSessionsAsync(It.IsAny<DateTime>(), null, 50))
            .ThrowsAsync(new Exception("DB error"));

        var act = () => _sut.CleanupStaleSessionsAsync(TimeSpan.FromMinutes(5));

        await act.Should().ThrowAsync<Exception>().WithMessage("*DB error*");
    }

    [Fact]
    public async Task ConcurrentHeartbeats_BothSucceed_OnActiveSession()
    {
        var session = new Session { Id = "s1", MachineId = "m1", Status = SessionStatus.Active };
        _sessionRepo.Setup(r => r.GetAsync("s1", "m1")).ReturnsAsync(session);
        _sessionRepo.Setup(r => r.UpdateAsync(It.IsAny<Session>()))
            .ReturnsAsync((Session s) => s);

        var tasks = Enumerable.Range(0, 5)
            .Select(_ => _sut.HeartbeatAsync("s1", "m1"))
            .ToArray();

        var results = await Task.WhenAll(tasks);

        results.Should().AllSatisfy(r => r.Status.Should().Be(SessionStatus.Active));
    }

    // --- Tool field tests ---

    [Fact]
    public async Task InitializeFromHookAsync_WithTool_SetsToolOnNewSession()
    {
        _sessionRepo.Setup(r => r.GetActiveByMachineAsync("m1"))
            .ReturnsAsync(new List<Session>());
        _sessionRepo.Setup(r => r.CreateAsync(It.IsAny<Session>()))
            .ReturnsAsync((Session s) => s);

        var result = await _sut.InitializeFromHookAsync(
            "s1", "m1", "repo", "main", "new", null, "user1", "copilot", "claude");

        result.Tool.Should().Be("claude");
        _sessionRepo.Verify(r => r.CreateAsync(It.Is<Session>(s => s.Tool == "claude")), Times.Once);
    }

    [Fact]
    public async Task InitializeFromHookAsync_WithTool_SetsToolOnResumedSession()
    {
        var existing = new Session
        {
            Id = "s1", MachineId = "m1", Status = SessionStatus.Completed, Tool = null
        };
        _sessionRepo.Setup(r => r.GetAsync("s1", "m1")).ReturnsAsync(existing);
        _sessionRepo.Setup(r => r.UpdateAsync(It.IsAny<Session>()))
            .ReturnsAsync((Session s) => s);

        var result = await _sut.InitializeFromHookAsync(
            "s1", "m1", "repo", "main", "resume", null, "user1", "copilot", "claude");

        result.Tool.Should().Be("claude");
        _sessionRepo.Verify(r => r.UpdateAsync(It.Is<Session>(s => s.Tool == "claude")), Times.Once);
    }

    [Fact]
    public async Task ListAsync_PassesToolToRepository()
    {
        var since = DateTime.UtcNow.AddDays(-1);
        _sessionRepo.Setup(r => r.ListAsync("m1", SessionStatus.Active, "claude", since, "tok", 25))
            .ReturnsAsync(new PagedResult<Session>
            {
                Items = [new Session { Id = "s1", Tool = "claude" }],
                ContinuationToken = null
            });

        var result = await _sut.ListAsync("m1", SessionStatus.Active, "claude", since, "tok", 25);

        result.Items.Should().HaveCount(1);
        _sessionRepo.Verify(r => r.ListAsync("m1", SessionStatus.Active, "claude", since, "tok", 25), Times.Once);
    }
}

namespace CopilotTracker.Core.Tests.Services;

using CopilotTracker.Core.Interfaces;
using CopilotTracker.Core.Models;
using CopilotTracker.Core.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

public class PromptServiceTests
{
    private readonly Mock<IPromptRepository> _promptRepo = new();
    private readonly PromptService _sut;

    public PromptServiceTests()
    {
        _sut = new PromptService(_promptRepo.Object, NullLogger<PromptService>.Instance);
    }

    // --- CreatePromptAsync tests ---

    [Fact]
    public async Task CreatePromptAsync_GeneratesGuid_AndSetsFieldsCorrectly()
    {
        _promptRepo.Setup(r => r.CreateAsync(It.IsAny<Prompt>()))
            .ReturnsAsync((Prompt p) => p);

        var before = DateTime.UtcNow;
        var result = await _sut.CreatePromptAsync(
            "session-1", "Fix the bug", "/home/user", 1700000000000, "user1", "Test User");

        result.Id.Should().NotBeNullOrEmpty();
        Guid.TryParse(result.Id, out _).Should().BeTrue("generated ID should be a valid GUID");
        result.SessionId.Should().Be("session-1");
        result.PromptText.Should().Be("Fix the bug");
        result.Cwd.Should().Be("/home/user");
        result.Status.Should().Be("started");
        result.HookTimestamp.Should().Be(1700000000000);
        result.UserId.Should().Be("user1");
        result.CreatedBy.Should().Be("Test User");
        result.CreatedAt.Should().BeOnOrAfter(before);
        result.UpdatedAt.Should().BeOnOrAfter(before);
    }

    [Fact]
    public async Task CreatePromptAsync_CallsRepositoryCreate()
    {
        _promptRepo.Setup(r => r.CreateAsync(It.IsAny<Prompt>()))
            .ReturnsAsync((Prompt p) => p);

        await _sut.CreatePromptAsync(
            "session-1", "Do something", null, 123, "user1", "copilot");

        _promptRepo.Verify(r => r.CreateAsync(It.IsAny<Prompt>()), Times.Once);
    }

    // --- CompleteActivePromptAsync tests ---

    [Fact]
    public async Task CompleteActivePromptAsync_FindsActivePrompt_MarksAsDone()
    {
        var active = new Prompt
        {
            Id = "p1", SessionId = "s1", Status = "started",
            PromptText = "Some prompt", CreatedAt = DateTime.UtcNow.AddMinutes(-5)
        };
        _promptRepo.Setup(r => r.GetActiveBySessionAsync("s1")).ReturnsAsync(active);
        _promptRepo.Setup(r => r.UpdateAsync(It.IsAny<Prompt>()))
            .ReturnsAsync((Prompt p) => p);

        var before = DateTime.UtcNow;
        var result = await _sut.CompleteActivePromptAsync("s1", 1700000000000);

        result.Should().NotBeNull();
        result!.Status.Should().Be("done");
        result.CompletedAt.Should().NotBeNull();
        result.CompletedAt.Should().BeOnOrAfter(before);
        result.UpdatedAt.Should().BeOnOrAfter(before);
    }

    [Fact]
    public async Task CompleteActivePromptAsync_ReturnsNull_WhenNoActivePrompt()
    {
        _promptRepo.Setup(r => r.GetActiveBySessionAsync("s1")).ReturnsAsync((Prompt?)null);

        var result = await _sut.CompleteActivePromptAsync("s1", 1700000000000);

        result.Should().BeNull();
        _promptRepo.Verify(r => r.UpdateAsync(It.IsAny<Prompt>()), Times.Never);
    }

    // --- GetOrCreateActivePromptAsync tests ---

    [Fact]
    public async Task GetOrCreateActivePromptAsync_ReturnsExisting_WhenActivePromptExists()
    {
        var existing = new Prompt
        {
            Id = "p1", SessionId = "s1", Status = "started", PromptText = "Real prompt"
        };
        _promptRepo.Setup(r => r.GetActiveBySessionAsync("s1")).ReturnsAsync(existing);

        var result = await _sut.GetOrCreateActivePromptAsync("s1", "user1", "copilot");

        result.Should().BeSameAs(existing);
        _promptRepo.Verify(r => r.CreateAsync(It.IsAny<Prompt>()), Times.Never);
    }

    [Fact]
    public async Task GetOrCreateActivePromptAsync_CreatesMissedStart_WhenNoActivePrompt()
    {
        _promptRepo.Setup(r => r.GetActiveBySessionAsync("s1")).ReturnsAsync((Prompt?)null);
        _promptRepo.Setup(r => r.CreateAsync(It.IsAny<Prompt>()))
            .ReturnsAsync((Prompt p) => p);

        var result = await _sut.GetOrCreateActivePromptAsync("s1", "user1", "copilot");

        result.Should().NotBeNull();
        result.PromptText.Should().Be("MISSED START");
        _promptRepo.Verify(r => r.CreateAsync(It.IsAny<Prompt>()), Times.Once);
    }

    [Fact]
    public async Task GetOrCreateActivePromptAsync_MissedStart_HasCorrectFields()
    {
        _promptRepo.Setup(r => r.GetActiveBySessionAsync("s1")).ReturnsAsync((Prompt?)null);
        _promptRepo.Setup(r => r.CreateAsync(It.IsAny<Prompt>()))
            .ReturnsAsync((Prompt p) => p);

        var before = DateTime.UtcNow;
        var result = await _sut.GetOrCreateActivePromptAsync("s1", "user1", "copilot");

        result.PromptText.Should().Be("MISSED START");
        result.Status.Should().Be("started");
        result.SessionId.Should().Be("s1");
        result.UserId.Should().Be("user1");
        result.CreatedBy.Should().Be("copilot");
        result.Id.Should().NotBeNullOrEmpty();
        Guid.TryParse(result.Id, out _).Should().BeTrue();
        result.CreatedAt.Should().BeOnOrAfter(before);
    }

    // --- GetAsync tests ---

    [Fact]
    public async Task GetAsync_DelegatesToRepository()
    {
        var prompt = new Prompt { Id = "p1", SessionId = "s1" };
        _promptRepo.Setup(r => r.GetAsync("s1", "p1")).ReturnsAsync(prompt);

        var result = await _sut.GetAsync("s1", "p1");

        result.Should().BeSameAs(prompt);
    }

    // --- ListAsync tests ---

    [Fact]
    public async Task ListAsync_DelegatesToRepository_WithFilters()
    {
        var since = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var paged = new PagedResult<Prompt>
        {
            Items = [new Prompt { Id = "p1" }],
            ContinuationToken = "next"
        };
        _promptRepo.Setup(r => r.ListAsync("s1", "started", since, "token", 25))
            .ReturnsAsync(paged);

        var result = await _sut.ListAsync("s1", "started", since, "token", 25);

        result.Items.Should().HaveCount(1);
        result.ContinuationToken.Should().Be("next");
    }

    // --- GetBySessionAsync tests ---

    [Fact]
    public async Task GetBySessionAsync_DelegatesToRepository()
    {
        var prompts = new List<Prompt>
        {
            new() { Id = "p1", SessionId = "s1" },
            new() { Id = "p2", SessionId = "s1" }
        };
        _promptRepo.Setup(r => r.GetBySessionAsync("s1")).ReturnsAsync(prompts);

        var result = await _sut.GetBySessionAsync("s1");

        result.Should().HaveCount(2);
    }
}

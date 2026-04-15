using System.Net;
using Microsoft.Azure.Cosmos;
using Moq;
using FluentAssertions;
using CopilotTracker.Core.Models;

namespace CopilotTracker.Cosmos.Tests;

#region Helpers

/// <summary>
/// Shared helpers for creating Cosmos SDK mocks across all repository edge-case tests.
/// </summary>
internal static class CosmosMockHelpers
{
    /// <summary>Creates a CosmosException with the given status code.</summary>
    public static CosmosException CreateCosmosException(HttpStatusCode statusCode, string message = "Cosmos error")
        => new(message, statusCode, (int)statusCode, Guid.NewGuid().ToString(), 1.0);

    /// <summary>Mocks an ItemResponse whose .Resource returns the given item.</summary>
    public static Mock<ItemResponse<T>> MockItemResponse<T>(T item)
    {
        var mock = new Mock<ItemResponse<T>>();
        mock.Setup(r => r.Resource).Returns(item);
        mock.Setup(r => r.StatusCode).Returns(HttpStatusCode.OK);
        return mock;
    }

    /// <summary>
    /// Creates a mock FeedIterator that yields one page of results,
    /// with an optional continuation token on that page.
    /// </summary>
    public static Mock<FeedIterator<T>> MockFeedIterator<T>(
        IReadOnlyList<T> items,
        string? continuationToken = null)
    {
        var feedResponse = new Mock<FeedResponse<T>>();
        feedResponse.Setup(r => r.GetEnumerator()).Returns(items.GetEnumerator());
        feedResponse.Setup(r => r.ContinuationToken).Returns(continuationToken!);
        feedResponse.Setup(r => r.Count).Returns(items.Count);

        var hasRead = false;
        var iterator = new Mock<FeedIterator<T>>();
        iterator.Setup(i => i.HasMoreResults).Returns(() => !hasRead);
        iterator.Setup(i => i.ReadNextAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                hasRead = true;
                return feedResponse.Object;
            });
        return iterator;
    }

    /// <summary>Creates an empty-results FeedIterator (HasMoreResults = false immediately).</summary>
    public static Mock<FeedIterator<T>> MockEmptyFeedIterator<T>()
    {
        var iterator = new Mock<FeedIterator<T>>();
        iterator.Setup(i => i.HasMoreResults).Returns(false);
        return iterator;
    }

    /// <summary>
    /// Creates a multi-page FeedIterator that returns pages sequentially,
    /// each with its own continuation token (last page has null token).
    /// </summary>
    public static Mock<FeedIterator<T>> MockMultiPageFeedIterator<T>(
        params (IReadOnlyList<T> items, string? token)[] pages)
    {
        var pageIndex = 0;
        var iterator = new Mock<FeedIterator<T>>();
        iterator.Setup(i => i.HasMoreResults).Returns(() => pageIndex < pages.Length);
        iterator.Setup(i => i.ReadNextAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                var page = pages[pageIndex++];
                var feedResponse = new Mock<FeedResponse<T>>();
                feedResponse.Setup(r => r.GetEnumerator()).Returns(page.items.GetEnumerator());
                feedResponse.Setup(r => r.ContinuationToken).Returns(page.token!);
                feedResponse.Setup(r => r.Count).Returns(page.items.Count);
                return feedResponse.Object;
            });
        return iterator;
    }
}

#endregion

#region Session Repository Edge Cases

public class CosmosSessionRepositoryEdgeCaseTests
{
    private readonly Mock<Database> _mockDatabase;
    private readonly Mock<Container> _mockContainer;
    private readonly CosmosSessionRepository _repo;

    public CosmosSessionRepositoryEdgeCaseTests()
    {
        _mockContainer = new Mock<Container>();
        _mockDatabase = new Mock<Database>();
        _mockDatabase.Setup(d => d.GetContainer("sessions")).Returns(_mockContainer.Object);
        _repo = new CosmosSessionRepository(_mockDatabase.Object);
    }

    private static Session MakeSession(string? id = null, string machineId = "machine-1")
        => new() { Id = id ?? Guid.NewGuid().ToString(), MachineId = machineId };

    // --- GetAsync: Not Found (404) ---

    [Fact]
    public async Task GetAsync_NotFound_ReturnsNull()
    {
        _mockContainer
            .Setup(c => c.ReadItemAsync<Session>(It.IsAny<string>(), It.IsAny<PartitionKey>(),
                It.IsAny<ItemRequestOptions>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(CosmosMockHelpers.CreateCosmosException(HttpStatusCode.NotFound));

        var result = await _repo.GetAsync("nonexistent", "machine-1");

        result.Should().BeNull();
    }

    // --- CreateAsync: Conflict (409) ---

    [Fact]
    public async Task CreateAsync_Conflict_ThrowsCosmosException()
    {
        _mockContainer
            .Setup(c => c.CreateItemAsync(It.IsAny<Session>(), It.IsAny<PartitionKey>(),
                It.IsAny<ItemRequestOptions>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(CosmosMockHelpers.CreateCosmosException(HttpStatusCode.Conflict));

        var session = MakeSession();
        var act = () => _repo.CreateAsync(session);

        (await act.Should().ThrowAsync<CosmosException>())
            .Where(ex => ex.StatusCode == HttpStatusCode.Conflict);
    }

    // --- CreateAsync: Happy path ---

    [Fact]
    public async Task CreateAsync_Success_ReturnsSession()
    {
        var session = MakeSession();
        var response = CosmosMockHelpers.MockItemResponse(session);
        _mockContainer
            .Setup(c => c.CreateItemAsync(session, It.IsAny<PartitionKey>(),
                It.IsAny<ItemRequestOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(response.Object);

        var result = await _repo.CreateAsync(session);

        result.Should().BeSameAs(session);
    }

    // --- CreateAsync: Throttled (429) ---

    [Fact]
    public async Task CreateAsync_Throttled_ThrowsCosmosException()
    {
        _mockContainer
            .Setup(c => c.CreateItemAsync(It.IsAny<Session>(), It.IsAny<PartitionKey>(),
                It.IsAny<ItemRequestOptions>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(CosmosMockHelpers.CreateCosmosException(HttpStatusCode.TooManyRequests));

        var act = () => _repo.CreateAsync(MakeSession());

        (await act.Should().ThrowAsync<CosmosException>())
            .Where(ex => ex.StatusCode == HttpStatusCode.TooManyRequests);
    }

    // --- UpdateAsync: Not Found on Replace ---

    [Fact]
    public async Task UpdateAsync_NotFound_ThrowsCosmosException()
    {
        _mockContainer
            .Setup(c => c.ReplaceItemAsync(It.IsAny<Session>(), It.IsAny<string>(),
                It.IsAny<PartitionKey>(), It.IsAny<ItemRequestOptions>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(CosmosMockHelpers.CreateCosmosException(HttpStatusCode.NotFound));

        var act = () => _repo.UpdateAsync(MakeSession());

        (await act.Should().ThrowAsync<CosmosException>())
            .Where(ex => ex.StatusCode == HttpStatusCode.NotFound);
    }

    // --- UpdateAsync: Success sets UpdatedAt ---

    [Fact]
    public async Task UpdateAsync_Success_SetsUpdatedAtAndReturns()
    {
        var session = MakeSession();
        session.UpdatedAt = DateTime.UtcNow.AddDays(-1); // stale timestamp
        var response = CosmosMockHelpers.MockItemResponse(session);

        _mockContainer
            .Setup(c => c.ReplaceItemAsync(It.IsAny<Session>(), It.IsAny<string>(),
                It.IsAny<PartitionKey>(), It.IsAny<ItemRequestOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(response.Object);

        var before = DateTime.UtcNow;
        var result = await _repo.UpdateAsync(session);

        result.UpdatedAt.Should().BeOnOrAfter(before);
    }

    // --- UpdateAsync: Service Unavailable (503) ---

    [Fact]
    public async Task UpdateAsync_ServiceUnavailable_ThrowsCosmosException()
    {
        _mockContainer
            .Setup(c => c.ReplaceItemAsync(It.IsAny<Session>(), It.IsAny<string>(),
                It.IsAny<PartitionKey>(), It.IsAny<ItemRequestOptions>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(CosmosMockHelpers.CreateCosmosException(HttpStatusCode.ServiceUnavailable));

        var act = () => _repo.UpdateAsync(MakeSession());

        (await act.Should().ThrowAsync<CosmosException>())
            .Where(ex => ex.StatusCode == HttpStatusCode.ServiceUnavailable);
    }

    // --- GetActiveByMachineAsync: Empty results ---

    [Fact]
    public async Task GetActiveByMachineAsync_NoResults_ReturnsEmptyList()
    {
        var iterator = CosmosMockHelpers.MockFeedIterator<Session>([], null);
        _mockContainer
            .Setup(c => c.GetItemQueryIterator<Session>(
                It.IsAny<QueryDefinition>(), It.IsAny<string>(), It.IsAny<QueryRequestOptions>()))
            .Returns(iterator.Object);

        var result = await _repo.GetActiveByMachineAsync("machine-1");

        result.Should().BeEmpty();
    }

    // --- GetActiveByMachineAsync: Multiple pages ---

    [Fact]
    public async Task GetActiveByMachineAsync_MultiplePages_ReturnsAll()
    {
        var s1 = MakeSession(id: "s1");
        var s2 = MakeSession(id: "s2");
        var s3 = MakeSession(id: "s3");

        var iterator = CosmosMockHelpers.MockMultiPageFeedIterator(
            (new List<Session> { s1 }, "token1"),
            (new List<Session> { s2, s3 }, (string?)null));

        _mockContainer
            .Setup(c => c.GetItemQueryIterator<Session>(
                It.IsAny<QueryDefinition>(), It.IsAny<string>(), It.IsAny<QueryRequestOptions>()))
            .Returns(iterator.Object);

        var result = await _repo.GetActiveByMachineAsync("machine-1");

        result.Should().HaveCount(3);
    }

    // --- ListAsync: Empty results ---

    [Fact]
    public async Task ListAsync_NoResults_ReturnsEmptyPagedResult()
    {
        var iterator = CosmosMockHelpers.MockEmptyFeedIterator<Session>();
        _mockContainer
            .Setup(c => c.GetItemQueryIterator<Session>(
                It.IsAny<QueryDefinition>(), It.IsAny<string>(), It.IsAny<QueryRequestOptions>()))
            .Returns(iterator.Object);

        var result = await _repo.ListAsync();

        result.Items.Should().BeEmpty();
        result.ContinuationToken.Should().BeNull();
        result.HasMore.Should().BeFalse();
    }

    // --- ListAsync: With continuation token (pagination) ---

    [Fact]
    public async Task ListAsync_WithContinuationToken_PassesTokenToIterator()
    {
        var session = MakeSession();
        var iterator = CosmosMockHelpers.MockFeedIterator<Session>([session], "next-token");
        _mockContainer
            .Setup(c => c.GetItemQueryIterator<Session>(
                It.IsAny<QueryDefinition>(), "prev-token", It.IsAny<QueryRequestOptions>()))
            .Returns(iterator.Object);

        var result = await _repo.ListAsync(continuationToken: "prev-token");

        result.Items.Should().HaveCount(1);
        result.ContinuationToken.Should().Be("next-token");
        result.HasMore.Should().BeTrue();
    }

    // --- ListAsync: Last page (null continuation token) ---

    [Fact]
    public async Task ListAsync_LastPage_HasNullContinuationToken()
    {
        var iterator = CosmosMockHelpers.MockFeedIterator<Session>([MakeSession()], null);
        _mockContainer
            .Setup(c => c.GetItemQueryIterator<Session>(
                It.IsAny<QueryDefinition>(), It.IsAny<string>(), It.IsAny<QueryRequestOptions>()))
            .Returns(iterator.Object);

        var result = await _repo.ListAsync();

        result.HasMore.Should().BeFalse();
        result.ContinuationToken.Should().BeNull();
    }

    // --- ListAsync: All filters applied ---

    [Fact]
    public async Task ListAsync_WithAllFilters_ReturnsResults()
    {
        var session = MakeSession(machineId: "m1");
        var iterator = CosmosMockHelpers.MockFeedIterator<Session>([session], null);
        _mockContainer
            .Setup(c => c.GetItemQueryIterator<Session>(
                It.IsAny<QueryDefinition>(), It.IsAny<string>(), It.IsAny<QueryRequestOptions>()))
            .Returns(iterator.Object);

        var result = await _repo.ListAsync(
            machineId: "m1",
            status: SessionStatus.Active,
            since: DateTime.UtcNow.AddDays(-7));

        result.Items.Should().ContainSingle();
    }

    // --- GetStaleSessionsAsync: Empty results ---

    [Fact]
    public async Task GetStaleSessionsAsync_NoResults_ReturnsEmptyPage()
    {
        var iterator = CosmosMockHelpers.MockEmptyFeedIterator<Session>();
        _mockContainer
            .Setup(c => c.GetItemQueryIterator<Session>(
                It.IsAny<QueryDefinition>(), It.IsAny<string>(), It.IsAny<QueryRequestOptions>()))
            .Returns(iterator.Object);

        var result = await _repo.GetStaleSessionsAsync(DateTime.UtcNow);

        result.Items.Should().BeEmpty();
    }

    // --- GetStaleSessionsAsync: With continuation ---

    [Fact]
    public async Task GetStaleSessionsAsync_WithContinuation_ReturnsContinuationToken()
    {
        var session = MakeSession();
        var iterator = CosmosMockHelpers.MockFeedIterator<Session>([session], "stale-next");
        _mockContainer
            .Setup(c => c.GetItemQueryIterator<Session>(
                It.IsAny<QueryDefinition>(), It.IsAny<string>(), It.IsAny<QueryRequestOptions>()))
            .Returns(iterator.Object);

        var result = await _repo.GetStaleSessionsAsync(DateTime.UtcNow, pageSize: 10);

        result.ContinuationToken.Should().Be("stale-next");
        result.HasMore.Should().BeTrue();
    }

    // --- Large payloads ---

    [Fact]
    public async Task CreateAsync_LargePayload_Succeeds()
    {
        var session = MakeSession();
        session.Summary = new string('x', 100_000);
        var response = CosmosMockHelpers.MockItemResponse(session);
        _mockContainer
            .Setup(c => c.CreateItemAsync(session, It.IsAny<PartitionKey>(),
                It.IsAny<ItemRequestOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(response.Object);

        var result = await _repo.CreateAsync(session);

        result.Summary.Should().HaveLength(100_000);
    }
}

#endregion

#region Task Repository Edge Cases

public class CosmosTaskRepositoryEdgeCaseTests
{
    private readonly Mock<Database> _mockDatabase;
    private readonly Mock<Container> _mockContainer;
    private readonly CosmosTaskRepository _repo;

    public CosmosTaskRepositoryEdgeCaseTests()
    {
        _mockContainer = new Mock<Container>();
        _mockDatabase = new Mock<Database>();
        _mockDatabase.Setup(d => d.GetContainer("tasks")).Returns(_mockContainer.Object);
        _repo = new CosmosTaskRepository(_mockDatabase.Object);
    }

    private static TrackerTask MakeTask(string? id = null, string queueName = "default")
        => new() { Id = id ?? Guid.NewGuid().ToString(), QueueName = queueName, SessionId = "session-1" };

    // --- GetAsync: Not Found ---

    [Fact]
    public async Task GetAsync_NotFound_ReturnsNull()
    {
        _mockContainer
            .Setup(c => c.ReadItemAsync<TrackerTask>(It.IsAny<string>(), It.IsAny<PartitionKey>(),
                It.IsAny<ItemRequestOptions>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(CosmosMockHelpers.CreateCosmosException(HttpStatusCode.NotFound));

        var result = await _repo.GetAsync("nonexistent", "default");

        result.Should().BeNull();
    }

    // --- CreateAsync: Conflict ---

    [Fact]
    public async Task CreateAsync_Conflict_ThrowsCosmosException()
    {
        _mockContainer
            .Setup(c => c.CreateItemAsync(It.IsAny<TrackerTask>(), It.IsAny<PartitionKey>(),
                It.IsAny<ItemRequestOptions>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(CosmosMockHelpers.CreateCosmosException(HttpStatusCode.Conflict));

        var act = () => _repo.CreateAsync(MakeTask());

        (await act.Should().ThrowAsync<CosmosException>())
            .Where(ex => ex.StatusCode == HttpStatusCode.Conflict);
    }

    // --- CreateAsync: Success ---

    [Fact]
    public async Task CreateAsync_Success_ReturnsTask()
    {
        var task = MakeTask();
        var response = CosmosMockHelpers.MockItemResponse(task);
        _mockContainer
            .Setup(c => c.CreateItemAsync(task, It.IsAny<PartitionKey>(),
                It.IsAny<ItemRequestOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(response.Object);

        var result = await _repo.CreateAsync(task);

        result.Should().BeSameAs(task);
    }

    // --- CreateAsync: Throttled (429) ---

    [Fact]
    public async Task CreateAsync_Throttled_ThrowsCosmosException()
    {
        _mockContainer
            .Setup(c => c.CreateItemAsync(It.IsAny<TrackerTask>(), It.IsAny<PartitionKey>(),
                It.IsAny<ItemRequestOptions>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(CosmosMockHelpers.CreateCosmosException(HttpStatusCode.TooManyRequests));

        var act = () => _repo.CreateAsync(MakeTask());

        (await act.Should().ThrowAsync<CosmosException>())
            .Where(ex => ex.StatusCode == HttpStatusCode.TooManyRequests);
    }

    // --- UpdateAsync: Not Found ---

    [Fact]
    public async Task UpdateAsync_NotFound_ThrowsCosmosException()
    {
        _mockContainer
            .Setup(c => c.ReplaceItemAsync(It.IsAny<TrackerTask>(), It.IsAny<string>(),
                It.IsAny<PartitionKey>(), It.IsAny<ItemRequestOptions>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(CosmosMockHelpers.CreateCosmosException(HttpStatusCode.NotFound));

        var act = () => _repo.UpdateAsync(MakeTask());

        (await act.Should().ThrowAsync<CosmosException>())
            .Where(ex => ex.StatusCode == HttpStatusCode.NotFound);
    }

    // --- UpdateAsync: Success sets UpdatedAt ---

    [Fact]
    public async Task UpdateAsync_Success_SetsUpdatedAtAndReturns()
    {
        var task = MakeTask();
        task.UpdatedAt = DateTime.UtcNow.AddDays(-1);
        var response = CosmosMockHelpers.MockItemResponse(task);

        _mockContainer
            .Setup(c => c.ReplaceItemAsync(It.IsAny<TrackerTask>(), It.IsAny<string>(),
                It.IsAny<PartitionKey>(), It.IsAny<ItemRequestOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(response.Object);

        var before = DateTime.UtcNow;
        var result = await _repo.UpdateAsync(task);

        result.UpdatedAt.Should().BeOnOrAfter(before);
    }

    // --- UpdateAsync: Service Unavailable ---

    [Fact]
    public async Task UpdateAsync_ServiceUnavailable_ThrowsCosmosException()
    {
        _mockContainer
            .Setup(c => c.ReplaceItemAsync(It.IsAny<TrackerTask>(), It.IsAny<string>(),
                It.IsAny<PartitionKey>(), It.IsAny<ItemRequestOptions>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(CosmosMockHelpers.CreateCosmosException(HttpStatusCode.ServiceUnavailable));

        var act = () => _repo.UpdateAsync(MakeTask());

        (await act.Should().ThrowAsync<CosmosException>())
            .Where(ex => ex.StatusCode == HttpStatusCode.ServiceUnavailable);
    }

    // --- GetBySessionAsync: Empty results ---

    [Fact]
    public async Task GetBySessionAsync_NoResults_ReturnsEmptyPage()
    {
        var iterator = CosmosMockHelpers.MockEmptyFeedIterator<TrackerTask>();
        _mockContainer
            .Setup(c => c.GetItemQueryIterator<TrackerTask>(
                It.IsAny<QueryDefinition>(), It.IsAny<string>(), It.IsAny<QueryRequestOptions>()))
            .Returns(iterator.Object);

        var result = await _repo.GetBySessionAsync("session-1");

        result.Items.Should().BeEmpty();
    }

    // --- GetBySessionAsync: With continuation ---

    [Fact]
    public async Task GetBySessionAsync_WithContinuation_ReturnsContinuationToken()
    {
        var task = MakeTask();
        var iterator = CosmosMockHelpers.MockFeedIterator<TrackerTask>([task], "next-token");
        _mockContainer
            .Setup(c => c.GetItemQueryIterator<TrackerTask>(
                It.IsAny<QueryDefinition>(), It.IsAny<string>(), It.IsAny<QueryRequestOptions>()))
            .Returns(iterator.Object);

        var result = await _repo.GetBySessionAsync("session-1");

        result.ContinuationToken.Should().Be("next-token");
        result.HasMore.Should().BeTrue();
    }

    // --- GetByQueueAsync: Empty results ---

    [Fact]
    public async Task GetByQueueAsync_NoResults_ReturnsEmptyPage()
    {
        var iterator = CosmosMockHelpers.MockEmptyFeedIterator<TrackerTask>();
        _mockContainer
            .Setup(c => c.GetItemQueryIterator<TrackerTask>(
                It.IsAny<QueryDefinition>(), It.IsAny<string>(), It.IsAny<QueryRequestOptions>()))
            .Returns(iterator.Object);

        var result = await _repo.GetByQueueAsync("default");

        result.Items.Should().BeEmpty();
    }

    // --- GetByQueueAsync: With status filter ---

    [Fact]
    public async Task GetByQueueAsync_WithStatusFilter_ReturnsFilteredResults()
    {
        var task = MakeTask(queueName: "build");
        var iterator = CosmosMockHelpers.MockFeedIterator<TrackerTask>([task], null);
        _mockContainer
            .Setup(c => c.GetItemQueryIterator<TrackerTask>(
                It.IsAny<QueryDefinition>(), It.IsAny<string>(), It.IsAny<QueryRequestOptions>()))
            .Returns(iterator.Object);

        var result = await _repo.GetByQueueAsync("build", status: Core.Models.TaskStatus.Started);

        result.Items.Should().ContainSingle();
    }

    // --- ListAsync: No filters ---

    [Fact]
    public async Task ListAsync_NoFilters_ReturnsResults()
    {
        var task = MakeTask();
        var iterator = CosmosMockHelpers.MockFeedIterator<TrackerTask>([task], null);
        _mockContainer
            .Setup(c => c.GetItemQueryIterator<TrackerTask>(
                It.IsAny<QueryDefinition>(), It.IsAny<string>(), It.IsAny<QueryRequestOptions>()))
            .Returns(iterator.Object);

        var result = await _repo.ListAsync();

        result.Items.Should().ContainSingle();
    }

    // --- ListAsync: With queue and status filters ---

    [Fact]
    public async Task ListAsync_WithFilters_ReturnsResults()
    {
        var task = MakeTask(queueName: "deploy");
        var iterator = CosmosMockHelpers.MockFeedIterator<TrackerTask>([task], null);
        _mockContainer
            .Setup(c => c.GetItemQueryIterator<TrackerTask>(
                It.IsAny<QueryDefinition>(), It.IsAny<string>(), It.IsAny<QueryRequestOptions>()))
            .Returns(iterator.Object);

        var result = await _repo.ListAsync(queueName: "deploy", status: Core.Models.TaskStatus.Done);

        result.Items.Should().ContainSingle();
    }

    // --- ListAsync: Empty ---

    [Fact]
    public async Task ListAsync_NoResults_ReturnsEmptyPage()
    {
        var iterator = CosmosMockHelpers.MockEmptyFeedIterator<TrackerTask>();
        _mockContainer
            .Setup(c => c.GetItemQueryIterator<TrackerTask>(
                It.IsAny<QueryDefinition>(), It.IsAny<string>(), It.IsAny<QueryRequestOptions>()))
            .Returns(iterator.Object);

        var result = await _repo.ListAsync();

        result.Items.Should().BeEmpty();
        result.HasMore.Should().BeFalse();
    }

    // --- Large payload ---

    [Fact]
    public async Task CreateAsync_LargeTitle_Succeeds()
    {
        var task = MakeTask();
        task.Title = new string('y', 50_000);
        var response = CosmosMockHelpers.MockItemResponse(task);
        _mockContainer
            .Setup(c => c.CreateItemAsync(task, It.IsAny<PartitionKey>(),
                It.IsAny<ItemRequestOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(response.Object);

        var result = await _repo.CreateAsync(task);

        result.Title.Should().HaveLength(50_000);
    }
}

#endregion

#region TaskLog Repository Edge Cases

public class CosmosTaskLogRepositoryEdgeCaseTests
{
    private readonly Mock<Database> _mockDatabase;
    private readonly Mock<Container> _mockContainer;
    private readonly CosmosTaskLogRepository _repo;

    public CosmosTaskLogRepositoryEdgeCaseTests()
    {
        _mockContainer = new Mock<Container>();
        _mockDatabase = new Mock<Database>();
        _mockDatabase.Setup(d => d.GetContainer("taskLogs")).Returns(_mockContainer.Object);
        _repo = new CosmosTaskLogRepository(_mockDatabase.Object);
    }

    private static TaskLog MakeLog(string? id = null, string taskId = "task-1")
        => new() { Id = id ?? Guid.NewGuid().ToString(), TaskId = taskId, Message = "log entry" };

    // --- CreateAsync: Conflict ---

    [Fact]
    public async Task CreateAsync_Conflict_ThrowsCosmosException()
    {
        _mockContainer
            .Setup(c => c.CreateItemAsync(It.IsAny<TaskLog>(), It.IsAny<PartitionKey>(),
                It.IsAny<ItemRequestOptions>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(CosmosMockHelpers.CreateCosmosException(HttpStatusCode.Conflict));

        var act = () => _repo.CreateAsync(MakeLog());

        (await act.Should().ThrowAsync<CosmosException>())
            .Where(ex => ex.StatusCode == HttpStatusCode.Conflict);
    }

    // --- CreateAsync: Success ---

    [Fact]
    public async Task CreateAsync_Success_ReturnsLog()
    {
        var log = MakeLog();
        var response = CosmosMockHelpers.MockItemResponse(log);
        _mockContainer
            .Setup(c => c.CreateItemAsync(log, It.IsAny<PartitionKey>(),
                It.IsAny<ItemRequestOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(response.Object);

        var result = await _repo.CreateAsync(log);

        result.Should().BeSameAs(log);
    }

    // --- CreateAsync: Throttled ---

    [Fact]
    public async Task CreateAsync_Throttled_ThrowsCosmosException()
    {
        _mockContainer
            .Setup(c => c.CreateItemAsync(It.IsAny<TaskLog>(), It.IsAny<PartitionKey>(),
                It.IsAny<ItemRequestOptions>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(CosmosMockHelpers.CreateCosmosException(HttpStatusCode.TooManyRequests));

        var act = () => _repo.CreateAsync(MakeLog());

        (await act.Should().ThrowAsync<CosmosException>())
            .Where(ex => ex.StatusCode == HttpStatusCode.TooManyRequests);
    }

    // --- CreateAsync: Service Unavailable ---

    [Fact]
    public async Task CreateAsync_ServiceUnavailable_ThrowsCosmosException()
    {
        _mockContainer
            .Setup(c => c.CreateItemAsync(It.IsAny<TaskLog>(), It.IsAny<PartitionKey>(),
                It.IsAny<ItemRequestOptions>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(CosmosMockHelpers.CreateCosmosException(HttpStatusCode.ServiceUnavailable));

        var act = () => _repo.CreateAsync(MakeLog());

        (await act.Should().ThrowAsync<CosmosException>())
            .Where(ex => ex.StatusCode == HttpStatusCode.ServiceUnavailable);
    }

    // --- GetByTaskAsync: Empty results ---

    [Fact]
    public async Task GetByTaskAsync_NoResults_ReturnsEmptyList()
    {
        var iterator = CosmosMockHelpers.MockFeedIterator<TaskLog>([], null);
        _mockContainer
            .Setup(c => c.GetItemQueryIterator<TaskLog>(
                It.IsAny<QueryDefinition>(), It.IsAny<string>(), It.IsAny<QueryRequestOptions>()))
            .Returns(iterator.Object);

        var result = await _repo.GetByTaskAsync("task-1");

        result.Should().BeEmpty();
    }

    // --- GetByTaskAsync: Multiple pages ---

    [Fact]
    public async Task GetByTaskAsync_MultiplePages_ReturnsAll()
    {
        var log1 = MakeLog(id: "l1");
        var log2 = MakeLog(id: "l2");
        var log3 = MakeLog(id: "l3");

        var iterator = CosmosMockHelpers.MockMultiPageFeedIterator(
            (new List<TaskLog> { log1, log2 }, "token1"),
            (new List<TaskLog> { log3 }, (string?)null));

        _mockContainer
            .Setup(c => c.GetItemQueryIterator<TaskLog>(
                It.IsAny<QueryDefinition>(), It.IsAny<string>(), It.IsAny<QueryRequestOptions>()))
            .Returns(iterator.Object);

        var result = await _repo.GetByTaskAsync("task-1");

        result.Should().HaveCount(3);
    }

    // --- GetByTaskPagedAsync: Empty results ---

    [Fact]
    public async Task GetByTaskPagedAsync_NoResults_ReturnsEmptyPage()
    {
        var iterator = CosmosMockHelpers.MockEmptyFeedIterator<TaskLog>();
        _mockContainer
            .Setup(c => c.GetItemQueryIterator<TaskLog>(
                It.IsAny<QueryDefinition>(), It.IsAny<string>(), It.IsAny<QueryRequestOptions>()))
            .Returns(iterator.Object);

        var result = await _repo.GetByTaskPagedAsync("task-1");

        result.Items.Should().BeEmpty();
        result.HasMore.Should().BeFalse();
    }

    // --- GetByTaskPagedAsync: With continuation ---

    [Fact]
    public async Task GetByTaskPagedAsync_WithContinuation_ReturnsContinuationToken()
    {
        var log = MakeLog();
        var iterator = CosmosMockHelpers.MockFeedIterator<TaskLog>([log], "next-log-token");
        _mockContainer
            .Setup(c => c.GetItemQueryIterator<TaskLog>(
                It.IsAny<QueryDefinition>(), It.IsAny<string>(), It.IsAny<QueryRequestOptions>()))
            .Returns(iterator.Object);

        var result = await _repo.GetByTaskPagedAsync("task-1");

        result.ContinuationToken.Should().Be("next-log-token");
        result.HasMore.Should().BeTrue();
    }

    // --- Large message payload ---

    [Fact]
    public async Task CreateAsync_LargeMessage_Succeeds()
    {
        var log = MakeLog();
        log.Message = new string('z', 100_000);
        var response = CosmosMockHelpers.MockItemResponse(log);
        _mockContainer
            .Setup(c => c.CreateItemAsync(log, It.IsAny<PartitionKey>(),
                It.IsAny<ItemRequestOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(response.Object);

        var result = await _repo.CreateAsync(log);

        result.Message.Should().HaveLength(100_000);
    }
}

#endregion



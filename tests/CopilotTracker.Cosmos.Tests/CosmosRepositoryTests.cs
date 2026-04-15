using Microsoft.Azure.Cosmos;
using Moq;
using FluentAssertions;
using CopilotTracker.Core.Models;

namespace CopilotTracker.Cosmos.Tests;

public class CosmosSessionRepositoryTests
{
    private readonly Mock<Database> _mockDatabase;
    private readonly Mock<Container> _mockContainer;

    public CosmosSessionRepositoryTests()
    {
        _mockContainer = new Mock<Container>();
        _mockDatabase = new Mock<Database>();
        _mockDatabase
            .Setup(d => d.GetContainer("sessions"))
            .Returns(_mockContainer.Object);
    }

    [Fact]
    public void Constructor_WithDatabase_DoesNotThrow()
    {
        var act = () => new CosmosSessionRepository(_mockDatabase.Object);
        act.Should().NotThrow();
    }

    [Fact]
    public void Constructor_GetsCorrectContainer()
    {
        _ = new CosmosSessionRepository(_mockDatabase.Object);
        _mockDatabase.Verify(d => d.GetContainer("sessions"), Times.Once);
    }
}

public class CosmosTaskRepositoryTests
{
    private readonly Mock<Database> _mockDatabase;
    private readonly Mock<Container> _mockContainer;

    public CosmosTaskRepositoryTests()
    {
        _mockContainer = new Mock<Container>();
        _mockDatabase = new Mock<Database>();
        _mockDatabase
            .Setup(d => d.GetContainer("tasks"))
            .Returns(_mockContainer.Object);
    }

    [Fact]
    public void Constructor_WithDatabase_DoesNotThrow()
    {
        var act = () => new CosmosTaskRepository(_mockDatabase.Object);
        act.Should().NotThrow();
    }

    [Fact]
    public void Constructor_GetsCorrectContainer()
    {
        _ = new CosmosTaskRepository(_mockDatabase.Object);
        _mockDatabase.Verify(d => d.GetContainer("tasks"), Times.Once);
    }
}

public class CosmosTaskLogRepositoryTests
{
    private readonly Mock<Database> _mockDatabase;
    private readonly Mock<Container> _mockContainer;

    public CosmosTaskLogRepositoryTests()
    {
        _mockContainer = new Mock<Container>();
        _mockDatabase = new Mock<Database>();
        _mockDatabase
            .Setup(d => d.GetContainer("taskLogs"))
            .Returns(_mockContainer.Object);
    }

    [Fact]
    public void Constructor_WithDatabase_DoesNotThrow()
    {
        var act = () => new CosmosTaskLogRepository(_mockDatabase.Object);
        act.Should().NotThrow();
    }

    [Fact]
    public void Constructor_GetsCorrectContainer()
    {
        _ = new CosmosTaskLogRepository(_mockDatabase.Object);
        _mockDatabase.Verify(d => d.GetContainer("taskLogs"), Times.Once);
    }
}

public class CosmosServiceExtensionsTests
{
    [Fact]
    public void AddCosmosRepositories_IsCallable()
    {
        // Verifies the extension method exists and is accessible at compile time
        var method = typeof(CosmosServiceExtensions).GetMethod("AddCosmosRepositories");
        method.Should().NotBeNull();
        method!.IsStatic.Should().BeTrue();
        method.IsPublic.Should().BeTrue();
    }
}

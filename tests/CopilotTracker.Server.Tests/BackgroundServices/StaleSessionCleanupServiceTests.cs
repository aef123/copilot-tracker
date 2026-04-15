using CopilotTracker.Core.Interfaces;
using CopilotTracker.Core.Models;
using CopilotTracker.Core.Services;
using CopilotTracker.Server.BackgroundServices;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace CopilotTracker.Server.Tests.BackgroundServices;

public class StaleSessionCleanupServiceTests
{
    private static (StaleSessionCleanupService Service, Mock<ISessionRepository> SessionRepo) CreateService(
        int intervalSeconds = 1,
        int thresholdMinutes = 10,
        int initialDelaySeconds = 0)
    {
        var sessionRepo = new Mock<ISessionRepository>();
        sessionRepo
            .Setup(r => r.GetStaleSessionsAsync(It.IsAny<DateTime>(), It.IsAny<string?>()))
            .ReturnsAsync(new PagedResult<Session> { Items = [] });

        var services = new ServiceCollection();
        services.AddSingleton(sessionRepo.Object);
        services.AddSingleton(Mock.Of<ITaskLogRepository>());
        services.AddSingleton<ILogger<SessionService>>(NullLogger<SessionService>.Instance);
        services.AddScoped<SessionService>();

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["StaleCleanup:IntervalMinutes"] = (intervalSeconds / 60.0).ToString(),
                ["StaleCleanup:ThresholdMinutes"] = thresholdMinutes.ToString(),
                ["StaleCleanup:InitialDelaySeconds"] = initialDelaySeconds.ToString()
            })
            .Build();

        var service = new StaleSessionCleanupService(
            services.BuildServiceProvider(),
            NullLogger<StaleSessionCleanupService>.Instance,
            config);

        return (service, sessionRepo);
    }

    [Fact]
    public async Task ExecuteAsync_CallsCleanupStaleSessionsAsync()
    {
        var (service, sessionRepo) = CreateService(intervalSeconds: 1, initialDelaySeconds: 0);

        using var cts = new CancellationTokenSource();
        await service.StartAsync(cts.Token);

        // Wait enough time for at least one tick
        await Task.Delay(TimeSpan.FromSeconds(3));

        await cts.CancelAsync();
        await service.StopAsync(CancellationToken.None);

        sessionRepo.Verify(
            r => r.GetStaleSessionsAsync(It.IsAny<DateTime>(), It.IsAny<string?>()),
            Times.AtLeastOnce);
    }

    [Fact]
    public async Task ExecuteAsync_ContinuesAfterException()
    {
        var (service, sessionRepo) = CreateService(intervalSeconds: 1, initialDelaySeconds: 0);

        int callCount = 0;
        sessionRepo
            .Setup(r => r.GetStaleSessionsAsync(It.IsAny<DateTime>(), It.IsAny<string?>()))
            .Returns(() =>
            {
                callCount++;
                if (callCount == 1)
                    throw new InvalidOperationException("Simulated failure");
                return Task.FromResult(new PagedResult<Session> { Items = [] });
            });

        using var cts = new CancellationTokenSource();
        await service.StartAsync(cts.Token);

        // Wait for multiple ticks so it can recover from the first failure
        await Task.Delay(TimeSpan.FromSeconds(4));

        await cts.CancelAsync();
        await service.StopAsync(CancellationToken.None);

        callCount.Should().BeGreaterThanOrEqualTo(2,
            "service should keep running after an exception");
    }

    [Fact]
    public async Task ExecuteAsync_RespectsCanellation()
    {
        var (service, _) = CreateService(intervalSeconds: 1, initialDelaySeconds: 0);

        using var cts = new CancellationTokenSource();
        await service.StartAsync(cts.Token);

        await cts.CancelAsync();
        await service.StopAsync(CancellationToken.None);

        // If we get here without hanging, cancellation works.
        // ExecuteTask completes (doesn't throw unhandled).
        var executeTask = service.ExecuteTask;
        if (executeTask is not null)
        {
            var completed = await Task.WhenAny(executeTask, Task.Delay(TimeSpan.FromSeconds(5)));
            completed.Should().Be(executeTask, "ExecuteAsync should complete promptly after cancellation");
        }
    }

    [Fact]
    public async Task UsesConfiguredThreshold()
    {
        var (service, sessionRepo) = CreateService(
            intervalSeconds: 1, thresholdMinutes: 42, initialDelaySeconds: 0);

        using var cts = new CancellationTokenSource();
        await service.StartAsync(cts.Token);

        await Task.Delay(TimeSpan.FromSeconds(3));

        await cts.CancelAsync();
        await service.StopAsync(CancellationToken.None);

        // The cutoff passed to GetStaleSessionsAsync should be ~42 minutes ago
        sessionRepo.Verify(
            r => r.GetStaleSessionsAsync(
                It.Is<DateTime>(d => d < DateTime.UtcNow.AddMinutes(-40) && d > DateTime.UtcNow.AddMinutes(-44)),
                It.IsAny<string?>()),
            Times.AtLeastOnce);
    }

    [Fact]
    public async Task ExecuteAsync_NoStaleSessions_DoesNotThrow()
    {
        var (service, sessionRepo) = CreateService(intervalSeconds: 1, initialDelaySeconds: 0);

        // Default mock already returns empty Items
        using var cts = new CancellationTokenSource();
        await service.StartAsync(cts.Token);

        await Task.Delay(TimeSpan.FromSeconds(2));

        await cts.CancelAsync();
        await service.StopAsync(CancellationToken.None);

        // Verify it ran at least once with no errors
        sessionRepo.Verify(
            r => r.GetStaleSessionsAsync(It.IsAny<DateTime>(), It.IsAny<string?>()),
            Times.AtLeastOnce);
    }

    [Fact]
    public async Task ExecuteAsync_ProcessesStaleSessions()
    {
        var sessionRepo = new Mock<ISessionRepository>();
        var staleSession = new Session
        {
            Id = "stale-1",
            MachineId = "m1",
            Status = SessionStatus.Active,
            LastHeartbeat = DateTime.UtcNow.AddHours(-1)
        };

        sessionRepo
            .Setup(r => r.GetStaleSessionsAsync(It.IsAny<DateTime>(), null))
            .ReturnsAsync(new PagedResult<Session> { Items = [staleSession], ContinuationToken = null });
        sessionRepo
            .Setup(r => r.GetStaleSessionsAsync(It.IsAny<DateTime>(), It.Is<string?>(t => t != null)))
            .ReturnsAsync(new PagedResult<Session> { Items = [] });
        sessionRepo
            .Setup(r => r.UpdateAsync(It.IsAny<Session>()))
            .ReturnsAsync((Session s) => s);

        var services = new ServiceCollection();
        services.AddSingleton(sessionRepo.Object);
        services.AddSingleton(Mock.Of<ITaskLogRepository>());
        services.AddSingleton<ILogger<SessionService>>(NullLogger<SessionService>.Instance);
        services.AddScoped<SessionService>();

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["StaleCleanup:IntervalMinutes"] = (1.0 / 60).ToString(),
                ["StaleCleanup:ThresholdMinutes"] = "10",
                ["StaleCleanup:InitialDelaySeconds"] = "0"
            })
            .Build();

        var service = new StaleSessionCleanupService(
            services.BuildServiceProvider(),
            NullLogger<StaleSessionCleanupService>.Instance,
            config);

        using var cts = new CancellationTokenSource();
        await service.StartAsync(cts.Token);

        await Task.Delay(TimeSpan.FromSeconds(3));

        await cts.CancelAsync();
        await service.StopAsync(CancellationToken.None);

        sessionRepo.Verify(r => r.UpdateAsync(It.Is<Session>(s => s.Status == SessionStatus.Stale)), Times.AtLeastOnce);
    }

    [Fact]
    public async Task ExecuteAsync_ImmediateCancellation_CompletesPromptly()
    {
        var (service, _) = CreateService(intervalSeconds: 60, initialDelaySeconds: 0);

        using var cts = new CancellationTokenSource();
        await service.StartAsync(cts.Token);

        // Cancel immediately
        await cts.CancelAsync();
        await service.StopAsync(CancellationToken.None);

        var executeTask = service.ExecuteTask;
        if (executeTask is not null)
        {
            var completed = await Task.WhenAny(executeTask, Task.Delay(TimeSpan.FromSeconds(3)));
            completed.Should().Be(executeTask, "Service should stop promptly on cancellation");
        }
    }
}

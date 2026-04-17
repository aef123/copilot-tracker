namespace CopilotTracker.Server.BackgroundServices;

using CopilotTracker.Core.Services;

public class StaleSessionCleanupService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<StaleSessionCleanupService> _logger;
    private readonly TimeSpan _interval;
    private readonly TimeSpan _idleThreshold;
    private readonly TimeSpan _initialDelay;

    public StaleSessionCleanupService(
        IServiceProvider serviceProvider,
        ILogger<StaleSessionCleanupService> logger,
        IConfiguration configuration)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;

        _interval = TimeSpan.FromMinutes(
            configuration.GetValue<double>("StaleCleanup:IntervalMinutes", 5));
        _idleThreshold = TimeSpan.FromMinutes(
            configuration.GetValue<double>("StaleCleanup:ThresholdMinutes", 10));
        _initialDelay = TimeSpan.FromSeconds(
            configuration.GetValue<double>("StaleCleanup:InitialDelaySeconds", 30));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "Stale session cleanup started. Interval: {Interval}, Threshold: {Threshold}",
            _interval, _idleThreshold);

        await Task.Delay(_initialDelay, stoppingToken);

        using var timer = new PeriodicTimer(_interval);

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var sessionService = scope.ServiceProvider.GetRequiredService<SessionService>();

                int cleaned = await sessionService.CleanupStaleSessionsAsync(_idleThreshold);

                if (cleaned > 0)
                    _logger.LogInformation("Cleaned up {Count} stale sessions", cleaned);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during stale session cleanup");
            }
        }

        _logger.LogInformation("Stale session cleanup stopped");
    }
}

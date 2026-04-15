namespace CopilotTracker.Server.BackgroundServices;

public static class BackgroundServiceExtensions
{
    public static IServiceCollection AddBackgroundServices(this IServiceCollection services)
    {
        services.AddHostedService<StaleSessionCleanupService>();
        return services;
    }
}

namespace CopilotTracker.Core;

using CopilotTracker.Core.Services;
using Microsoft.Extensions.DependencyInjection;

public static class ServiceRegistrationExtensions
{
    public static IServiceCollection AddCoreServices(this IServiceCollection services)
    {
        services.AddSingleton<SessionService>();
        services.AddSingleton<TaskService>();
        services.AddSingleton<TaskLogService>();
        services.AddSingleton<HealthService>();
        services.AddSingleton<PromptService>();
        services.AddSingleton<PromptLogService>();
        return services;
    }
}

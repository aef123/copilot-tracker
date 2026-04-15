namespace CopilotTracker.Server.Auth;

using Microsoft.Identity.Web;

public static class EntraAuthExtensions
{
    public static IServiceCollection AddEntraAuth(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddAuthentication("Bearer")
            .AddMicrosoftIdentityWebApi(configuration.GetSection("AzureAd"));

        services.AddAuthorization();
        return services;
    }
}

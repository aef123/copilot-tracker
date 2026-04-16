using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Identity.Web;

namespace CopilotTracker.Server.Auth;

public static class EntraAuthExtensions
{
    public static IServiceCollection AddEntraAuth(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddMicrosoftIdentityWebApi(configuration.GetSection("AzureAd"));

        services.Configure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
        {
            options.TokenValidationParameters.ValidateAudience = true;
            // Allow both v1 and v2 token formats
            var clientId = configuration["AzureAd:ClientId"];
            var tenantId = configuration["AzureAd:TenantId"];
            options.TokenValidationParameters.ValidAudiences = new[]
            {
                clientId,
                $"api://{clientId}"
            };
            // Accept both v1 (sts.windows.net) and v2 (login.microsoftonline.com) issuers
            options.TokenValidationParameters.ValidIssuers = new[]
            {
                $"https://login.microsoftonline.com/{tenantId}/v2.0",
                $"https://sts.windows.net/{tenantId}/"
            };
        });

        services.AddAuthorization(options =>
        {
            // Default policy: require authenticated user OR app
            options.DefaultPolicy = new Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .Build();

            // Policy for endpoints that accept both users and apps
            options.AddPolicy("TrackerAccess", policy =>
            {
                policy.RequireAuthenticatedUser();
                // Accept if token has either a scope (user) or a role (app) or is a valid app token
                policy.RequireAssertion(context =>
                {
                    var user = context.User;
                    // User token: has scope claim
                    var hasScope = user.HasClaim(c => c.Type == "scp" ||
                        c.Type == "http://schemas.microsoft.com/identity/claims/scope");
                    if (hasScope) return true;

                    // App token: has roles claim
                    var hasRoles = user.HasClaim(c => c.Type == "roles" ||
                        c.Type == System.Security.Claims.ClaimTypes.Role);
                    if (hasRoles) return true;

                    // Also accept app tokens without explicit roles (client_credentials basic)
                    // if they have a valid oid (service principal) and no scope
                    var hasOid = user.HasClaim(c => c.Type == "oid" ||
                        c.Type == "http://schemas.microsoft.com/identity/claims/objectidentifier");
                    return !hasScope && hasOid;
                });
            });
        });

        return services;
    }
}

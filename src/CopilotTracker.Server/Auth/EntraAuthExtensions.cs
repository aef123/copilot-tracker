using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;

namespace CopilotTracker.Server.Auth;

public static class EntraAuthExtensions
{
    public static IServiceCollection AddEntraAuth(this IServiceCollection services, IConfiguration configuration)
    {
        var clientId = configuration["AzureAd:ClientId"];
        var tenantId = configuration["AzureAd:TenantId"];
        var instance = configuration["AzureAd:Instance"] ?? "https://login.microsoftonline.com/";

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                // Use v2.0 authority for key discovery (Microsoft signs both v1 and v2 with same keys)
                options.Authority = $"{instance}{tenantId}/v2.0";
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuers = new[]
                    {
                        $"https://login.microsoftonline.com/{tenantId}/v2.0",
                        $"https://sts.windows.net/{tenantId}/"
                    },
                    ValidateAudience = true,
                    ValidAudiences = new[]
                    {
                        clientId,
                        $"api://{clientId}"
                    },
                    ValidateLifetime = true,
                };
            });

        services.AddAuthorization(options =>
        {
            options.DefaultPolicy = new Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .Build();

            options.AddPolicy("TrackerAccess", policy =>
            {
                policy.RequireAuthenticatedUser();
                policy.RequireAssertion(context =>
                {
                    var user = context.User;
                    var hasScope = user.HasClaim(c => c.Type == "scp" ||
                        c.Type == "http://schemas.microsoft.com/identity/claims/scope");
                    if (hasScope) return true;

                    var hasRoles = user.HasClaim(c => c.Type == "roles" ||
                        c.Type == System.Security.Claims.ClaimTypes.Role);
                    if (hasRoles) return true;

                    var hasOid = user.HasClaim(c => c.Type == "oid" ||
                        c.Type == "http://schemas.microsoft.com/identity/claims/objectidentifier");
                    return !hasScope && hasOid;
                });
            });
        });

        return services;
    }
}

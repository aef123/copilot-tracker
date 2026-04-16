using System.Security.Claims;

namespace CopilotTracker.Server.Auth;

public enum CallerType
{
    User,
    Application,
    Anonymous
}

public static class UserContext
{
    public static string GetUserId(ClaimsPrincipal user)
    {
        // For both user and app tokens, oid is the primary identifier
        var oid = user.FindFirst("http://schemas.microsoft.com/identity/claims/objectidentifier")?.Value
                  ?? user.FindFirst("oid")?.Value;

        if (!string.IsNullOrEmpty(oid))
            return oid;

        // For app tokens, fall back to azp/appid
        var appId = user.FindFirst("azp")?.Value ?? user.FindFirst("appid")?.Value;
        if (!string.IsNullOrEmpty(appId))
            return appId;

        return "anonymous";
    }

    public static string GetDisplayName(ClaimsPrincipal user)
    {
        // User tokens have a name claim
        var name = user.FindFirst("name")?.Value;
        if (!string.IsNullOrEmpty(name))
            return name;

        // App tokens: use app_displayname if available
        var appDisplayName = user.FindFirst("app_displayname")?.Value;
        if (!string.IsNullOrEmpty(appDisplayName))
            return appDisplayName;

        // Fall back to "App: {clientId}"
        var appId = user.FindFirst("azp")?.Value ?? user.FindFirst("appid")?.Value;
        if (!string.IsNullOrEmpty(appId))
            return $"App: {appId}";

        return "anonymous";
    }

    public static CallerType GetCallerType(ClaimsPrincipal user)
    {
        if (user.Identity?.IsAuthenticated != true)
            return CallerType.Anonymous;

        // App-only tokens have no scp (scope) claim
        var hasScope = user.FindFirst("scp") != null
                       || user.FindFirst("http://schemas.microsoft.com/identity/claims/scope") != null;

        return hasScope ? CallerType.User : CallerType.Application;
    }
}

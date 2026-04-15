namespace CopilotTracker.Server.Auth;

using System.Security.Claims;

public static class UserContext
{
    public static string GetUserId(ClaimsPrincipal user)
    {
        return user.FindFirst("http://schemas.microsoft.com/identity/claims/objectidentifier")?.Value
            ?? user.FindFirst("oid")?.Value
            ?? throw new UnauthorizedAccessException("User ID not found in token");
    }

    public static string GetDisplayName(ClaimsPrincipal user)
    {
        return user.FindFirst("name")?.Value
            ?? user.FindFirst(ClaimTypes.Name)?.Value
            ?? "Unknown";
    }
}

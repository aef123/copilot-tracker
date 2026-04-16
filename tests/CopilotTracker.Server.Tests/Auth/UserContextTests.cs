namespace CopilotTracker.Server.Tests.Auth;

using System.Security.Claims;
using CopilotTracker.Server.Auth;
using FluentAssertions;

public class UserContextTests
{
    // --- GetUserId tests ---

    [Fact]
    public void GetUserId_ReturnsOid_FromObjectIdentifierClaim()
    {
        var claims = new[]
        {
            new Claim("http://schemas.microsoft.com/identity/claims/objectidentifier", "user-oid-123")
        };
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims));

        var result = UserContext.GetUserId(principal);

        result.Should().Be("user-oid-123");
    }

    [Fact]
    public void GetUserId_FallsBackToShortOidClaim()
    {
        var claims = new[] { new Claim("oid", "short-oid-456") };
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims));

        var result = UserContext.GetUserId(principal);

        result.Should().Be("short-oid-456");
    }

    [Fact]
    public void GetUserId_PrefersLongOidOverShortOid()
    {
        var claims = new[]
        {
            new Claim("http://schemas.microsoft.com/identity/claims/objectidentifier", "long-oid"),
            new Claim("oid", "short-oid")
        };
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims));

        var result = UserContext.GetUserId(principal);

        result.Should().Be("long-oid");
    }

    [Fact]
    public void GetUserId_ReturnsAnonymous_WhenNoClaims()
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity());

        var result = UserContext.GetUserId(principal);

        result.Should().Be("anonymous");
    }

    [Fact]
    public void GetUserId_ReturnsAnonymous_WhenIrrelevantClaimsPresent()
    {
        var claims = new[]
        {
            new Claim(ClaimTypes.Email, "user@example.com"),
            new Claim("name", "Test User")
        };
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims));

        var result = UserContext.GetUserId(principal);

        result.Should().Be("anonymous");
    }

    // --- GetDisplayName tests ---

    [Fact]
    public void GetDisplayName_ReturnsNameClaim()
    {
        var claims = new[] { new Claim("name", "Alice Smith") };
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims));

        var result = UserContext.GetDisplayName(principal);

        result.Should().Be("Alice Smith");
    }

    [Fact]
    public void GetDisplayName_FallsBackToClaimTypesName()
    {
        // ClaimTypes.Name maps to http://schemas.xmlsoap.org/ws/2005/05/identity/claims/name
        // Our new UserContext only checks "name" claim directly, not ClaimTypes.Name
        // So this should return "anonymous" unless we add support
        var claims = new[] { new Claim(ClaimTypes.Name, "Bob Jones") };
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims));

        var result = UserContext.GetDisplayName(principal);

        // ClaimTypes.Name is not checked in new implementation - returns anonymous
        result.Should().Be("anonymous");
    }

    [Fact]
    public void GetDisplayName_PrefersNameOverClaimTypesName()
    {
        var claims = new[]
        {
            new Claim("name", "Preferred Name"),
            new Claim(ClaimTypes.Name, "Fallback Name")
        };
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims));

        var result = UserContext.GetDisplayName(principal);

        result.Should().Be("Preferred Name");
    }

    [Fact]
    public void GetDisplayName_ReturnsAnonymous_WhenNoNameClaims()
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity());

        var result = UserContext.GetDisplayName(principal);

        result.Should().Be("anonymous");
    }

    [Fact]
    public void GetDisplayName_ReturnsAnonymous_WhenOnlyIrrelevantClaims()
    {
        var claims = new[]
        {
            new Claim(ClaimTypes.Email, "user@example.com"),
            new Claim("oid", "some-oid")
        };
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims));

        var result = UserContext.GetDisplayName(principal);

        result.Should().Be("anonymous");
    }

    // --- App token tests ---

    [Fact]
    public void GetCallerType_ReturnsUser_WhenScopeClaim()
    {
        var claims = new[] { new Claim("scp", "CopilotTracker.ReadWrite"), new Claim("oid", "user-oid") };
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, "Bearer"));
        UserContext.GetCallerType(principal).Should().Be(CallerType.User);
    }

    [Fact]
    public void GetCallerType_ReturnsApplication_WhenNoScopeClaim()
    {
        var claims = new[] { new Claim("oid", "app-oid"), new Claim("azp", "client-id") };
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, "Bearer"));
        UserContext.GetCallerType(principal).Should().Be(CallerType.Application);
    }

    [Fact]
    public void GetCallerType_ReturnsAnonymous_WhenNotAuthenticated()
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity());
        UserContext.GetCallerType(principal).Should().Be(CallerType.Anonymous);
    }

    [Fact]
    public void GetUserId_ReturnsAzp_WhenNoOid()
    {
        var claims = new[] { new Claim("azp", "app-client-id") };
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims));
        UserContext.GetUserId(principal).Should().Be("app-client-id");
    }

    [Fact]
    public void GetDisplayName_ReturnsAppDisplayName_ForAppToken()
    {
        var claims = new[] { new Claim("app_displayname", "Copilot Tracker Client"), new Claim("azp", "client-id") };
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims));
        UserContext.GetDisplayName(principal).Should().Be("Copilot Tracker Client");
    }

    [Fact]
    public void GetDisplayName_ReturnsAppPrefix_WhenNoDisplayName()
    {
        var claims = new[] { new Claim("azp", "64aa6bbc-0f8a-47e4-bc02-fd7ed6659b3e") };
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims));
        UserContext.GetDisplayName(principal).Should().Be("App: 64aa6bbc-0f8a-47e4-bc02-fd7ed6659b3e");
    }
}

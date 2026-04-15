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
    public void GetUserId_ThrowsUnauthorized_WhenNoClaims()
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity());

        Action act = () => UserContext.GetUserId(principal);

        act.Should().Throw<UnauthorizedAccessException>()
            .WithMessage("*User ID not found*");
    }

    [Fact]
    public void GetUserId_ThrowsUnauthorized_WhenIrrelevantClaimsPresent()
    {
        var claims = new[]
        {
            new Claim(ClaimTypes.Email, "user@example.com"),
            new Claim("name", "Test User")
        };
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims));

        Action act = () => UserContext.GetUserId(principal);

        act.Should().Throw<UnauthorizedAccessException>();
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
        var claims = new[] { new Claim(ClaimTypes.Name, "Bob Jones") };
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims));

        var result = UserContext.GetDisplayName(principal);

        result.Should().Be("Bob Jones");
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
    public void GetDisplayName_ReturnsUnknown_WhenNoNameClaims()
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity());

        var result = UserContext.GetDisplayName(principal);

        result.Should().Be("Unknown");
    }

    [Fact]
    public void GetDisplayName_ReturnsUnknown_WhenOnlyIrrelevantClaims()
    {
        var claims = new[]
        {
            new Claim(ClaimTypes.Email, "user@example.com"),
            new Claim("oid", "some-oid")
        };
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims));

        var result = UserContext.GetDisplayName(principal);

        result.Should().Be("Unknown");
    }
}

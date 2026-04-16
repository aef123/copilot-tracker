namespace CopilotTracker.Server.Tests.Integration;

using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using CopilotTracker.Core.Interfaces;
using CopilotTracker.Core.Models;
using FluentAssertions;
using Moq;

public class TrackerWebApplicationFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureAppConfiguration((context, config) =>
        {
            // Provide required config values so Cosmos registration doesn't throw
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Cosmos:Endpoint"] = "https://localhost:8081/",
                ["Cosmos:Database"] = "TestDb",
                ["AzureAd:Instance"] = "https://login.microsoftonline.com/",
                ["AzureAd:TenantId"] = "fake-tenant",
                ["AzureAd:ClientId"] = "fake-client",
            });
        });

        builder.ConfigureTestServices(services =>
        {
            // Remove the real Cosmos Database singleton so it won't try to connect
            services.RemoveAll<Database>();

            // Replace repository interfaces with mocks
            var mockSessionRepo = new Mock<ISessionRepository>();
            mockSessionRepo
                .Setup(r => r.ListAsync(It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<DateTime?>(), It.IsAny<string?>(), It.IsAny<int>()))
                .ReturnsAsync(new PagedResult<Session> { Items = [] });
            mockSessionRepo
                .Setup(r => r.GetAsync(It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync((Session?)null);
            mockSessionRepo
                .Setup(r => r.GetStaleSessionsAsync(It.IsAny<DateTime>(), It.IsAny<string?>(), It.IsAny<int>()))
                .ReturnsAsync(new PagedResult<Session> { Items = [] });
            mockSessionRepo
                .Setup(r => r.GetActiveByMachineAsync(It.IsAny<string>()))
                .ReturnsAsync(Array.Empty<Session>());

            var mockTaskRepo = new Mock<ITaskRepository>();
            mockTaskRepo
                .Setup(r => r.ListAsync(It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<int>()))
                .ReturnsAsync(new PagedResult<TrackerTask> { Items = [] });
            mockTaskRepo
                .Setup(r => r.GetAsync(It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync((TrackerTask?)null);

            var mockTaskLogRepo = new Mock<ITaskLogRepository>();
            mockTaskLogRepo
                .Setup(r => r.GetByTaskPagedAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<int>()))
                .ReturnsAsync(new PagedResult<TaskLog> { Items = [] });

            var mockPromptRepo = new Mock<IPromptRepository>();
            mockPromptRepo
                .Setup(r => r.ListAsync(It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<DateTime?>(), It.IsAny<string?>(), It.IsAny<int>()))
                .ReturnsAsync(new PagedResult<Prompt> { Items = [] });
            mockPromptRepo
                .Setup(r => r.GetAsync(It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync((Prompt?)null);
            mockPromptRepo
                .Setup(r => r.GetActiveBySessionAsync(It.IsAny<string>()))
                .ReturnsAsync((Prompt?)null);
            mockPromptRepo
                .Setup(r => r.GetBySessionAsync(It.IsAny<string>()))
                .ReturnsAsync(Array.Empty<Prompt>());
            mockPromptRepo
                .Setup(r => r.CountByStatusAsync(It.IsAny<string?>()))
                .ReturnsAsync(0);

            var mockPromptLogRepo = new Mock<IPromptLogRepository>();
            mockPromptLogRepo
                .Setup(r => r.GetByPromptPagedAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<int>()))
                .ReturnsAsync(new PagedResult<PromptLog> { Items = [] });

            services.RemoveAll<ISessionRepository>();
            services.RemoveAll<ITaskRepository>();
            services.RemoveAll<ITaskLogRepository>();
            services.RemoveAll<IPromptRepository>();
            services.RemoveAll<IPromptLogRepository>();
            services.AddSingleton(mockSessionRepo.Object);
            services.AddSingleton(mockTaskRepo.Object);
            services.AddSingleton(mockTaskLogRepo.Object);
            services.AddSingleton(mockPromptRepo.Object);
            services.AddSingleton(mockPromptLogRepo.Object);

            // Replace auth with a test scheme
            services.AddAuthentication("Test")
                .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>("Test", _ => { });
        });
    }
}

/// <summary>
/// Auth handler that authenticates when the request has an Authorization header,
/// and returns no-result (letting [AllowAnonymous] pass) otherwise.
/// </summary>
public class TestAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public TestAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : base(options, logger, encoder)
    {
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.ContainsKey("Authorization"))
            return Task.FromResult(AuthenticateResult.NoResult());

        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, "test-user"),
            new Claim(ClaimTypes.Name, "Test User"),
            new Claim("oid", "test-oid-123"),
            new Claim("scp", "CopilotTracker.ReadWrite"),
        };
        var identity = new ClaimsIdentity(claims, "Test");
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, "Test");

        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}

public class ApiPipelineTests : IClassFixture<TrackerWebApplicationFactory>
{
    private readonly HttpClient _unauthClient;
    private readonly HttpClient _authClient;

    public ApiPipelineTests(TrackerWebApplicationFactory factory)
    {
        _unauthClient = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
        });

        _authClient = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
        });
        _authClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Test", "fake-token");
    }

    // ---------------------------------------------------------------
    // Auth enforcement: anonymous access
    // ---------------------------------------------------------------

    [Fact]
    public async Task Health_IsAccessibleAnonymously()
    {
        var response = await _unauthClient.GetAsync("/api/health");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Sessions_ReturnsUnauthorized_WithoutToken()
    {
        var response = await _unauthClient.GetAsync("/api/sessions?machineId=test-machine");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Tasks_ReturnsUnauthorized_WithoutToken()
    {
        var response = await _unauthClient.GetAsync("/api/tasks?queueName=default");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Mcp_ReturnsNotFound_Unauthenticated()
    {
        var response = await _unauthClient.PostAsync("/mcp", null);

        // MCP endpoint removed; POST to /mcp is no longer routed
        response.StatusCode.Should().BeOneOf(
            HttpStatusCode.NotFound, HttpStatusCode.MethodNotAllowed);
    }

    // ---------------------------------------------------------------
    // Route binding: authenticated access
    // ---------------------------------------------------------------

    [Fact]
    public async Task Sessions_ReturnsOk_WithAuth()
    {
        var response = await _authClient.GetAsync("/api/sessions?machineId=test-machine");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Sessions_ResponseIsJson()
    {
        var response = await _authClient.GetAsync("/api/sessions?machineId=test-machine");

        response.Content.Headers.ContentType!.MediaType.Should().Be("application/json");
    }

    [Fact]
    public async Task Tasks_ReturnsOk_WithAuth()
    {
        var response = await _authClient.GetAsync("/api/tasks?queueName=default");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Tasks_ResponseIsJson()
    {
        var response = await _authClient.GetAsync("/api/tasks?queueName=default");

        response.Content.Headers.ContentType!.MediaType.Should().Be("application/json");
    }

    [Fact]
    public async Task Health_ResponseIsJson()
    {
        var response = await _unauthClient.GetAsync("/api/health");

        response.Content.Headers.ContentType!.MediaType.Should().Be("application/json");
    }

    [Fact]
    public async Task NonexistentApiRoute_Returns404_NotSpaHtml()
    {
        var response = await _unauthClient.GetAsync("/api/nonexistent");

        // Bad /api/* routes should return 404, not 200 HTML from the SPA fallback
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Mcp_ReturnsNotFound_Authenticated()
    {
        var jsonRpc = new StringContent(
            """{"jsonrpc":"2.0","method":"initialize","id":1,"params":{"protocolVersion":"2025-03-26","capabilities":{},"clientInfo":{"name":"test","version":"1.0"}}}""",
            System.Text.Encoding.UTF8,
            "application/json");

        var response = await _authClient.PostAsync("/mcp", jsonRpc);

        // MCP endpoint removed; POST to /mcp is no longer routed
        response.StatusCode.Should().BeOneOf(
            HttpStatusCode.NotFound, HttpStatusCode.MethodNotAllowed);
    }
}

/// <summary>
/// Factory that configures mock repositories to throw, for error propagation tests.
/// </summary>
public class FaultyWebApplicationFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Cosmos:Endpoint"] = "https://localhost:8081/",
                ["Cosmos:Database"] = "TestDb",
                ["AzureAd:Instance"] = "https://login.microsoftonline.com/",
                ["AzureAd:TenantId"] = "fake-tenant",
                ["AzureAd:ClientId"] = "fake-client",
            });
        });

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<Database>();

            var mockSessionRepo = new Mock<ISessionRepository>();
            mockSessionRepo
                .Setup(r => r.ListAsync(It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<DateTime?>(), It.IsAny<string?>(), It.IsAny<int>()))
                .ThrowsAsync(new InvalidOperationException("Simulated session repo failure"));
            mockSessionRepo
                .Setup(r => r.GetAsync(It.IsAny<string>(), It.IsAny<string>()))
                .ThrowsAsync(new InvalidOperationException("Simulated session repo failure"));
            mockSessionRepo
                .Setup(r => r.GetStaleSessionsAsync(It.IsAny<DateTime>(), It.IsAny<string?>(), It.IsAny<int>()))
                .ThrowsAsync(new InvalidOperationException("Simulated session repo failure"));
            mockSessionRepo
                .Setup(r => r.GetActiveByMachineAsync(It.IsAny<string>()))
                .ThrowsAsync(new InvalidOperationException("Simulated session repo failure"));

            var mockTaskRepo = new Mock<ITaskRepository>();
            mockTaskRepo
                .Setup(r => r.ListAsync(It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<int>()))
                .ThrowsAsync(new InvalidOperationException("Simulated task repo failure"));
            mockTaskRepo
                .Setup(r => r.GetAsync(It.IsAny<string>(), It.IsAny<string>()))
                .ThrowsAsync(new InvalidOperationException("Simulated task repo failure"));

            var mockTaskLogRepo = new Mock<ITaskLogRepository>();
            mockTaskLogRepo
                .Setup(r => r.GetByTaskPagedAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<int>()))
                .ThrowsAsync(new InvalidOperationException("Simulated task log repo failure"));

            var mockPromptRepo = new Mock<IPromptRepository>();
            mockPromptRepo
                .Setup(r => r.ListAsync(It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<DateTime?>(), It.IsAny<string?>(), It.IsAny<int>()))
                .ThrowsAsync(new InvalidOperationException("Simulated prompt repo failure"));
            mockPromptRepo
                .Setup(r => r.GetAsync(It.IsAny<string>(), It.IsAny<string>()))
                .ThrowsAsync(new InvalidOperationException("Simulated prompt repo failure"));
            mockPromptRepo
                .Setup(r => r.GetActiveBySessionAsync(It.IsAny<string>()))
                .ThrowsAsync(new InvalidOperationException("Simulated prompt repo failure"));
            mockPromptRepo
                .Setup(r => r.CountByStatusAsync(It.IsAny<string?>()))
                .ThrowsAsync(new InvalidOperationException("Simulated prompt repo failure"));

            var mockPromptLogRepo = new Mock<IPromptLogRepository>();
            mockPromptLogRepo
                .Setup(r => r.GetByPromptPagedAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<int>()))
                .ThrowsAsync(new InvalidOperationException("Simulated prompt log repo failure"));

            services.RemoveAll<ISessionRepository>();
            services.RemoveAll<ITaskRepository>();
            services.RemoveAll<ITaskLogRepository>();
            services.RemoveAll<IPromptRepository>();
            services.RemoveAll<IPromptLogRepository>();
            services.AddSingleton(mockSessionRepo.Object);
            services.AddSingleton(mockTaskRepo.Object);
            services.AddSingleton(mockTaskLogRepo.Object);
            services.AddSingleton(mockPromptRepo.Object);
            services.AddSingleton(mockPromptLogRepo.Object);

            services.AddAuthentication("Test")
                .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>("Test", _ => { });
        });
    }
}

public class ErrorPropagationTests : IClassFixture<FaultyWebApplicationFactory>
{
    private readonly HttpClient _authClient;

    public ErrorPropagationTests(FaultyWebApplicationFactory factory)
    {
        _authClient = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
        });
        _authClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Test", "fake-token");
    }

    [Fact]
    public async Task Sessions_WhenServiceThrows_Returns500()
    {
        var response = await _authClient.GetAsync("/api/sessions?machineId=test-machine");

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
    }

    [Fact]
    public async Task Tasks_WhenServiceThrows_Returns500()
    {
        var response = await _authClient.GetAsync("/api/tasks?queueName=default");

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
    }

    [Fact]
    public async Task Health_WhenServiceThrows_ReturnsError()
    {
        var response = await _authClient.GetAsync("/api/health");

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
    }
}

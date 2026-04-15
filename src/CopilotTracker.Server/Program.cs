using CopilotTracker.Core;
using CopilotTracker.Cosmos;
using CopilotTracker.Server.Auth;

var builder = WebApplication.CreateBuilder(args);

// Authentication
builder.Services.AddEntraAuth(builder.Configuration);

// Cosmos DB repositories
builder.Services.AddCosmosRepositories(builder.Configuration);

// Core services
builder.Services.AddCoreServices();

// HttpContextAccessor for MCP tools to access user claims
builder.Services.AddHttpContextAccessor();

// MCP Server (discovers [McpServerToolType] classes in this assembly)
builder.Services.AddMcpServer()
    .WithHttpTransport()
    .WithToolsFromAssembly();

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();

app.UseStaticFiles();

app.MapGet("/api/health", () => Results.Ok(new { status = "healthy", timestamp = DateTime.UtcNow }));

// MCP endpoint
app.MapMcp();

// SPA fallback (must be last)
app.MapFallbackToFile("index.html");

app.Run();

public partial class Program { }

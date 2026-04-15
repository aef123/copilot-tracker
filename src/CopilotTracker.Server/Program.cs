using CopilotTracker.Core;
using CopilotTracker.Cosmos;
using CopilotTracker.Server.Auth;
using CopilotTracker.Server.BackgroundServices;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEntraAuth(builder.Configuration);
builder.Services.AddCosmosRepositories(builder.Configuration);
builder.Services.AddCoreServices();
builder.Services.AddHttpContextAccessor();
builder.Services.AddControllers();
builder.Services.AddBackgroundServices();
builder.Services.AddMcpServer()
    .WithHttpTransport()
    .WithToolsFromAssembly();

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();
app.UseStaticFiles();

app.MapControllers();
app.MapMcp();

// SPA fallback (must be last)
app.MapFallbackToFile("index.html");

app.Run();

public partial class Program { }

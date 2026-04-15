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

builder.Services.AddProblemDetails();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
    app.UseExceptionHandler();

app.UseStatusCodePages();
app.UseAuthentication();
app.UseAuthorization();
app.UseStaticFiles();

app.MapControllers();
app.MapMcp("/mcp").RequireAuthorization();

// Diagnostic endpoint to test Cosmos connectivity
app.MapGet("/api/diag", async (IServiceProvider sp) =>
{
    try
    {
        var db = sp.GetRequiredService<Microsoft.Azure.Cosmos.Database>();
        var container = db.GetContainer("sessions");
        var response = await container.ReadContainerAsync();
        return Results.Ok(new { status = "ok", container = response.Resource.Id });
    }
    catch (Exception ex)
    {
        return Results.Json(new
        {
            status = "error",
            error = ex.GetType().FullName,
            message = ex.Message,
            inner = ex.InnerException?.GetType().FullName,
            innerMessage = ex.InnerException?.Message
        }, statusCode: 500);
    }
}).AllowAnonymous();

// SPA fallback — exclude API and MCP routes so bad paths return 404, not HTML
app.MapFallbackToFile("{**path:nonfile}", "index.html")
    .Add(endpointBuilder =>
    {
        var original = endpointBuilder.RequestDelegate!;
        endpointBuilder.RequestDelegate = context =>
        {
            var path = context.Request.Path.Value ?? "";
            if (path.StartsWith("/api/", StringComparison.OrdinalIgnoreCase) ||
                path.StartsWith("/mcp", StringComparison.OrdinalIgnoreCase))
            {
                context.Response.StatusCode = 404;
                return Task.CompletedTask;
            }
            return original(context);
        };
    });

app.Run();

public partial class Program { }

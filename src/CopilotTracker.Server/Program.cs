var builder = WebApplication.CreateBuilder(args);

// TODO: Add authentication (Phase 1)
// TODO: Add MCP server (Phase 1)
// TODO: Add Cosmos DB repositories (Phase 1)
// TODO: Add services (Phase 2)

var app = builder.Build();

app.UseStaticFiles();

app.MapGet("/api/health", () => Results.Ok(new { status = "healthy", timestamp = DateTime.UtcNow }));

// TODO: Map API controllers (Phase 2)
// TODO: Map MCP endpoint (Phase 1)

// SPA fallback
app.MapFallbackToFile("index.html");

app.Run();

public partial class Program { }

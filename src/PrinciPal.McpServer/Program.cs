using System.Diagnostics;
using PrinciPal.McpServer.Services;

// Parse CLI args
var port = 9229;
var parentPid = 0;

for (int i = 0; i < args.Length - 1; i++)
{
    if (args[i] == "--port" && int.TryParse(args[i + 1], out var p))
        port = p;
    else if (args[i] == "--parent-pid" && int.TryParse(args[i + 1], out var pid))
        parentPid = pid;
}

// Parent-pid watchdog: exit if the parent process (e.g. VS) dies
if (parentPid > 0)
{
    var watchdogThread = new Thread(() =>
    {
        while (true)
        {
            Thread.Sleep(5000);
            try
            {
                Process.GetProcessById(parentPid);
            }
            catch
            {
                Environment.Exit(0);
            }
        }
    })
    {
        IsBackground = true,
        Name = "ParentPidWatchdog"
    };
    watchdogThread.Start();
}

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton<DebugStateStore>();

builder.Services.AddMcpServer(options =>
{
    options.ServerInfo = new()
    {
        Name = "princiPal",
        Version = "1.0.0",
    };
})
.WithHttpTransport()
.WithToolsFromAssembly();

var app = builder.Build();

// REST endpoints for the VSIX extension to push debug state
app.MapPost("/api/debug-state", (DebugStateStore store, PrinciPal.Contracts.DebugState state) =>
{
    store.Update(state);
    return Results.Ok();
});

app.MapPost("/api/debug-state/expression", (DebugStateStore store, PrinciPal.Contracts.ExpressionResult result) =>
{
    store.UpdateExpression(result);
    return Results.Ok();
});

app.MapDelete("/api/debug-state", (DebugStateStore store) =>
{
    store.Clear();
    return Results.Ok();
});

app.MapGet("/api/debug-state/history", (DebugStateStore store) =>
{
    return Results.Ok(store.GetHistory());
});

app.MapDelete("/api/debug-state/history", (DebugStateStore store) =>
{
    store.ClearHistory();
    return Results.Ok();
});

app.MapGet("/api/health", () => Results.Ok(new { status = "running" }));

// MCP endpoint (SSE transport)
app.MapMcp();

app.Run($"http://localhost:{port}");

// Needed for integration tests with WebApplicationFactory
public partial class Program { }

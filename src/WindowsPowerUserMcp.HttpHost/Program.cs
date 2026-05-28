using System.ComponentModel;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using ModelContextProtocol.Server;
using WindowsPowerUserMcp.Core;
using WindowsPowerUserMcp.HttpHost;

var builder = WebApplication.CreateBuilder(args);
var options = new WindowsPowerUserMcpOptions();
builder.Configuration.Bind(options);
options.LocalHttpEnabled = options.LocalHttpEnabled || args.Contains("--enable-http", StringComparer.OrdinalIgnoreCase);

builder.WebHost.UseUrls($"http://127.0.0.1:{options.LocalHttpPort}");
builder.Services.AddSingleton(options);
builder.Services.AddSingleton(new HttpBrokerToolProxy(new BrokerPipeClient(options.IpcPipeName, TimeSpan.FromSeconds(3))));
builder.Services
    .AddMcpServer()
    .WithHttpTransport(http =>
    {
        http.Stateless = true;
    })
    .WithToolsFromAssembly();

var app = builder.Build();

app.MapGet("/", () => Results.Json(new
{
    name = "WindowsPowerUserMcp.HttpHost",
    enabled = options.LocalHttpEnabled,
    mcp = options.LocalHttpEnabled ? "/mcp" : null,
    note = options.LocalHttpEnabled ? "HTTP MCP is enabled on localhost." : "HTTP MCP is disabled. Start with --enable-http or set LocalHttpEnabled=true."
}));

if (options.OptionalHttpAuthRequired)
{
    var token = Environment.GetEnvironmentVariable("WINDOWS_POWER_USER_MCP_HTTP_TOKEN");
    app.Use(async (context, next) =>
    {
        if (!context.Request.Path.StartsWithSegments("/mcp"))
        {
            await next(context);
            return;
        }

        if (!options.LocalHttpEnabled)
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await context.Response.WriteAsync("HTTP MCP is disabled. Start with --enable-http or set LocalHttpEnabled=true.");
            return;
        }

        if (string.IsNullOrWhiteSpace(token))
        {
            context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            await context.Response.WriteAsync("Set WINDOWS_POWER_USER_MCP_HTTP_TOKEN before enabling authenticated HTTP MCP.");
            return;
        }

        var authorization = context.Request.Headers.Authorization.ToString();
        if (!authorization.Equals($"Bearer {token}", StringComparison.Ordinal))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsync("Missing or invalid bearer token.");
            return;
        }

        await next(context);
    });
}

app.MapMcp("/mcp");
await app.RunAsync();

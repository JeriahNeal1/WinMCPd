using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using WindowsPowerUserMcp.Core;
using WindowsPowerUserMcp.StdioBridge;

var builder = Host.CreateApplicationBuilder(args);
var options = new WindowsPowerUserMcpOptions();
options.IpcPipeName = builder.Configuration["IpcPipeName"] ?? options.IpcPipeName;

builder.Logging.AddConsole(o => o.LogToStandardErrorThreshold = LogLevel.Trace);
builder.Services.AddSingleton(options);
var proxy = new BrokerToolProxy(new BrokerPipeClient(options.IpcPipeName, TimeSpan.FromSeconds(3)));
builder.Services.AddSingleton(proxy);
builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithTools(DirectBrokerToolFactory.CreateTools(proxy));

await builder.Build().RunAsync();

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.WindowsServices;
using Microsoft.Extensions.Logging;
using WindowsPowerUserMcp.BrokerService;
using WindowsPowerUserMcp.Core;
using WindowsPowerUserMcp.Orchestration;
using WindowsPowerUserMcp.Security;
using WindowsPowerUserMcp.Windows;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.Configure<WindowsPowerUserMcpOptions>(builder.Configuration);

var options = new WindowsPowerUserMcpOptions();
options.IpcPipeName = builder.Configuration["IpcPipeName"] ?? options.IpcPipeName;
options.IpcCurrentUserOnly = !bool.TryParse(builder.Configuration["IpcCurrentUserOnly"], out var currentUserOnly) || currentUserOnly;
options.IpcAllowBuiltinAdministrators = bool.TryParse(builder.Configuration["IpcAllowBuiltinAdministrators"], out var allowAdmins) && allowAdmins;
var configuredSids = builder.Configuration.GetSection("IpcAllowedUserSids")
    .GetChildren()
    .Select(c => c.Value)
    .Where(s => !string.IsNullOrWhiteSpace(s))
    .Cast<string>()
    .ToArray();
options.IpcAllowedUserSids = configuredSids.Length > 0
    ? configuredSids
    : (builder.Configuration["IpcAllowedUserSids"] ?? string.Empty)
        .Split([';', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
if (int.TryParse(builder.Configuration["DefaultTimeoutSeconds"], out var defaultTimeoutSeconds))
{
    options.DefaultTimeoutSeconds = defaultTimeoutSeconds;
}
var layout = PlatformPaths.CreateLayout(options, serviceLevel: WindowsServiceHelpers.IsWindowsService());

builder.Services.AddSingleton(options);
builder.Services.AddSingleton(layout);
builder.Services.AddSingleton<SecretRedactor>();
builder.Services.AddSingleton<RiskClassifier>();
builder.Services.AddSingleton<SafetyGuards>();
builder.Services.AddSingleton<IAuditLogger, JsonlAuditLogger>();
builder.Services.AddSingleton<TaskLedger>();
builder.Services.AddSingleton<CommandRunner>();
builder.Services.AddSingleton<WaitServices>();
builder.Services.AddSingleton<SystemOperations>();
builder.Services.AddSingleton<FileSystemOperations>();
builder.Services.AddSingleton<ProcessOperations>();
builder.Services.AddSingleton<RegistryOperations>();
builder.Services.AddSingleton<ServiceOperations>();
builder.Services.AddSingleton<ToolingOperations>();
builder.Services.AddSingleton<BrokerToolRegistry>();
builder.Services.AddHostedService<BrokerWorker>();
builder.Services.AddWindowsService(o => o.ServiceName = "WindowsPowerUserMcp Broker");
builder.Logging.AddConsole();

await builder.Build().RunAsync();

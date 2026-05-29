using System.Text.Json;
using System.Windows;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.WindowsServices;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using WindowsPowerUserMcp.AppManagement;
using WindowsPowerUserMcp.BrokerService;
using WindowsPowerUserMcp.Core;
using WindowsPowerUserMcp.Orchestration;
using WindowsPowerUserMcp.Security;
using WindowsPowerUserMcp.StdioBridge;
using WindowsPowerUserMcp.Windows;

namespace WindowsPowerUserMcp.App;

public static class Program
{
    [STAThread]
    public static int Main(string[] args) =>
        MainAsync(args).GetAwaiter().GetResult();

    private static async Task<int> MainAsync(string[] args)
    {
        var parsed = AppArguments.Parse(args);
        try
        {
            return parsed.Mode switch
            {
                AppMode.Broker => await RunBrokerAsync(args, parsed).ConfigureAwait(false),
                AppMode.Stdio => await RunStdioAsync(args).ConfigureAwait(false),
                AppMode.Http => await RunHttpAsync(args).ConfigureAwait(false),
                AppMode.DesktopAgent => await RunDesktopAgentLauncherAsync().ConfigureAwait(false),
                AppMode.Install => await RunInstallAsync(parsed).ConfigureAwait(false),
                AppMode.Uninstall => await RunUninstallAsync().ConfigureAwait(false),
                AppMode.Update => await RunUpdateAsync().ConfigureAwait(false),
                AppMode.ApplyUpdate => await RunApplyUpdateAsync(parsed).ConfigureAwait(false),
                _ => RunDashboard(parsed)
            };
        }
        catch (Exception ex)
        {
            WriteJson(new { success = false, error = ex.Message });
            return 1;
        }
    }

    private static int RunDashboard(AppArguments parsed)
    {
        using var mutex = new Mutex(initiallyOwned: true, "Local\\WindowsPowerUserMcp.App.Dashboard", out var owned);
        if (!owned)
        {
            MessageBox.Show("WindowsPowerUserMcp dashboard is already running. Open it from the notification area.", ProductConstants.ProductName, MessageBoxButton.OK, MessageBoxImage.Information);
            return 0;
        }

        var app = new App();
        app.InitializeComponent();
        var manager = new AppManager();
        var settings = manager.LoadSettingsAsync().GetAwaiter().GetResult();
        var window = new MainWindow(manager, settings)
        {
            WindowState = parsed.TrayOnly || settings.StartMinimizedToTray ? WindowState.Minimized : WindowState.Normal
        };
        app.Run(window);
        return 0;
    }

    private static async Task<int> RunBrokerAsync(string[] args, AppArguments parsed)
    {
        var settings = await new AppSettingsStore().LoadAsync().ConfigureAwait(false);
        var builder = Host.CreateApplicationBuilder(args);
        var options = BuildOptions(builder.Configuration, settings);
        var serviceLevel = parsed.Service || WindowsServiceHelpers.IsWindowsService();
        var layout = PlatformPaths.CreateLayout(options, serviceLevel);

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
        builder.Services.AddWindowsService(o => o.ServiceName = ProductConstants.BrokerServiceDisplayName);
        builder.Logging.AddConsole();

        await builder.Build().RunAsync().ConfigureAwait(false);
        return 0;
    }

    private static async Task<int> RunStdioAsync(string[] args)
    {
        var builder = Host.CreateApplicationBuilder(args);
        var settings = await new AppSettingsStore().LoadAsync().ConfigureAwait(false);
        var options = BuildOptions(builder.Configuration, settings);

        builder.Logging.AddConsole(o => o.LogToStandardErrorThreshold = LogLevel.Trace);
        builder.Services.AddSingleton(options);
        var proxy = new BrokerToolProxy(new BrokerPipeClient(options.IpcPipeName, TimeSpan.FromSeconds(3)));
        builder.Services.AddSingleton(proxy);
        builder.Services
            .AddMcpServer()
            .WithStdioServerTransport()
            .WithTools(DirectBrokerToolFactory.CreateTools(proxy));

        await builder.Build().RunAsync().ConfigureAwait(false);
        return 0;
    }

    private static async Task<int> RunHttpAsync(string[] args)
    {
        var settings = await new AppSettingsStore().LoadAsync().ConfigureAwait(false);
        var builder = WebApplication.CreateBuilder(args);
        var options = BuildOptions(builder.Configuration, settings);
        options.LocalHttpEnabled = true;
        options.LocalHttpPort = settings.HttpPort;
        options.OptionalHttpAuthRequired = settings.RequireHttpToken;

        builder.WebHost.UseUrls($"http://127.0.0.1:{options.LocalHttpPort}");
        var proxy = new BrokerToolProxy(new BrokerPipeClient(options.IpcPipeName, TimeSpan.FromSeconds(3)));
        builder.Services.AddSingleton(options);
        builder.Services.AddSingleton(proxy);
        builder.Services
            .AddMcpServer()
            .WithHttpTransport(http => http.Stateless = true)
            .WithTools(DirectBrokerToolFactory.CreateTools(proxy));

        var app = builder.Build();
        app.MapGet("/", () => Results.Json(new
        {
            name = ProductConstants.ProductName,
            enabled = true,
            mcp = "/mcp",
            port = options.LocalHttpPort,
            auth_required = options.OptionalHttpAuthRequired
        }));

        if (options.OptionalHttpAuthRequired)
        {
            var token = Environment.GetEnvironmentVariable("WINDOWS_POWER_USER_MCP_HTTP_TOKEN");
            app.Use(async (context, next) =>
            {
                if (!context.Request.Path.StartsWithSegments("/mcp"))
                {
                    await next(context).ConfigureAwait(false);
                    return;
                }

                if (string.IsNullOrWhiteSpace(token))
                {
                    context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
                    await context.Response.WriteAsync("Set WINDOWS_POWER_USER_MCP_HTTP_TOKEN before enabling authenticated HTTP MCP.").ConfigureAwait(false);
                    return;
                }

                if (!context.Request.Headers.Authorization.ToString().Equals($"Bearer {token}", StringComparison.Ordinal))
                {
                    context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    await context.Response.WriteAsync("Missing or invalid bearer token.").ConfigureAwait(false);
                    return;
                }

                await next(context).ConfigureAwait(false);
            });
        }

        app.MapMcp("/mcp");
        await app.RunAsync().ConfigureAwait(false);
        return 0;
    }

    private static async Task<int> RunDesktopAgentLauncherAsync()
    {
        var manager = new AppManager();
        var result = await manager.StartDesktopAgentAsync().ConfigureAwait(false);
        WriteJson(result);
        return result.Success ? 0 : 1;
    }

    private static async Task<int> RunInstallAsync(AppArguments parsed)
    {
        var manager = new AppManager();
        var settings = await manager.LoadSettingsAsync().ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(parsed.InstallRoot))
        {
            settings = settings with { InstallRoot = parsed.InstallRoot };
            await manager.SaveSettingsAsync(settings).ConfigureAwait(false);
        }

        Directory.CreateDirectory(settings.InstallRoot);
        await manager.CreateInstallStateAsync(settings, ["app"]).ConfigureAwait(false);
        if (parsed.Service || parsed.ServiceOnly)
        {
            WriteJson(await manager.InstallBrokerServiceAsync(settings).ConfigureAwait(false));
        }

        if (parsed.TrayOnly || parsed.Start)
        {
            WriteJson(await manager.InstallTrayAutostartAsync(settings).ConfigureAwait(false));
        }

        WriteJson(new { success = true, install_root = settings.InstallRoot, executable = AppManager.ResolveCurrentExecutablePath() });
        return 0;
    }

    private static async Task<int> RunUninstallAsync()
    {
        var manager = new AppManager();
        WriteJson(await manager.UninstallTrayAutostartAsync().ConfigureAwait(false));
        if (AppManager.IsElevated())
        {
            WriteJson(await manager.UninstallBrokerServiceAsync().ConfigureAwait(false));
        }
        else
        {
            WriteJson(new { success = true, message = "Service uninstall skipped because this session is not elevated. User data is preserved." });
        }

        return 0;
    }

    private static async Task<int> RunUpdateAsync()
    {
        var manager = new AppManager();
        var settings = await manager.LoadSettingsAsync().ConfigureAwait(false);
        var result = await manager.CheckForUpdatesAsync(settings).ConfigureAwait(false);
        WriteJson(result);
        return result.Success ? 0 : 1;
    }

    private static async Task<int> RunApplyUpdateAsync(AppArguments parsed)
    {
        var manager = new AppManager();
        var settings = await manager.LoadSettingsAsync().ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(parsed.StagingPath))
        {
            WriteJson(new { success = false, code = "missing_staging", message = "--apply-update requires --staging <path>." });
            return 1;
        }

        var result = await manager.ApplyStagedUpdateAsync(parsed.StagingPath, parsed.InstallRoot ?? settings.InstallRoot).ConfigureAwait(false);
        WriteJson(result);
        return result.Success ? 0 : 1;
    }

    private static WindowsPowerUserMcpOptions BuildOptions(Microsoft.Extensions.Configuration.IConfiguration configuration, AppSettings settings)
    {
        var options = new WindowsPowerUserMcpOptions();
        configuration.Bind(options);
        options.IpcPipeName = configuration["IpcPipeName"] ?? settings.PipeName;
        options.DataRoot = settings.DataRoot;
        options.ServiceDataRoot = settings.ServiceDataRoot;
        options.LocalHttpPort = settings.HttpPort;
        options.LocalHttpEnabled = settings.LocalHttpEnabled;
        options.OptionalHttpAuthRequired = settings.RequireHttpToken;
        options.IpcAllowedUserSids = settings.ServiceAllowedUserSids;
        options.IpcAllowBuiltinAdministrators = settings.AllowBuiltinAdministratorsForServicePipe;
        return options;
    }

    private static void WriteJson<T>(T value)
    {
        try
        {
            Console.WriteLine(JsonSerializer.Serialize(value, JsonDefaults.Options));
        }
        catch
        {
            // WinExe processes may have no attached console in dashboard launches.
        }
    }
}

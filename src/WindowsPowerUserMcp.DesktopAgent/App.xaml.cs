using System.IO;
using System.Windows;
using WindowsPowerUserMcp.Core;
using WindowsPowerUserMcp.Tray;

namespace WindowsPowerUserMcp.DesktopAgent;

public partial class App : System.Windows.Application
{
    private TrayController? _tray;
    private CancellationTokenSource? _serverCts;
    private MainWindow? _mainWindow;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var options = new WindowsPowerUserMcpOptions();
        var layout = PlatformPaths.CreateLayout(options);
        var brokerProject = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "WindowsPowerUserMcp.BrokerService", "WindowsPowerUserMcp.BrokerService.csproj"));

        _mainWindow = new MainWindow(layout);
        _tray = new TrayController(() =>
        {
            _mainWindow.Show();
            _mainWindow.Activate();
        }, brokerProject, Shutdown);

        var ui = new DesktopUiAutomationService(layout);
        var server = new DesktopAgentPipeServer(options.IpcPipeName + ".Desktop", ui, _tray, _mainWindow);
        _serverCts = new CancellationTokenSource();
        _ = server.RunAsync(_serverCts.Token);
        _tray.SetStatus(TrayBackendStatus.Running, "DesktopAgent UI pipe ready");
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _serverCts?.Cancel();
        _tray?.Dispose();
        base.OnExit(e);
    }
}

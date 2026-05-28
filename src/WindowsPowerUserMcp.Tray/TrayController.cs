using System.Diagnostics;
using System.Windows.Forms;
using WindowsPowerUserMcp.Core;

namespace WindowsPowerUserMcp.Tray;

public sealed class TrayController : IDisposable
{
    private readonly NotifyIcon _notifyIcon;
    private readonly ToolStripMenuItem _statusItem;
    private readonly Action _showDashboard;
    private readonly Action _exit;
    private readonly string _brokerProjectPath;
    private Process? _brokerProcess;

    public TrayController(Action showDashboard, string brokerProjectPath, Action? exit = null)
    {
        _showDashboard = showDashboard;
        _exit = exit ?? (() => Environment.Exit(0));
        _brokerProjectPath = brokerProjectPath;
        _statusItem = new ToolStripMenuItem("Status: starting") { Enabled = false };
        _notifyIcon = new NotifyIcon
        {
            Icon = System.Drawing.SystemIcons.Application,
            Text = "WindowsPowerUserMcp",
            Visible = true,
            ContextMenuStrip = BuildMenu()
        };
        SetStatus(TrayBackendStatus.Starting, "DesktopAgent starting");
    }

    public void SetStatus(TrayBackendStatus status, string? detail = null)
    {
        var text = $"Status: {status}" + (string.IsNullOrWhiteSpace(detail) ? string.Empty : $" - {detail}");
        _statusItem.Text = text.Length > 120 ? text[..120] : text;
        _notifyIcon.Text = text.Length > 63 ? text[..63] : text;
    }

    public void Dispose()
    {
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _brokerProcess?.Dispose();
    }

    private ContextMenuStrip BuildMenu()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add(_statusItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Start Backend", null, (_, _) => StartBackend());
        menu.Items.Add("Stop Backend", null, (_, _) => StopBackend());
        menu.Items.Add("Restart Backend", null, (_, _) => RestartBackend());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Open Dashboard", null, (_, _) => _showDashboard());
        menu.Items.Add("View Logs", null, (_, _) => OpenFolder(Path.Combine(PlatformPaths.LocalDataRoot, "logs")));
        menu.Items.Add("Pending Tasks", null, (_, _) => _showDashboard());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit Tray Agent", null, (_, _) => _exit());
        return menu;
    }

    private void StartBackend()
    {
        if (_brokerProcess is { HasExited: false })
        {
            SetStatus(TrayBackendStatus.Running, "backend already started by tray");
            return;
        }

        try
        {
            if (File.Exists(_brokerProjectPath))
            {
                _brokerProcess = Process.Start(new ProcessStartInfo
                {
                    FileName = "dotnet",
                    Arguments = $"run --project \"{_brokerProjectPath}\"",
                    UseShellExecute = true,
                    WindowStyle = ProcessWindowStyle.Hidden
                });
            }
            else
            {
                var exe = Path.Combine(AppContext.BaseDirectory, "WindowsPowerUserMcp.BrokerService.exe");
                _brokerProcess = Process.Start(new ProcessStartInfo
                {
                    FileName = exe,
                    UseShellExecute = true,
                    WindowStyle = ProcessWindowStyle.Hidden
                });
            }

            SetStatus(TrayBackendStatus.Starting, "backend launch requested");
        }
        catch (Exception ex)
        {
            SetStatus(TrayBackendStatus.Error, ex.Message);
        }
    }

    private void StopBackend()
    {
        try
        {
            if (_brokerProcess is { HasExited: false })
            {
                _brokerProcess.Kill(entireProcessTree: true);
                SetStatus(TrayBackendStatus.Stopped, "backend stopped by tray");
            }
            else
            {
                SetStatus(TrayBackendStatus.Stopped, "no tray-started backend");
            }
        }
        catch (Exception ex)
        {
            SetStatus(TrayBackendStatus.Error, ex.Message);
        }
    }

    private void RestartBackend()
    {
        StopBackend();
        StartBackend();
    }

    private static void OpenFolder(string folder)
    {
        Directory.CreateDirectory(folder);
        Process.Start(new ProcessStartInfo { FileName = folder, UseShellExecute = true });
    }
}

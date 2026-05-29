using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using WindowsPowerUserMcp.AppManagement;
using WindowsPowerUserMcp.Core;
using Forms = System.Windows.Forms;
using WpfComboBox = System.Windows.Controls.ComboBox;

namespace WindowsPowerUserMcp.App;

public partial class MainWindow : Window
{
    private static readonly JsonSerializerOptions PrettyJson = new(JsonDefaults.Options) { WriteIndented = true };

    private readonly AppManager _manager;
    private readonly DispatcherTimer _refreshTimer = new() { Interval = TimeSpan.FromSeconds(8) };
    private Forms.NotifyIcon? _notifyIcon;
    private AppSettings _settings;
    private bool _explicitExit;
    private UpdateRelease? _latestRelease;
    private string? _stagedUpdatePath;

    public MainWindow() : this(new AppManager(), new AppSettings())
    {
    }

    public MainWindow(AppManager manager, AppSettings settings)
    {
        _manager = manager;
        _settings = settings;
        InitializeComponent();
        InitializeTray();
        PopulateSettings();
        PopulateStaticText();
        _refreshTimer.Tick += async (_, _) => await RefreshHealthAsync();
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        await RefreshHealthAsync();
        await RefreshLogsAsync();
        await RefreshTasksAndProcessesAsync();
        _refreshTimer.Start();
        if (_settings.StartMinimizedToTray || WindowState == WindowState.Minimized)
        {
            Hide();
        }

        if (_settings.CheckForUpdatesOnStartup)
        {
            _ = CheckUpdatesQuietlyAsync();
        }
    }

    private void Window_StateChanged(object? sender, EventArgs e)
    {
        if (WindowState == WindowState.Minimized && _settings.StartMinimizedToTray)
        {
            Hide();
        }
    }

    private void Window_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (!_explicitExit && _settings.MinimizeOnClose)
        {
            e.Cancel = true;
            Hide();
            ShowBalloon("Dashboard hidden", "WindowsPowerUserMcp is still available from the notification area.", Forms.ToolTipIcon.Info);
            return;
        }

        _notifyIcon?.Dispose();
    }

    private void InitializeTray()
    {
        _notifyIcon = new Forms.NotifyIcon
        {
            Icon = System.Drawing.SystemIcons.Application,
            Text = "WindowsPowerUserMcp - Status unknown",
            Visible = true,
            ContextMenuStrip = BuildTrayMenu()
        };
        _notifyIcon.DoubleClick += (_, _) => ShowDashboard();
    }

    private Forms.ContextMenuStrip BuildTrayMenu()
    {
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Open Dashboard", null, (_, _) => Dispatcher.Invoke(ShowDashboard));
        menu.Items.Add("Start Backend", null, async (_, _) => await RunUiOperationAsync("Starting backend...", async ct => await _manager.StartBrokerUserModeAsync(_settings, ct)));
        menu.Items.Add("Start Backend Elevated", null, async (_, _) => await RunUiOperationAsync("Requesting elevation...", async ct => await _manager.StartBrokerElevatedAsync(ct)));
        menu.Items.Add("Stop Backend", null, async (_, _) => await RunUiOperationAsync("Stopping backend...", async ct => await _manager.StopBrokerUserModeAsync(_settings, ct)));
        menu.Items.Add("Restart Backend", null, async (_, _) => await RunUiOperationAsync("Restarting backend...", async ct => await _manager.RestartBrokerUserModeAsync(_settings, ct)));
        menu.Items.Add("Install/Update", null, (_, _) => Dispatcher.Invoke(() => ShowDashboard()));
        menu.Items.Add("Open Logs", null, (_, _) => Dispatcher.Invoke(OpenLogsFolder));
        menu.Items.Add("Open Data Folder", null, (_, _) => Dispatcher.Invoke(OpenDataFolder));
        menu.Items.Add("Diagnostics", null, async (_, _) => await Dispatcher.InvokeAsync(async () => await RunDiagnosticsAsync()));
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Exit Dashboard", null, async (_, _) => await Dispatcher.InvokeAsync(async () => await ExitWithChoiceAsync(ExitChoice.ExitOnly)));
        menu.Items.Add("Exit and Stop Backend", null, async (_, _) => await Dispatcher.InvokeAsync(async () => await ExitWithChoiceAsync(ExitChoice.StopUserBackend)));
        return menu;
    }

    private void PopulateStaticText()
    {
        VersionText.Text = $"Version: {VersionInfo.CurrentVersion}  Build: {VersionInfo.BuildDateUtc}";
        AboutText.Text =
            $"Version {VersionInfo.CurrentVersion}\n" +
            "Target framework: net10.0 / net10.0-windows\n" +
            "UI shell: WPF, selected after Qt 6 audit for stable .NET 10 build, tray support, and single-file publishing.\n" +
            "Safety boundary: no UAC bypass, credential dumping, cookie extraction, MFA bypass, stealth persistence, or security evasion.";
        CodexConfigText.Text = IsDevelopmentRun()
            ? _manager.GetDevelopmentCodexConfig(FindRepoRoot() ?? Environment.CurrentDirectory)
            : _manager.GetInstalledCodexConfig(_settings);
    }

    private async Task RefreshHealthAsync()
    {
        await RunUiOperationAsync("Refreshing status...", async ct =>
        {
            _settings = await _manager.LoadSettingsAsync(ct);
            PopulateSettings();
            var result = await _manager.GetHealthAsync(_settings, ct);
            if (result.Data is { } health)
            {
                ApplyHealth(health);
            }

            return result;
        }, showMessage: false, refreshAfter: false);
    }

    private void ApplyHealth(HealthSnapshot health)
    {
        var brokerBrush = health.BrokerPipeHealthy ? "HealthyBrush" : health.BrokerStatus == "starting" ? "WarningBrush" : "StoppedBrush";
        SetStatus(BrokerStatusText, health.BrokerStatus, brokerBrush);
        BrokerHealthText.Text = health.BrokerPipeHealthy ? "Pipe healthy" : "Pipe unavailable";
        SetStatus(DesktopAgentStatusText, health.DesktopAgentPipeHealthy ? "Available" : "Unavailable", health.DesktopAgentPipeHealthy ? "HealthyBrush" : "StoppedBrush");
        UiAutomationSummaryText.Text = health.DesktopAgentPipeHealthy ? "Interactive UI automation pipe responded" : "Start DesktopAgent in the signed-in user session";
        SetStatus(CodexReadyText, health.BrokerPipeHealthy ? "Ready" : "Broker needed", health.BrokerPipeHealthy ? "HealthyBrush" : "WarningBrush");
        ToolCountText.Text = $"{ToolCatalog.All.Count} direct MCP tools available";
        SetStatus(InstallStatusText, health.InstallInspection.Mode, health.InstallInspection.Issues.Any(i => i.Severity == "error") ? "ErrorBrush" : health.InstallInspection.Issues.Any() ? "WarningBrush" : "HealthyBrush");
        HttpStatusText.Text = _settings.LocalHttpEnabled ? $"HTTP enabled on 127.0.0.1:{_settings.HttpPort}" : "HTTP disabled";
        AdminStatusText.Text = health.IsElevated ? "Elevation: administrator" : "Elevation: standard user";
        ActiveTaskCountText.Text = $"Active tasks: {health.ActiveTaskCount}";
        TrackedProcessCountText.Text = $"Tracked processes: {health.TrackedProcessCount}";
        InstallRootText.Text = health.InstallInspection.InstallRoot;
        DataRootText.Text = health.InstallInspection.DataRoot;
        LogRootText.Text = health.InstallInspection.LogRoot;
        BackendDetailsText.Text = ToPrettyJson(health.InstallInspection);
        DesktopAgentDetailsText.Text = ToPrettyJson(new
        {
            desktop_agent_pipe_healthy = health.DesktopAgentPipeHealthy,
            screenshot_folder = Path.Combine(_settings.DataRoot, "screenshots"),
            tray_autostart = health.InstallInspection.TrayTask,
            running_processes = health.InstallInspection.RunningProcesses.Where(p => p.ProcessName.Contains("DesktopAgent", StringComparison.OrdinalIgnoreCase))
        });
        DiagnosticsText.Text = ToPrettyJson(health);
        UpdateTrayTooltip(health.BrokerStatus);
        FooterStatusText.Text = $"Last refresh: {DateTime.Now:T}";
    }

    private async Task CheckUpdatesQuietlyAsync()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        var result = await _manager.CheckForUpdatesAsync(_settings, cts.Token);
        if (result.Success && result.Data?.UpdateAvailable == true)
        {
            _latestRelease = result.Data.LatestRelease;
            UpdateStatusText.Text = $"Update available: {_latestRelease?.Version}";
            ShowBalloon("Update available", $"WindowsPowerUserMcp {_latestRelease?.Version} is available.", Forms.ToolTipIcon.Info);
        }
    }

    private void PopulateSettings()
    {
        InstallRootBox.Text = _settings.InstallRoot;
        DataRootBox.Text = _settings.DataRoot;
        ServiceDataRootBox.Text = _settings.ServiceDataRoot;
        PipeNameBox.Text = _settings.PipeName;
        AllowedSidsBox.Text = string.Join(Environment.NewLine, _settings.ServiceAllowedUserSids);
        HttpPortBox.Text = _settings.HttpPort.ToString();
        AllowAdminsPipeCheck.IsChecked = _settings.AllowBuiltinAdministratorsForServicePipe;
        LocalHttpCheck.IsChecked = _settings.LocalHttpEnabled;
        RequireHttpTokenCheck.IsChecked = _settings.RequireHttpToken;
        StartMinimizedCheck.IsChecked = _settings.StartMinimizedToTray;
        MinimizeOnCloseCheck.IsChecked = _settings.MinimizeOnClose;
        CheckUpdatesStartupCheck.IsChecked = _settings.CheckForUpdatesOnStartup;
        IncludePrereleaseCheck.IsChecked = _settings.IncludePrereleaseUpdates;
        SelectComboValue(ThemeBox, _settings.Theme);
        SelectComboValue(LogLevelBox, _settings.LogLevel);
        CodexConfigText.Text = IsDevelopmentRun()
            ? _manager.GetDevelopmentCodexConfig(FindRepoRoot() ?? Environment.CurrentDirectory)
            : _manager.GetInstalledCodexConfig(_settings);
    }

    private async Task RunUiOperationAsync<T>(string busyText, Func<CancellationToken, Task<AppOperationResult<T>>> operation, bool showMessage = true, bool refreshAfter = true)
    {
        FooterStatusText.Text = busyText;
        Cursor = System.Windows.Input.Cursors.Wait;
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        try
        {
            var result = await operation(cts.Token);
            FooterStatusText.Text = result.Message;
            if (showMessage)
            {
                ShowBalloon(result.Success ? "Completed" : "Needs attention", result.Message, result.Success ? Forms.ToolTipIcon.Info : Forms.ToolTipIcon.Warning);
            }
        }
        catch (Exception ex)
        {
            FooterStatusText.Text = ex.Message;
            if (showMessage)
            {
                ShowBalloon("Error", ex.Message, Forms.ToolTipIcon.Error);
            }
        }
        finally
        {
            Cursor = null;
            if (refreshAfter)
            {
                await RefreshHealthAsync();
            }
        }
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e) => await RefreshHealthAsync();

    private async void StartBackend_Click(object sender, RoutedEventArgs e) =>
        await RunUiOperationAsync("Starting backend...", async ct => await _manager.StartBrokerUserModeAsync(_settings, ct));

    private async void StartElevated_Click(object sender, RoutedEventArgs e) =>
        await RunUiOperationAsync("Requesting elevation...", async ct => await _manager.StartBrokerElevatedAsync(ct));

    private async void StopBackend_Click(object sender, RoutedEventArgs e) =>
        await RunUiOperationAsync("Stopping backend...", async ct => await _manager.StopBrokerUserModeAsync(_settings, ct));

    private async void RestartBackend_Click(object sender, RoutedEventArgs e) =>
        await RunUiOperationAsync("Restarting backend...", async ct => await _manager.RestartBrokerUserModeAsync(_settings, ct));

    private async void InstallService_Click(object sender, RoutedEventArgs e)
    {
        if (!Confirm("Install the broker service?", "This uses normal Windows service installation and requires an elevated session."))
        {
            return;
        }

        await RunUiOperationAsync("Installing service...", async ct => await _manager.InstallBrokerServiceAsync(_settings, ct));
    }

    private async void UninstallService_Click(object sender, RoutedEventArgs e)
    {
        if (!Confirm("Uninstall the broker service?", "The service entry is removed. Data, logs, and tasks are preserved."))
        {
            return;
        }

        await RunUiOperationAsync("Uninstalling service...", async ct => await _manager.UninstallBrokerServiceAsync(ct));
    }

    private async void StartService_Click(object sender, RoutedEventArgs e) =>
        await RunUiOperationAsync("Starting service...", async ct => await _manager.StartServiceAsync(ct));

    private async void StopService_Click(object sender, RoutedEventArgs e) =>
        await RunUiOperationAsync("Stopping service...", async ct => await _manager.StopServiceAsync(ct));

    private async void RestartService_Click(object sender, RoutedEventArgs e) =>
        await RunUiOperationAsync("Restarting service...", async ct => await _manager.RestartServiceAsync(ct));

    private async void RepairService_Click(object sender, RoutedEventArgs e) =>
        await RunUiOperationAsync("Repairing service path...", async ct => await _manager.RepairServicePathAsync(_settings, ct));

    private async void TestBrokerPipe_Click(object sender, RoutedEventArgs e) =>
        await RunUiOperationAsync("Testing broker pipe...", async ct => await _manager.CheckBrokerPipeAsync(_settings, ct));

    private async void TestDesktopPipe_Click(object sender, RoutedEventArgs e) =>
        await RunUiOperationAsync("Testing DesktopAgent pipe...", async ct => await _manager.CheckDesktopAgentPipeAsync(_settings, ct));

    private async void TestStdio_Click(object sender, RoutedEventArgs e) =>
        await RunUiOperationAsync("Testing --stdio...", async ct => await _manager.TestStdioModeAsync(ct));

    private async void StartDesktopAgent_Click(object sender, RoutedEventArgs e) =>
        await RunUiOperationAsync("Starting DesktopAgent...", async ct => await _manager.StartDesktopAgentAsync(ct));

    private async void RestartDesktopAgent_Click(object sender, RoutedEventArgs e) =>
        await RunUiOperationAsync("Restarting DesktopAgent...", async ct => await _manager.RestartDesktopAgentAsync(_settings, ct));

    private async void StopDesktopAgent_Click(object sender, RoutedEventArgs e) =>
        await RunUiOperationAsync("Stopping DesktopAgent...", async ct => await _manager.StopDesktopAgentAsync(_settings, ct));

    private async void InstallTray_Click(object sender, RoutedEventArgs e) =>
        await RunUiOperationAsync("Installing tray autostart...", async ct => await _manager.InstallTrayAutostartAsync(_settings, ct));

    private async void UninstallTray_Click(object sender, RoutedEventArgs e) =>
        await RunUiOperationAsync("Removing tray autostart...", async ct => await _manager.UninstallTrayAutostartAsync(ct));

    private async void RepairTray_Click(object sender, RoutedEventArgs e) =>
        await RunUiOperationAsync("Repairing tray autostart...", async ct => await _manager.InstallTrayAutostartAsync(_settings, ct));

    private async void TakeScreenshot_Click(object sender, RoutedEventArgs e) =>
        await RunBrokerJsonAsync("ui_screenshot_desktop", new { });

    private async void RunUiSelfTest_Click(object sender, RoutedEventArgs e) =>
        await RunBrokerJsonAsync("ui_get_desktop_snapshot", new { });

    private async Task RunBrokerJsonAsync(string toolName, object args)
    {
        await RunUiOperationAsync($"Running {toolName}...", async ct =>
        {
            var result = await _manager.InvokeBrokerToolAsync(toolName, args, _settings, ct);
            DesktopAgentDetailsText.Text = result.Data.ValueKind == JsonValueKind.Undefined ? result.Message : ToPrettyJson(result.Data);
            return result;
        });
    }

    private void CopyCodexConfig_Click(object sender, RoutedEventArgs e)
    {
        Clipboard.SetText(CodexConfigText.Text);
        FooterStatusText.Text = "Codex config copied.";
    }

    private void OpenCodexFolder_Click(object sender, RoutedEventArgs e)
    {
        var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex");
        Directory.CreateDirectory(folder);
        OpenPath(folder);
    }

    private void OpenCodex_Click(object sender, RoutedEventArgs e) => OpenPath(Path.Combine(FindRepoRoot() ?? Environment.CurrentDirectory, "CODEX_CONFIG.md"));

    private async void RefreshToolCatalog_Click(object sender, RoutedEventArgs e)
    {
        await RunBrokerJsonAsync("tool_list", new { });
        ToolCountText.Text = $"{ToolCatalog.All.Count} direct MCP tools available";
    }

    private async void CheckUpdates_Click(object sender, RoutedEventArgs e)
    {
        await RunUiOperationAsync("Checking for updates...", async ct =>
        {
            var result = await _manager.CheckForUpdatesAsync(_settings, ct);
            _latestRelease = result.Data?.LatestRelease;
            UpdateDetailsText.Text = ToPrettyJson(result);
            UpdateStatusText.Text = result.Data?.Message ?? result.Message;
            return result;
        });
    }

    private async void DownloadUpdate_Click(object sender, RoutedEventArgs e)
    {
        if (_latestRelease is null)
        {
            await RunUiOperationAsync("Checking for updates...", async ct =>
            {
                var result = await _manager.CheckForUpdatesAsync(_settings, ct);
                _latestRelease = result.Data?.LatestRelease;
                return result;
            });
        }

        var asset = _latestRelease?.Assets.FirstOrDefault(a => a.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                    ?? _latestRelease?.Assets.FirstOrDefault();
        if (asset is null)
        {
            UpdateDetailsText.Text = "No downloadable release asset was found. Publish a zip or exe asset in GitHub Releases.";
            return;
        }

        await RunUiOperationAsync("Downloading update...", async ct =>
        {
            var result = await _manager.StageUpdateAsync(asset, ct);
            _stagedUpdatePath = result.Data;
            UpdateDetailsText.Text = ToPrettyJson(new { asset, staged_path = _stagedUpdatePath, result });
            return result;
        });
    }

    private async void ApplyUpdate_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_stagedUpdatePath))
        {
            MessageBox.Show("Download a release asset first.", ProductConstants.ProductName, MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (!Confirm("Apply staged update?", "Managed backend processes should be stopped first. Data, logs, and task state are preserved."))
        {
            return;
        }

        await RunUiOperationAsync("Applying update...", async ct => await _manager.ApplyStagedUpdateAsync(_stagedUpdatePath, _settings.InstallRoot, ct));
    }

    private async void InstallToDefault_Click(object sender, RoutedEventArgs e)
    {
        _settings = _settings with { InstallRoot = ProductConstants.DefaultInstallRoot };
        await _manager.SaveSettingsAsync(_settings);
        await _manager.CreateInstallStateAsync(_settings, ["app"]);
        PopulateSettings();
        await RefreshHealthAsync();
    }

    private async void ChooseInstallRoot_Click(object sender, RoutedEventArgs e)
    {
        using var dialog = new Forms.FolderBrowserDialog
        {
            Description = "Choose WindowsPowerUserMcp install root",
            SelectedPath = _settings.InstallRoot,
            UseDescriptionForTitle = true
        };
        if (dialog.ShowDialog() == Forms.DialogResult.OK)
        {
            _settings = _settings with { InstallRoot = dialog.SelectedPath };
            await _manager.SaveSettingsAsync(_settings);
            await _manager.CreateInstallStateAsync(_settings, ["app"]);
            PopulateSettings();
            await RefreshHealthAsync();
        }
    }

    private async void SafeUninstall_Click(object sender, RoutedEventArgs e)
    {
        var deletesData = DeleteLogsCheck.IsChecked == true || DeleteScreenshotsCheck.IsChecked == true ||
                          DeleteTaskLedgerCheck.IsChecked == true || DeleteAllUserDataCheck.IsChecked == true;
        var message = deletesData
            ? "Selected user data will be deleted. This cannot be undone."
            : "Service and tray autostart are removed where possible. User data is preserved.";
        if (!Confirm("Run uninstall actions?", message))
        {
            return;
        }

        await RunUiOperationAsync("Removing tray autostart...", async ct => await _manager.UninstallTrayAutostartAsync(ct));
        if (AppManager.IsElevated())
        {
            await RunUiOperationAsync("Removing service...", async ct => await _manager.UninstallBrokerServiceAsync(ct));
        }

        if (deletesData)
        {
            DeleteSelectedData();
        }
    }

    private async void RefreshTasks_Click(object sender, RoutedEventArgs e) => await RefreshTasksAndProcessesAsync();

    private async void RefreshProcesses_Click(object sender, RoutedEventArgs e) => await RefreshTasksAndProcessesAsync();

    private async Task RefreshTasksAndProcessesAsync()
    {
        var tasks = await _manager.InvokeBrokerToolAsync("task_list_active", settings: _settings);
        var pending = await _manager.InvokeBrokerToolAsync("task_list_pending", settings: _settings);
        var processes = await _manager.InvokeBrokerToolAsync("process_list_tracked", settings: _settings);
        TasksText.Text = ToPrettyJson(new
        {
            active_tasks = tasks.Success ? tasks.Data : JsonDefaults.ToElement(tasks),
            pending = pending.Success ? pending.Data : JsonDefaults.ToElement(pending),
            tracked_processes = processes.Success ? processes.Data : JsonDefaults.ToElement(processes)
        });
    }

    private async void RefreshLogs_Click(object sender, RoutedEventArgs e) => await RefreshLogsAsync();

    private async Task RefreshLogsAsync()
    {
        var logRoot = Path.Combine(_settings.DataRoot, "logs");
        if (!Directory.Exists(logRoot))
        {
            LogsText.Text = "Log directory does not exist yet.";
            return;
        }

        var builder = new StringBuilder();
        foreach (var file in Directory.EnumerateFiles(logRoot, "*.*", SearchOption.TopDirectoryOnly).OrderByDescending(File.GetLastWriteTimeUtc).Take(8))
        {
            builder.AppendLine($"--- {Path.GetFileName(file)} ({new FileInfo(file).Length:N0} bytes) ---");
            builder.AppendLine(await ReadTailAsync(file, 8 * 1024));
            builder.AppendLine();
        }

        LogsText.Text = builder.ToString();
    }

    private async void RunDiagnostics_Click(object sender, RoutedEventArgs e) => await RunDiagnosticsAsync();

    private async Task RunDiagnosticsAsync()
    {
        await RunUiOperationAsync("Running diagnostics...", async ct =>
        {
            var result = await _manager.GetHealthAsync(_settings, ct);
            DiagnosticsText.Text = ToPrettyJson(result);
            return result;
        }, showMessage: false);
    }

    private async void ExportDiagnostics_Click(object sender, RoutedEventArgs e)
    {
        await RunUiOperationAsync("Exporting diagnostic bundle...", async ct =>
        {
            var result = await _manager.ExportDiagnosticBundleAsync(_settings, ct);
            DiagnosticsText.Text = ToPrettyJson(result);
            if (result.Success && result.Data is not null)
            {
                OpenPath(Path.GetDirectoryName(result.Data.Path)!);
            }

            return result;
        });
    }

    private async void SaveSettings_Click(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(HttpPortBox.Text, out var port))
        {
            MessageBox.Show("HTTP port must be a number.", ProductConstants.ProductName, MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        _settings = _settings with
        {
            InstallRoot = InstallRootBox.Text,
            DataRoot = DataRootBox.Text,
            ServiceDataRoot = ServiceDataRootBox.Text,
            PipeName = PipeNameBox.Text,
            ServiceAllowedUserSids = AllowedSidsBox.Text.Split(['\r', '\n', ';', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
            HttpPort = port,
            AllowBuiltinAdministratorsForServicePipe = AllowAdminsPipeCheck.IsChecked == true,
            LocalHttpEnabled = LocalHttpCheck.IsChecked == true,
            RequireHttpToken = RequireHttpTokenCheck.IsChecked == true,
            StartMinimizedToTray = StartMinimizedCheck.IsChecked == true,
            MinimizeOnClose = MinimizeOnCloseCheck.IsChecked == true,
            CheckForUpdatesOnStartup = CheckUpdatesStartupCheck.IsChecked == true,
            IncludePrereleaseUpdates = IncludePrereleaseCheck.IsChecked == true,
            Theme = ComboText(ThemeBox),
            LogLevel = ComboText(LogLevelBox)
        };
        await _manager.SaveSettingsAsync(_settings);
        PopulateSettings();
        await RefreshHealthAsync();
    }

    private async void ReloadSettings_Click(object sender, RoutedEventArgs e)
    {
        _settings = await _manager.LoadSettingsAsync();
        PopulateSettings();
        await RefreshHealthAsync();
    }

    private void OpenLogs_Click(object sender, RoutedEventArgs e) => OpenLogsFolder();

    private void OpenData_Click(object sender, RoutedEventArgs e) => OpenDataFolder();

    private void OpenScreenshots_Click(object sender, RoutedEventArgs e) => OpenPath(Path.Combine(_settings.DataRoot, "screenshots"), createDirectory: true);

    private void OpenTroubleshooting_Click(object sender, RoutedEventArgs e) => OpenPath(Path.Combine(FindRepoRoot() ?? Environment.CurrentDirectory, "TROUBLESHOOTING.md"));

    private async void ExitDashboard_Click(object sender, RoutedEventArgs e) => await ExitWithChoiceAsync(null);

    private async Task ExitWithChoiceAsync(ExitChoice? preselected)
    {
        var choice = preselected ?? ShowExitChoiceDialog();
        if (choice == ExitChoice.Cancel)
        {
            return;
        }

        if (choice == ExitChoice.StopUserBackend)
        {
            await _manager.StopBrokerUserModeAsync(_settings);
        }
        else if (choice == ExitChoice.StopService)
        {
            if (AppManager.IsElevated())
            {
                await _manager.StopServiceAsync();
            }
            else
            {
                MessageBox.Show("Stopping the service requires an elevated dashboard session.", ProductConstants.ProductName, MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
        }

        _explicitExit = true;
        _notifyIcon?.Dispose();
        Close();
    }

    private ExitChoice ShowExitChoiceDialog()
    {
        var dialog = new Window
        {
            Owner = this,
            Title = "Exit WindowsPowerUserMcp",
            Width = 480,
            Height = 270,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.NoResize,
            Background = (Brush)FindResource("WindowBackgroundBrush")
        };
        var panel = new StackPanel { Margin = new Thickness(20) };
        panel.Children.Add(new TextBlock { Text = "Choose what should happen when the dashboard exits.", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 12) });
        var combo = new ComboBox { Margin = new Thickness(0, 0, 0, 16) };
        combo.Items.Add("Exit dashboard only");
        combo.Items.Add("Leave backend running");
        combo.Items.Add("Stop user-mode backend");
        combo.Items.Add("Stop broker service");
        combo.SelectedIndex = 0;
        panel.Children.Add(combo);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var ok = new Button { Content = "Exit", IsDefault = true, Width = 88 };
        var cancel = new Button { Content = "Cancel", IsCancel = true, Width = 88 };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);
        panel.Children.Add(buttons);
        dialog.Content = panel;
        var choice = ExitChoice.Cancel;
        ok.Click += (_, _) =>
        {
            choice = combo.SelectedIndex switch
            {
                2 => ExitChoice.StopUserBackend,
                3 => ExitChoice.StopService,
                _ => ExitChoice.ExitOnly
            };
            dialog.DialogResult = true;
            dialog.Close();
        };
        dialog.ShowDialog();
        return choice;
    }

    private void ShowDashboard()
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
    }

    private void OpenLogsFolder() => OpenPath(Path.Combine(_settings.DataRoot, "logs"), createDirectory: true);

    private void OpenDataFolder() => OpenPath(_settings.DataRoot, createDirectory: true);

    private static void OpenPath(string path, bool createDirectory = false)
    {
        if (createDirectory)
        {
            Directory.CreateDirectory(path);
        }

        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
    }

    private void DeleteSelectedData()
    {
        if (DeleteAllUserDataCheck.IsChecked == true)
        {
            TryDeleteDirectory(_settings.DataRoot);
            return;
        }

        if (DeleteLogsCheck.IsChecked == true) TryDeleteDirectory(Path.Combine(_settings.DataRoot, "logs"));
        if (DeleteScreenshotsCheck.IsChecked == true) TryDeleteDirectory(Path.Combine(_settings.DataRoot, "screenshots"));
        if (DeleteTaskLedgerCheck.IsChecked == true) TryDeleteDirectory(Path.Combine(_settings.DataRoot, "db"));
    }

    private static void TryDeleteDirectory(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }

    private static async Task<string> ReadTailAsync(string path, int maxBytes)
    {
        await using var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        var start = Math.Max(0, stream.Length - maxBytes);
        stream.Seek(start, SeekOrigin.Begin);
        using var reader = new StreamReader(stream);
        return await reader.ReadToEndAsync();
    }

    private void SetStatus(TextBlock block, string text, string brushKey)
    {
        block.Text = text;
        block.Foreground = (Brush)FindResource(brushKey);
    }

    private void UpdateTrayTooltip(string brokerStatus)
    {
        if (_notifyIcon is null) return;
        var text = $"WindowsPowerUserMcp - Broker {brokerStatus}";
        _notifyIcon.Text = text.Length > 63 ? text[..63] : text;
    }

    private void ShowBalloon(string title, string text, Forms.ToolTipIcon icon)
    {
        if (_notifyIcon is not null)
        {
            _notifyIcon.ShowBalloonTip(2500, title, text, icon);
        }
    }

    private bool Confirm(string title, string message) =>
        MessageBox.Show(message, title, MessageBoxButton.OKCancel, MessageBoxImage.Warning) == MessageBoxResult.OK;

    private static string ToPrettyJson<T>(T value) => JsonSerializer.Serialize(value, PrettyJson);

    private static string ComboText(WpfComboBox combo) =>
        (combo.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? combo.Text;

    private static void SelectComboValue(WpfComboBox combo, string value)
    {
        for (var i = 0; i < combo.Items.Count; i++)
        {
            if ((combo.Items[i] as ComboBoxItem)?.Content?.ToString()?.Equals(value, StringComparison.OrdinalIgnoreCase) == true)
            {
                combo.SelectedIndex = i;
                return;
            }
        }
    }

    private static bool IsDevelopmentRun() => AppManager.ResolveCurrentExecutablePath().Contains(@"\src\", StringComparison.OrdinalIgnoreCase);

    private static string? FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "WindowsPowerUserMcp.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        return null;
    }

    private enum ExitChoice
    {
        Cancel,
        ExitOnly,
        StopUserBackend,
        StopService
    }
}

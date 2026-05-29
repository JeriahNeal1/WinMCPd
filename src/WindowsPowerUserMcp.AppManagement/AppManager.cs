using System.Diagnostics;
using System.IO.Compression;
using System.Security.Principal;
using System.ServiceProcess;
using System.Text;
using System.Text.Json;
using Microsoft.Win32;
using WindowsPowerUserMcp.Core;
using WindowsPowerUserMcp.Security;

namespace WindowsPowerUserMcp.AppManagement;

public sealed class AppManager(
    AppSettingsStore? settingsStore = null,
    InstallStateStore? installStateStore = null,
    UpdateService? updateService = null)
{
    private readonly AppSettingsStore _settingsStore = settingsStore ?? new AppSettingsStore();
    private readonly InstallStateStore _installStateStore = installStateStore ?? new InstallStateStore();
    private readonly UpdateService _updateService = updateService ?? new UpdateService();
    private readonly SecretRedactor _redactor = new(new WindowsPowerUserMcpOptions());

    public async Task<AppSettings> LoadSettingsAsync(CancellationToken cancellationToken = default) =>
        await _settingsStore.LoadAsync(cancellationToken).ConfigureAwait(false);

    public async Task<AppOperationResult<AppSettings>> SaveSettingsAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        await _settingsStore.SaveAsync(settings, cancellationToken).ConfigureAwait(false);
        return AppOperationResult<AppSettings>.Ok(settings, "Settings saved.");
    }

    public async Task<AppOperationResult<InstallInspection>> InspectInstallAsync(AppSettings? settings = null, CancellationToken cancellationToken = default)
    {
        settings ??= await LoadSettingsAsync(cancellationToken).ConfigureAwait(false);
        var currentExecutable = ResolveCurrentExecutablePath();
        var state = await _installStateStore.LoadAsync(cancellationToken).ConfigureAwait(false);
        var service = await GetServiceStatusAsync(cancellationToken).ConfigureAwait(false);
        var task = await GetScheduledTaskAsync(cancellationToken).ConfigureAwait(false);
        var knownRoots = new[]
        {
            settings.InstallRoot,
            ProductConstants.DefaultInstallRoot,
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), ProductConstants.ProductName)
        };
        var processes = GetManagedProcesses(settings.InstallRoot, currentExecutable);
        var issues = InstallAnalyzer.DetectIssues(state, service, task, currentExecutable, knownRoots);
        var inspection = new InstallInspection(
            InstallAnalyzer.DetectRunMode(currentExecutable),
            currentExecutable,
            settings.InstallRoot,
            settings.DataRoot,
            Path.Combine(settings.DataRoot, "logs"),
            state,
            service,
            task,
            processes,
            issues);

        return AppOperationResult<InstallInspection>.Ok(inspection, "Install inspection complete.", riskLevel: RiskLevel.ReadOnly);
    }

    public async Task<AppOperationResult<HealthSnapshot>> GetHealthAsync(AppSettings? settings = null, CancellationToken cancellationToken = default)
    {
        settings ??= await LoadSettingsAsync(cancellationToken).ConfigureAwait(false);
        var inspection = (await InspectInstallAsync(settings, cancellationToken).ConfigureAwait(false)).Data!;
        var broker = await CheckPipeAsync(settings.PipeName, "broker_get_status", cancellationToken).ConfigureAwait(false);
        var desktop = await CheckPipeAsync(settings.PipeName + ".Desktop", "ui_get_foreground_window", cancellationToken).ConfigureAwait(false);
        var (activeTaskCount, trackedProcessCount) = await GetBrokerCountsAsync(settings, cancellationToken).ConfigureAwait(false);
        var brokerStatus = broker.Success
            ? inspection.Service is { Installed: true, Status: "Running" } ? "service-running" : "running"
            : inspection.RunningProcesses.Any(p => p.ProcessName.Contains("Broker", StringComparison.OrdinalIgnoreCase)) ? "starting" : "stopped";

        var snapshot = new HealthSnapshot(
            DateTimeOffset.UtcNow,
            IsElevated(),
            broker.Success,
            desktop.Success,
            brokerStatus,
            activeTaskCount,
            trackedProcessCount,
            inspection);
        return AppOperationResult<HealthSnapshot>.Ok(snapshot, "Health check complete.", riskLevel: RiskLevel.ReadOnly);
    }

    public async Task<AppOperationResult<int>> StartBrokerUserModeAsync(AppSettings? settings = null, CancellationToken cancellationToken = default)
    {
        settings ??= await LoadSettingsAsync(cancellationToken).ConfigureAwait(false);
        var running = GetManagedProcesses(settings.InstallRoot, ResolveCurrentExecutablePath())
            .FirstOrDefault(p => p.CommandLine?.Contains("--broker", StringComparison.OrdinalIgnoreCase) == true);
        if (running is not null)
        {
            return AppOperationResult<int>.Ok(running.ProcessId, "Broker is already running.");
        }

        var start = StartAppMode("--broker", useShellExecute: false, verb: null);
        return start.Success
            ? AppOperationResult<int>.Ok(start.Data, "Broker started.")
            : AppOperationResult<int>.Fail(start.Code, start.Message, RiskLevel.Medium, start.RecommendedActions);
    }

    public Task<AppOperationResult<int>> StartBrokerElevatedAsync(CancellationToken cancellationToken = default)
    {
        var start = StartAppMode("--broker", useShellExecute: true, verb: "runas");
        return Task.FromResult(start.Success
            ? AppOperationResult<int>.Ok(start.Data, "Windows UAC elevation was requested for the broker.", riskLevel: RiskLevel.High)
            : AppOperationResult<int>.Fail(start.Code, start.Message, RiskLevel.High, start.RecommendedActions));
    }

    public async Task<AppOperationResult<int>> StopBrokerUserModeAsync(AppSettings? settings = null, CancellationToken cancellationToken = default)
    {
        settings ??= await LoadSettingsAsync(cancellationToken).ConfigureAwait(false);
        var currentPid = Environment.ProcessId;
        var candidates = GetManagedProcesses(settings.InstallRoot, ResolveCurrentExecutablePath())
            .Where(p => p.ProcessId != currentPid && p.CommandLine?.Contains("--broker", StringComparison.OrdinalIgnoreCase) == true)
            .ToArray();
        var stopped = 0;
        foreach (var candidate in candidates)
        {
            if (!IsOwnedProductProcess(candidate, settings.InstallRoot))
            {
                continue;
            }

            try
            {
                using var process = Process.GetProcessById(candidate.ProcessId);
                process.CloseMainWindow();
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(TimeSpan.FromSeconds(5));
                try
                {
                    await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    process.Kill(entireProcessTree: true);
                    await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
                }

                stopped++;
            }
            catch
            {
                // The process may have exited after discovery.
            }
        }

        return AppOperationResult<int>.Ok(stopped, stopped == 0 ? "No owned user-mode broker processes were running." : $"Stopped {stopped} broker process(es).", riskLevel: RiskLevel.Destructive);
    }

    public async Task<AppOperationResult<int>> RestartBrokerUserModeAsync(AppSettings? settings = null, CancellationToken cancellationToken = default)
    {
        settings ??= await LoadSettingsAsync(cancellationToken).ConfigureAwait(false);
        await StopBrokerUserModeAsync(settings, cancellationToken).ConfigureAwait(false);
        return await StartBrokerUserModeAsync(settings, cancellationToken).ConfigureAwait(false);
    }

    public async Task<AppOperationResult<string>> InstallBrokerServiceAsync(AppSettings? settings = null, CancellationToken cancellationToken = default)
    {
        settings ??= await LoadSettingsAsync(cancellationToken).ConfigureAwait(false);
        if (!IsElevated())
        {
            return AppOperationResult<string>.Fail("not_elevated", "Service installation requires an elevated app session. Use Start Elevated or run the installer as administrator.", RiskLevel.High);
        }

        var executable = ResolveCurrentExecutablePath();
        var binPath = $"\"{executable}\" --broker --service";
        var existing = await GetServiceStatusAsync(cancellationToken).ConfigureAwait(false);
        if (existing.Installed)
        {
            return AppOperationResult<string>.Fail("service_exists", $"Service '{ProductConstants.BrokerServiceName}' is already installed.", RiskLevel.High, ["Use Repair Service to update the service path."]);
        }

        var create = await RunProcessAsync("sc.exe", $"create \"{ProductConstants.BrokerServiceName}\" binPath= \"{binPath}\" DisplayName= \"{ProductConstants.BrokerServiceDisplayName}\" start= delayed-auto", null, TimeSpan.FromSeconds(30), cancellationToken).ConfigureAwait(false);
        if (!create.Success)
        {
            return AppOperationResult<string>.Fail("service_create_failed", create.Message, RiskLevel.High);
        }

        await RunProcessAsync("sc.exe", $"description \"{ProductConstants.BrokerServiceName}\" \"Owner-authorized local broker for WindowsPowerUserMcp.\"", null, TimeSpan.FromSeconds(15), cancellationToken).ConfigureAwait(false);
        var state = await CreateInstallStateAsync(settings, ["app", "broker-service"], cancellationToken).ConfigureAwait(false);
        return AppOperationResult<string>.Ok(state.ExecutablePath, "Broker service installed. Start it from the dashboard or Services.", riskLevel: RiskLevel.High);
    }

    public async Task<AppOperationResult<string>> UninstallBrokerServiceAsync(CancellationToken cancellationToken = default)
    {
        if (!IsElevated())
        {
            return AppOperationResult<string>.Fail("not_elevated", "Service uninstall requires an elevated app session.", RiskLevel.High);
        }

        var service = await GetServiceStatusAsync(cancellationToken).ConfigureAwait(false);
        if (!service.Installed)
        {
            return AppOperationResult<string>.Ok(ProductConstants.BrokerServiceName, "Broker service is not installed.", riskLevel: RiskLevel.Destructive);
        }

        if (string.Equals(service.Status, "Running", StringComparison.OrdinalIgnoreCase))
        {
            await StopServiceAsync(cancellationToken).ConfigureAwait(false);
        }

        var delete = await RunProcessAsync("sc.exe", $"delete \"{ProductConstants.BrokerServiceName}\"", null, TimeSpan.FromSeconds(30), cancellationToken).ConfigureAwait(false);
        return delete.Success
            ? AppOperationResult<string>.Ok(ProductConstants.BrokerServiceName, "Broker service uninstalled.", riskLevel: RiskLevel.Destructive)
            : AppOperationResult<string>.Fail("service_delete_failed", delete.Message, RiskLevel.Destructive);
    }

    public Task<AppOperationResult<string>> StartServiceAsync(CancellationToken cancellationToken = default) =>
        ControlServiceAsync("start", RiskLevel.High, cancellationToken);

    public Task<AppOperationResult<string>> StopServiceAsync(CancellationToken cancellationToken = default) =>
        ControlServiceAsync("stop", RiskLevel.Destructive, cancellationToken);

    public async Task<AppOperationResult<string>> RestartServiceAsync(CancellationToken cancellationToken = default)
    {
        var stop = await StopServiceAsync(cancellationToken).ConfigureAwait(false);
        if (!stop.Success && stop.Code != "service_not_running")
        {
            return stop;
        }

        return await StartServiceAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<AppOperationResult<string>> RepairServicePathAsync(AppSettings? settings = null, CancellationToken cancellationToken = default)
    {
        settings ??= await LoadSettingsAsync(cancellationToken).ConfigureAwait(false);
        if (!IsElevated())
        {
            return AppOperationResult<string>.Fail("not_elevated", "Repairing the service path requires elevation.", RiskLevel.High);
        }

        var service = await GetServiceStatusAsync(cancellationToken).ConfigureAwait(false);
        if (!service.Installed)
        {
            return AppOperationResult<string>.Fail("service_missing", "Broker service is not installed.", RiskLevel.High);
        }

        var binPath = $"\"{ResolveCurrentExecutablePath()}\" --broker --service";
        var config = await RunProcessAsync("sc.exe", $"config \"{ProductConstants.BrokerServiceName}\" binPath= \"{binPath}\" start= delayed-auto", null, TimeSpan.FromSeconds(30), cancellationToken).ConfigureAwait(false);
        return config.Success
            ? AppOperationResult<string>.Ok(binPath, "Service path repaired.", riskLevel: RiskLevel.High)
            : AppOperationResult<string>.Fail("service_repair_failed", config.Message, RiskLevel.High);
    }

    public async Task<AppOperationResult<string>> InstallTrayAutostartAsync(AppSettings? settings = null, CancellationToken cancellationToken = default)
    {
        settings ??= await LoadSettingsAsync(cancellationToken).ConfigureAwait(false);
        var executable = ResolveCurrentExecutablePath();
        var action = $"\"{executable}\" --dashboard";
        var result = await RunProcessAsync("schtasks.exe", $"/Create /TN \"{ProductConstants.TrayTaskName}\" /SC ONLOGON /TR \"{action}\" /RL LIMITED /F", null, TimeSpan.FromSeconds(30), cancellationToken).ConfigureAwait(false);
        if (!result.Success)
        {
            return AppOperationResult<string>.Fail("tray_autostart_failed", result.Message, RiskLevel.Medium);
        }

        var updatedSettings = settings with { AutoStartTrayAtLogin = true };
        await SaveSettingsAsync(updatedSettings, cancellationToken).ConfigureAwait(false);
        await CreateInstallStateAsync(updatedSettings, ["app", "tray-autostart"], cancellationToken).ConfigureAwait(false);
        return AppOperationResult<string>.Ok(action, "Tray autostart installed.", riskLevel: RiskLevel.Medium);
    }

    public async Task<AppOperationResult<string>> UninstallTrayAutostartAsync(CancellationToken cancellationToken = default)
    {
        var result = await RunProcessAsync("schtasks.exe", $"/Delete /TN \"{ProductConstants.TrayTaskName}\" /F", null, TimeSpan.FromSeconds(30), cancellationToken).ConfigureAwait(false);
        if (!result.Success && !result.Message.Contains("cannot find", StringComparison.OrdinalIgnoreCase))
        {
            return AppOperationResult<string>.Fail("tray_autostart_remove_failed", result.Message, RiskLevel.Destructive);
        }

        var settings = await LoadSettingsAsync(cancellationToken).ConfigureAwait(false);
        await SaveSettingsAsync(settings with { AutoStartTrayAtLogin = false }, cancellationToken).ConfigureAwait(false);
        return AppOperationResult<string>.Ok(ProductConstants.TrayTaskName, "Tray autostart removed.", riskLevel: RiskLevel.Destructive);
    }

    public async Task<AppOperationResult<string>> TestStdioModeAsync(CancellationToken cancellationToken = default)
    {
        var executable = ResolveCurrentExecutablePath();
        var result = await RunProcessAsync(executable, "--stdio", null, TimeSpan.FromSeconds(2), cancellationToken, killOnTimeout: true).ConfigureAwait(false);
        if (!result.Success && result.Code == "process_timeout")
        {
            return AppOperationResult<string>.Ok(executable, "--stdio started and stayed alive long enough for MCP startup.", riskLevel: RiskLevel.ReadOnly);
        }

        return result.Success
            ? AppOperationResult<string>.Ok(executable, "--stdio exited cleanly during smoke test.", riskLevel: RiskLevel.ReadOnly)
            : AppOperationResult<string>.Fail("stdio_test_failed", result.Message, RiskLevel.Low);
    }

    public async Task<AppOperationResult<int>> StartDesktopAgentAsync(CancellationToken cancellationToken = default)
    {
        var existing = GetManagedProcesses(ProductConstants.DefaultInstallRoot, ResolveCurrentExecutablePath())
            .FirstOrDefault(p => p.ProcessName.Equals("WindowsPowerUserMcp.DesktopAgent", StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            return AppOperationResult<int>.Ok(existing.ProcessId, "DesktopAgent is already running.");
        }

        var executable = ResolveSiblingExecutable("WindowsPowerUserMcp.DesktopAgent.exe");
        if (executable is null)
        {
            return AppOperationResult<int>.Fail("desktop_agent_missing", "DesktopAgent executable was not found next to the app. Build or publish the DesktopAgent project.", RiskLevel.Medium);
        }

        try
        {
            var process = Process.Start(new ProcessStartInfo
            {
                FileName = executable,
                UseShellExecute = true,
                WindowStyle = ProcessWindowStyle.Normal
            });
            return process is null
                ? AppOperationResult<int>.Fail("desktop_agent_start_failed", "Process.Start returned null.", RiskLevel.Medium)
                : AppOperationResult<int>.Ok(process.Id, "DesktopAgent started.");
        }
        catch (Exception ex)
        {
            return AppOperationResult<int>.Fail("desktop_agent_start_exception", ex.Message, RiskLevel.Medium);
        }
    }

    public async Task<AppOperationResult<int>> StopDesktopAgentAsync(AppSettings? settings = null, CancellationToken cancellationToken = default)
    {
        settings ??= await LoadSettingsAsync(cancellationToken).ConfigureAwait(false);
        var candidates = GetManagedProcesses(settings.InstallRoot, ResolveCurrentExecutablePath())
            .Where(p => p.ProcessName.Equals("WindowsPowerUserMcp.DesktopAgent", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var stopped = 0;
        foreach (var candidate in candidates)
        {
            if (!IsOwnedProductProcess(candidate, settings.InstallRoot))
            {
                continue;
            }

            try
            {
                using var process = Process.GetProcessById(candidate.ProcessId);
                process.CloseMainWindow();
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(TimeSpan.FromSeconds(5));
                try
                {
                    await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    process.Kill(entireProcessTree: true);
                    await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
                }

                stopped++;
            }
            catch
            {
                // It may have exited during refresh.
            }
        }

        return AppOperationResult<int>.Ok(stopped, stopped == 0 ? "No owned DesktopAgent process was running." : $"Stopped {stopped} DesktopAgent process(es).", riskLevel: RiskLevel.Destructive);
    }

    public async Task<AppOperationResult<int>> RestartDesktopAgentAsync(AppSettings? settings = null, CancellationToken cancellationToken = default)
    {
        settings ??= await LoadSettingsAsync(cancellationToken).ConfigureAwait(false);
        await StopDesktopAgentAsync(settings, cancellationToken).ConfigureAwait(false);
        return await StartDesktopAgentAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<AppOperationResult<JsonElement>> InvokeBrokerToolAsync(string toolName, object? arguments = null, AppSettings? settings = null, CancellationToken cancellationToken = default)
    {
        settings ??= await LoadSettingsAsync(cancellationToken).ConfigureAwait(false);
        var client = new BrokerPipeClient(settings.PipeName, TimeSpan.FromSeconds(5));
        var response = await client.InvokeRawAsync(toolName, arguments ?? new { }, cancellationToken).ConfigureAwait(false);
        return response.Success && response.Result is { } result
            ? AppOperationResult<JsonElement>.Ok(result, $"Broker tool '{toolName}' completed.", riskLevel: RiskLevel.ReadOnly)
            : AppOperationResult<JsonElement>.Fail(response.ErrorCode ?? "broker_call_failed", response.ErrorMessage ?? $"Broker tool '{toolName}' failed.", RiskLevel.Low);
    }

    public async Task<AppOperationResult<string>> CheckBrokerPipeAsync(AppSettings? settings = null, CancellationToken cancellationToken = default)
    {
        settings ??= await LoadSettingsAsync(cancellationToken).ConfigureAwait(false);
        var response = await CheckPipeAsync(settings.PipeName, "broker_get_status", cancellationToken).ConfigureAwait(false);
        return response.Success
            ? AppOperationResult<string>.Ok(settings.PipeName, "Broker pipe is healthy.", riskLevel: RiskLevel.ReadOnly)
            : AppOperationResult<string>.Fail(response.ErrorCode ?? "pipe_failed", response.ErrorMessage ?? "Pipe test failed.", RiskLevel.ReadOnly);
    }

    public async Task<AppOperationResult<string>> CheckDesktopAgentPipeAsync(AppSettings? settings = null, CancellationToken cancellationToken = default)
    {
        settings ??= await LoadSettingsAsync(cancellationToken).ConfigureAwait(false);
        var response = await CheckPipeAsync(settings.PipeName + ".Desktop", "ui_get_foreground_window", cancellationToken).ConfigureAwait(false);
        return response.Success
            ? AppOperationResult<string>.Ok(settings.PipeName + ".Desktop", "DesktopAgent pipe is healthy.", riskLevel: RiskLevel.ReadOnly)
            : AppOperationResult<string>.Fail(response.ErrorCode ?? "desktop_pipe_failed", response.ErrorMessage ?? "DesktopAgent pipe test failed.", RiskLevel.ReadOnly);
    }

    public Task<AppOperationResult<UpdateCheckResult>> CheckForUpdatesAsync(AppSettings settings, CancellationToken cancellationToken = default) =>
        _updateService.CheckForUpdatesAsync(settings.ReleaseRepository, VersionInfo.CurrentVersion, settings.IncludePrereleaseUpdates, cancellationToken);

    public async Task<AppOperationResult<string>> StageUpdateAsync(UpdateReleaseAsset asset, CancellationToken cancellationToken = default)
    {
        var stagingRoot = Path.Combine(PlatformPaths.LocalDataRoot, "updates", DateTimeOffset.UtcNow.ToString("yyyyMMddHHmmss"));
        return await _updateService.DownloadAssetAsync(asset, stagingRoot, cancellationToken).ConfigureAwait(false);
    }

    public async Task<AppOperationResult<string>> ApplyStagedUpdateAsync(string stagingPath, string installRoot, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(stagingPath) || !File.Exists(stagingPath))
        {
            return AppOperationResult<string>.Fail("staging_missing", "The staged update asset does not exist.", RiskLevel.High);
        }

        Directory.CreateDirectory(installRoot);
        var backupRoot = Path.Combine(PlatformPaths.ProgramDataRoot, "update-backups", DateTimeOffset.UtcNow.ToString("yyyyMMddHHmmss"));
        Directory.CreateDirectory(backupRoot);

        try
        {
            foreach (var file in Directory.EnumerateFiles(installRoot, "*", SearchOption.TopDirectoryOnly))
            {
                File.Copy(file, Path.Combine(backupRoot, Path.GetFileName(file)), overwrite: true);
            }

            if (Path.GetExtension(stagingPath).Equals(".zip", StringComparison.OrdinalIgnoreCase))
            {
                ZipFile.ExtractToDirectory(stagingPath, installRoot, overwriteFiles: true);
            }
            else
            {
                File.Copy(stagingPath, Path.Combine(installRoot, ProductConstants.AppExecutableName), overwrite: true);
            }

            var settings = await LoadSettingsAsync(cancellationToken).ConfigureAwait(false);
            await CreateInstallStateAsync(settings with { InstallRoot = installRoot }, ["app"], cancellationToken, lastUpdate: DateTimeOffset.UtcNow).ConfigureAwait(false);
            return AppOperationResult<string>.Ok(installRoot, $"Update applied. Backup: {backupRoot}", riskLevel: RiskLevel.High);
        }
        catch (Exception ex)
        {
            return AppOperationResult<string>.Fail("update_apply_failed", $"Update apply failed after backup '{backupRoot}': {ex.Message}", RiskLevel.High, ["Review backup and reinstall from a published artifact if needed."]);
        }
    }

    public async Task<AppOperationResult<DiagnosticBundleResult>> ExportDiagnosticBundleAsync(AppSettings? settings = null, CancellationToken cancellationToken = default)
    {
        settings ??= await LoadSettingsAsync(cancellationToken).ConfigureAwait(false);
        var timestamp = DateTimeOffset.UtcNow.ToString("yyyyMMddHHmmss");
        var root = Path.Combine(settings.DataRoot, "diagnostics", timestamp);
        var bundlePath = Path.Combine(settings.DataRoot, "diagnostics", $"diagnostic-bundle-{timestamp}.zip");
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(Path.GetDirectoryName(bundlePath)!);
        var redactions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var files = 0;

        await WriteRedactedJsonAsync(Path.Combine(root, "settings.json"), settings, redactions, cancellationToken).ConfigureAwait(false);
        await WriteRedactedJsonAsync(Path.Combine(root, "health.json"), (await GetHealthAsync(settings, cancellationToken).ConfigureAwait(false)).Data, redactions, cancellationToken).ConfigureAwait(false);

        var logRoot = Path.Combine(settings.DataRoot, "logs");
        if (Directory.Exists(logRoot))
        {
            var tailsRoot = Path.Combine(root, "recent-log-tails");
            Directory.CreateDirectory(tailsRoot);
            foreach (var log in Directory.EnumerateFiles(logRoot, "*.*", SearchOption.TopDirectoryOnly).OrderByDescending(File.GetLastWriteTimeUtc).Take(12))
            {
                var tail = await ReadTailAsync(log, 64 * 1024, cancellationToken).ConfigureAwait(false);
                var redacted = _redactor.Redact(tail);
                foreach (var redaction in redacted.RedactionsApplied) redactions.Add(redaction);
                await File.WriteAllTextAsync(Path.Combine(tailsRoot, Path.GetFileName(log)), redacted.Text, cancellationToken).ConfigureAwait(false);
            }
        }

        if (File.Exists(bundlePath))
        {
            File.Delete(bundlePath);
        }

        ZipFile.CreateFromDirectory(root, bundlePath, CompressionLevel.Optimal, includeBaseDirectory: false);
        files = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).Count();
        return AppOperationResult<DiagnosticBundleResult>.Ok(new DiagnosticBundleResult(bundlePath, files, redactions.ToArray()), "Diagnostic bundle exported.", riskLevel: RiskLevel.Low);
    }

    public async Task<InstallState> CreateInstallStateAsync(
        AppSettings settings,
        IEnumerable<string> installedComponents,
        CancellationToken cancellationToken = default,
        DateTimeOffset? lastUpdate = null)
    {
        var current = await _installStateStore.LoadAsync(cancellationToken).ConfigureAwait(false);
        var state = new InstallState
        {
            ProductVersion = VersionInfo.CurrentVersion,
            InstallRoot = settings.InstallRoot,
            DataRoot = settings.DataRoot,
            ServiceDataRoot = settings.ServiceDataRoot,
            ExecutablePath = ResolveCurrentExecutablePath(),
            InstalledComponents = installedComponents.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            LastUpdateTimestamp = lastUpdate ?? current?.LastUpdateTimestamp,
            OwnerSids = GetCurrentUserSid() is { } sid ? [sid] : [],
            PipeName = settings.PipeName,
            PreviousInstallRoots = current is null || string.Equals(current.InstallRoot, settings.InstallRoot, StringComparison.OrdinalIgnoreCase)
                ? current?.PreviousInstallRoots ?? []
                : current.PreviousInstallRoots.Append(current.InstallRoot).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            MigrationHistory = current?.MigrationHistory ?? []
        };

        await _installStateStore.SaveAsync(state, cancellationToken).ConfigureAwait(false);
        return state;
    }

    public string GetInstalledCodexConfig(AppSettings settings) => CodexConfigGenerator.GenerateInstalled(settings.InstallRoot);

    public string GetDevelopmentCodexConfig(string repoRoot) => CodexConfigGenerator.GenerateDevelopment(repoRoot);

    public static bool IsElevated()
    {
        using var identity = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);
        return principal.IsInRole(WindowsBuiltInRole.Administrator);
    }

    public static string ResolveCurrentExecutablePath()
    {
        var path = Environment.ProcessPath;
        if (!string.IsNullOrWhiteSpace(path) && string.Equals(Path.GetExtension(path), ".exe", StringComparison.OrdinalIgnoreCase))
        {
            return path;
        }

        var module = Process.GetCurrentProcess().MainModule?.FileName;
        if (!string.IsNullOrWhiteSpace(module) && string.Equals(Path.GetExtension(module), ".exe", StringComparison.OrdinalIgnoreCase))
        {
            return module;
        }

        var appHostCandidate = Path.Combine(AppContext.BaseDirectory, ProductConstants.AppExecutableName);
        return File.Exists(appHostCandidate) ? appHostCandidate : AppContext.BaseDirectory;
    }

    public static string? ResolveSiblingExecutable(string fileName)
    {
        var current = ResolveCurrentExecutablePath();
        var directory = Path.GetDirectoryName(current);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            var sibling = Path.Combine(directory, fileName);
            if (File.Exists(sibling))
            {
                return sibling;
            }
        }

        var repoRoot = FindRepoRoot(AppContext.BaseDirectory);
        if (repoRoot is not null)
        {
            var candidate = Path.Combine(repoRoot, "src", Path.GetFileNameWithoutExtension(fileName), "bin", "Debug", "net10.0-windows", fileName);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private async Task<AppOperationResult<string>> ControlServiceAsync(string operation, RiskLevel riskLevel, CancellationToken cancellationToken)
    {
        var service = await GetServiceStatusAsync(cancellationToken).ConfigureAwait(false);
        if (!service.Installed)
        {
            return AppOperationResult<string>.Fail("service_missing", "Broker service is not installed.", riskLevel);
        }

        try
        {
            using var controller = new ServiceController(ProductConstants.BrokerServiceName);
            switch (operation)
            {
                case "start":
                    if (controller.Status == ServiceControllerStatus.Running)
                    {
                        return AppOperationResult<string>.Ok(ProductConstants.BrokerServiceName, "Service is already running.", riskLevel: riskLevel);
                    }
                    controller.Start();
                    await WaitForServiceStatusAsync(controller, ServiceControllerStatus.Running, TimeSpan.FromSeconds(30), cancellationToken).ConfigureAwait(false);
                    return AppOperationResult<string>.Ok(ProductConstants.BrokerServiceName, "Service started.", riskLevel: riskLevel);
                case "stop":
                    if (controller.Status == ServiceControllerStatus.Stopped)
                    {
                        return AppOperationResult<string>.Fail("service_not_running", "Service is already stopped.", riskLevel);
                    }
                    controller.Stop();
                    await WaitForServiceStatusAsync(controller, ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(30), cancellationToken).ConfigureAwait(false);
                    return AppOperationResult<string>.Ok(ProductConstants.BrokerServiceName, "Service stopped.", riskLevel: riskLevel);
                default:
                    return AppOperationResult<string>.Fail("unsupported_service_operation", operation, riskLevel);
            }
        }
        catch (Exception ex)
        {
            return AppOperationResult<string>.Fail($"service_{operation}_failed", ex.Message, riskLevel);
        }
    }

    private AppOperationResult<int> StartAppMode(string arguments, bool useShellExecute, string? verb)
    {
        try
        {
            var executable = ResolveCurrentExecutablePath();
            var startInfo = new ProcessStartInfo
            {
                FileName = executable,
                Arguments = arguments,
                UseShellExecute = useShellExecute,
                CreateNoWindow = !useShellExecute,
                WindowStyle = useShellExecute ? ProcessWindowStyle.Normal : ProcessWindowStyle.Hidden,
                Verb = verb ?? string.Empty
            };
            var process = Process.Start(startInfo);
            return process is null
                ? AppOperationResult<int>.Fail("start_failed", "Process.Start returned null.", RiskLevel.Medium)
                : AppOperationResult<int>.Ok(process.Id, "Started.");
        }
        catch (Exception ex)
        {
            return AppOperationResult<int>.Fail("start_exception", ex.Message, RiskLevel.Medium);
        }
    }

    private async Task<BrokerResponse> CheckPipeAsync(string pipeName, string toolName, CancellationToken cancellationToken)
    {
        var client = new BrokerPipeClient(pipeName, TimeSpan.FromSeconds(2));
        return await client.InvokeRawAsync(toolName, new { }, cancellationToken).ConfigureAwait(false);
    }

    private async Task<(int ActiveTasks, int TrackedProcesses)> GetBrokerCountsAsync(AppSettings settings, CancellationToken cancellationToken)
    {
        var client = new BrokerPipeClient(settings.PipeName, TimeSpan.FromSeconds(2));
        var activeTasks = 0;
        var trackedProcesses = 0;
        var tasks = await client.InvokeRawAsync("task_list_active", new { }, cancellationToken).ConfigureAwait(false);
        if (tasks.Success && tasks.Result is { } taskElement && taskElement.TryGetProperty("data", out var taskData) && taskData.ValueKind == JsonValueKind.Array)
        {
            activeTasks = taskData.GetArrayLength();
        }

        var processes = await client.InvokeRawAsync("process_list_tracked", new { }, cancellationToken).ConfigureAwait(false);
        if (processes.Success && processes.Result is { } processElement && processElement.TryGetProperty("data", out var processData) && processData.ValueKind == JsonValueKind.Array)
        {
            trackedProcesses = processData.GetArrayLength();
        }

        return (activeTasks, trackedProcesses);
    }

    private IReadOnlyList<ManagedProcessInfo> GetManagedProcesses(string installRoot, string currentExecutable)
    {
        var names = new[]
        {
            "WindowsPowerUserMcp.App",
            "WindowsPowerUserMcp.BrokerService",
            "WindowsPowerUserMcp.DesktopAgent",
            "WindowsPowerUserMcp.StdioBridge",
            "WindowsPowerUserMcp.HttpHost"
        };

        var results = new List<ManagedProcessInfo>();
        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                try
                {
                    if (!names.Any(n => process.ProcessName.Equals(n, StringComparison.OrdinalIgnoreCase)))
                    {
                        continue;
                    }

                    var path = TryGetMainModule(process);
                    var commandLine = TryGetCommandLine(process.Id);
                    results.Add(new ManagedProcessInfo(process.Id, process.ProcessName, path, commandLine, TryGetStartTime(process)));
                }
                catch
                {
                    // Some protected processes deny metadata; skip them.
                }
            }
        }

        return results
            .Where(p => IsOwnedProductProcess(p, installRoot) || string.Equals(p.Path, currentExecutable, StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(p.Path))
            .OrderBy(p => p.ProcessName)
            .ThenBy(p => p.ProcessId)
            .ToArray();
    }

    private static bool IsOwnedProductProcess(ManagedProcessInfo process, string installRoot)
    {
        if (string.IsNullOrWhiteSpace(process.Path))
        {
            return false;
        }

        var fullPath = SafeFullPath(process.Path);
        return fullPath.Contains(@"\WindowsPowerUserMcp.", StringComparison.OrdinalIgnoreCase) &&
               (fullPath.StartsWith(SafeFullPath(installRoot), StringComparison.OrdinalIgnoreCase) ||
                fullPath.Contains(@"\WinMCPd\src\", StringComparison.OrdinalIgnoreCase) ||
                fullPath.Contains(@"\WinMCPd\artifacts\", StringComparison.OrdinalIgnoreCase));
    }

    private async Task<ServiceStatusInfo> GetServiceStatusAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var controller = ServiceController.GetServices().FirstOrDefault(s => string.Equals(s.ServiceName, ProductConstants.BrokerServiceName, StringComparison.OrdinalIgnoreCase));
            if (controller is null)
            {
                return new ServiceStatusInfo(false, ProductConstants.BrokerServiceName, null, null, null, null, null, null);
            }

            var registry = ReadServiceRegistry(ProductConstants.BrokerServiceName);
            var pid = await GetServicePidAsync(cancellationToken).ConfigureAwait(false);
            return new ServiceStatusInfo(
                true,
                controller.ServiceName,
                controller.DisplayName,
                controller.Status.ToString(),
                registry.StartType,
                registry.Account,
                registry.BinaryPath,
                pid);
        }
        catch
        {
            return new ServiceStatusInfo(false, ProductConstants.BrokerServiceName, null, null, null, null, null, null);
        }
    }

    private async Task<int?> GetServicePidAsync(CancellationToken cancellationToken)
    {
        var result = await RunProcessAsync("sc.exe", $"queryex \"{ProductConstants.BrokerServiceName}\"", null, TimeSpan.FromSeconds(10), cancellationToken).ConfigureAwait(false);
        if (!result.Success)
        {
            return null;
        }

        foreach (var line in (result.Data?.Stdout ?? string.Empty).Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = line.Split(':', 2);
            if (parts.Length == 2 && parts[0].Trim().Equals("PID", StringComparison.OrdinalIgnoreCase) && int.TryParse(parts[1].Trim(), out var pid) && pid > 0)
            {
                return pid;
            }
        }

        return null;
    }

    private async Task<ScheduledTaskInfo> GetScheduledTaskAsync(CancellationToken cancellationToken)
    {
        var result = await RunProcessAsync("schtasks.exe", $"/Query /TN \"{ProductConstants.TrayTaskName}\" /FO LIST /V", null, TimeSpan.FromSeconds(10), cancellationToken).ConfigureAwait(false);
        if (!result.Success)
        {
            return new ScheduledTaskInfo(false, ProductConstants.TrayTaskName, null, null, null, null);
        }

        var values = ParseListOutput(result.Data?.Stdout ?? string.Empty);
        values.TryGetValue("TaskName", out var path);
        values.TryGetValue("Status", out var state);
        values.TryGetValue("Task To Run", out var action);
        values.TryGetValue("Last Run Time", out var lastRun);
        return new ScheduledTaskInfo(true, ProductConstants.TrayTaskName, path, state, action, lastRun);
    }

    private static (string? StartType, string? Account, string? BinaryPath) ReadServiceRegistry(string serviceName)
    {
        using var key = Registry.LocalMachine.OpenSubKey($@"SYSTEM\CurrentControlSet\Services\{serviceName}");
        if (key is null)
        {
            return (null, null, null);
        }

        var start = key.GetValue("Start") is int startValue ? startValue switch
        {
            2 => "Automatic",
            3 => "Manual",
            4 => "Disabled",
            _ => $"Start={startValue}"
        } : null;
        var delayed = key.GetValue("DelayedAutoStart") is int delayedValue && delayedValue == 1;
        return (delayed && start == "Automatic" ? "DelayedAutomatic" : start, key.GetValue("ObjectName") as string, key.GetValue("ImagePath") as string);
    }

    private static async Task WaitForServiceStatusAsync(ServiceController controller, ServiceControllerStatus status, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            controller.Refresh();
            if (controller.Status == status)
            {
                return;
            }

            await Task.Delay(500, cancellationToken).ConfigureAwait(false);
        }

        throw new System.TimeoutException($"Service did not reach {status} within {timeout.TotalSeconds:N0} seconds.");
    }

    private async Task<AppOperationResult<ProcessRunResult>> RunProcessAsync(
        string fileName,
        string arguments,
        string? workingDirectory,
        TimeSpan timeout,
        CancellationToken cancellationToken,
        bool killOnTimeout = false)
    {
        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = fileName,
                    Arguments = arguments,
                    WorkingDirectory = workingDirectory ?? Environment.CurrentDirectory,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                },
                EnableRaisingEvents = true
            };

            var stdout = new StringBuilder();
            var stderr = new StringBuilder();
            process.OutputDataReceived += (_, e) => { if (e.Data is not null) stdout.AppendLine(e.Data); };
            process.ErrorDataReceived += (_, e) => { if (e.Data is not null) stderr.AppendLine(e.Data); };
            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(timeout);
            try
            {
                await process.WaitForExitAsync(timeoutCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                if (killOnTimeout)
                {
                    try { process.Kill(entireProcessTree: true); } catch { }
                }

                return AppOperationResult<ProcessRunResult>.Fail("process_timeout", $"{fileName} timed out after {timeout.TotalSeconds:N0}s.", RiskLevel.Medium);
            }

            var output = new ProcessRunResult(process.ExitCode, _redactor.Redact(stdout.ToString()).Text, _redactor.Redact(stderr.ToString()).Text);
            return process.ExitCode == 0
                ? AppOperationResult<ProcessRunResult>.Ok(output, "Process completed.")
                : AppOperationResult<ProcessRunResult>.Fail("process_failed", $"{fileName} exited {process.ExitCode}. {output.Stderr}{output.Stdout}", RiskLevel.Medium);
        }
        catch (Exception ex)
        {
            return AppOperationResult<ProcessRunResult>.Fail("process_exception", ex.Message, RiskLevel.Medium);
        }
    }

    private async Task WriteRedactedJsonAsync<T>(string path, T value, HashSet<string> redactions, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(value, JsonDefaults.Options);
        var redacted = _redactor.Redact(json);
        foreach (var redaction in redacted.RedactionsApplied) redactions.Add(redaction);
        await File.WriteAllTextAsync(path, redacted.Text, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<string> ReadTailAsync(string path, int maxBytes, CancellationToken cancellationToken)
    {
        await using var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        var length = stream.Length;
        var start = Math.Max(0, length - maxBytes);
        stream.Seek(start, SeekOrigin.Begin);
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
    }

    private static Dictionary<string, string> ParseListOutput(string text)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in text.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = line.Split(':', 2);
            if (parts.Length == 2)
            {
                result[parts[0].Trim()] = parts[1].Trim();
            }
        }

        return result;
    }

    private static string? TryGetMainModule(Process process)
    {
        try { return process.MainModule?.FileName; }
        catch { return null; }
    }

    private static DateTimeOffset? TryGetStartTime(Process process)
    {
        try { return process.StartTime; }
        catch { return null; }
    }

    private static string? TryGetCommandLine(int pid)
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey($@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Image File Execution Options");
            _ = key;
            using var searcher = new System.Management.ManagementObjectSearcher($"SELECT CommandLine FROM Win32_Process WHERE ProcessId = {pid}");
            foreach (var item in searcher.Get())
            {
                return item["CommandLine"]?.ToString();
            }
        }
        catch
        {
            // WMI can be unavailable or blocked.
        }

        return null;
    }

    private static string? GetCurrentUserSid()
    {
        try { return WindowsIdentity.GetCurrent().User?.Value; }
        catch { return null; }
    }

    private static string SafeFullPath(string path)
    {
        try { return Path.GetFullPath(Environment.ExpandEnvironmentVariables(path)); }
        catch { return path; }
    }

    private static string? FindRepoRoot(string start)
    {
        var directory = new DirectoryInfo(start);
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

    private sealed record ProcessRunResult(int ExitCode, string Stdout, string Stderr);
}

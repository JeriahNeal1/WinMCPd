using System.Text.Json.Serialization;
using WindowsPowerUserMcp.Core;

namespace WindowsPowerUserMcp.AppManagement;

public static class ProductConstants
{
    public const string ProductName = "WindowsPowerUserMcp";
    public const string AppExecutableName = "WindowsPowerUserMcp.App.exe";
    public const string BrokerServiceName = "WindowsPowerUserMcp.Broker";
    public const string BrokerServiceDisplayName = "WindowsPowerUserMcp Broker";
    public const string TrayTaskName = "WindowsPowerUserMcp Dashboard";
    public const string DefaultInstallRoot = @"C:\Tools\WindowsPowerUserMcp";
    public const string DefaultReleaseRepository = "JeriahNeal1/WinMCPd";
}

public enum AppMode
{
    Dashboard,
    Broker,
    Stdio,
    DesktopAgent,
    Http,
    Install,
    Uninstall,
    Update,
    ApplyUpdate
}

public sealed record AppArguments(
    AppMode Mode,
    bool Service = false,
    bool ServiceOnly = false,
    bool TrayOnly = false,
    bool Start = false,
    string? InstallRoot = null,
    string? StagingPath = null,
    IReadOnlyList<string>? Passthrough = null)
{
    public static AppArguments Parse(IEnumerable<string> args)
    {
        var values = args.ToArray();
        var mode = AppMode.Dashboard;
        var service = false;
        var serviceOnly = false;
        var trayOnly = false;
        var start = false;
        string? installRoot = null;
        string? stagingPath = null;
        var passthrough = new List<string>();

        for (var i = 0; i < values.Length; i++)
        {
            var arg = values[i];
            switch (arg.ToLowerInvariant())
            {
                case "--dashboard":
                    mode = AppMode.Dashboard;
                    break;
                case "--broker":
                    mode = AppMode.Broker;
                    break;
                case "--stdio":
                    mode = AppMode.Stdio;
                    break;
                case "--desktop-agent":
                    mode = AppMode.DesktopAgent;
                    break;
                case "--http":
                    mode = AppMode.Http;
                    break;
                case "--install":
                    mode = AppMode.Install;
                    break;
                case "--uninstall":
                    mode = AppMode.Uninstall;
                    break;
                case "--update":
                    mode = AppMode.Update;
                    break;
                case "--apply-update":
                    mode = AppMode.ApplyUpdate;
                    break;
                case "--service":
                    service = true;
                    break;
                case "--service-only":
                    serviceOnly = true;
                    break;
                case "--tray-only":
                    trayOnly = true;
                    break;
                case "--start":
                    start = true;
                    break;
                case "--install-root" when i + 1 < values.Length:
                    installRoot = values[++i];
                    break;
                case "--staging" when i + 1 < values.Length:
                    stagingPath = values[++i];
                    break;
                default:
                    passthrough.Add(arg);
                    break;
            }
        }

        return new AppArguments(mode, service, serviceOnly, trayOnly, start, installRoot, stagingPath, passthrough);
    }
}

public sealed record AppOperationResult<T>(
    bool Success,
    string Code,
    string Message,
    T? Data = default,
    RiskLevel RiskLevel = RiskLevel.Low,
    IReadOnlyList<string>? RecommendedActions = null)
{
    public static AppOperationResult<T> Ok(T data, string message = "OK", string code = "ok", RiskLevel riskLevel = RiskLevel.Low) =>
        new(true, code, message, data, riskLevel);

    public static AppOperationResult<T> Fail(string code, string message, RiskLevel riskLevel = RiskLevel.Medium, IReadOnlyList<string>? recommendedActions = null) =>
        new(false, code, message, default, riskLevel, recommendedActions);
}

public sealed record AppSettings
{
    public string InstallRoot { get; init; } = ProductConstants.DefaultInstallRoot;
    public string DataRoot { get; init; } = PlatformPaths.LocalDataRoot;
    public string ServiceDataRoot { get; init; } = PlatformPaths.ProgramDataRoot;
    public string PipeName { get; init; } = "WindowsPowerUserMcp.Broker";
    public string[] ServiceAllowedUserSids { get; init; } = [];
    public bool AllowBuiltinAdministratorsForServicePipe { get; init; }
    public bool LocalHttpEnabled { get; init; }
    public int HttpPort { get; init; } = 49321;
    public bool RequireHttpToken { get; init; } = true;
    public bool StartMinimizedToTray { get; init; }
    public bool MinimizeOnClose { get; init; } = true;
    public bool AutoStartTrayAtLogin { get; init; }
    public bool CheckForUpdatesOnStartup { get; init; } = true;
    public bool IncludePrereleaseUpdates { get; init; }
    public int RetainProcessLogsDays { get; init; } = 14;
    public int RetainScreenshotsDays { get; init; } = 14;
    public string Theme { get; init; } = "system";
    public string LogLevel { get; init; } = "Information";
    public string ReleaseRepository { get; init; } = ProductConstants.DefaultReleaseRepository;
}

public sealed record InstallState
{
    public string ProductVersion { get; init; } = VersionInfo.CurrentVersion;
    public string InstallRoot { get; init; } = ProductConstants.DefaultInstallRoot;
    public string DataRoot { get; init; } = PlatformPaths.LocalDataRoot;
    public string ServiceDataRoot { get; init; } = PlatformPaths.ProgramDataRoot;
    public string ExecutablePath { get; init; } = string.Empty;
    public string ServiceName { get; init; } = ProductConstants.BrokerServiceName;
    public string ScheduledTaskName { get; init; } = ProductConstants.TrayTaskName;
    public string[] InstalledComponents { get; init; } = [];
    public DateTimeOffset InstallTimestamp { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? LastUpdateTimestamp { get; init; }
    public string[] OwnerSids { get; init; } = [];
    public string PipeName { get; init; } = "WindowsPowerUserMcp.Broker";
    public IReadOnlyDictionary<string, string> ExpectedFileHashes { get; init; } = new Dictionary<string, string>();
    public string[] PreviousInstallRoots { get; init; } = [];
    public string[] MigrationHistory { get; init; } = [];
}

public sealed record ManagedProcessInfo(int ProcessId, string ProcessName, string? Path, string? CommandLine, DateTimeOffset? StartedAt);

public sealed record ServiceStatusInfo(
    bool Installed,
    string ServiceName,
    string? DisplayName,
    string? Status,
    string? StartType,
    string? ServiceAccount,
    string? BinaryPath,
    int? ProcessId);

public sealed record ScheduledTaskInfo(
    bool Installed,
    string TaskName,
    string? TaskPath,
    string? State,
    string? Action,
    string? LastRunTime);

public sealed record InstallIssue(string Severity, string Code, string Message, string? RepairAction);

public sealed record InstallInspection(
    string Mode,
    string CurrentExecutablePath,
    string InstallRoot,
    string DataRoot,
    string LogRoot,
    InstallState? InstallState,
    ServiceStatusInfo Service,
    ScheduledTaskInfo TrayTask,
    IReadOnlyList<ManagedProcessInfo> RunningProcesses,
    IReadOnlyList<InstallIssue> Issues);

public sealed record HealthSnapshot(
    DateTimeOffset Timestamp,
    bool IsElevated,
    bool BrokerPipeHealthy,
    bool DesktopAgentPipeHealthy,
    string BrokerStatus,
    int ActiveTaskCount,
    int TrackedProcessCount,
    InstallInspection InstallInspection);

public sealed record UpdateReleaseAsset(string Name, string BrowserDownloadUrl, long Size, string? Sha256);

public sealed record UpdateRelease(
    string Version,
    string Name,
    string Body,
    DateTimeOffset? PublishedAt,
    bool Prerelease,
    IReadOnlyList<UpdateReleaseAsset> Assets);

public sealed record UpdateCheckResult(
    bool UpdateAvailable,
    string CurrentVersion,
    UpdateRelease? LatestRelease,
    string Message);

public sealed record DiagnosticBundleResult(string Path, int FileCount, IReadOnlyList<string> RedactionsApplied);

public sealed record UninstallPlan(
    string InstallRoot,
    bool RemoveService,
    bool RemoveTrayAutostart,
    bool RemoveBinaries,
    bool DeleteLogs,
    bool DeleteScreenshots,
    bool DeleteTaskLedger,
    bool DeleteAllUserData)
{
    public bool DeletesData => DeleteLogs || DeleteScreenshots || DeleteTaskLedger || DeleteAllUserData;
}

public static class VersionInfo
{
    public const string CurrentVersion = "0.2.0";
    public static string BuildDateUtc => ThisAssemblyBuildDate.Value;

    private static class ThisAssemblyBuildDate
    {
        internal static readonly string Value = DateTimeOffset.UtcNow.ToString("yyyy-MM-dd");
    }
}

[JsonSerializable(typeof(AppSettings))]
[JsonSerializable(typeof(InstallState))]
internal partial class AppManagementJsonContext : JsonSerializerContext;

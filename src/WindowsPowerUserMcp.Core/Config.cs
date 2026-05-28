namespace WindowsPowerUserMcp.Core;

public sealed class WindowsPowerUserMcpOptions
{
    public string TargetFrameworkPreference { get; set; } = "net10.0";
    public bool StdioEnabled { get; set; } = true;
    public bool LocalHttpEnabled { get; set; }
    public int LocalHttpPort { get; set; } = 49321;
    public string IpcPipeName { get; set; } = "WindowsPowerUserMcp.Broker";
    public bool IpcCurrentUserOnly { get; set; } = true;
    public string[] IpcAllowedUserSids { get; set; } = [];
    public bool IpcAllowBuiltinAdministrators { get; set; }
    public string? DataRoot { get; set; }
    public string? ServiceDataRoot { get; set; }
    public string? LogRoot { get; set; }
    public string? SqlitePath { get; set; }
    public int DefaultTimeoutSeconds { get; set; } = 300;
    public int MaxStdoutCaptureBytes { get; set; } = 64 * 1024;
    public int MaxStderrCaptureBytes { get; set; } = 64 * 1024;
    public bool SaveFullProcessLogs { get; set; } = true;
    public bool RedactEnvironmentVariables { get; set; } = true;
    public string[] RedactSecretPatterns { get; set; } =
    [
        "password",
        "passwd",
        "pwd",
        "secret",
        "token",
        "api[_-]?key",
        "authorization",
        "cookie"
    ];

    public bool ShellEnabled { get; set; } = true;
    public bool UiAutomationEnabled { get; set; } = true;
    public bool ScreenshotsEnabled { get; set; } = true;
    public bool DesktopAgentRequiredForUi { get; set; } = true;
    public bool GenericShellRequiresElevatedForAdminTasks { get; set; } = true;
    public bool PackageInstallEnabled { get; set; } = true;
    public bool ServiceControlEnabled { get; set; } = true;
    public bool RegistryWriteEnabled { get; set; } = true;
    public bool ScheduledTaskEnabled { get; set; } = true;
    public bool AutostartEnabled { get; set; } = true;
    public bool TrayEnabled { get; set; } = true;
    public bool TaskContinuationsEnabled { get; set; } = true;
    public bool FilesystemWatchersEnabled { get; set; } = true;
    public bool OptionalHttpAuthRequired { get; set; } = true;
    public string[] BlockedCredentialOperations { get; set; } =
    [
        "credential_dumping",
        "browser_password_extraction",
        "cookie_extraction",
        "token_theft",
        "mfa_bypass"
    ];
}

public static class PlatformPaths
{
    public const string AppName = "WindowsPowerUserMcp";

    public static string LocalDataRoot =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), AppName);

    public static string ProgramDataRoot =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), AppName);

    public static string ResolveDataRoot(WindowsPowerUserMcpOptions options, bool serviceLevel = false) =>
        !string.IsNullOrWhiteSpace(options.DataRoot)
            ? Environment.ExpandEnvironmentVariables(options.DataRoot)
            : serviceLevel
                ? (!string.IsNullOrWhiteSpace(options.ServiceDataRoot)
                    ? Environment.ExpandEnvironmentVariables(options.ServiceDataRoot)
                    : ProgramDataRoot)
                : LocalDataRoot;

    public static StorageLayout CreateLayout(WindowsPowerUserMcpOptions options, bool serviceLevel = false)
    {
        var root = ResolveDataRoot(options, serviceLevel);
        var logRoot = !string.IsNullOrWhiteSpace(options.LogRoot)
            ? Environment.ExpandEnvironmentVariables(options.LogRoot)
            : Path.Combine(root, "logs");
        var sqlitePath = !string.IsNullOrWhiteSpace(options.SqlitePath)
            ? Environment.ExpandEnvironmentVariables(options.SqlitePath)
            : Path.Combine(root, "db", "windows_power_user_mcp.sqlite3");

        var layout = new StorageLayout(
            root,
            Path.Combine(root, "db"),
            logRoot,
            Path.Combine(root, "tasks"),
            Path.Combine(root, "processes"),
            Path.Combine(root, "screenshots"),
            Path.Combine(root, "handoffs"),
            Path.Combine(root, "patches"),
            sqlitePath);

        layout.EnsureCreated();
        return layout;
    }
}

public sealed record StorageLayout(
    string Root,
    string Db,
    string Logs,
    string Tasks,
    string Processes,
    string Screenshots,
    string Handoffs,
    string Patches,
    string SqlitePath)
{
    public void EnsureCreated()
    {
        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(Db);
        Directory.CreateDirectory(Logs);
        Directory.CreateDirectory(Tasks);
        Directory.CreateDirectory(Processes);
        Directory.CreateDirectory(Screenshots);
        Directory.CreateDirectory(Handoffs);
        Directory.CreateDirectory(Patches);
    }
}

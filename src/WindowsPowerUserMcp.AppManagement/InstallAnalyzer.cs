namespace WindowsPowerUserMcp.AppManagement;

public static class InstallAnalyzer
{
    public static IReadOnlyList<InstallIssue> DetectIssues(
        InstallState? state,
        ServiceStatusInfo service,
        ScheduledTaskInfo trayTask,
        string currentExecutablePath,
        IEnumerable<string>? knownInstallRoots = null)
    {
        var issues = new List<InstallIssue>();
        var currentFullPath = SafeFullPath(currentExecutablePath);
        var expectedRoot = state?.InstallRoot ?? ProductConstants.DefaultInstallRoot;
        var roots = (knownInstallRoots ?? [])
            .Concat(state?.PreviousInstallRoots ?? [])
            .Append(expectedRoot)
            .Where(r => !string.IsNullOrWhiteSpace(r))
            .Select(SafeFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (service.Installed && !string.IsNullOrWhiteSpace(service.BinaryPath))
        {
            var servicePath = ExtractExecutablePath(service.BinaryPath);
            if (!string.IsNullOrWhiteSpace(servicePath) && !PathEquals(servicePath, currentFullPath) && !PathEquals(servicePath, Path.Combine(expectedRoot, ProductConstants.AppExecutableName)))
            {
                issues.Add(new InstallIssue("warning", "stale_service_path", $"Service points to '{servicePath}', not the current app executable.", "repair_service_path"));
            }
        }

        if (trayTask.Installed && !string.IsNullOrWhiteSpace(trayTask.Action))
        {
            var taskPath = ExtractExecutablePath(trayTask.Action);
            if (!string.IsNullOrWhiteSpace(taskPath) && !PathEquals(taskPath, currentFullPath) && !PathEquals(taskPath, Path.Combine(expectedRoot, ProductConstants.AppExecutableName)))
            {
                issues.Add(new InstallIssue("warning", "stale_tray_task_path", $"Tray autostart task points to '{taskPath}', not the current app executable.", "repair_tray_task_path"));
            }
        }

        var existingRoots = roots.Where(Directory.Exists).ToArray();
        if (existingRoots.Length > 1)
        {
            issues.Add(new InstallIssue("warning", "duplicate_install_roots", "Multiple install roots exist for this product.", "review_duplicate_installs"));
        }

        if (state is null && (service.Installed || trayTask.Installed || existingRoots.Length > 0))
        {
            issues.Add(new InstallIssue("info", "install_state_missing", "Install state is missing but service/task/files exist; state can be reconstructed.", "reconstruct_install_state"));
        }

        if (!File.Exists(currentExecutablePath))
        {
            issues.Add(new InstallIssue("error", "current_executable_missing", "The current executable path cannot be validated on disk.", "repair_install"));
        }

        return issues;
    }

    public static string DetectRunMode(string executablePath)
    {
        var path = SafeFullPath(executablePath);
        if (path.Contains(@"\bin\Debug\", StringComparison.OrdinalIgnoreCase) ||
            path.Contains(@"\bin\Release\", StringComparison.OrdinalIgnoreCase) ||
            path.Contains(@"\src\", StringComparison.OrdinalIgnoreCase))
        {
            return "development";
        }

        return path.StartsWith(SafeFullPath(ProductConstants.DefaultInstallRoot), StringComparison.OrdinalIgnoreCase) ||
               path.StartsWith(SafeFullPath(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles)), StringComparison.OrdinalIgnoreCase)
            ? "installed"
            : "portable";
    }

    public static string ExtractExecutablePath(string commandLine)
    {
        var trimmed = commandLine.Trim();
        if (trimmed.StartsWith("\"", StringComparison.Ordinal))
        {
            var end = trimmed.IndexOf('"', 1);
            return end > 0 ? trimmed[1..end] : trimmed.Trim('"');
        }

        var exeIndex = trimmed.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
        return exeIndex >= 0 ? trimmed[..(exeIndex + 4)] : trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? trimmed;
    }

    private static bool PathEquals(string left, string right) =>
        string.Equals(SafeFullPath(left), SafeFullPath(right), StringComparison.OrdinalIgnoreCase);

    private static string SafeFullPath(string path)
    {
        try { return Path.GetFullPath(Environment.ExpandEnvironmentVariables(path)); }
        catch { return path; }
    }
}

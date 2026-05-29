using System.Text.Json;
using WindowsPowerUserMcp.AppManagement;
using WindowsPowerUserMcp.Core;
using WindowsPowerUserMcp.Security;

namespace WindowsPowerUserMcp.Tests;

public sealed class AppManagementTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "WindowsPowerUserMcp.AppManagement.Tests", Guid.NewGuid().ToString("n"));

    [Fact]
    public void AppArguments_Parse_All_In_One_Modes()
    {
        var parsed = AppArguments.Parse(["--broker", "--service", "--install-root", @"C:\Tools\WindowsPowerUserMcp"]);

        Assert.Equal(AppMode.Broker, parsed.Mode);
        Assert.True(parsed.Service);
        Assert.Equal(@"C:\Tools\WindowsPowerUserMcp", parsed.InstallRoot);

        Assert.Equal(AppMode.Stdio, AppArguments.Parse(["--stdio"]).Mode);
        Assert.Equal(AppMode.ApplyUpdate, AppArguments.Parse(["--apply-update", "--staging", "update.zip"]).Mode);
    }

    [Fact]
    public void CodexConfigGenerator_Uses_App_Stdio_And_Fallback()
    {
        var installed = CodexConfigGenerator.GenerateInstalled(@"C:\Tools\WindowsPowerUserMcp");
        var development = CodexConfigGenerator.GenerateDevelopment(@"C:\dev\WinMCPd");
        var fallback = CodexConfigGenerator.GenerateFallbackStdioBridge(@"C:\Tools\WindowsPowerUserMcp");

        Assert.Contains("WindowsPowerUserMcp.App.exe", installed);
        Assert.Contains("\"--stdio\"", installed);
        Assert.Contains("WindowsPowerUserMcp.App.csproj", development);
        Assert.Contains("WindowsPowerUserMcp.StdioBridge.exe", fallback);
    }

    [Fact]
    public async Task SettingsStore_Save_And_Load_RoundTrips()
    {
        var path = Path.Combine(_root, "settings.json");
        var store = new AppSettingsStore(path);
        var settings = new AppSettings
        {
            InstallRoot = @"D:\Tools\WinMCPd",
            LocalHttpEnabled = true,
            HttpPort = 50001,
            ServiceAllowedUserSids = ["S-1-5-21-1-2-3-1001"]
        };

        await store.SaveAsync(settings);
        var loaded = await store.LoadAsync();

        Assert.Equal(settings.InstallRoot, loaded.InstallRoot);
        Assert.True(loaded.LocalHttpEnabled);
        Assert.Equal(50001, loaded.HttpPort);
        Assert.Single(loaded.ServiceAllowedUserSids);
    }

    [Fact]
    public void InstallAnalyzer_Detects_Stale_Service_And_Tray_Task()
    {
        Directory.CreateDirectory(Path.Combine(_root, "new"));
        Directory.CreateDirectory(Path.Combine(_root, "old"));
        var current = Path.Combine(_root, "new", ProductConstants.AppExecutableName);
        File.WriteAllText(current, "");
        var state = new InstallState
        {
            InstallRoot = Path.Combine(_root, "new"),
            ExecutablePath = current,
            PreviousInstallRoots = [Path.Combine(_root, "old")]
        };
        var service = new ServiceStatusInfo(true, ProductConstants.BrokerServiceName, "Broker", "Stopped", "Manual", "LocalSystem", $"\"{Path.Combine(_root, "old", ProductConstants.AppExecutableName)}\" --broker --service", null);
        var task = new ScheduledTaskInfo(true, ProductConstants.TrayTaskName, "\\WindowsPowerUserMcp Dashboard", "Ready", $"\"{Path.Combine(_root, "old", ProductConstants.AppExecutableName)}\" --dashboard", null);

        var issues = InstallAnalyzer.DetectIssues(state, service, task, current, [state.InstallRoot, Path.Combine(_root, "old")]);

        Assert.Contains(issues, i => i.Code == "stale_service_path");
        Assert.Contains(issues, i => i.Code == "stale_tray_task_path");
        Assert.Contains(issues, i => i.Code == "duplicate_install_roots");
    }

    [Fact]
    public void SemanticVersionComparer_Handles_Releases_And_Prereleases()
    {
        Assert.True(SemanticVersionComparer.Compare("1.2.3", "1.2.2") > 0);
        Assert.True(SemanticVersionComparer.Compare("1.2.3", "1.2.3-preview.1") > 0);
        Assert.True(SemanticVersionComparer.Compare("v2.0.0", "1.9.9") > 0);
        Assert.Equal(0, SemanticVersionComparer.Compare("1.0", "1.0.0"));
    }

    [Fact]
    public void UpdateService_ParseReleases_Reads_GitHub_Assets()
    {
        using var doc = JsonDocument.Parse("""
            [{
              "tag_name": "v0.3.0",
              "name": "WindowsPowerUserMcp 0.3.0",
              "body": "Release notes",
              "published_at": "2026-05-28T10:00:00Z",
              "prerelease": false,
              "assets": [
                { "name": "WindowsPowerUserMcp.zip", "browser_download_url": "https://example.test/app.zip", "size": 42 }
              ]
            }]
            """);

        var releases = UpdateService.ParseReleases(doc.RootElement);

        var release = Assert.Single(releases);
        Assert.Equal("0.3.0", release.Version);
        Assert.Single(release.Assets);
        Assert.Equal("WindowsPowerUserMcp.zip", release.Assets[0].Name);
    }

    [Fact]
    public void UninstallPlan_Default_Preserves_User_Data()
    {
        var plan = new UninstallPlan(@"C:\Tools\WindowsPowerUserMcp", RemoveService: true, RemoveTrayAutostart: true, RemoveBinaries: true, DeleteLogs: false, DeleteScreenshots: false, DeleteTaskLedger: false, DeleteAllUserData: false);

        Assert.False(plan.DeletesData);
    }

    [Fact]
    public void DiagnosticBundle_Redaction_Removes_Secrets()
    {
        var redactor = new SecretRedactor(new WindowsPowerUserMcpOptions());

        var result = redactor.Redact("token=super-secret-value Authorization: Bearer abcdefghijklmnopqrstuvwxyz");

        Assert.DoesNotContain("super-secret-value", result.Text);
        Assert.DoesNotContain("abcdefghijklmnopqrstuvwxyz", result.Text);
    }

    [Fact]
    public void Tray_Autostart_Script_Uses_Valid_RunLevel()
    {
        var repo = FindRepoRoot();
        var script = File.ReadAllText(Path.Combine(repo, "scripts", "install-tray-autostart.ps1"));

        Assert.DoesNotContain("LeastPrivilege", script);
        Assert.Contains("-RunLevel Limited", script);
        Assert.Contains("CreateShortcut", script);
    }

    [Fact]
    public void Conditional_Service_Tests_Are_Opt_In()
    {
        if (Environment.GetEnvironmentVariable("WINMCPD_RUN_SERVICE_TESTS") != "1")
        {
            return;
        }

        Assert.True(AppManager.IsElevated(), "WINMCPD_RUN_SERVICE_TESTS=1 requires an elevated test session.");
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch
        {
        }
    }

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "WindowsPowerUserMcp.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Repo root not found.");
    }
}

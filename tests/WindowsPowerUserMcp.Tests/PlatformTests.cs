using System.Text.Json;
using WindowsPowerUserMcp.Core;
using WindowsPowerUserMcp.Orchestration;
using WindowsPowerUserMcp.Security;

namespace WindowsPowerUserMcp.Tests;

public sealed class PlatformTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "WindowsPowerUserMcp.Tests", Guid.NewGuid().ToString("n"));
    private readonly StorageLayout _layout;
    private readonly WindowsPowerUserMcpOptions _options = new() { DefaultTimeoutSeconds = 5 };

    public PlatformTests()
    {
        _layout = new StorageLayout(
            _root,
            Path.Combine(_root, "db"),
            Path.Combine(_root, "logs"),
            Path.Combine(_root, "tasks"),
            Path.Combine(_root, "processes"),
            Path.Combine(_root, "screenshots"),
            Path.Combine(_root, "handoffs"),
            Path.Combine(_root, "patches"),
            Path.Combine(_root, "db", "test.sqlite3"));
        _layout.EnsureCreated();
    }

    [Fact]
    public void Redactor_Removes_Common_Secrets()
    {
        var redactor = new SecretRedactor(_options);
        var result = redactor.Redact("Authorization: Bearer abcdefghijklmnopqrstuvwxyz password=hunter2 token=eyJaaaaaaaaaaaa.bbbbbbbbbbbbb.cccccccccccc");

        Assert.DoesNotContain("hunter2", result.Text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("abcdefghijklmnopqrstuvwxyz", result.Text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("secret_assignment", result.RedactionsApplied);
    }

    [Fact]
    public void RiskClassifier_Labels_Destructive_And_Readonly()
    {
        var classifier = new RiskClassifier();
        Assert.Equal(RiskLevel.Destructive, classifier.ClassifyTool("delete_file_permanent"));
        Assert.Equal(RiskLevel.ReadOnly, classifier.ClassifyTool("get_system_summary"));
    }

    [Fact]
    public void Ipc_Contracts_Serialize_With_Snake_Case()
    {
        var request = new BrokerRequest("1", "get_system_summary", JsonDefaults.ToElement(new { }), DateTimeOffset.UtcNow);
        var json = JsonSerializer.Serialize(request, JsonDefaults.Options);
        Assert.Contains("request_id", json);
        Assert.Contains("tool_name", json);
    }

    [Fact]
    public void UiPlan_Model_Serializes()
    {
        var plan = new UiPlanRequest("task1", [new UiPlanAction(UiActionType.SendHotkey, null, null, 0, 0, 1000, null, "CTRL+S", null, 0, "stop", false, false, true, true, true, true, true)]);
        var json = JsonSerializer.Serialize(plan, JsonDefaults.Options);
        Assert.Contains("send_hotkey", json);
        Assert.Contains("ctrl", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task TaskLedger_CRUD_And_Continuation_Work()
    {
        var ledger = new TaskLedger(_layout);
        await ledger.InitializeAsync();
        var task = await ledger.StartTaskAsync("Build", "Build the platform", priority: 5);
        await ledger.UpdateTaskFieldsAsync(task.Id, currentSummary: "Started", nextSteps: "Run tests");
        await ledger.AppendEventAsync(task.Id, "codex", "tool", "Ran build", toolName: "run_dotnet_build");
        var continuation = await ledger.QueueContinuationAsync(task.Id, ContinuationConditionType.ManualUserContinue, "{}", "Continue later");

        var loaded = await ledger.GetTaskAsync(task.Id);
        var events = await ledger.ListEventsAsync(task.Id);
        var continuations = await ledger.ListPendingContinuationsAsync();

        Assert.NotNull(loaded);
        Assert.Equal("Started", loaded.CurrentSummary);
        Assert.Single(events);
        Assert.Contains(continuations, c => c.Id == continuation.Id);
    }

    [Fact]
    public async Task CommandRunner_Captures_Stdout_Log()
    {
        var runner = await CreateRunnerAsync();
        var result = await runner.RunAsync(new ProcessStartRequest("cmd.exe", "/d /s /c echo hello", TimeoutSeconds: 10));

        Assert.True(result.Success, result.Message);
        Assert.Contains("hello", result.Data!.StdoutTail, StringComparison.OrdinalIgnoreCase);
        Assert.True(File.Exists(result.Data.StdoutLogPath));
    }

    [Fact]
    public async Task CommandRunner_Times_Out()
    {
        var runner = await CreateRunnerAsync();
        var result = await runner.RunAsync(new ProcessStartRequest("powershell.exe", "-NoProfile -Command \"Start-Sleep -Seconds 5\"", TimeoutSeconds: 1));

        Assert.False(result.Success);
        Assert.Equal(OperationStatus.TimedOut, result.Status);
    }

    [Fact]
    public async Task FileWatcher_Triggers_On_Create()
    {
        var ledger = new TaskLedger(_layout);
        await ledger.InitializeAsync();
        var waits = new WaitServices(ledger);
        var path = Path.Combine(_root, "watched.txt");
        var watch = waits.WatchFileCreatedAsync(path, 5);
        await Task.Delay(250);
        await File.WriteAllTextAsync(path, "done");

        var result = await watch;
        Assert.True(result.Success, result.Message);
    }

    [Fact]
    public void Scripts_And_Codex_Config_Are_Present()
    {
        var repo = FindRepoRoot();
        var installService = File.ReadAllText(Path.Combine(repo, "scripts", "install-service.ps1"));
        var codex = File.ReadAllText(Path.Combine(repo, "config", "codex-config.sample.toml"));

        Assert.Contains("sc.exe create", installService);
        Assert.Contains("Run this script from an elevated PowerShell session", installService);
        Assert.Contains("mcp_servers.windows_power_user", codex);
        Assert.Contains("WindowsPowerUserMcp.StdioBridge", codex);
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

    private async Task<CommandRunner> CreateRunnerAsync()
    {
        var ledger = new TaskLedger(_layout);
        await ledger.InitializeAsync();
        var redactor = new SecretRedactor(_options);
        var audit = new JsonlAuditLogger(_layout, redactor);
        return new CommandRunner(_layout, _options, redactor, new SafetyGuards(), audit, ledger);
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

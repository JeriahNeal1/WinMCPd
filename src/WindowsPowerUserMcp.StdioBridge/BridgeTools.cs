using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;
using WindowsPowerUserMcp.Core;

namespace WindowsPowerUserMcp.StdioBridge;

public sealed class BrokerToolProxy(BrokerPipeClient client)
{
    public Task<JsonElement> InvokeAsync(string toolName, object? args = null) =>
        client.InvokeForMcpAsync(toolName, args);

    public Task<JsonElement> InvokeElementAsync(string toolName, JsonElement arguments, CancellationToken cancellationToken = default) =>
        client.InvokeForMcpAsync(toolName, arguments, cancellationToken);

    public async Task<JsonElement> InvokeJsonAsync(string toolName, string argumentsJson, CancellationToken cancellationToken = default)
    {
        using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(argumentsJson) ? "{}" : argumentsJson);
        return await client.InvokeForMcpAsync(toolName, doc.RootElement.Clone(), cancellationToken).ConfigureAwait(false);
    }
}

[McpServerToolType]
public sealed class WindowsPowerUserTools(BrokerToolProxy proxy)
{
    [McpServerTool, Description("Call any broker tool by name with a JSON object argument payload.")]
    public Task<JsonElement> broker_call(
        [Description("Broker tool name, for example list_directory or process_start_tracked.")] string tool_name,
        [Description("JSON object arguments for the broker tool.")] string arguments_json = "{}") =>
        proxy.InvokeJsonAsync(tool_name, arguments_json);

    [McpServerTool, Description("Get broker status, elevation state, storage roots, and broker-side tool descriptors.")]
    public Task<JsonElement> broker_get_status() => proxy.InvokeAsync("broker_get_status");

    [McpServerTool, Description("List broker tools and implementation status.")]
    public Task<JsonElement> tool_list() => proxy.InvokeAsync("tool_list");

    [McpServerTool, Description("Return system summary for this Windows machine.")]
    public Task<JsonElement> get_system_summary() => proxy.InvokeAsync("get_system_summary");

    [McpServerTool, Description("Return OS version information.")]
    public Task<JsonElement> get_os_info() => proxy.InvokeAsync("get_os_info");

    [McpServerTool, Description("Return whether the broker is elevated/admin.")]
    public Task<JsonElement> get_admin_status() => proxy.InvokeAsync("get_admin_status");

    [McpServerTool, Description("List local drives.")]
    public Task<JsonElement> get_drives() => proxy.InvokeAsync("get_drives");

    [McpServerTool, Description("Get metadata for a path.")]
    public Task<JsonElement> get_path_info(string path) => proxy.InvokeAsync("get_path_info", new { path });

    [McpServerTool, Description("List a directory. Use broker_call for advanced options.")]
    public Task<JsonElement> list_directory(string path, string? search_pattern = null, bool recursive = false, int limit = 500) =>
        proxy.InvokeAsync("list_directory", new { path, search_pattern, recursive, limit });

    [McpServerTool, Description("Read a UTF-8 text file with truncation.")]
    public Task<JsonElement> read_file(string path, int max_bytes = 262144) =>
        proxy.InvokeAsync("read_file", new { path, max_bytes });

    [McpServerTool(Destructive = true), Description("Write a UTF-8 text file.")]
    public Task<JsonElement> write_file(string path, string content, bool overwrite = false) =>
        proxy.InvokeAsync("write_file", new { path, content, overwrite });

    [McpServerTool, Description("Search files under a root path.")]
    public Task<JsonElement> search_files(string root, string pattern, bool recursive = true, int limit = 500) =>
        proxy.InvokeAsync("search_files", new { root, pattern, recursive, limit });

    [McpServerTool, Description("Search text in files under a root path.")]
    public Task<JsonElement> search_text(string root, string pattern, string text, bool recursive = true, int limit = 200) =>
        proxy.InvokeAsync("search_text", new { root, pattern, text, recursive, limit });

    [McpServerTool, Description("Compute a file hash.")]
    public Task<JsonElement> compute_hash(string path, string algorithm = "SHA256") =>
        proxy.InvokeAsync("compute_hash", new { path, algorithm });

    [McpServerTool, Description("Start a durable task ledger entry.")]
    public Task<JsonElement> task_start(string title, string goal, string? owner_user = null, int priority = 0) =>
        proxy.InvokeAsync("task_start", new { title, goal, owner_user, priority });

    [McpServerTool, Description("Get a task by id.")]
    public Task<JsonElement> task_get(string id) => proxy.InvokeAsync("task_get", new { id });

    [McpServerTool, Description("Get task summary and recent events.")]
    public Task<JsonElement> task_get_summary(string id, int limit = 25) =>
        proxy.InvokeAsync("task_get_summary", new { id, limit });

    [McpServerTool, Description("Append an event to a task.")]
    public Task<JsonElement> task_append_event(string task_id, string summary, string actor = "codex", string event_type = "note", string? tool_name = null, string? result_status = null) =>
        proxy.InvokeAsync("task_append_event", new { task_id, summary, actor, event_type, tool_name, result_status });

    [McpServerTool, Description("List active and waiting tasks.")]
    public Task<JsonElement> task_list_active() => proxy.InvokeAsync("task_list_active");

    [McpServerTool, Description("List pending tasks and continuations.")]
    public Task<JsonElement> task_list_pending() => proxy.InvokeAsync("task_list_pending");

    [McpServerTool, Description("Queue a task continuation.")]
    public Task<JsonElement> task_queue_continuation(string task_id, string prompt, string condition_type = "manual_user_continue", string condition_payload_json = "{}", string? due_at = null) =>
        proxy.InvokeAsync("task_queue_continuation", new { task_id, prompt, condition_type, condition_payload_json, due_at });

    [McpServerTool, Description("Mark a task complete.")]
    public Task<JsonElement> task_mark_complete(string id, string? completion_evidence = null) =>
        proxy.InvokeAsync("task_mark_complete", new { id, completion_evidence });

    [McpServerTool, Description("Export a task handoff bundle.")]
    public Task<JsonElement> task_export_handoff_bundle(string id) =>
        proxy.InvokeAsync("task_export_handoff_bundle", new { id });

    [McpServerTool(OpenWorld = true), Description("Run a process with bounded timeout and captured logs.")]
    public Task<JsonElement> run_process(string file_name, string arguments = "", string? working_directory = null, int? timeout_seconds = null) =>
        proxy.InvokeAsync("run_process", new { file_name, arguments, working_directory, timeout_seconds });

    [McpServerTool(OpenWorld = true), Description("Run Windows PowerShell with bounded timeout and captured logs.")]
    public Task<JsonElement> run_powershell(string command, string? working_directory = null, int? timeout_seconds = null) =>
        proxy.InvokeAsync("run_powershell", new { command, working_directory, timeout_seconds });

    [McpServerTool(OpenWorld = true), Description("Run cmd.exe with bounded timeout and captured logs.")]
    public Task<JsonElement> run_cmd(string command, string? working_directory = null, int? timeout_seconds = null) =>
        proxy.InvokeAsync("run_cmd", new { command, working_directory, timeout_seconds });

    [McpServerTool(OpenWorld = true), Description("Start a tracked long-running process.")]
    public Task<JsonElement> process_start_tracked(string file_name, string arguments = "", string? working_directory = null, int? timeout_seconds = null, string? task_id = null, string? expected_completion_signal = null, string? continuation_prompt = null) =>
        proxy.InvokeAsync("process_start_tracked", new { file_name, arguments, working_directory, timeout_seconds, task_id, expected_completion_signal, continuation_prompt });

    [McpServerTool, Description("Get tracked process status.")]
    public Task<JsonElement> process_get_status(string id) => proxy.InvokeAsync("process_get_status", new { id });

    [McpServerTool, Description("Wait for a tracked process.")]
    public Task<JsonElement> process_wait(string id, int timeout_seconds = 60) =>
        proxy.InvokeAsync("process_wait", new { id, timeout_seconds });

    [McpServerTool, Description("Tail tracked process output.")]
    public Task<JsonElement> process_tail_output(string id, int bytes = 65536) =>
        proxy.InvokeAsync("process_tail_output", new { id, bytes });

    [McpServerTool(Destructive = true), Description("Cancel a tracked process.")]
    public Task<JsonElement> process_cancel(string id, bool kill_tree = true) =>
        proxy.InvokeAsync("process_cancel", new { id, kill_tree });

    [McpServerTool, Description("List tracked processes.")]
    public Task<JsonElement> process_list_tracked() => proxy.InvokeAsync("process_list_tracked");

    [McpServerTool, Description("List processes.")]
    public Task<JsonElement> list_processes(string? name_filter = null, int limit = 500) =>
        proxy.InvokeAsync("list_processes", new { name_filter, limit });

    [McpServerTool, Description("Get process detail.")]
    public Task<JsonElement> get_process_detail(int pid) => proxy.InvokeAsync("get_process_detail", new { pid });

    [McpServerTool(Destructive = true), Description("Stop a process by PID.")]
    public Task<JsonElement> stop_process(int pid, bool kill_tree = false) =>
        proxy.InvokeAsync("stop_process", new { pid, kill_tree });

    [McpServerTool, Description("Read registry key or value.")]
    public Task<JsonElement> registry_read(string key_path, string? value_name = null) =>
        proxy.InvokeAsync("registry_read", new { key_path, value_name });

    [McpServerTool, Description("List registry key values/subkeys.")]
    public Task<JsonElement> registry_list(string key_path) =>
        proxy.InvokeAsync("registry_list", new { key_path });

    [McpServerTool, Description("List Windows services.")]
    public Task<JsonElement> list_services(string? name_filter = null) =>
        proxy.InvokeAsync("list_services", new { name_filter });

    [McpServerTool, Description("Get Windows service detail.")]
    public Task<JsonElement> get_service_detail(string service_name) =>
        proxy.InvokeAsync("get_service_detail", new { service_name });

    [McpServerTool, Description("Detect .NET SDKs.")]
    public Task<JsonElement> detect_dotnet_sdks() => proxy.InvokeAsync("detect_dotnet_sdks");

    [McpServerTool, Description("Detect Git.")]
    public Task<JsonElement> detect_git() => proxy.InvokeAsync("detect_git");

    [McpServerTool, Description("Run dotnet build.")]
    public Task<JsonElement> run_dotnet_build(string path, string? working_directory = null, int? timeout_seconds = null) =>
        proxy.InvokeAsync("run_dotnet_build", new { path, working_directory, timeout_seconds });

    [McpServerTool, Description("Run dotnet test.")]
    public Task<JsonElement> run_dotnet_test(string path, string? working_directory = null, int? timeout_seconds = null) =>
        proxy.InvokeAsync("run_dotnet_test", new { path, working_directory, timeout_seconds });

    [McpServerTool, Description("Get a desktop snapshot from DesktopAgent.")]
    public Task<JsonElement> ui_get_desktop_snapshot() => proxy.InvokeAsync("ui_get_desktop_snapshot");

    [McpServerTool, Description("Execute a DesktopAgent UI plan. Provide UiPlanRequest JSON.")]
    public Task<JsonElement> ui_execute_plan(string arguments_json) => proxy.InvokeJsonAsync("ui_execute_plan", arguments_json);

    [McpServerTool, Description("Take a desktop screenshot through DesktopAgent.")]
    public Task<JsonElement> ui_screenshot_desktop() => proxy.InvokeAsync("ui_screenshot_desktop");
}

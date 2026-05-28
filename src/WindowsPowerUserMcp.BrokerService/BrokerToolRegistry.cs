using System.Text;
using System.Text.Json;
using WindowsPowerUserMcp.Core;
using WindowsPowerUserMcp.Orchestration;
using WindowsPowerUserMcp.Security;
using WindowsPowerUserMcp.Windows;

namespace WindowsPowerUserMcp.BrokerService;

using LedgerTaskStatus = WindowsPowerUserMcp.Core.TaskStatus;

public sealed class BrokerToolRegistry(
    WindowsPowerUserMcpOptions options,
    StorageLayout layout,
    SystemOperations system,
    FileSystemOperations files,
    ProcessOperations processes,
    RegistryOperations registryOps,
    ServiceOperations services,
    ToolingOperations tooling,
    CommandRunner commandRunner,
    TaskLedger ledger,
    WaitServices waits)
{
    private readonly Dictionary<string, ToolEntry> _tools = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<ToolDescriptor> Descriptors => _tools.Values.Select(t => t.Descriptor).OrderBy(t => t.Name).ToArray();

    public void RegisterTools()
    {
        if (_tools.Count > 0)
        {
            return;
        }

        Implement("broker_get_status", "Return broker status and tool descriptors.", RiskLevel.ReadOnly, _ => Status());
        Implement("tool_list", "List broker tools and implementation status.", RiskLevel.ReadOnly, _ => ResultEnvelope<object>.Ok(Descriptors, "Tool list.", RiskLevel.ReadOnly));

        Implement("get_system_summary", "Return machine/user/drive summary.", RiskLevel.ReadOnly, _ => system.GetSystemSummary());
        Implement("get_os_info", "Return OS version information.", RiskLevel.ReadOnly, _ => system.GetOsInfo());
        Implement("get_hardware_summary", "Return hardware summary from WMI where available.", RiskLevel.ReadOnly, _ => system.GetHardwareSummary());
        Implement("get_environment_summary", "Return environment variables with secret redaction.", RiskLevel.ReadOnly, _ => system.GetEnvironmentSummary());
        Implement("get_user_context", "Return user/session context.", RiskLevel.ReadOnly, _ => system.GetUserContext());
        Implement("get_admin_status", "Return elevation/admin status.", RiskLevel.ReadOnly, _ => system.GetAdminStatus());
        Implement("get_session_info", "Return current broker session information.", RiskLevel.ReadOnly, _ => system.GetSessionInfo());
        Implement("get_drives", "List Windows drives.", RiskLevel.ReadOnly, _ => system.GetDrives());
        Implement("get_path_info", "Return file/directory metadata.", RiskLevel.ReadOnly, a => system.GetPathInfo(GetString(a, "path")));
        Implement("get_installed_apps", "List installed apps from registry uninstall keys.", RiskLevel.ReadOnly, _ => system.GetInstalledApps());
        Implement("get_startup_items", "List visible startup folder and Run key items.", RiskLevel.ReadOnly, _ => system.GetStartupItems());

        Shell("get_windows_features", "Read Windows optional feature state.", "powershell.exe", "-NoProfile -Command \"Get-WindowsOptionalFeature -Online | Select-Object FeatureName,State | ConvertTo-Json -Depth 3\"", RiskLevel.ReadOnly);
        Shell("get_windows_updates_status", "Read recent Windows update hotfix state.", "powershell.exe", "-NoProfile -Command \"Get-HotFix | Sort-Object InstalledOn -Descending | Select-Object -First 30 | ConvertTo-Json -Depth 3\"", RiskLevel.ReadOnly);
        Shell("get_defender_status_readonly", "Read Microsoft Defender status.", "powershell.exe", "-NoProfile -Command \"Get-MpComputerStatus | ConvertTo-Json -Depth 4\"", RiskLevel.ReadOnly);
        Shell("get_firewall_status_readonly", "Read Windows Firewall profile status.", "powershell.exe", "-NoProfile -Command \"Get-NetFirewallProfile | Select-Object Name,Enabled,DefaultInboundAction,DefaultOutboundAction | ConvertTo-Json -Depth 3\"", RiskLevel.ReadOnly);

        Implement("list_processes", "List processes.", RiskLevel.ReadOnly, a => processes.ListProcesses(GetOptionalString(a, "name_filter"), GetInt(a, "limit", 500)));
        Implement("get_process_detail", "Get process detail.", RiskLevel.ReadOnly, a => processes.GetProcessDetail(GetInt(a, "pid")));
        Implement("start_process", "Start an interactive process without capture.", RiskLevel.Medium, a => processes.StartProcess(GetString(a, "file_name"), GetOptionalString(a, "arguments") ?? "", GetOptionalString(a, "working_directory")));
        Implement("stop_process", "Stop a process by PID.", RiskLevel.Destructive, a => processes.StopProcess(GetInt(a, "pid"), GetBool(a, "kill_tree", false)));
        Alias("kill_process_tree", "stop_process", "Kill a process tree by PID.", RiskLevel.Destructive);
        Implement("get_process_modules", "List loaded modules for a process where Windows permits access.", RiskLevel.ReadOnly, a => processes.GetProcessModules(GetInt(a, "pid")));
        Implement("get_process_open_windows", "List top-level visible windows owned by a process.", RiskLevel.ReadOnly, a => processes.GetProcessOpenWindows(GetInt(a, "pid")));
        Implement("get_process_network_connections", "List TCP/UDP network connections owned by a process.", RiskLevel.ReadOnly, a => GetProcessNetworkConnectionsAsync(GetInt(a, "pid")));

        Implement("run_process", "Run a process with bounded timeout and captured logs.", RiskLevel.Medium, a => commandRunner.RunAsync(new ProcessStartRequest(GetString(a, "file_name"), GetOptionalString(a, "arguments") ?? "", GetOptionalString(a, "working_directory"), GetOptionalInt(a, "timeout_seconds"))));
        Implement("run_powershell", "Run Windows PowerShell with bounded timeout and captured logs.", RiskLevel.Medium, a => commandRunner.RunAsync(new ProcessStartRequest("powershell.exe", $"-NoProfile -Command {Quote(GetString(a, "command"))}", GetOptionalString(a, "working_directory"), GetOptionalInt(a, "timeout_seconds"))));
        Implement("run_cmd", "Run cmd.exe with bounded timeout and captured logs.", RiskLevel.Medium, a => commandRunner.RunAsync(new ProcessStartRequest("cmd.exe", $"/d /s /c {Quote(GetString(a, "command"))}", GetOptionalString(a, "working_directory"), GetOptionalInt(a, "timeout_seconds"))));
        Implement("run_script_file", "Run a PowerShell script file.", RiskLevel.Medium, a => commandRunner.RunAsync(new ProcessStartRequest("powershell.exe", $"-NoProfile -File {Quote(GetString(a, "path"))}", GetOptionalString(a, "working_directory"), GetOptionalInt(a, "timeout_seconds"))));
        Implement("run_wsl_command", "Run a command through WSL.", RiskLevel.Medium, a => commandRunner.RunAsync(new ProcessStartRequest("wsl.exe", GetString(a, "command"), GetOptionalString(a, "working_directory"), GetOptionalInt(a, "timeout_seconds"))));
        Implement("run_elevated_process_if_already_elevated", "Run a process only if this broker is already elevated.", RiskLevel.High, a => SystemOperations.IsElevated()
            ? commandRunner.RunAsync(new ProcessStartRequest(GetString(a, "file_name"), GetOptionalString(a, "arguments") ?? "", GetOptionalString(a, "working_directory"), GetOptionalInt(a, "timeout_seconds")))
            : Task.FromResult(ResultEnvelope<CommandExecutionResult>.Fail("not_elevated", "Broker is not elevated. Start/install the broker elevated; no UAC bypass is attempted.", OperationStatus.Failed, RiskLevel.High)));
        Alias("run_admin_task", "run_elevated_process_if_already_elevated", "Run an admin task only when broker is already elevated.", RiskLevel.High);

        Implement("process_start_tracked", "Start a tracked long-running process.", RiskLevel.Medium, a => commandRunner.StartTrackedAsync(new ProcessStartRequest(GetString(a, "file_name"), GetOptionalString(a, "arguments") ?? "", GetOptionalString(a, "working_directory"), GetOptionalInt(a, "timeout_seconds"), GetOptionalString(a, "task_id"), GetOptionalString(a, "expected_completion_signal"), GetOptionalString(a, "continuation_prompt"))));
        Implement("process_get_status", "Get tracked process status.", RiskLevel.ReadOnly, a => commandRunner.GetStatusAsync(GetString(a, "id")));
        Implement("process_wait", "Wait for a tracked process.", RiskLevel.ReadOnly, a => commandRunner.WaitAsync(GetString(a, "id"), GetInt(a, "timeout_seconds", 60)));
        Implement("process_read_stdout", "Read tracked process stdout tail.", RiskLevel.ReadOnly, async a => await ReadTrackedLogAsync(GetString(a, "id"), stdout: true, GetInt(a, "bytes", 65536)).ConfigureAwait(false));
        Implement("process_read_stderr", "Read tracked process stderr tail.", RiskLevel.ReadOnly, async a => await ReadTrackedLogAsync(GetString(a, "id"), stdout: false, GetInt(a, "bytes", 65536)).ConfigureAwait(false));
        Implement("process_tail_output", "Read stdout and stderr tails for a tracked process.", RiskLevel.ReadOnly, async a =>
        {
            var status = await ledger.GetTrackedProcessAsync(GetString(a, "id")).ConfigureAwait(false);
            return status is null
                ? ResultEnvelope<object>.Fail("process_not_found", "Tracked process was not found.", OperationStatus.Failed, RiskLevel.ReadOnly)
                : ResultEnvelope<object>.Ok(new { stdout = commandRunner.ReadLogTail(status.StdoutLogPath, GetInt(a, "bytes", 65536)).Data, stderr = commandRunner.ReadLogTail(status.StderrLogPath, GetInt(a, "bytes", 65536)).Data }, "Tracked output tails.", RiskLevel.ReadOnly);
        });
        Implement("process_cancel", "Cancel a tracked process.", RiskLevel.Destructive, a => commandRunner.CancelAsync(GetString(a, "id"), GetBool(a, "kill_tree", true)));
        Alias("process_kill_tree", "process_cancel", "Kill a tracked process tree.", RiskLevel.Destructive);
        Implement("process_list_tracked", "List tracked processes.", RiskLevel.ReadOnly, _ => commandRunner.ListTrackedAsync());
        Implement("process_mark_requires_user_intervention", "Mark tracked process as waiting for user intervention.", RiskLevel.Low, a => commandRunner.MarkRequiresUserInterventionAsync(GetString(a, "id"), GetOptionalString(a, "continuation_prompt")));
        Implement("process_generate_continuation_prompt", "Generate a continuation prompt for a tracked process.", RiskLevel.Low, async a =>
        {
            var id = GetString(a, "id");
            var record = await ledger.GetTrackedProcessAsync(id).ConfigureAwait(false);
            return record is null
                ? ResultEnvelope<object>.Fail("process_not_found", $"Tracked process '{id}' was not found.", OperationStatus.Failed, RiskLevel.ReadOnly)
                : ResultEnvelope<object>.Ok(new { prompt = $"Resume tracked process {record.Id}. Check status, inspect logs at {record.StdoutLogPath} and {record.StderrLogPath}, then continue the task. Last known status: {record.Status}." }, "Continuation prompt generated.", RiskLevel.Low);
        });
        Scaffold("process_attach_to_task", "Attaching an existing tracked process to another task is scaffolded; start with task_id for now.", RiskLevel.Low);

        RegisterTaskTools();
        RegisterFileTools();
        RegisterRegistryTools();
        RegisterServiceTools();
        RegisterEventAndScheduledTools();
        RegisterPackageAndDevTools();
        RegisterWaitAndWatchTools();
        RegisterUiDelegates();
        RegisterAgentDelegationTools();
    }

    public Task<object> InvokeAsync(string name, JsonElement args, CancellationToken cancellationToken)
    {
        if (!_tools.TryGetValue(name, out var entry))
        {
            return Task.FromResult<object>(ResultEnvelope<object>.Fail("tool_not_found", $"Broker tool '{name}' is not registered.", OperationStatus.Failed, RiskLevel.Low));
        }

        return entry.Handler(args, cancellationToken);
    }

    private void RegisterTaskTools()
    {
        Implement("task_start", "Start a durable task ledger entry.", RiskLevel.Low, async a => ResultEnvelope<TaskRecord>.Ok(await ledger.StartTaskAsync(GetString(a, "title"), GetString(a, "goal"), GetOptionalString(a, "owner_user"), GetInt(a, "priority", 0)).ConfigureAwait(false), "Task started.", RiskLevel.Low));
        Implement("task_get", "Get a task by id.", RiskLevel.ReadOnly, async a => WrapNullable(await ledger.GetTaskAsync(GetString(a, "id")).ConfigureAwait(false), "task_not_found", "Task was not found."));
        Implement("task_get_summary", "Get task summary and recent events.", RiskLevel.ReadOnly, async a =>
        {
            var task = await ledger.GetTaskAsync(GetString(a, "id")).ConfigureAwait(false);
            if (task is null) return ResultEnvelope<object>.Fail("task_not_found", "Task was not found.", OperationStatus.Failed, RiskLevel.ReadOnly);
            var events = await ledger.ListEventsAsync(task.Id, GetInt(a, "limit", 25)).ConfigureAwait(false);
            return ResultEnvelope<object>.Ok(new { task, events }, "Task summary.", RiskLevel.ReadOnly);
        });
        Implement("task_append_event", "Append a durable task event.", RiskLevel.Low, async a => ResultEnvelope<TaskEventRecord>.Ok(await ledger.AppendEventAsync(GetString(a, "task_id"), GetOptionalString(a, "actor") ?? "codex", GetOptionalString(a, "event_type") ?? "note", GetString(a, "summary"), GetOptionalString(a, "tool_name"), GetOptionalString(a, "command_line_redacted"), GetOptionalString(a, "working_directory"), GetOptionalString(a, "result_status")).ConfigureAwait(false), "Task event appended.", RiskLevel.Low));
        Implement("task_update_summary", "Update task current summary.", RiskLevel.Low, async a => { await ledger.UpdateTaskFieldsAsync(GetString(a, "task_id"), currentSummary: GetString(a, "current_summary")).ConfigureAwait(false); return ResultEnvelope<object>.Ok(new { task_id = GetString(a, "task_id") }, "Task summary updated.", RiskLevel.Low); });
        Implement("task_set_next_steps", "Update task next steps.", RiskLevel.Low, async a => { await ledger.UpdateTaskFieldsAsync(GetString(a, "task_id"), nextSteps: GetString(a, "next_steps")).ConfigureAwait(false); return ResultEnvelope<object>.Ok(new { task_id = GetString(a, "task_id") }, "Task next steps updated.", RiskLevel.Low); });
        Implement("task_queue_continuation", "Queue a pending continuation.", RiskLevel.Low, async a => ResultEnvelope<PendingContinuationRecord>.Ok(await ledger.QueueContinuationAsync(GetString(a, "task_id"), ParseCondition(GetOptionalString(a, "condition_type") ?? "manual_user_continue"), GetOptionalString(a, "condition_payload_json") ?? "{}", GetString(a, "prompt"), GetOptionalDate(a, "due_at")).ConfigureAwait(false), "Continuation queued.", RiskLevel.Low));
        Implement("task_list_pending", "List waiting/blocked active tasks and pending continuations.", RiskLevel.ReadOnly, async _ => ResultEnvelope<object>.Ok(new { tasks = await ledger.ListTasksAsync([LedgerTaskStatus.Waiting, LedgerTaskStatus.Blocked]).ConfigureAwait(false), continuations = await ledger.ListPendingContinuationsAsync().ConfigureAwait(false) }, "Pending tasks.", RiskLevel.ReadOnly));
        Implement("task_list_active", "List active tasks.", RiskLevel.ReadOnly, async _ => ResultEnvelope<object>.Ok(await ledger.ListTasksAsync([LedgerTaskStatus.Active, LedgerTaskStatus.Waiting, LedgerTaskStatus.Blocked]).ConfigureAwait(false), "Active tasks.", RiskLevel.ReadOnly));
        Implement("task_resume", "Mark a waiting task active and return context.", RiskLevel.Low, async a => { var id = GetString(a, "id"); await ledger.UpdateTaskFieldsAsync(id, status: LedgerTaskStatus.Active).ConfigureAwait(false); var task = await ledger.GetTaskAsync(id).ConfigureAwait(false); return WrapNullable(task, "task_not_found", "Task was not found."); });
        StatusTool("task_mark_complete", LedgerTaskStatus.Complete, "Task marked complete.");
        StatusTool("task_mark_blocked", LedgerTaskStatus.Blocked, "Task marked blocked.");
        StatusTool("task_mark_failed", LedgerTaskStatus.Failed, "Task marked failed.");
        StatusTool("task_mark_cancelled", LedgerTaskStatus.Cancelled, "Task marked cancelled.");
        Alias("task_get_completion_evidence", "task_get", "Get task completion evidence.", RiskLevel.ReadOnly);
        Implement("task_generate_final_summary", "Generate a structured final task summary from ledger state.", RiskLevel.Low, async a => await GenerateTaskSummaryAsync(GetString(a, "id")).ConfigureAwait(false));
        Alias("task_generate_handoff_prompt", "task_generate_final_summary", "Generate a handoff prompt from ledger state.", RiskLevel.Low);
        Implement("task_export_handoff_bundle", "Export a task handoff bundle JSON file.", RiskLevel.Low, async a =>
        {
            var summary = await GenerateTaskSummaryAsync(GetString(a, "id")).ConfigureAwait(false);
            var path = Path.Combine(layout.Handoffs, $"handoff-{GetString(a, "id")}-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}.json");
            await File.WriteAllTextAsync(path, JsonSerializer.Serialize(summary, JsonDefaults.Options)).ConfigureAwait(false);
            return ResultEnvelope<object>.Ok(new { path }, "Handoff bundle exported.", RiskLevel.Low);
        });
        Implement("task_import_handoff_bundle", "Import a handoff bundle JSON file as an event on a task.", RiskLevel.Low, async a =>
        {
            var content = await File.ReadAllTextAsync(GetString(a, "path")).ConfigureAwait(false);
            var task = await ledger.StartTaskAsync(GetOptionalString(a, "title") ?? "Imported handoff", GetOptionalString(a, "goal") ?? "Continue imported handoff").ConfigureAwait(false);
            await ledger.AppendEventAsync(task.Id, "mcp", "handoff_imported", content).ConfigureAwait(false);
            return ResultEnvelope<object>.Ok(new { task }, "Handoff imported.", RiskLevel.Low);
        });
    }

    private void RegisterFileTools()
    {
        Implement("list_directory", "List directory entries.", RiskLevel.ReadOnly, a => files.ListDirectory(GetString(a, "path"), GetOptionalString(a, "search_pattern"), GetBool(a, "recursive", false), GetInt(a, "limit", 500)));
        Implement("read_file", "Read a UTF-8 text file with truncation.", RiskLevel.ReadOnly, a => files.ReadFileAsync(GetString(a, "path"), GetInt(a, "max_bytes", 262144)));
        Implement("write_file", "Write a UTF-8 text file.", RiskLevel.Medium, a => files.WriteFileAsync(GetString(a, "path"), GetString(a, "content"), GetBool(a, "overwrite", false)));
        Implement("append_file", "Append UTF-8 text to a file.", RiskLevel.Medium, a => files.AppendFileAsync(GetString(a, "path"), GetString(a, "content")));
        Implement("create_directory", "Create a directory.", RiskLevel.Low, a => files.CreateDirectory(GetString(a, "path")));
        Implement("search_files", "Search files by glob pattern.", RiskLevel.ReadOnly, a => files.SearchFiles(GetString(a, "root"), GetString(a, "pattern"), GetBool(a, "recursive", true), GetInt(a, "limit", 500)));
        Implement("search_text", "Search text in files.", RiskLevel.ReadOnly, a => files.SearchTextAsync(GetString(a, "root"), GetString(a, "pattern"), GetString(a, "text"), GetBool(a, "recursive", true), GetInt(a, "limit", 200)));
        Implement("compute_hash", "Compute a file hash.", RiskLevel.ReadOnly, a => files.ComputeHashAsync(GetString(a, "path"), GetOptionalString(a, "algorithm") ?? "SHA256"));
        Implement("copy_file", "Copy a file.", RiskLevel.Medium, a => files.CopyFile(GetString(a, "source"), GetString(a, "destination"), GetBool(a, "overwrite", false)));
        Implement("move_file", "Move a file.", RiskLevel.Medium, a => files.MoveFile(GetString(a, "source"), GetString(a, "destination"), GetBool(a, "overwrite", false)));
        Implement("delete_file_to_recycle_bin", "Delete a file to the recycle bin.", RiskLevel.Destructive, a => files.DeleteFileToRecycleBin(GetString(a, "path")));
        Implement("delete_file_permanent", "Permanently delete a file.", RiskLevel.Destructive, a => files.DeleteFilePermanent(GetString(a, "path")));
        Implement("create_backup", "Create a timestamped file backup.", RiskLevel.Low, a => files.CreateBackup(GetString(a, "path")));
        Implement("create_zip_archive", "Create a zip archive.", RiskLevel.Medium, a => files.CreateZipArchive(GetString(a, "source_directory"), GetString(a, "destination_zip"), GetBool(a, "overwrite", false)));
        Implement("extract_zip_archive", "Extract a zip archive.", RiskLevel.Medium, a => files.ExtractZipArchive(GetString(a, "zip_path"), GetString(a, "destination_directory"), GetBool(a, "overwrite", false)));
        Implement("apply_unified_diff_patch", "Apply or dry-run a unified diff with path validation and backups.", RiskLevel.Medium, a => files.ApplyUnifiedDiffPatch(GetString(a, "patch_text"), GetOptionalString(a, "root"), GetBool(a, "dry_run", true), GetBool(a, "backup", true)));
        Implement("get_file_acl", "Read file or directory ACLs including owner and SDDL.", RiskLevel.ReadOnly, a => files.GetFileAcl(GetString(a, "path")));
        Implement("set_file_acl", "Add a file or directory ACL rule after writing an SDDL backup.", RiskLevel.High, a => files.SetFileAcl(GetString(a, "path"), GetString(a, "identity"), GetString(a, "rights"), GetOptionalString(a, "access_type") ?? "Allow", GetBool(a, "inherit", false)));
        Implement("take_ownership_if_elevated", "Take file or directory ownership only when broker is already elevated.", RiskLevel.Destructive, a => files.TakeOwnershipIfElevated(GetString(a, "path")));
    }

    private void RegisterRegistryTools()
    {
        Implement("registry_read", "Read a registry key or value.", RiskLevel.ReadOnly, a => registryOps.Read(GetString(a, "key_path"), GetOptionalString(a, "value_name")));
        Implement("registry_list", "List registry key values/subkeys.", RiskLevel.ReadOnly, a => registryOps.List(GetString(a, "key_path")));
        Implement("registry_export", "Export a registry key to .reg.", RiskLevel.ReadOnly, a => registryOps.ExportAsync(GetString(a, "key_path"), GetOptionalString(a, "destination_reg_file")));
        Implement("registry_write", "Write a registry value after automatic export backup.", RiskLevel.High, a => registryOps.WriteAsync(GetString(a, "key_path"), GetString(a, "value_name"), GetString(a, "value"), GetOptionalString(a, "value_kind") ?? "String"));
        Implement("registry_delete", "Delete a registry value/key after automatic export backup.", RiskLevel.Destructive, a => registryOps.DeleteAsync(GetString(a, "key_path"), GetOptionalString(a, "value_name"), GetBool(a, "recursive", false)));
        Implement("registry_import_file", "Import a .reg file.", RiskLevel.High, a => registryOps.ImportFileAsync(GetString(a, "reg_file")));
    }

    private void RegisterServiceTools()
    {
        Implement("list_services", "List Windows services.", RiskLevel.ReadOnly, a => services.ListServices(GetOptionalString(a, "name_filter")));
        Implement("get_service_detail", "Get Windows service detail.", RiskLevel.ReadOnly, a => services.GetServiceDetail(GetString(a, "service_name")));
        Implement("start_service", "Start a Windows service.", RiskLevel.High, a => services.StartServiceAsync(GetString(a, "service_name"), GetInt(a, "timeout_seconds", 60)));
        Implement("stop_service", "Stop a Windows service.", RiskLevel.High, a => services.StopServiceAsync(GetString(a, "service_name"), GetInt(a, "timeout_seconds", 60)));
        Implement("restart_service", "Restart a Windows service.", RiskLevel.High, a => services.RestartServiceAsync(GetString(a, "service_name"), GetInt(a, "timeout_seconds", 120)));
        Implement("set_service_startup_type", "Set Windows service startup type.", RiskLevel.High, a => services.SetStartupTypeAsync(GetString(a, "service_name"), GetString(a, "startup_type")));
        Implement("get_service_recovery_options", "Read service recovery options through sc.exe.", RiskLevel.ReadOnly, a => services.GetRecoveryOptionsAsync(GetString(a, "service_name")));
        Implement("set_service_recovery_options", "Set service recovery options through sc.exe.", RiskLevel.High, a => services.SetRecoveryOptionsAsync(GetString(a, "service_name"), GetOptionalString(a, "actions") ?? "restart/60000/restart/60000/none/60000", GetInt(a, "reset_seconds", 86400)));
    }

    private void RegisterEventAndScheduledTools()
    {
        Implement("query_event_logs", "Query recent Windows events.", RiskLevel.ReadOnly, a => commandRunner.RunAsync(new ProcessStartRequest("powershell.exe", $"-NoProfile -Command {Quote($"Get-WinEvent -LogName '{GetString(a, "log_name")}' -MaxEvents {GetInt(a, "max_events", 50)} | Select-Object TimeCreated,Id,LevelDisplayName,ProviderName,Message | ConvertTo-Json -Depth 4")}", TimeoutSeconds: GetOptionalInt(a, "timeout_seconds"))));
        Implement("get_recent_errors", "Get recent Application/System errors.", RiskLevel.ReadOnly, _ => commandRunner.RunAsync(new ProcessStartRequest("powershell.exe", "-NoProfile -Command \"Get-WinEvent -FilterHashtable @{LogName='Application','System'; Level=2; StartTime=(Get-Date).AddDays(-1)} -MaxEvents 100 | Select-Object TimeCreated,LogName,Id,ProviderName,Message | ConvertTo-Json -Depth 4\"", TimeoutSeconds: 60)));
        Implement("get_app_crash_events", "Get recent app crash events.", RiskLevel.ReadOnly, _ => commandRunner.RunAsync(new ProcessStartRequest("powershell.exe", "-NoProfile -Command \"Get-WinEvent -FilterHashtable @{LogName='Application'; Id=1000} -MaxEvents 50 | Select-Object TimeCreated,ProviderName,Message | ConvertTo-Json -Depth 4\"", TimeoutSeconds: 60)));
        Implement("get_windows_update_events", "Get recent Windows Update client events.", RiskLevel.ReadOnly, _ => commandRunner.RunAsync(new ProcessStartRequest("powershell.exe", "-NoProfile -Command \"Get-WinEvent -LogName 'Microsoft-Windows-WindowsUpdateClient/Operational' -MaxEvents 50 | Select-Object TimeCreated,Id,LevelDisplayName,Message | ConvertTo-Json -Depth 4\"", TimeoutSeconds: 60)));
        Alias("export_event_log_slice", "query_event_logs", "Export event log slice via captured command output.", RiskLevel.ReadOnly);
        Implement("watch_event_log", "Start a durable tracked event-log watcher and queue a continuation on watcher exit.", RiskLevel.ReadOnly, a => WatchEventLogAsync(a));

        Implement("list_scheduled_tasks", "List scheduled tasks.", RiskLevel.ReadOnly, _ => commandRunner.RunAsync(new ProcessStartRequest("schtasks.exe", "/query /fo LIST /v", TimeoutSeconds: 60)));
        Implement("get_scheduled_task", "Get scheduled task by name.", RiskLevel.ReadOnly, a => commandRunner.RunAsync(new ProcessStartRequest("schtasks.exe", $"/query /tn {Quote(GetString(a, "task_name"))} /fo LIST /v", TimeoutSeconds: 60)));
        Implement("create_scheduled_task", "Create a scheduled task using schtasks arguments.", RiskLevel.High, a => commandRunner.RunAsync(new ProcessStartRequest("schtasks.exe", GetString(a, "arguments"), TimeoutSeconds: GetOptionalInt(a, "timeout_seconds"))));
        Implement("enable_scheduled_task", "Enable a scheduled task.", RiskLevel.High, a => commandRunner.RunAsync(new ProcessStartRequest("schtasks.exe", $"/change /tn {Quote(GetString(a, "task_name"))} /enable")));
        Implement("disable_scheduled_task", "Disable a scheduled task.", RiskLevel.High, a => commandRunner.RunAsync(new ProcessStartRequest("schtasks.exe", $"/change /tn {Quote(GetString(a, "task_name"))} /disable")));
        Implement("delete_scheduled_task", "Delete a scheduled task.", RiskLevel.Destructive, a => commandRunner.RunAsync(new ProcessStartRequest("schtasks.exe", $"/delete /tn {Quote(GetString(a, "task_name"))} /f")));
        Implement("run_scheduled_task", "Run a scheduled task.", RiskLevel.High, a => commandRunner.RunAsync(new ProcessStartRequest("schtasks.exe", $"/run /tn {Quote(GetString(a, "task_name"))}")));
    }

    private void RegisterPackageAndDevTools()
    {
        Detect("winget_detect", "winget");
        Implement("winget_search", "Search packages with winget.", RiskLevel.ReadOnly, a => tooling.WingetAsync($"search {GetString(a, "query")}"));
        Implement("winget_install", "Install a package with winget.", RiskLevel.High, a => tooling.WingetAsync($"install {GetString(a, "package")}"));
        Implement("winget_upgrade", "Upgrade packages with winget.", RiskLevel.High, a => tooling.WingetAsync(GetOptionalString(a, "package") is { } p ? $"upgrade {p}" : "upgrade --all"));
        Implement("winget_uninstall", "Uninstall a package with winget.", RiskLevel.Destructive, a => tooling.WingetAsync($"uninstall {GetString(a, "package")}"));
        Detect("choco_detect", "choco");
        Implement("choco_install", "Install a package with Chocolatey.", RiskLevel.High, a => commandRunner.RunAsync(new ProcessStartRequest("choco", $"install {GetString(a, "package")} -y")));
        Detect("scoop_detect", "scoop");
        Implement("scoop_install", "Install a package with Scoop.", RiskLevel.High, a => commandRunner.RunAsync(new ProcessStartRequest("scoop", $"install {GetString(a, "package")}")));

        Detect("detect_dotnet_sdks", "dotnet", "--list-sdks");
        Detect("detect_git", "git");
        Detect("detect_node", "node");
        Detect("detect_pnpm", "pnpm");
        Detect("detect_python", "python");
        Detect("detect_uv", "uv");
        Detect("detect_java", "java", "-version");
        Detect("detect_vscode", "code", "--version");
        Detect("detect_visual_studio", "where.exe", "devenv.exe");
        Detect("detect_visual_studio_build_tools", "where.exe", "MSBuild.exe");
        Detect("detect_windows_sdk", "where.exe", "signtool.exe");
        Detect("detect_android_sdk", "where.exe", "adb.exe");
        Detect("detect_unity", "where.exe", "Unity.exe");

        Implement("inspect_project_structure", "Inspect a project directory structure.", RiskLevel.ReadOnly, a => files.ListDirectory(GetString(a, "path"), null, recursive: true, GetInt(a, "limit", 1000)));
        Implement("summarize_repo_state", "Summarize git repo state.", RiskLevel.ReadOnly, a => commandRunner.RunAsync(new ProcessStartRequest("git", "status --short --branch", GetString(a, "working_directory"), TimeoutSeconds: 30)));
        Implement("run_solution_build", "Run dotnet build on a solution/project.", RiskLevel.Medium, a => commandRunner.RunAsync(new ProcessStartRequest("dotnet", $"build {Quote(GetString(a, "path"))}", GetOptionalString(a, "working_directory"), GetOptionalInt(a, "timeout_seconds"))));
        Alias("run_dotnet_build", "run_solution_build", "Run dotnet build.", RiskLevel.Medium);
        Implement("run_dotnet_test", "Run dotnet test.", RiskLevel.Medium, a => commandRunner.RunAsync(new ProcessStartRequest("dotnet", $"test {Quote(GetString(a, "path"))}", GetOptionalString(a, "working_directory"), GetOptionalInt(a, "timeout_seconds"))));
        Implement("run_msbuild", "Run MSBuild.", RiskLevel.Medium, a => commandRunner.RunAsync(new ProcessStartRequest("MSBuild.exe", GetString(a, "arguments"), GetOptionalString(a, "working_directory"), GetOptionalInt(a, "timeout_seconds"))));
        Implement("run_npm_script", "Run npm script.", RiskLevel.Medium, a => commandRunner.RunAsync(new ProcessStartRequest("npm", $"run {GetString(a, "script")}", GetString(a, "working_directory"), GetOptionalInt(a, "timeout_seconds"))));
        Implement("run_pnpm_script", "Run pnpm script.", RiskLevel.Medium, a => commandRunner.RunAsync(new ProcessStartRequest("pnpm", $"run {GetString(a, "script")}", GetString(a, "working_directory"), GetOptionalInt(a, "timeout_seconds"))));
        Implement("run_python", "Run python.", RiskLevel.Medium, a => commandRunner.RunAsync(new ProcessStartRequest("python", GetString(a, "arguments"), GetOptionalString(a, "working_directory"), GetOptionalInt(a, "timeout_seconds"))));
        Implement("run_git", "Run git.", RiskLevel.Medium, a => commandRunner.RunAsync(new ProcessStartRequest("git", GetString(a, "arguments"), GetOptionalString(a, "working_directory"), GetOptionalInt(a, "timeout_seconds"))));
        Alias("run_lint", "run_npm_script", "Run a lint script.", RiskLevel.Medium);
        Alias("run_tests", "run_dotnet_test", "Run tests.", RiskLevel.Medium);
        Scaffold("generate_codex_handoff_summary", "Use task_generate_final_summary for durable handoffs in this milestone.", RiskLevel.Low);

        Implement("wsl_status", "Get WSL status.", RiskLevel.ReadOnly, _ => tooling.WslAsync("--status"));
        Implement("wsl_list_distros", "List WSL distributions.", RiskLevel.ReadOnly, _ => tooling.WslAsync("--list --verbose"));
        Implement("wsl_run_command", "Run WSL command.", RiskLevel.Medium, a => tooling.WslAsync(GetString(a, "command")));
        Alias("wsl_open_project", "wsl_run_command", "Open/check a WSL project command.", RiskLevel.Medium);
        Alias("wsl_check_tooling", "wsl_run_command", "Check WSL tooling.", RiskLevel.ReadOnly);

        Implement("docker_status", "Get Docker status.", RiskLevel.ReadOnly, _ => tooling.DockerAsync("version"));
        Implement("docker_list_containers", "List Docker containers.", RiskLevel.ReadOnly, _ => tooling.DockerAsync("ps -a"));
        Implement("docker_list_images", "List Docker images.", RiskLevel.ReadOnly, _ => tooling.DockerAsync("images"));
        Implement("docker_compose_ps", "Run docker compose ps.", RiskLevel.ReadOnly, a => commandRunner.RunAsync(new ProcessStartRequest("docker", "compose ps", GetOptionalString(a, "working_directory"))));
        Implement("docker_compose_up", "Run docker compose up.", RiskLevel.Medium, a => commandRunner.RunAsync(new ProcessStartRequest("docker", "compose up -d", GetOptionalString(a, "working_directory"), GetOptionalInt(a, "timeout_seconds"))));
        Implement("docker_compose_down", "Run docker compose down.", RiskLevel.Medium, a => commandRunner.RunAsync(new ProcessStartRequest("docker", "compose down", GetOptionalString(a, "working_directory"), GetOptionalInt(a, "timeout_seconds"))));
        Implement("docker_logs", "Read Docker logs.", RiskLevel.ReadOnly, a => tooling.DockerAsync($"logs {GetString(a, "container")} --tail {GetInt(a, "tail", 200)}"));
        Implement("docker_exec", "Run docker exec.", RiskLevel.Medium, a => tooling.DockerAsync($"exec {GetString(a, "container")} {GetString(a, "command")}"));

        Detect("adb_detect", "adb");
        Implement("adb_devices", "List ADB devices.", RiskLevel.ReadOnly, _ => tooling.AdbAsync("devices -l"));
        Implement("adb_shell", "Run adb shell.", RiskLevel.Medium, a => tooling.AdbAsync($"shell {GetString(a, "command")}"));
        Implement("adb_install_apk", "Install APK through adb.", RiskLevel.High, a => tooling.AdbAsync($"install {Quote(GetString(a, "apk_path"))}"));
        Implement("adb_logcat", "Read adb logcat.", RiskLevel.ReadOnly, a => tooling.AdbAsync($"logcat -d -t {GetInt(a, "lines", 500)}"));
        Implement("adb_reboot_device", "Reboot ADB device.", RiskLevel.High, _ => tooling.AdbAsync("reboot"));
    }

    private void RegisterWaitAndWatchTools()
    {
        Implement("wait_until_time", "Wait until a specific time.", RiskLevel.ReadOnly, a => waits.WaitUntilTimeAsync(GetDate(a, "due_at")));
        Implement("wait_until_process_exit", "Wait until tracked process exits.", RiskLevel.ReadOnly, a => commandRunner.WaitAsync(GetString(a, "id"), GetInt(a, "timeout_seconds", 300)));
        Implement("wait_until_file_exists", "Wait for a file/path to exist.", RiskLevel.ReadOnly, a => waits.WaitUntilFileExistsAsync(GetString(a, "path"), GetInt(a, "timeout_seconds", 300)));
        Implement("wait_until_port_open", "Wait for TCP port to open.", RiskLevel.ReadOnly, a => waits.WaitUntilPortOpenAsync(GetString(a, "host"), GetInt(a, "port"), GetInt(a, "timeout_seconds", 300)));
        Implement("watch_file_created", "Watch for file creation with bounded timeout.", RiskLevel.ReadOnly, a => waits.WatchFileCreatedAsync(GetString(a, "path"), GetInt(a, "timeout_seconds", 300)));
        Implement("watch_file_changed", "Watch for file changes with bounded timeout.", RiskLevel.ReadOnly, a => waits.WatchFileChangedAsync(GetString(a, "path"), GetInt(a, "timeout_seconds", 300)));
        Implement("watch_directory", "Watch a directory for the first bounded change.", RiskLevel.ReadOnly, a => waits.WatchDirectoryAsync(GetString(a, "path"), GetInt(a, "timeout_seconds", 300)));
        Implement("watch_download_complete", "Wait for a download target to exist and become stable.", RiskLevel.ReadOnly, a => waits.WatchDownloadCompleteAsync(GetString(a, "path"), GetInt(a, "timeout_seconds", 300)));
        Implement("watch_log_for_pattern", "Watch appended log output for a pattern.", RiskLevel.ReadOnly, a => waits.WatchLogForPatternAsync(GetString(a, "path"), GetString(a, "pattern"), GetInt(a, "timeout_seconds", 300)));
        Implement("wait_until_window_exists", "Delegate a bounded window wait to DesktopAgent.", RiskLevel.ReadOnly, a => DelegateToDesktopAgentAsync("ui_wait_for_window", a), ToolImplementationStatus.DelegatedToDesktopAgent);
        Implement("wait_until_dialog_detected", "Delegate a bounded dialog wait to DesktopAgent.", RiskLevel.ReadOnly, a => DelegateToDesktopAgentAsync("ui_wait_for_dialog", a), ToolImplementationStatus.DelegatedToDesktopAgent);
        Implement("wait_until_user_continues", "Queue a manual user continuation.", RiskLevel.Low, a => waits.QueueManualContinuationAsync(GetString(a, "task_id"), GetString(a, "prompt")));
    }

    private void RegisterUiDelegates()
    {
        foreach (var name in new[]
        {
            "ui_get_desktop_snapshot", "ui_list_windows", "ui_get_foreground_window", "ui_focus_window",
            "ui_move_window", "ui_resize_window", "ui_minimize_window", "ui_maximize_window", "ui_close_window",
            "ui_inspect_window", "ui_find_control", "ui_invoke_control", "ui_set_control_value", "ui_select_item",
            "ui_expand_collapse", "ui_scroll", "ui_click", "ui_double_click", "ui_right_click", "ui_type_text",
            "ui_send_hotkey", "ui_send_keys", "ui_drag_drop", "ui_clipboard_set", "ui_clipboard_get_redacted", "ui_screenshot_desktop",
            "ui_screenshot_window", "ui_wait_for_window", "ui_wait_for_control", "ui_wait_for_text", "ui_wait_for_dialog", "ui_wait_until_idle",
            "ui_detect_common_dialogs", "ui_execute_plan", "ui_stop_current_plan", "ui_get_plan_status", "ui_read_plan_log"
        })
        {
            Implement(name, "Delegate UI automation to the interactive DesktopAgent.", RiskLevel.Medium, a => DelegateToDesktopAgentAsync(name, a), ToolImplementationStatus.DelegatedToDesktopAgent);
        }
    }

    private void RegisterAgentDelegationTools()
    {
        Implement("detect_agent_tools", "Detect local agent CLI tools.", RiskLevel.ReadOnly, async _ => ResultEnvelope<object>.Ok(new
        {
            codex = await tooling.DetectToolAsync("codex").ConfigureAwait(false),
            claude = await tooling.DetectToolAsync("claude").ConfigureAwait(false),
            gemini = await tooling.DetectToolAsync("gemini").ConfigureAwait(false)
        }, "Agent tool detection.", RiskLevel.ReadOnly));
        Scaffold("install_agent_tool", "Agent installation requires explicit user approval and is scaffolded.", RiskLevel.High);
        Implement("create_agent_prompt", "Create a local agent prompt artifact.", RiskLevel.Low, async a =>
        {
            var path = Path.Combine(layout.Handoffs, $"agent-prompt-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}.md");
            await File.WriteAllTextAsync(path, GetString(a, "prompt")).ConfigureAwait(false);
            return ResultEnvelope<object>.Ok(new { path }, "Agent prompt created.", RiskLevel.Low);
        });
        Implement("run_agent_cli", "Run a local agent CLI command with capture.", RiskLevel.Medium, a => commandRunner.RunAsync(new ProcessStartRequest(GetString(a, "file_name"), GetString(a, "arguments"), GetOptionalString(a, "working_directory"), GetOptionalInt(a, "timeout_seconds"))));
        Alias("read_agent_output", "process_tail_output", "Read tracked agent process output.", RiskLevel.ReadOnly);
        Implement("import_agent_patch", "Validate an agent patch for secrets/unsafe patterns, then dry-run or apply it.", RiskLevel.Medium, a => ImportAgentPatchAsync(a));
        Implement("validate_agent_output", "Scan agent output or a patch file for secrets and unsafe patterns.", RiskLevel.Medium, a => ValidateAgentOutputAsync(a));
        Alias("summarize_agent_result", "task_generate_final_summary", "Summarize agent result through task ledger.", RiskLevel.Low);
        Alias("create_cross_agent_handoff_bundle", "task_export_handoff_bundle", "Create cross-agent handoff bundle.", RiskLevel.Low);
    }

    private ResultEnvelope<object> Status()
    {
        var status = new BrokerStatus(true, "BrokerService", typeof(BrokerToolRegistry).Assembly.GetName().Version?.ToString() ?? "dev", options.IpcPipeName, SystemOperations.IsElevated(), layout.Root, PlatformPaths.ProgramDataRoot, DateTimeOffset.UtcNow, Descriptors);
        return ResultEnvelope<object>.Ok(status, "Broker running.", RiskLevel.ReadOnly);
    }

    private async Task<object> DelegateToDesktopAgentAsync(string name, JsonElement args)
    {
        var client = new BrokerPipeClient(options.IpcPipeName + ".Desktop", TimeSpan.FromSeconds(2));
        var response = await client.InvokeRawAsync(name, args).ConfigureAwait(false);
        return response.Success && response.Result is { } result
            ? result
            : ResultEnvelope<object>.Fail(response.ErrorCode ?? "desktop_agent_unavailable", response.ErrorMessage ?? "DesktopAgent is not connected. Start the tray/DesktopAgent in the interactive user session.", OperationStatus.Failed, RiskLevel.Medium);
    }

    private async Task<object> ReadTrackedLogAsync(string id, bool stdout, int bytes)
    {
        var record = await ledger.GetTrackedProcessAsync(id).ConfigureAwait(false);
        return record is null
            ? ResultEnvelope<object>.Fail("process_not_found", "Tracked process was not found.", OperationStatus.Failed, RiskLevel.ReadOnly)
            : commandRunner.ReadLogTail(stdout ? record.StdoutLogPath : record.StderrLogPath, bytes);
    }

    private Task<object> GetProcessNetworkConnectionsAsync(int pid)
    {
        var script =
            $$"""
            $ErrorActionPreference = 'SilentlyContinue'
            $pidToInspect = {{pid}}
            $tcp = @(Get-NetTCPConnection -OwningProcess $pidToInspect | Select-Object LocalAddress,LocalPort,RemoteAddress,RemotePort,State,OwningProcess,CreationTime)
            $udp = @(Get-NetUDPEndpoint -OwningProcess $pidToInspect | Select-Object LocalAddress,LocalPort,OwningProcess,CreationTime)
            [pscustomobject]@{
              pid = $pidToInspect
              tcp = $tcp
              udp = $udp
            } | ConvertTo-Json -Depth 5
            """;

        return commandRunner.RunAsync(new ProcessStartRequest("powershell.exe", EncodedPowerShell(script), TimeoutSeconds: 30))
            .ContinueWith(t => (object)t.Result, TaskScheduler.Default);
    }

    private async Task<object> WatchEventLogAsync(JsonElement args)
    {
        var logName = GetString(args, "log_name");
        var taskId = GetString(args, "task_id");
        var timeoutSeconds = GetInt(args, "timeout_seconds", 300);
        var query = GetOptionalString(args, "query");
        var continuationPrompt = GetOptionalString(args, "continuation_prompt")
            ?? $"Resume task {taskId}. The event-log watcher for '{logName}' has exited; inspect the tracked process logs and continue.";

        var script =
            $$"""
            $ErrorActionPreference = 'SilentlyContinue'
            $logName = '{{PsSingleQuote(logName)}}'
            $xpath = '{{PsSingleQuote(query ?? string.Empty)}}'
            $start = Get-Date
            $deadline = $start.AddSeconds({{timeoutSeconds}})
            while ((Get-Date) -lt $deadline) {
              if ([string]::IsNullOrWhiteSpace($xpath)) {
                $events = @(Get-WinEvent -LogName $logName -MaxEvents 25 | Where-Object { $_.TimeCreated -ge $start })
              } else {
                $events = @(Get-WinEvent -LogName $logName -FilterXPath $xpath -MaxEvents 25 | Where-Object { $_.TimeCreated -ge $start })
              }

              if ($events.Count -gt 0) {
                $events | Select-Object TimeCreated,LogName,Id,LevelDisplayName,ProviderName,Message | ConvertTo-Json -Depth 5
                exit 0
              }

              Start-Sleep -Seconds 2
            }

            Write-Output "No matching events observed before timeout."
            exit 2
            """;

        var started = await commandRunner.StartTrackedAsync(new ProcessStartRequest(
            "powershell.exe",
            EncodedPowerShell(script),
            TimeoutSeconds: timeoutSeconds + 30,
            TaskId: taskId,
            ExpectedCompletionSignal: "event_log_match_or_timeout",
            ContinuationPrompt: continuationPrompt)).ConfigureAwait(false);

        if (started.Success && started.Data is { } tracked)
        {
            await ledger.QueueContinuationAsync(
                taskId,
                ContinuationConditionType.ProcessExit,
                JsonSerializer.Serialize(new { tracked_process_id = tracked.Id, log_name = logName }, JsonDefaults.Options),
                continuationPrompt).ConfigureAwait(false);
        }

        return started;
    }

    private async Task<object> ValidateAgentOutputAsync(JsonElement args)
    {
        var patchText = await ReadPatchOrTextAsync(args).ConfigureAwait(false);
        if (patchText is null)
        {
            return ResultEnvelope<object>.Fail("missing_patch", "Provide patch_text or path.", OperationStatus.Failed, RiskLevel.Medium);
        }

        var scan = new PatchSafetyScanner().Scan(patchText);
        return ResultEnvelope<object>.Ok(new
        {
            safe = scan.Safe,
            findings = scan.Findings,
            redactions_applied = scan.RedactionsApplied,
            redacted_preview = scan.RedactedPreview
        }, scan.Safe ? "Agent output validation passed." : "Agent output validation found unsafe or sensitive content.", RiskLevel.Medium);
    }

    private async Task<object> ImportAgentPatchAsync(JsonElement args)
    {
        var patchText = await ReadPatchOrTextAsync(args).ConfigureAwait(false);
        if (patchText is null)
        {
            return ResultEnvelope<object>.Fail("missing_patch", "Provide patch_text or path.", OperationStatus.Failed, RiskLevel.Medium);
        }

        var scan = new PatchSafetyScanner().Scan(patchText);
        if (!scan.Safe)
        {
            return ResultEnvelope<object>.Fail(
                "unsafe_agent_patch",
                "Imported agent patch contains secrets or unsafe patterns. Review validate_agent_output for redacted details.",
                OperationStatus.Failed,
                RiskLevel.SecuritySensitive,
                redactionsApplied: scan.RedactionsApplied);
        }

        var patchResult = files.ApplyUnifiedDiffPatch(patchText, GetOptionalString(args, "root"), GetBool(args, "dry_run", true), backup: true);
        return patchResult.Success
            ? ResultEnvelope<object>.Ok(new { validation = scan, patch_result = patchResult.Data }, patchResult.Message, RiskLevel.Medium)
            : ResultEnvelope<object>.Fail(patchResult.ErrorCode ?? "patch_import_failed", patchResult.Message ?? "Patch import failed.", patchResult.Status, RiskLevel.Medium);
    }

    private static async Task<string?> ReadPatchOrTextAsync(JsonElement args)
    {
        if (GetOptionalString(args, "patch_text") is { Length: > 0 } patchText)
        {
            return patchText;
        }

        if (GetOptionalString(args, "path") is { Length: > 0 } path)
        {
            return await File.ReadAllTextAsync(Environment.ExpandEnvironmentVariables(path)).ConfigureAwait(false);
        }

        return null;
    }

    private async Task<ResultEnvelope<object>> GenerateTaskSummaryAsync(string id)
    {
        var task = await ledger.GetTaskAsync(id).ConfigureAwait(false);
        if (task is null)
        {
            return ResultEnvelope<object>.Fail("task_not_found", "Task was not found.", OperationStatus.Failed, RiskLevel.ReadOnly);
        }

        var events = await ledger.ListEventsAsync(id, 100).ConfigureAwait(false);
        var processes = (await ledger.ListTrackedProcessesAsync().ConfigureAwait(false)).Where(p => p.TaskId == id).ToArray();
        return ResultEnvelope<object>.Ok(new
        {
            task,
            recent_events = events,
            tracked_processes = processes,
            handoff_prompt = $"Continue task '{task.Title}' ({task.Id}). Goal: {task.Goal}. Current summary: {task.CurrentSummary}. Next steps: {task.NextSteps}. Completion evidence: {task.CompletionEvidence}."
        }, "Task final summary generated.", RiskLevel.Low);
    }

    private void Implement<T>(string name, string description, RiskLevel riskLevel, Func<JsonElement, T> handler, ToolImplementationStatus status = ToolImplementationStatus.Implemented) =>
        _tools[name] = new ToolEntry(new ToolDescriptor(name, description, riskLevel, status, "BrokerService"), (args, _) => Task.FromResult<object>(handler(args)!));

    private void Implement<T>(string name, string description, RiskLevel riskLevel, Func<JsonElement, Task<T>> handler, ToolImplementationStatus status = ToolImplementationStatus.Implemented) =>
        _tools[name] = new ToolEntry(new ToolDescriptor(name, description, riskLevel, status, "BrokerService"), async (args, _) => (object)(await handler(args).ConfigureAwait(false))!);

    private void Shell(string name, string description, string fileName, string arguments, RiskLevel riskLevel) =>
        Implement(name, description, riskLevel, _ => commandRunner.RunAsync(new ProcessStartRequest(fileName, arguments)));

    private void Detect(string name, string executable, string versionArgs = "--version") =>
        Implement(name, $"Detect {executable}.", RiskLevel.ReadOnly, _ => tooling.DetectToolAsync(executable, versionArgs));

    private void Alias(string name, string target, string description, RiskLevel riskLevel) =>
        Implement(name, description, riskLevel, a => InvokeAsync(target, a, CancellationToken.None), _tools.TryGetValue(target, out var entry) ? entry.Descriptor.ImplementationStatus : ToolImplementationStatus.Implemented);

    private void Scaffold(string name, string description, RiskLevel riskLevel) =>
        _tools[name] = new ToolEntry(new ToolDescriptor(name, description, riskLevel, ToolImplementationStatus.Scaffolded, "BrokerService"), (_, _) => Task.FromResult<object>(ResultEnvelope<object>.Fail("not_implemented", description, OperationStatus.NotImplemented, riskLevel)));

    private void StatusTool(string name, LedgerTaskStatus status, string message) =>
        Implement(name, message, RiskLevel.Low, async a =>
        {
            var id = GetString(a, "id");
            await ledger.UpdateTaskFieldsAsync(id, completionEvidence: GetOptionalString(a, "completion_evidence"), status: status).ConfigureAwait(false);
            return ResultEnvelope<object>.Ok(new { id, status }, message, RiskLevel.Low);
        });

    private static ResultEnvelope<T> WrapNullable<T>(T? value, string errorCode, string message) where T : class =>
        value is null
            ? ResultEnvelope<T>.Fail(errorCode, message, OperationStatus.Failed, RiskLevel.ReadOnly)
            : ResultEnvelope<T>.Ok(value, "Found.", RiskLevel.ReadOnly);

    private static string GetString(JsonElement args, string name) =>
        args.TryGetProperty(name, out var property) ? property.GetString() ?? throw new ArgumentException($"Argument '{name}' is required.") : throw new ArgumentException($"Argument '{name}' is required.");

    private static string? GetOptionalString(JsonElement args, string name) =>
        args.TryGetProperty(name, out var property) && property.ValueKind != JsonValueKind.Null ? property.GetString() : null;

    private static int GetInt(JsonElement args, string name, int? fallback = null) =>
        args.TryGetProperty(name, out var property) && property.ValueKind != JsonValueKind.Null ? property.GetInt32() : fallback ?? throw new ArgumentException($"Argument '{name}' is required.");

    private static int? GetOptionalInt(JsonElement args, string name) =>
        args.TryGetProperty(name, out var property) && property.ValueKind != JsonValueKind.Null ? property.GetInt32() : null;

    private static bool GetBool(JsonElement args, string name, bool fallback) =>
        args.TryGetProperty(name, out var property) && property.ValueKind != JsonValueKind.Null ? property.GetBoolean() : fallback;

    private static DateTimeOffset GetDate(JsonElement args, string name) =>
        DateTimeOffset.Parse(GetString(args, name));

    private static DateTimeOffset? GetOptionalDate(JsonElement args, string name) =>
        GetOptionalString(args, name) is { } value ? DateTimeOffset.Parse(value) : null;

    private static ContinuationConditionType ParseCondition(string value)
    {
        var pascal = string.Concat(value.Split('_', '-').Select(s => string.IsNullOrEmpty(s) ? s : char.ToUpperInvariant(s[0]) + s[1..]));
        return Enum.Parse<ContinuationConditionType>(pascal, ignoreCase: true);
    }

    private static string Quote(string value) => "\"" + value.Replace("\"", "\\\"") + "\"";

    private static string EncodedPowerShell(string command) =>
        "-NoProfile -EncodedCommand " + Convert.ToBase64String(Encoding.Unicode.GetBytes(command));

    private static string PsSingleQuote(string value) =>
        value.Replace("'", "''", StringComparison.Ordinal);

    private sealed record ToolEntry(ToolDescriptor Descriptor, Func<JsonElement, CancellationToken, Task<object>> Handler);
}

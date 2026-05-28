using System.Text.Json;
using System.Text.Json.Nodes;

namespace WindowsPowerUserMcp.Core;

public enum ToolArgumentKind
{
    String,
    Integer,
    Boolean,
    Json,
    DateTime
}

public sealed record ToolArgumentDescriptor(
    string Name,
    ToolArgumentKind Kind,
    bool Required = false,
    string? Description = null,
    object? DefaultValue = null);

public sealed record ToolCatalogEntry(
    string Name,
    string Description,
    RiskLevel RiskLevel,
    ToolImplementationStatus ImplementationStatus,
    string Component,
    IReadOnlyList<ToolArgumentDescriptor> Arguments)
{
    public ToolDescriptor ToDescriptor() =>
        new(Name, Description, RiskLevel, ImplementationStatus, Component);

    public JsonElement InputSchema => ToolCatalog.CreateInputSchema(Arguments);
}

public static class ToolCatalog
{
    private static readonly string[] ToolNames =
    [
        "adb_detect", "adb_devices", "adb_install_apk", "adb_logcat", "adb_reboot_device", "adb_shell",
        "append_file", "apply_unified_diff_patch", "broker_get_status", "choco_detect", "choco_install",
        "compute_hash", "copy_file", "create_agent_prompt", "create_backup", "create_cross_agent_handoff_bundle",
        "create_directory", "create_scheduled_task", "create_zip_archive", "delete_file_permanent",
        "delete_file_to_recycle_bin", "delete_scheduled_task", "detect_agent_tools", "detect_android_sdk",
        "detect_dotnet_sdks", "detect_git", "detect_java", "detect_node", "detect_pnpm", "detect_python",
        "detect_unity", "detect_uv", "detect_visual_studio", "detect_visual_studio_build_tools",
        "detect_vscode", "detect_windows_sdk", "disable_scheduled_task", "docker_compose_down",
        "docker_compose_ps", "docker_compose_up", "docker_exec", "docker_list_containers",
        "docker_list_images", "docker_logs", "docker_status", "enable_scheduled_task",
        "export_event_log_slice", "extract_zip_archive", "generate_codex_handoff_summary",
        "get_admin_status", "get_app_crash_events", "get_defender_status_readonly", "get_drives",
        "get_environment_summary", "get_file_acl", "get_firewall_status_readonly", "get_hardware_summary",
        "get_installed_apps", "get_os_info", "get_path_info", "get_process_detail", "get_process_modules",
        "get_process_network_connections", "get_process_open_windows", "get_recent_errors",
        "get_scheduled_task", "get_service_detail", "get_service_recovery_options", "get_session_info",
        "get_startup_items", "get_system_summary", "get_user_context", "get_windows_features",
        "get_windows_update_events", "get_windows_updates_status", "import_agent_patch",
        "inspect_project_structure", "install_agent_tool", "kill_process_tree", "list_directory",
        "list_processes", "list_scheduled_tasks", "list_services", "move_file", "process_attach_to_task",
        "process_cancel", "process_generate_continuation_prompt", "process_get_status",
        "process_kill_tree", "process_list_tracked", "process_mark_requires_user_intervention",
        "process_read_stderr", "process_read_stdout", "process_start_tracked", "process_tail_output",
        "process_wait", "query_event_logs", "read_agent_output", "read_file", "registry_delete",
        "registry_export", "registry_import_file", "registry_list", "registry_read", "registry_write",
        "restart_service", "run_admin_task", "run_agent_cli", "run_cmd", "run_dotnet_build",
        "run_dotnet_test", "run_elevated_process_if_already_elevated", "run_git", "run_lint",
        "run_msbuild", "run_npm_script", "run_pnpm_script", "run_powershell", "run_process",
        "run_python", "run_scheduled_task", "run_script_file", "run_solution_build", "run_tests",
        "run_wsl_command", "scoop_detect", "scoop_install", "search_files", "search_text",
        "set_file_acl", "set_service_recovery_options", "set_service_startup_type", "start_process",
        "start_service", "stop_process", "stop_service", "summarize_agent_result",
        "summarize_repo_state", "take_ownership_if_elevated", "task_append_event",
        "task_export_handoff_bundle", "task_generate_final_summary", "task_generate_handoff_prompt",
        "task_get", "task_get_completion_evidence", "task_get_summary", "task_import_handoff_bundle",
        "task_list_active", "task_list_pending", "task_mark_blocked", "task_mark_cancelled",
        "task_mark_complete", "task_mark_failed", "task_queue_continuation", "task_resume",
        "task_set_next_steps", "task_start", "task_update_summary", "tool_list", "validate_agent_output",
        "wait_until_dialog_detected", "wait_until_file_exists", "wait_until_port_open",
        "wait_until_process_exit", "wait_until_time", "wait_until_user_continues",
        "wait_until_window_exists", "watch_directory", "watch_download_complete", "watch_event_log",
        "watch_file_changed", "watch_file_created", "watch_log_for_pattern", "winget_detect",
        "winget_install", "winget_search", "winget_uninstall", "winget_upgrade", "write_file",
        "wsl_check_tooling", "wsl_list_distros", "wsl_open_project", "wsl_run_command", "wsl_status",
        "ui_get_desktop_snapshot", "ui_list_windows", "ui_get_foreground_window", "ui_focus_window",
        "ui_move_window", "ui_resize_window", "ui_minimize_window", "ui_maximize_window",
        "ui_close_window", "ui_inspect_window", "ui_find_control", "ui_invoke_control",
        "ui_set_control_value", "ui_select_item", "ui_expand_collapse", "ui_scroll", "ui_click",
        "ui_double_click", "ui_right_click", "ui_type_text", "ui_send_hotkey", "ui_send_keys",
        "ui_drag_drop",
        "ui_clipboard_set", "ui_clipboard_get_redacted", "ui_screenshot_desktop",
        "ui_screenshot_window", "ui_wait_for_window", "ui_wait_for_control", "ui_wait_for_text",
        "ui_wait_for_dialog", "ui_wait_until_idle", "ui_detect_common_dialogs", "ui_execute_plan",
        "ui_stop_current_plan", "ui_get_plan_status", "ui_read_plan_log"
    ];

    public static IReadOnlyList<ToolCatalogEntry> All { get; } =
        ToolNames.Select(CreateEntry).OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase).ToArray();

    public static ToolCatalogEntry? Find(string name) =>
        All.FirstOrDefault(t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase));

    public static IReadOnlyList<string> ValidateArguments(ToolCatalogEntry entry, JsonElement arguments)
    {
        var errors = new List<string>();
        if (arguments.ValueKind != JsonValueKind.Object)
        {
            errors.Add("Arguments must be a JSON object.");
            return errors;
        }

        foreach (var parameter in entry.Arguments.Where(a => a.Required))
        {
            if (!arguments.TryGetProperty(parameter.Name, out var value) || value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            {
                errors.Add($"Missing required argument '{parameter.Name}'.");
            }
        }

        foreach (var property in arguments.EnumerateObject())
        {
            var parameter = entry.Arguments.FirstOrDefault(a => string.Equals(a.Name, property.Name, StringComparison.OrdinalIgnoreCase));
            if (parameter is null)
            {
                errors.Add($"Unknown argument '{property.Name}'.");
                continue;
            }

            if (!KindMatches(parameter.Kind, property.Value))
            {
                errors.Add($"Argument '{parameter.Name}' must be {parameter.Kind.ToString().ToLowerInvariant()}.");
            }
        }

        return errors;
    }

    public static JsonElement CreateInputSchema(IReadOnlyList<ToolArgumentDescriptor> arguments)
    {
        var properties = new JsonObject();
        var required = new JsonArray();
        foreach (var argument in arguments)
        {
            var node = new JsonObject
            {
                ["description"] = argument.Description ?? Humanize(argument.Name)
            };
            if (argument.Kind == ToolArgumentKind.Json)
            {
                node["description"] = (argument.Description ?? Humanize(argument.Name)) + " JSON object.";
            }
            else
            {
                node["type"] = argument.Kind switch
                {
                    ToolArgumentKind.Integer => "integer",
                    ToolArgumentKind.Boolean => "boolean",
                    ToolArgumentKind.DateTime => "string",
                    _ => "string"
                };
                if (argument.Kind == ToolArgumentKind.DateTime)
                {
                    node["format"] = "date-time";
                }
            }

            if (argument.DefaultValue is not null)
            {
                node["default"] = JsonValue.Create(argument.DefaultValue);
            }

            properties[argument.Name] = node;
            if (argument.Required)
            {
                required.Add(argument.Name);
            }
        }

        var schema = new JsonObject
        {
            ["type"] = "object",
            ["additionalProperties"] = false,
            ["properties"] = properties,
            ["required"] = required
        };
        return JsonSerializer.SerializeToElement(schema, JsonDefaults.Options);
    }

    private static ToolCatalogEntry CreateEntry(string name) =>
        new(
            name,
            Describe(name),
            RiskFor(name),
            ImplementationStatusFor(name),
            name.StartsWith("ui_", StringComparison.Ordinal) ? "DesktopAgent" : "BrokerService",
            ArgumentsFor(name));

    private static IReadOnlyList<ToolArgumentDescriptor> ArgumentsFor(string name)
    {
        static ToolArgumentDescriptor S(string n, bool req = false, string? desc = null, object? def = null) => new(n, ToolArgumentKind.String, req, desc, def);
        static ToolArgumentDescriptor I(string n, bool req = false, string? desc = null, object? def = null) => new(n, ToolArgumentKind.Integer, req, desc, def);
        static ToolArgumentDescriptor B(string n, bool req = false, string? desc = null, object? def = null) => new(n, ToolArgumentKind.Boolean, req, desc, def);
        static ToolArgumentDescriptor J(string n, bool req = false, string? desc = null) => new(n, ToolArgumentKind.Json, req, desc);
        static ToolArgumentDescriptor D(string n, bool req = false, string? desc = null) => new(n, ToolArgumentKind.DateTime, req, desc);

        return name switch
        {
            "get_path_info" or "read_file" or "delete_file_to_recycle_bin" or "delete_file_permanent" or "create_backup" or "get_file_acl" or "take_ownership_if_elevated" => [S("path", true), I("max_bytes", false, def: 262144)],
            "write_file" => [S("path", true), S("content", true), B("overwrite", false, def: false)],
            "append_file" => [S("path", true), S("content", true)],
            "create_directory" => [S("path", true)],
            "search_files" => [S("root", true), S("pattern", true), B("recursive", false, def: true), I("limit", false, def: 500)],
            "search_text" => [S("root", true), S("pattern", true), S("text", true), B("recursive", false, def: true), I("limit", false, def: 200)],
            "compute_hash" => [S("path", true), S("algorithm", false, def: "SHA256")],
            "copy_file" or "move_file" => [S("source", true), S("destination", true), B("overwrite", false, def: false)],
            "create_zip_archive" => [S("source_directory", true), S("destination_zip", true), B("overwrite", false, def: false)],
            "extract_zip_archive" => [S("zip_path", true), S("destination_directory", true), B("overwrite", false, def: false)],
            "apply_unified_diff_patch" => [S("patch_text", true), S("root", false), B("dry_run", false, def: true), B("backup", false, def: true)],
            "set_file_acl" => [S("path", true), S("identity", true), S("rights", true), S("access_type", false, def: "Allow"), B("inherit", false, def: false)],

            "get_process_detail" or "stop_process" or "kill_process_tree" => [I("pid", true), B("kill_tree", false, def: false)],
            "get_process_modules" or "get_process_open_windows" or "get_process_network_connections" => [I("pid", true)],
            "list_processes" => [S("name_filter"), I("limit", false, def: 500)],
            "start_process" or "run_process" or "run_elevated_process_if_already_elevated" or "run_admin_task" or "run_agent_cli" => [S("file_name", true), S("arguments", false, def: ""), S("working_directory"), I("timeout_seconds")],
            "run_powershell" or "run_cmd" => [S("command", true), S("working_directory"), I("timeout_seconds")],
            "run_script_file" => [S("path", true), S("working_directory"), I("timeout_seconds")],
            "run_wsl_command" or "wsl_run_command" or "wsl_open_project" or "wsl_check_tooling" or "adb_shell" => [S("command", true), S("working_directory"), I("timeout_seconds")],
            "process_start_tracked" => [S("file_name", true), S("arguments", false, def: ""), S("working_directory"), I("timeout_seconds"), S("task_id"), S("expected_completion_signal"), S("continuation_prompt")],
            "process_get_status" or "process_wait" or "process_read_stdout" or "process_read_stderr" or "process_tail_output" or "process_cancel" or "process_kill_tree" or "process_generate_continuation_prompt" or "process_attach_to_task" => [S("id", true), I("timeout_seconds", false, def: 60), I("bytes", false, def: 65536), B("kill_tree", false, def: true), S("task_id")],
            "process_mark_requires_user_intervention" => [S("id", true), S("continuation_prompt")],

            "task_start" => [S("title", true), S("goal", true), S("owner_user"), I("priority", false, def: 0)],
            "task_get" or "task_get_summary" or "task_resume" or "task_mark_complete" or "task_mark_blocked" or "task_mark_failed" or "task_mark_cancelled" or "task_get_completion_evidence" or "task_generate_final_summary" or "task_generate_handoff_prompt" or "task_export_handoff_bundle" or "summarize_agent_result" or "create_cross_agent_handoff_bundle" => [S("id", true), I("limit", false, def: 25), S("completion_evidence")],
            "task_append_event" => [S("task_id", true), S("summary", true), S("actor", false, def: "codex"), S("event_type", false, def: "note"), S("tool_name"), S("command_line_redacted"), S("working_directory"), S("result_status")],
            "task_update_summary" => [S("task_id", true), S("current_summary", true)],
            "task_set_next_steps" => [S("task_id", true), S("next_steps", true)],
            "task_queue_continuation" => [S("task_id", true), S("prompt", true), S("condition_type", false, def: "manual_user_continue"), S("condition_payload_json", false, def: "{}"), D("due_at")],
            "task_import_handoff_bundle" => [S("path", true), S("title"), S("goal")],

            "registry_read" or "registry_list" or "registry_export" or "registry_delete" => [S("key_path", true), S("value_name"), S("destination_reg_file"), B("recursive", false, def: false)],
            "registry_write" => [S("key_path", true), S("value_name", true), S("value", true), S("value_kind", false, def: "String")],
            "registry_import_file" => [S("reg_file", true)],

            "get_service_detail" or "start_service" or "stop_service" or "restart_service" or "set_service_startup_type" or "get_service_recovery_options" or "set_service_recovery_options" => [S("service_name", true), I("timeout_seconds", false, def: 60), S("startup_type"), S("actions"), I("reset_seconds", false, def: 86400)],
            "list_services" => [S("name_filter")],

            "query_event_logs" or "export_event_log_slice" => [S("log_name", true), I("max_events", false, def: 50), I("timeout_seconds")],
            "watch_event_log" => [S("log_name", true), S("task_id", true), S("query"), S("continuation_prompt"), I("timeout_seconds", false, def: 300)],
            "get_scheduled_task" or "enable_scheduled_task" or "disable_scheduled_task" or "delete_scheduled_task" or "run_scheduled_task" => [S("task_name", true)],
            "create_scheduled_task" => [S("arguments", true), I("timeout_seconds")],

            "winget_search" => [S("query", true)],
            "winget_install" or "winget_uninstall" or "choco_install" or "scoop_install" => [S("package", true)],
            "winget_upgrade" => [S("package")],
            "inspect_project_structure" or "run_solution_build" or "run_dotnet_build" or "run_dotnet_test" => [S("path", true), S("working_directory"), I("timeout_seconds"), I("limit", false, def: 1000)],
            "summarize_repo_state" or "run_npm_script" or "run_pnpm_script" => [S("working_directory", true), S("script"), I("timeout_seconds")],
            "run_msbuild" or "run_python" or "run_git" => [S("arguments", true), S("working_directory"), I("timeout_seconds")],
            "run_lint" or "run_tests" => [S("path"), S("working_directory"), S("script"), I("timeout_seconds")],
            "docker_compose_ps" or "docker_compose_up" or "docker_compose_down" => [S("working_directory"), I("timeout_seconds")],
            "docker_logs" => [S("container", true), I("tail", false, def: 200)],
            "docker_exec" => [S("container", true), S("command", true)],
            "adb_install_apk" => [S("apk_path", true)],
            "adb_logcat" => [I("lines", false, def: 500)],

            "wait_until_time" => [D("due_at", true)],
            "wait_until_process_exit" => [S("id", true), I("timeout_seconds", false, def: 300)],
            "wait_until_file_exists" or "watch_file_created" or "watch_file_changed" or "watch_directory" or "watch_download_complete" => [S("path", true), I("timeout_seconds", false, def: 300)],
            "wait_until_port_open" => [S("host", true), I("port", true), I("timeout_seconds", false, def: 300)],
            "wait_until_user_continues" => [S("task_id", true), S("prompt", true)],
            "watch_log_for_pattern" => [S("path", true), S("pattern", true), S("task_id"), I("timeout_seconds", false, def: 300)],

            "create_agent_prompt" => [S("prompt", true)],
            "install_agent_tool" => [S("tool_name", true)],
            "import_agent_patch" or "validate_agent_output" => [S("path"), S("patch_text"), S("root"), B("dry_run", false, def: true)],

            "ui_execute_plan" => [J("plan", false, "Structured UiPlanRequest payload"), J("actions", false, "Action list when not wrapping in plan"), S("task_id")],
            "ui_get_plan_status" or "ui_read_plan_log" or "ui_stop_current_plan" => [S("plan_id", true)],
            var ui when ui.StartsWith("ui_wait_", StringComparison.Ordinal) => [J("selector"), S("window_title"), S("name"), S("text"), I("timeout_ms", false, def: 5000)],
            "ui_drag_drop" => [J("selector"), I("x"), I("y"), I("to_x", true), I("to_y", true), I("timeout_ms")],
            var ui when ui.StartsWith("ui_", StringComparison.Ordinal) => [J("selector"), S("window_title"), S("window_class"), S("automation_id"), S("name"), S("control_type"), S("text"), S("value"), S("hotkey"), I("x"), I("y"), I("width"), I("height"), I("timeout_ms")],

            _ => []
        };
    }

    private static string Describe(string name) =>
        name switch
        {
            "broker_get_status" => "Return broker status and tool descriptors.",
            "tool_list" => "List all broker tools.",
            var ui when ui.StartsWith("ui_", StringComparison.Ordinal) => "DesktopAgent UI automation tool: " + Humanize(name) + ".",
            _ => Humanize(name) + "."
        };

    private static ToolImplementationStatus ImplementationStatusFor(string name)
    {
        string[] scaffolded =
        [
            "process_attach_to_task", "generate_codex_handoff_summary", "install_agent_tool"
        ];
        if (scaffolded.Contains(name, StringComparer.OrdinalIgnoreCase))
        {
            return ToolImplementationStatus.Scaffolded;
        }

        return name.StartsWith("ui_", StringComparison.Ordinal)
            ? ToolImplementationStatus.DelegatedToDesktopAgent
            : ToolImplementationStatus.Implemented;
    }

    private static RiskLevel RiskFor(string name)
    {
        var lower = name.ToLowerInvariant();
        if (lower.Contains("credential") || lower.Contains("password") || lower.Contains("token") || lower.Contains("cookie"))
        {
            return RiskLevel.CredentialSensitive;
        }

        if (lower.Contains("delete") || lower.Contains("kill") || lower.Contains("uninstall") || lower.Contains("reboot") || lower.Contains("ownership"))
        {
            return RiskLevel.Destructive;
        }

        if (lower.Contains("registry_write") || lower.Contains("registry_delete") || lower.Contains("service") || lower.Contains("scheduled_task") || lower.Contains("acl") || lower.Contains("install"))
        {
            return RiskLevel.High;
        }

        if (lower.StartsWith("run_", StringComparison.Ordinal) || lower.Contains("write") || lower.Contains("start_process") || lower.StartsWith("ui_", StringComparison.Ordinal))
        {
            return RiskLevel.Medium;
        }

        if (lower.StartsWith("get_", StringComparison.Ordinal) || lower.StartsWith("list_", StringComparison.Ordinal) || lower.StartsWith("read_", StringComparison.Ordinal) || lower.StartsWith("detect_", StringComparison.Ordinal))
        {
            return RiskLevel.ReadOnly;
        }

        return RiskLevel.Low;
    }

    private static bool KindMatches(ToolArgumentKind kind, JsonElement value) =>
        value.ValueKind == JsonValueKind.Null ||
        kind switch
        {
            ToolArgumentKind.Integer => value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out _),
            ToolArgumentKind.Boolean => value.ValueKind is JsonValueKind.True or JsonValueKind.False,
            ToolArgumentKind.Json => value.ValueKind is JsonValueKind.Object or JsonValueKind.Array,
            ToolArgumentKind.DateTime => value.ValueKind == JsonValueKind.String && DateTimeOffset.TryParse(value.GetString(), out _),
            _ => value.ValueKind == JsonValueKind.String
        };

    private static string Humanize(string name) =>
        string.Join(' ', name.Split('_', StringSplitOptions.RemoveEmptyEntries)).Trim();
}

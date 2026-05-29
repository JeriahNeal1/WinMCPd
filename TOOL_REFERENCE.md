# Tool Reference

## StdioBridge Exposure

`WindowsPowerUserMcp.App.exe --stdio` and the standalone StdioBridge generate one direct MCP method for every broker descriptor in `ToolCatalog`. Codex can call tools by name directly instead of routing through string-based dispatch. Each generated tool carries JSON input schema, description, read-only/destructive/open-world metadata, and broker-side argument validation.

`broker_call` remains as a deprecated compatibility escape hatch for this milestone. New Codex configs should prefer direct tools; remove `broker_call` once all clients have refreshed their tool cache.

## Implemented In This Milestone

Broker/status:
`broker_get_status`, `tool_list`.

System:
`get_system_summary`, `get_os_info`, `get_hardware_summary`, `get_environment_summary`, `get_user_context`, `get_admin_status`, `get_session_info`, `get_drives`, `get_path_info`, `get_installed_apps`, `get_startup_items`, `get_windows_features`, `get_windows_updates_status`, `get_defender_status_readonly`, `get_firewall_status_readonly`.

Tasks:
`task_start`, `task_append_event`, `task_get`, `task_get_summary`, `task_update_summary`, `task_set_next_steps`, `task_queue_continuation`, `task_list_pending`, `task_list_active`, `task_resume`, `task_mark_complete`, `task_mark_blocked`, `task_mark_failed`, `task_mark_cancelled`, `task_generate_final_summary`, `task_export_handoff_bundle`, `task_import_handoff_bundle`.

Processes and shell:
`run_process`, `run_powershell`, `run_cmd`, `run_script_file`, `run_wsl_command`, `run_elevated_process_if_already_elevated`, `run_admin_task`, `process_start_tracked`, `process_get_status`, `process_wait`, `process_read_stdout`, `process_read_stderr`, `process_tail_output`, `process_cancel`, `process_kill_tree`, `process_list_tracked`, `process_generate_continuation_prompt`, `process_mark_requires_user_intervention`, `list_processes`, `get_process_detail`, `get_process_modules`, `get_process_open_windows`, `get_process_network_connections`, `start_process`, `stop_process`, `kill_process_tree`.

Filesystem:
`list_directory`, `read_file`, `write_file`, `append_file`, `create_directory`, `search_files`, `search_text`, `compute_hash`, `copy_file`, `move_file`, `delete_file_to_recycle_bin`, `delete_file_permanent`, `create_backup`, `apply_unified_diff_patch`, `create_zip_archive`, `extract_zip_archive`, `get_file_acl`, `set_file_acl`, `take_ownership_if_elevated`.

Registry/services/events/scheduled tasks:
`registry_read`, `registry_list`, `registry_export`, `registry_write`, `registry_delete`, `registry_import_file`, `list_services`, `get_service_detail`, `start_service`, `stop_service`, `restart_service`, `set_service_startup_type`, `get_service_recovery_options`, `set_service_recovery_options`, `query_event_logs`, `get_recent_errors`, `get_app_crash_events`, `get_windows_update_events`, `export_event_log_slice`, `watch_event_log`, `list_scheduled_tasks`, `get_scheduled_task`, `create_scheduled_task`, `enable_scheduled_task`, `disable_scheduled_task`, `delete_scheduled_task`, `run_scheduled_task`.

Package/dev/WSL/Docker/ADB:
`winget_detect`, `winget_search`, `winget_install`, `winget_upgrade`, `winget_uninstall`, `choco_detect`, `choco_install`, `scoop_detect`, `scoop_install`, `detect_dotnet_sdks`, `detect_git`, `detect_node`, `detect_pnpm`, `detect_python`, `detect_uv`, `detect_java`, `detect_vscode`, `detect_visual_studio`, `detect_visual_studio_build_tools`, `detect_windows_sdk`, `detect_android_sdk`, `detect_unity`, `inspect_project_structure`, `summarize_repo_state`, `run_solution_build`, `run_msbuild`, `run_dotnet_build`, `run_dotnet_test`, `run_npm_script`, `run_pnpm_script`, `run_python`, `run_git`, `run_lint`, `run_tests`, `wsl_status`, `wsl_list_distros`, `wsl_run_command`, `wsl_open_project`, `wsl_check_tooling`, `docker_status`, `docker_list_containers`, `docker_list_images`, `docker_compose_ps`, `docker_compose_up`, `docker_compose_down`, `docker_logs`, `docker_exec`, `adb_detect`, `adb_devices`, `adb_shell`, `adb_install_apk`, `adb_logcat`, `adb_reboot_device`.

UI:
DesktopAgent implements `ui_get_desktop_snapshot`, `ui_list_windows`, `ui_get_foreground_window`, `ui_focus_window`, `ui_move_window`, `ui_resize_window`, `ui_minimize_window`, `ui_maximize_window`, `ui_close_window`, `ui_inspect_window`, `ui_find_control`, `ui_click`, `ui_double_click`, `ui_right_click`, `ui_invoke_control`, `ui_set_control_value`, `ui_select_item`, `ui_expand_collapse`, `ui_scroll`, `ui_type_text`, `ui_send_hotkey`, `ui_send_keys`, `ui_drag_drop`, `ui_clipboard_set`, `ui_clipboard_get_redacted`, `ui_screenshot_desktop`, `ui_screenshot_window`, `ui_wait_for_window`, `ui_wait_for_control`, `ui_wait_for_text`, `ui_wait_for_dialog`, `ui_wait_until_idle`, `ui_detect_common_dialogs`, `ui_execute_plan`, `ui_stop_current_plan`, `ui_get_plan_status`, and `ui_read_plan_log`.

Agent delegation:
`detect_agent_tools`, `create_agent_prompt`, `run_agent_cli`, `read_agent_output`, `import_agent_patch`, `validate_agent_output`, `summarize_agent_result`, `create_cross_agent_handoff_bundle`.

## Scaffolded

`install_agent_tool`, `process_attach_to_task`, and `generate_codex_handoff_summary` remain scaffolded. `broker_call` is deprecated but still present for compatibility.

Broker-level `wait_until_window_exists` and `wait_until_dialog_detected` are delegated to DesktopAgent. File, directory, download, log-pattern, and event-log watchers are implemented with bounded waits/tracked processes.

## App Management Surface

The app-management functions are not MCP tools; they are local desktop/product operations exposed through `WindowsPowerUserMcp.App` and scripts:

- start/stop/restart user-mode broker;
- start elevated through normal UAC `runas`;
- install/start/stop/restart/uninstall `WindowsPowerUserMcp.Broker`;
- install/uninstall tray scheduled task `WindowsPowerUserMcp Dashboard`;
- test broker and DesktopAgent pipes;
- test `App.exe --stdio`;
- check/stage/apply updates;
- export redacted diagnostic bundles;
- detect stale service paths, stale tray task paths, duplicate install roots, and missing install state.

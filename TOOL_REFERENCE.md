# Tool Reference

## Implemented In This Milestone

Broker/status:
`broker_get_status`, `tool_list`, `broker_call`.

System:
`get_system_summary`, `get_os_info`, `get_hardware_summary`, `get_environment_summary`, `get_user_context`, `get_admin_status`, `get_session_info`, `get_drives`, `get_path_info`, `get_installed_apps`, `get_startup_items`, `get_windows_features`, `get_windows_updates_status`, `get_defender_status_readonly`, `get_firewall_status_readonly`.

Tasks:
`task_start`, `task_append_event`, `task_get`, `task_get_summary`, `task_update_summary`, `task_set_next_steps`, `task_queue_continuation`, `task_list_pending`, `task_list_active`, `task_resume`, `task_mark_complete`, `task_mark_blocked`, `task_mark_failed`, `task_mark_cancelled`, `task_generate_final_summary`, `task_export_handoff_bundle`, `task_import_handoff_bundle`.

Processes and shell:
`run_process`, `run_powershell`, `run_cmd`, `run_script_file`, `run_wsl_command`, `run_elevated_process_if_already_elevated`, `run_admin_task`, `process_start_tracked`, `process_get_status`, `process_wait`, `process_read_stdout`, `process_read_stderr`, `process_tail_output`, `process_cancel`, `process_kill_tree`, `process_list_tracked`, `process_generate_continuation_prompt`, `process_mark_requires_user_intervention`, `list_processes`, `get_process_detail`, `start_process`, `stop_process`, `kill_process_tree`.

Filesystem:
`list_directory`, `read_file`, `write_file`, `append_file`, `create_directory`, `search_files`, `search_text`, `compute_hash`, `copy_file`, `move_file`, `delete_file_to_recycle_bin`, `delete_file_permanent`, `create_backup`, `create_zip_archive`, `extract_zip_archive`.

Registry/services/events/scheduled tasks:
`registry_read`, `registry_list`, `registry_export`, `registry_write`, `registry_delete`, `registry_import_file`, `list_services`, `get_service_detail`, `start_service`, `stop_service`, `restart_service`, `set_service_startup_type`, `get_service_recovery_options`, `set_service_recovery_options`, `query_event_logs`, `get_recent_errors`, `get_app_crash_events`, `get_windows_update_events`, `export_event_log_slice`, `list_scheduled_tasks`, `get_scheduled_task`, `create_scheduled_task`, `enable_scheduled_task`, `disable_scheduled_task`, `delete_scheduled_task`, `run_scheduled_task`.

Package/dev/WSL/Docker/ADB:
`winget_detect`, `winget_search`, `winget_install`, `winget_upgrade`, `winget_uninstall`, `choco_detect`, `choco_install`, `scoop_detect`, `scoop_install`, `detect_dotnet_sdks`, `detect_git`, `detect_node`, `detect_pnpm`, `detect_python`, `detect_uv`, `detect_java`, `detect_vscode`, `detect_visual_studio`, `detect_visual_studio_build_tools`, `detect_windows_sdk`, `detect_android_sdk`, `detect_unity`, `inspect_project_structure`, `summarize_repo_state`, `run_solution_build`, `run_msbuild`, `run_dotnet_build`, `run_dotnet_test`, `run_npm_script`, `run_pnpm_script`, `run_python`, `run_git`, `run_lint`, `run_tests`, `wsl_status`, `wsl_list_distros`, `wsl_run_command`, `wsl_open_project`, `wsl_check_tooling`, `docker_status`, `docker_list_containers`, `docker_list_images`, `docker_compose_ps`, `docker_compose_up`, `docker_compose_down`, `docker_logs`, `docker_exec`, `adb_detect`, `adb_devices`, `adb_shell`, `adb_install_apk`, `adb_logcat`, `adb_reboot_device`.

UI:
DesktopAgent implements `ui_get_desktop_snapshot`, `ui_list_windows`, `ui_get_foreground_window`, `ui_focus_window`, `ui_click`, `ui_double_click`, `ui_right_click`, `ui_invoke_control`, `ui_set_control_value`, `ui_type_text`, `ui_send_hotkey`, `ui_clipboard_set`, `ui_clipboard_get_redacted`, `ui_screenshot_desktop`, and a basic `ui_execute_plan`.

## Scaffolded

`apply_unified_diff_patch`, ACL/ownership mutation, deep process module/window/network correlation, durable event-log watches, full scheduled-task object modeling, advanced UI waits/select/scroll/drag/drop/dialog handling, and patch-based cross-agent import validation are scaffolded with explicit `not_implemented` results or broker delegation placeholders.

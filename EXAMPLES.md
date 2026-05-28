# Examples

Start a task:

```json
{
  "tool": "task_start",
  "arguments": {
    "title": "Install SDK",
    "goal": "Install and validate a Windows SDK",
    "priority": 5
  }
}
```

Run a tracked process:

```json
{
  "tool": "process_start_tracked",
  "arguments": {
    "file_name": "dotnet",
    "arguments": "build WindowsPowerUserMcp.slnx",
    "working_directory": "C:\\dev\\WindowsPowerUserMcp",
    "task_id": "<task id>"
  }
}
```

Resume later:

```json
{
  "tool": "task_list_pending",
  "arguments": {}
}
```

UI plan:

```json
{
  "task_id": "task1",
  "actions": [
    {
      "action_type": "send_hotkey",
      "hotkey": "CTRL+L",
      "wait_before_ms": 0,
      "wait_after_ms": 250,
      "timeout_ms": 2000,
      "retry_count": 0,
      "failure_behavior": "stop",
      "screenshot_before": false,
      "screenshot_after": false,
      "safe_text_entry_mode": true,
      "stop_if_unexpected_sensitive_field_appears": true,
      "stop_if_uac_prompt_appears": true,
      "stop_if_credential_or_mfa_prompt_appears": true,
      "stop_if_user_intervention_required": true
    }
  ]
}
```

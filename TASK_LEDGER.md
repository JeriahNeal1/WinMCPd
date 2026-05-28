# Task Ledger

The broker stores durable operational memory in SQLite:

- `Tasks`
- `TaskEvents`
- `TrackedProcesses`
- `PendingContinuations`
- `Approvals`
- `AuditEvents`

The ledger stores goals, summaries, commands run, outputs/log paths, files/artifacts, blockers, next steps, continuation prompts, completion evidence, and process status. It does not store hidden chain-of-thought.

Process stdout/stderr are written to `%LOCALAPPDATA%\WindowsPowerUserMcp\processes`. Audit logs are append-only JSONL in `%LOCALAPPDATA%\WindowsPowerUserMcp\logs`.

Typical flow:

1. `task_start`
2. `process_start_tracked` or a typed operation
3. `task_append_event`
4. `process_wait` or `task_queue_continuation`
5. `task_generate_final_summary`
6. `task_mark_complete`

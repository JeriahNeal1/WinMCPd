using Microsoft.Data.Sqlite;
using WindowsPowerUserMcp.Core;

namespace WindowsPowerUserMcp.Orchestration;

using LedgerTaskStatus = WindowsPowerUserMcp.Core.TaskStatus;

public sealed class TaskLedger(StorageLayout layout)
{
    private readonly string _connectionString = new SqliteConnectionStringBuilder { DataSource = layout.SqlitePath }.ToString();

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(layout.SqlitePath)!);
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        var command = connection.CreateCommand();
        command.CommandText =
            """
            PRAGMA journal_mode=WAL;
            CREATE TABLE IF NOT EXISTS Tasks (
                id TEXT PRIMARY KEY,
                title TEXT NOT NULL,
                goal TEXT NOT NULL,
                status TEXT NOT NULL,
                created_at TEXT NOT NULL,
                updated_at TEXT NOT NULL,
                current_summary TEXT NULL,
                next_steps TEXT NULL,
                continuation_prompt TEXT NULL,
                completion_evidence TEXT NULL,
                owner_user TEXT NULL,
                priority INTEGER NOT NULL
            );

            CREATE TABLE IF NOT EXISTS TaskEvents (
                id TEXT PRIMARY KEY,
                task_id TEXT NOT NULL,
                timestamp TEXT NOT NULL,
                actor TEXT NOT NULL,
                event_type TEXT NOT NULL,
                summary TEXT NOT NULL,
                tool_name TEXT NULL,
                command_line_redacted TEXT NULL,
                working_directory TEXT NULL,
                result_status TEXT NULL,
                stdout_log_path TEXT NULL,
                stderr_log_path TEXT NULL,
                artifacts_json TEXT NULL,
                redactions_applied_json TEXT NULL
            );

            CREATE TABLE IF NOT EXISTS TrackedProcesses (
                id TEXT PRIMARY KEY,
                task_id TEXT NULL,
                pid INTEGER NULL,
                process_name TEXT NOT NULL,
                command_line_redacted TEXT NOT NULL,
                working_directory TEXT NULL,
                started_at TEXT NOT NULL,
                exited_at TEXT NULL,
                exit_code INTEGER NULL,
                status TEXT NOT NULL,
                stdout_log_path TEXT NOT NULL,
                stderr_log_path TEXT NOT NULL,
                expected_completion_signal TEXT NULL,
                continuation_prompt TEXT NULL,
                requires_user_intervention INTEGER NOT NULL
            );

            CREATE TABLE IF NOT EXISTS PendingContinuations (
                id TEXT PRIMARY KEY,
                task_id TEXT NOT NULL,
                condition_type TEXT NOT NULL,
                condition_payload_json TEXT NOT NULL,
                prompt TEXT NOT NULL,
                status TEXT NOT NULL,
                created_at TEXT NOT NULL,
                due_at TEXT NULL,
                triggered_at TEXT NULL
            );

            CREATE TABLE IF NOT EXISTS Approvals (
                id TEXT PRIMARY KEY,
                task_id TEXT NULL,
                timestamp TEXT NOT NULL,
                operation TEXT NOT NULL,
                risk_level TEXT NOT NULL,
                approval_source TEXT NOT NULL,
                approval_text_summary TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS AuditEvents (
                id TEXT PRIMARY KEY,
                timestamp TEXT NOT NULL,
                severity TEXT NOT NULL,
                component TEXT NOT NULL,
                operation TEXT NOT NULL,
                risk_level TEXT NOT NULL,
                user TEXT NOT NULL,
                result TEXT NOT NULL,
                summary TEXT NOT NULL,
                details_json_redacted TEXT NULL
            );
            """;
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<TaskRecord> StartTaskAsync(string title, string goal, string? ownerUser = null, int priority = 0, CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        var record = new TaskRecord(Guid.NewGuid().ToString("n"), title, goal, LedgerTaskStatus.Active, now, now, null, null, null, null, ownerUser ?? Environment.UserName, priority);
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO Tasks (id,title,goal,status,created_at,updated_at,current_summary,next_steps,continuation_prompt,completion_evidence,owner_user,priority)
            VALUES ($id,$title,$goal,$status,$created_at,$updated_at,$current_summary,$next_steps,$continuation_prompt,$completion_evidence,$owner_user,$priority);
            """;
        AddTaskParameters(command, record);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        return record;
    }

    public async Task<TaskRecord?> GetTaskAsync(string id, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM Tasks WHERE id = $id;";
        command.Parameters.AddWithValue("$id", id);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false) ? ReadTask(reader) : null;
    }

    public async Task<IReadOnlyList<TaskRecord>> ListTasksAsync(LedgerTaskStatus[] statuses, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        var statusValues = statuses.Select((s, i) => (s, Param: $"$s{i}")).ToArray();
        command.CommandText = $"SELECT * FROM Tasks WHERE status IN ({string.Join(",", statusValues.Select(s => s.Param))}) ORDER BY priority DESC, updated_at DESC;";
        foreach (var (status, param) in statusValues)
        {
            command.Parameters.AddWithValue(param, ToDb(status));
        }

        var results = new List<TaskRecord>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            results.Add(ReadTask(reader));
        }

        return results;
    }

    public async Task UpdateTaskFieldsAsync(
        string taskId,
        string? currentSummary = null,
        string? nextSteps = null,
        string? continuationPrompt = null,
        string? completionEvidence = null,
        LedgerTaskStatus? status = null,
        CancellationToken cancellationToken = default)
    {
        var updates = new List<string> { "updated_at = $updated_at" };
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.Parameters.AddWithValue("$id", taskId);
        command.Parameters.AddWithValue("$updated_at", DateTimeOffset.UtcNow.ToString("O"));

        AddUpdate(command, updates, "current_summary", currentSummary);
        AddUpdate(command, updates, "next_steps", nextSteps);
        AddUpdate(command, updates, "continuation_prompt", continuationPrompt);
        AddUpdate(command, updates, "completion_evidence", completionEvidence);
        if (status is { } s)
        {
            updates.Add("status = $status");
            command.Parameters.AddWithValue("$status", ToDb(s));
        }

        command.CommandText = $"UPDATE Tasks SET {string.Join(", ", updates)} WHERE id = $id;";
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<TaskEventRecord> AppendEventAsync(
        string taskId,
        string actor,
        string eventType,
        string summary,
        string? toolName = null,
        string? commandLineRedacted = null,
        string? workingDirectory = null,
        string? resultStatus = null,
        string? stdoutLogPath = null,
        string? stderrLogPath = null,
        string? artifactsJson = null,
        string? redactionsAppliedJson = null,
        CancellationToken cancellationToken = default)
    {
        var evt = new TaskEventRecord(Guid.NewGuid().ToString("n"), taskId, DateTimeOffset.UtcNow, actor, eventType, summary, toolName, commandLineRedacted, workingDirectory, resultStatus, stdoutLogPath, stderrLogPath, artifactsJson, redactionsAppliedJson);
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO TaskEvents (id,task_id,timestamp,actor,event_type,summary,tool_name,command_line_redacted,working_directory,result_status,stdout_log_path,stderr_log_path,artifacts_json,redactions_applied_json)
            VALUES ($id,$task_id,$timestamp,$actor,$event_type,$summary,$tool_name,$command_line_redacted,$working_directory,$result_status,$stdout_log_path,$stderr_log_path,$artifacts_json,$redactions_applied_json);
            UPDATE Tasks SET updated_at = $timestamp WHERE id = $task_id;
            """;
        command.Parameters.AddWithValue("$id", evt.Id);
        command.Parameters.AddWithValue("$task_id", evt.TaskId);
        command.Parameters.AddWithValue("$timestamp", evt.Timestamp.ToString("O"));
        command.Parameters.AddWithValue("$actor", evt.Actor);
        command.Parameters.AddWithValue("$event_type", evt.EventType);
        command.Parameters.AddWithValue("$summary", evt.Summary);
        AddNullable(command, "$tool_name", evt.ToolName);
        AddNullable(command, "$command_line_redacted", evt.CommandLineRedacted);
        AddNullable(command, "$working_directory", evt.WorkingDirectory);
        AddNullable(command, "$result_status", evt.ResultStatus);
        AddNullable(command, "$stdout_log_path", evt.StdoutLogPath);
        AddNullable(command, "$stderr_log_path", evt.StderrLogPath);
        AddNullable(command, "$artifacts_json", evt.ArtifactsJson);
        AddNullable(command, "$redactions_applied_json", evt.RedactionsAppliedJson);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        return evt;
    }

    public async Task<IReadOnlyList<TaskEventRecord>> ListEventsAsync(string taskId, int limit = 100, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM TaskEvents WHERE task_id = $task_id ORDER BY timestamp DESC LIMIT $limit;";
        command.Parameters.AddWithValue("$task_id", taskId);
        command.Parameters.AddWithValue("$limit", limit);
        var results = new List<TaskEventRecord>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            results.Add(ReadTaskEvent(reader));
        }

        return results;
    }

    public async Task<PendingContinuationRecord> QueueContinuationAsync(
        string taskId,
        ContinuationConditionType conditionType,
        string conditionPayloadJson,
        string prompt,
        DateTimeOffset? dueAt = null,
        CancellationToken cancellationToken = default)
    {
        var record = new PendingContinuationRecord(Guid.NewGuid().ToString("n"), taskId, conditionType, conditionPayloadJson, prompt, "pending", DateTimeOffset.UtcNow, dueAt, null);
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO PendingContinuations (id,task_id,condition_type,condition_payload_json,prompt,status,created_at,due_at,triggered_at)
            VALUES ($id,$task_id,$condition_type,$condition_payload_json,$prompt,$status,$created_at,$due_at,$triggered_at);
            UPDATE Tasks SET status = 'waiting', continuation_prompt = $prompt, updated_at = $created_at WHERE id = $task_id;
            """;
        command.Parameters.AddWithValue("$id", record.Id);
        command.Parameters.AddWithValue("$task_id", record.TaskId);
        command.Parameters.AddWithValue("$condition_type", ToDb(record.ConditionType));
        command.Parameters.AddWithValue("$condition_payload_json", record.ConditionPayloadJson);
        command.Parameters.AddWithValue("$prompt", record.Prompt);
        command.Parameters.AddWithValue("$status", record.Status);
        command.Parameters.AddWithValue("$created_at", record.CreatedAt.ToString("O"));
        AddNullable(command, "$due_at", record.DueAt?.ToString("O"));
        AddNullable(command, "$triggered_at", record.TriggeredAt?.ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        return record;
    }

    public async Task<IReadOnlyList<PendingContinuationRecord>> ListPendingContinuationsAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM PendingContinuations WHERE status = 'pending' ORDER BY created_at DESC;";
        var results = new List<PendingContinuationRecord>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            results.Add(ReadContinuation(reader));
        }

        return results;
    }

    public async Task InsertTrackedProcessAsync(TrackedProcessRecord record, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO TrackedProcesses (id,task_id,pid,process_name,command_line_redacted,working_directory,started_at,exited_at,exit_code,status,stdout_log_path,stderr_log_path,expected_completion_signal,continuation_prompt,requires_user_intervention)
            VALUES ($id,$task_id,$pid,$process_name,$command_line_redacted,$working_directory,$started_at,$exited_at,$exit_code,$status,$stdout_log_path,$stderr_log_path,$expected_completion_signal,$continuation_prompt,$requires_user_intervention);
            """;
        AddTrackedParameters(command, record);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task UpdateTrackedProcessAsync(string id, TrackedProcessStatus status, int? exitCode = null, DateTimeOffset? exitedAt = null, bool? requiresUserIntervention = null, string? continuationPrompt = null, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        var updates = new List<string> { "status = $status" };
        command.Parameters.AddWithValue("$id", id);
        command.Parameters.AddWithValue("$status", ToDb(status));
        if (exitCode is not null)
        {
            updates.Add("exit_code = $exit_code");
            command.Parameters.AddWithValue("$exit_code", exitCode.Value);
        }

        if (exitedAt is not null)
        {
            updates.Add("exited_at = $exited_at");
            command.Parameters.AddWithValue("$exited_at", exitedAt.Value.ToString("O"));
        }

        if (requiresUserIntervention is not null)
        {
            updates.Add("requires_user_intervention = $requires_user_intervention");
            command.Parameters.AddWithValue("$requires_user_intervention", requiresUserIntervention.Value ? 1 : 0);
        }

        if (continuationPrompt is not null)
        {
            updates.Add("continuation_prompt = $continuation_prompt");
            command.Parameters.AddWithValue("$continuation_prompt", continuationPrompt);
        }

        command.CommandText = $"UPDATE TrackedProcesses SET {string.Join(", ", updates)} WHERE id = $id;";
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<TrackedProcessRecord?> GetTrackedProcessAsync(string id, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM TrackedProcesses WHERE id = $id;";
        command.Parameters.AddWithValue("$id", id);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false) ? ReadTrackedProcess(reader) : null;
    }

    public async Task<IReadOnlyList<TrackedProcessRecord>> ListTrackedProcessesAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM TrackedProcesses ORDER BY started_at DESC LIMIT 250;";
        var results = new List<TrackedProcessRecord>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            results.Add(ReadTrackedProcess(reader));
        }

        return results;
    }

    private async Task<SqliteConnection> OpenAsync(CancellationToken cancellationToken)
    {
        var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        return connection;
    }

    private static void AddUpdate(SqliteCommand command, List<string> updates, string column, string? value)
    {
        if (value is null)
        {
            return;
        }

        var parameter = "$" + column;
        updates.Add($"{column} = {parameter}");
        command.Parameters.AddWithValue(parameter, value);
    }

    private static void AddTaskParameters(SqliteCommand command, TaskRecord record)
    {
        command.Parameters.AddWithValue("$id", record.Id);
        command.Parameters.AddWithValue("$title", record.Title);
        command.Parameters.AddWithValue("$goal", record.Goal);
        command.Parameters.AddWithValue("$status", ToDb(record.Status));
        command.Parameters.AddWithValue("$created_at", record.CreatedAt.ToString("O"));
        command.Parameters.AddWithValue("$updated_at", record.UpdatedAt.ToString("O"));
        AddNullable(command, "$current_summary", record.CurrentSummary);
        AddNullable(command, "$next_steps", record.NextSteps);
        AddNullable(command, "$continuation_prompt", record.ContinuationPrompt);
        AddNullable(command, "$completion_evidence", record.CompletionEvidence);
        AddNullable(command, "$owner_user", record.OwnerUser);
        command.Parameters.AddWithValue("$priority", record.Priority);
    }

    private static void AddTrackedParameters(SqliteCommand command, TrackedProcessRecord record)
    {
        command.Parameters.AddWithValue("$id", record.Id);
        AddNullable(command, "$task_id", record.TaskId);
        AddNullable(command, "$pid", record.Pid);
        command.Parameters.AddWithValue("$process_name", record.ProcessName);
        command.Parameters.AddWithValue("$command_line_redacted", record.CommandLineRedacted);
        AddNullable(command, "$working_directory", record.WorkingDirectory);
        command.Parameters.AddWithValue("$started_at", record.StartedAt.ToString("O"));
        AddNullable(command, "$exited_at", record.ExitedAt?.ToString("O"));
        AddNullable(command, "$exit_code", record.ExitCode);
        command.Parameters.AddWithValue("$status", ToDb(record.Status));
        command.Parameters.AddWithValue("$stdout_log_path", record.StdoutLogPath);
        command.Parameters.AddWithValue("$stderr_log_path", record.StderrLogPath);
        AddNullable(command, "$expected_completion_signal", record.ExpectedCompletionSignal);
        AddNullable(command, "$continuation_prompt", record.ContinuationPrompt);
        command.Parameters.AddWithValue("$requires_user_intervention", record.RequiresUserIntervention ? 1 : 0);
    }

    private static void AddNullable(SqliteCommand command, string name, object? value) =>
        command.Parameters.AddWithValue(name, value ?? DBNull.Value);

    private static TaskRecord ReadTask(SqliteDataReader reader) =>
        new(
            reader.GetString(reader.GetOrdinal("id")),
            reader.GetString(reader.GetOrdinal("title")),
            reader.GetString(reader.GetOrdinal("goal")),
            ParseEnum<LedgerTaskStatus>(reader.GetString(reader.GetOrdinal("status"))),
            DateTimeOffset.Parse(reader.GetString(reader.GetOrdinal("created_at"))),
            DateTimeOffset.Parse(reader.GetString(reader.GetOrdinal("updated_at"))),
            ReadNullableString(reader, "current_summary"),
            ReadNullableString(reader, "next_steps"),
            ReadNullableString(reader, "continuation_prompt"),
            ReadNullableString(reader, "completion_evidence"),
            ReadNullableString(reader, "owner_user"),
            reader.GetInt32(reader.GetOrdinal("priority")));

    private static TaskEventRecord ReadTaskEvent(SqliteDataReader reader) =>
        new(
            reader.GetString(reader.GetOrdinal("id")),
            reader.GetString(reader.GetOrdinal("task_id")),
            DateTimeOffset.Parse(reader.GetString(reader.GetOrdinal("timestamp"))),
            reader.GetString(reader.GetOrdinal("actor")),
            reader.GetString(reader.GetOrdinal("event_type")),
            reader.GetString(reader.GetOrdinal("summary")),
            ReadNullableString(reader, "tool_name"),
            ReadNullableString(reader, "command_line_redacted"),
            ReadNullableString(reader, "working_directory"),
            ReadNullableString(reader, "result_status"),
            ReadNullableString(reader, "stdout_log_path"),
            ReadNullableString(reader, "stderr_log_path"),
            ReadNullableString(reader, "artifacts_json"),
            ReadNullableString(reader, "redactions_applied_json"));

    private static PendingContinuationRecord ReadContinuation(SqliteDataReader reader) =>
        new(
            reader.GetString(reader.GetOrdinal("id")),
            reader.GetString(reader.GetOrdinal("task_id")),
            ParseEnum<ContinuationConditionType>(reader.GetString(reader.GetOrdinal("condition_type"))),
            reader.GetString(reader.GetOrdinal("condition_payload_json")),
            reader.GetString(reader.GetOrdinal("prompt")),
            reader.GetString(reader.GetOrdinal("status")),
            DateTimeOffset.Parse(reader.GetString(reader.GetOrdinal("created_at"))),
            ReadNullableDate(reader, "due_at"),
            ReadNullableDate(reader, "triggered_at"));

    private static TrackedProcessRecord ReadTrackedProcess(SqliteDataReader reader) =>
        new(
            reader.GetString(reader.GetOrdinal("id")),
            ReadNullableString(reader, "task_id"),
            ReadNullableInt(reader, "pid"),
            reader.GetString(reader.GetOrdinal("process_name")),
            reader.GetString(reader.GetOrdinal("command_line_redacted")),
            ReadNullableString(reader, "working_directory"),
            DateTimeOffset.Parse(reader.GetString(reader.GetOrdinal("started_at"))),
            ReadNullableDate(reader, "exited_at"),
            ReadNullableInt(reader, "exit_code"),
            ParseEnum<TrackedProcessStatus>(reader.GetString(reader.GetOrdinal("status"))),
            reader.GetString(reader.GetOrdinal("stdout_log_path")),
            reader.GetString(reader.GetOrdinal("stderr_log_path")),
            ReadNullableString(reader, "expected_completion_signal"),
            ReadNullableString(reader, "continuation_prompt"),
            reader.GetInt32(reader.GetOrdinal("requires_user_intervention")) == 1);

    private static string? ReadNullableString(SqliteDataReader reader, string name)
    {
        var ordinal = reader.GetOrdinal(name);
        return reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
    }

    private static int? ReadNullableInt(SqliteDataReader reader, string name)
    {
        var ordinal = reader.GetOrdinal(name);
        return reader.IsDBNull(ordinal) ? null : reader.GetInt32(ordinal);
    }

    private static DateTimeOffset? ReadNullableDate(SqliteDataReader reader, string name)
    {
        var value = ReadNullableString(reader, name);
        return value is null ? null : DateTimeOffset.Parse(value);
    }

    private static string ToDb<T>(T value) where T : struct, Enum =>
        value.ToString().ToLowerInvariant();

    private static T ParseEnum<T>(string value) where T : struct, Enum
    {
        foreach (var enumValue in Enum.GetValues<T>())
        {
            if (string.Equals(enumValue.ToString(), value, StringComparison.OrdinalIgnoreCase))
            {
                return enumValue;
            }
        }

        var pascal = string.Concat(value.Split('_', '-').Select(s => string.IsNullOrEmpty(s) ? s : char.ToUpperInvariant(s[0]) + s[1..]));
        return Enum.Parse<T>(pascal, ignoreCase: true);
    }
}

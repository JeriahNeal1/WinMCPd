using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using WindowsPowerUserMcp.Core;
using WindowsPowerUserMcp.Security;

namespace WindowsPowerUserMcp.Orchestration;

public sealed record ProcessStartRequest(
    string FileName,
    string Arguments,
    string? WorkingDirectory = null,
    int? TimeoutSeconds = null,
    string? TaskId = null,
    string? ExpectedCompletionSignal = null,
    string? ContinuationPrompt = null);

internal sealed record TrackedRuntime(Process Process, Task MonitorTask);

public sealed class CommandRunner(
    StorageLayout layout,
    WindowsPowerUserMcpOptions options,
    SecretRedactor redactor,
    SafetyGuards safetyGuards,
    IAuditLogger auditLogger,
    TaskLedger ledger)
{
    private readonly ConcurrentDictionary<string, TrackedRuntime> _tracked = new();

    public async Task<ResultEnvelope<CommandExecutionResult>> RunAsync(ProcessStartRequest request, CancellationToken cancellationToken = default)
    {
        var commandLine = $"{request.FileName} {request.Arguments}".Trim();
        var redactedCommand = redactor.Redact(commandLine);
        var safety = safetyGuards.CheckCommandAllowed(redactedCommand.Text);
        if (!safety.Allowed)
        {
            return ResultEnvelope<CommandExecutionResult>.Fail("blocked_command", safety.Reason!, OperationStatus.Failed, RiskLevel.SecuritySensitive);
        }

        var started = DateTimeOffset.UtcNow;
        var stdoutPath = NewProcessLogPath("stdout");
        var stderrPath = NewProcessLogPath("stderr");
        var startInfo = CreateStartInfo(request);
        await using var stdoutWriter = new StreamWriter(stdoutPath, append: false, Encoding.UTF8);
        await using var stderrWriter = new StreamWriter(stderrPath, append: false, Encoding.UTF8);
        using var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        var stdoutTail = new BoundedTextBuffer(options.MaxStdoutCaptureBytes);
        var stderrTail = new BoundedTextBuffer(options.MaxStderrCaptureBytes);

        try
        {
            if (!process.Start())
            {
                return ResultEnvelope<CommandExecutionResult>.Fail("process_start_failed", "Process.Start returned false.", OperationStatus.Failed, RiskLevel.Medium);
            }

            var stdoutTask = PumpAsync(process.StandardOutput, stdoutWriter, stdoutTail, cancellationToken);
            var stderrTask = PumpAsync(process.StandardError, stderrWriter, stderrTail, cancellationToken);
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(request.TimeoutSeconds ?? options.DefaultTimeoutSeconds));

            var timedOut = false;
            try
            {
                await process.WaitForExitAsync(timeoutCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                timedOut = true;
                TryKill(process);
                await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
            }

            await Task.WhenAll(stdoutTask, stderrTask).ConfigureAwait(false);
            var duration = DateTimeOffset.UtcNow - started;
            var result = new CommandExecutionResult(
                redactedCommand.Text,
                request.WorkingDirectory,
                process.ExitCode,
                duration,
                timedOut,
                stdoutTail.ToString(),
                stderrTail.ToString(),
                stdoutPath,
                stderrPath);

            var auditId = await auditLogger.WriteAsync("orchestration", "run_process", RiskLevel.Medium, timedOut ? "timed_out" : "complete", redactedCommand.Text, result, cancellationToken: CancellationToken.None).ConfigureAwait(false);
            return timedOut
                ? ResultEnvelope<CommandExecutionResult>.Fail("process_timeout", "Process timed out and was terminated.", OperationStatus.TimedOut, RiskLevel.Medium, auditId, LogArtifacts(stdoutPath, stderrPath), redactedCommand.RedactionsApplied)
                : ResultEnvelope<CommandExecutionResult>.Ok(result, "Process completed.", RiskLevel.Medium, auditId, LogArtifacts(stdoutPath, stderrPath), redactedCommand.RedactionsApplied);
        }
        catch (Exception ex)
        {
            var duration = DateTimeOffset.UtcNow - started;
            var result = new CommandExecutionResult(redactedCommand.Text, request.WorkingDirectory, null, duration, false, stdoutTail.ToString(), stderrTail.ToString(), stdoutPath, stderrPath);
            var auditId = await auditLogger.WriteAsync("orchestration", "run_process", RiskLevel.Medium, "failed", ex.Message, result, "error", CancellationToken.None).ConfigureAwait(false);
            return ResultEnvelope<CommandExecutionResult>.Fail("process_exception", ex.Message, OperationStatus.Failed, RiskLevel.Medium, auditId, LogArtifacts(stdoutPath, stderrPath), redactedCommand.RedactionsApplied);
        }
    }

    public async Task<ResultEnvelope<TrackedProcessRecord>> StartTrackedAsync(ProcessStartRequest request, CancellationToken cancellationToken = default)
    {
        var commandLine = $"{request.FileName} {request.Arguments}".Trim();
        var redactedCommand = redactor.Redact(commandLine);
        var safety = safetyGuards.CheckCommandAllowed(redactedCommand.Text);
        if (!safety.Allowed)
        {
            return ResultEnvelope<TrackedProcessRecord>.Fail("blocked_command", safety.Reason!, OperationStatus.Failed, RiskLevel.SecuritySensitive);
        }

        var stdoutPath = NewProcessLogPath("stdout");
        var stderrPath = NewProcessLogPath("stderr");
        var process = new Process { StartInfo = CreateStartInfo(request), EnableRaisingEvents = true };
        if (!process.Start())
        {
            return ResultEnvelope<TrackedProcessRecord>.Fail("process_start_failed", "Process.Start returned false.", OperationStatus.Failed, RiskLevel.Medium);
        }

        var record = new TrackedProcessRecord(
            Guid.NewGuid().ToString("n"),
            request.TaskId,
            process.Id,
            request.FileName,
            redactedCommand.Text,
            request.WorkingDirectory,
            DateTimeOffset.UtcNow,
            null,
            null,
            TrackedProcessStatus.Running,
            stdoutPath,
            stderrPath,
            request.ExpectedCompletionSignal,
            request.ContinuationPrompt,
            false);

        await ledger.InsertTrackedProcessAsync(record, cancellationToken).ConfigureAwait(false);
        var monitorTask = MonitorTrackedProcessAsync(record.Id, process, stdoutPath, stderrPath, cancellationToken);
        _tracked[record.Id] = new TrackedRuntime(process, monitorTask);
        var auditId = await auditLogger.WriteAsync("orchestration", "process_start_tracked", RiskLevel.Medium, "started", redactedCommand.Text, record, cancellationToken: cancellationToken).ConfigureAwait(false);
        return ResultEnvelope<TrackedProcessRecord>.Ok(record, "Tracked process started.", RiskLevel.Medium, auditId, LogArtifacts(stdoutPath, stderrPath), redactedCommand.RedactionsApplied);
    }

    public async Task<ResultEnvelope<TrackedProcessRecord>> GetStatusAsync(string id, CancellationToken cancellationToken = default)
    {
        var record = await ledger.GetTrackedProcessAsync(id, cancellationToken).ConfigureAwait(false);
        return record is null
            ? ResultEnvelope<TrackedProcessRecord>.Fail("process_not_found", $"Tracked process '{id}' was not found.", OperationStatus.Failed, RiskLevel.ReadOnly)
            : ResultEnvelope<TrackedProcessRecord>.Ok(record, "Tracked process status.", RiskLevel.ReadOnly);
    }

    public async Task<ResultEnvelope<TrackedProcessRecord>> WaitAsync(string id, int timeoutSeconds, CancellationToken cancellationToken = default)
    {
        if (_tracked.TryGetValue(id, out var runtime))
        {
            var completed = await Task.WhenAny(runtime.MonitorTask, Task.Delay(TimeSpan.FromSeconds(timeoutSeconds), cancellationToken)).ConfigureAwait(false) == runtime.MonitorTask;
            if (!completed)
            {
                var current = await ledger.GetTrackedProcessAsync(id, cancellationToken).ConfigureAwait(false);
                return current is null
                    ? ResultEnvelope<TrackedProcessRecord>.Fail("process_not_found", $"Tracked process '{id}' was not found.", OperationStatus.Failed, RiskLevel.ReadOnly)
                    : ResultEnvelope<TrackedProcessRecord>.Fail("wait_timeout", "Timed out waiting for tracked process.", OperationStatus.TimedOut, RiskLevel.ReadOnly);
            }
        }
        else
        {
            var deadline = DateTimeOffset.UtcNow.AddSeconds(timeoutSeconds);
            while (DateTimeOffset.UtcNow < deadline)
            {
                var polled = await ledger.GetTrackedProcessAsync(id, cancellationToken).ConfigureAwait(false);
                if (polled is null)
                {
                    return ResultEnvelope<TrackedProcessRecord>.Fail("process_not_found", $"Tracked process '{id}' was not found.", OperationStatus.Failed, RiskLevel.ReadOnly);
                }

                if (polled.Status is TrackedProcessStatus.Exited or TrackedProcessStatus.Cancelled or TrackedProcessStatus.Failed or TrackedProcessStatus.TimedOut)
                {
                    return ResultEnvelope<TrackedProcessRecord>.Ok(polled, "Tracked process finished.", RiskLevel.ReadOnly);
                }

                await Task.Delay(500, cancellationToken).ConfigureAwait(false);
            }
        }

        var record = await ledger.GetTrackedProcessAsync(id, cancellationToken).ConfigureAwait(false);
        return record is null
            ? ResultEnvelope<TrackedProcessRecord>.Fail("process_not_found", $"Tracked process '{id}' was not found.", OperationStatus.Failed, RiskLevel.ReadOnly)
            : ResultEnvelope<TrackedProcessRecord>.Ok(record, "Tracked process finished.", RiskLevel.ReadOnly);
    }

    public async Task<ResultEnvelope<object>> CancelAsync(string id, bool killTree = true, CancellationToken cancellationToken = default)
    {
        if (!_tracked.TryGetValue(id, out var runtime))
        {
            return ResultEnvelope<object>.Fail("process_not_running", $"Tracked process '{id}' is not running in this broker session.", OperationStatus.Failed, RiskLevel.High);
        }

        TryKill(runtime.Process, killTree);
        await ledger.UpdateTrackedProcessAsync(id, TrackedProcessStatus.Cancelled, exitedAt: DateTimeOffset.UtcNow, cancellationToken: cancellationToken).ConfigureAwait(false);
        return ResultEnvelope<object>.Ok(new { id }, "Tracked process cancelled.", RiskLevel.High);
    }

    public async Task<ResultEnvelope<object>> MarkRequiresUserInterventionAsync(string id, string? continuationPrompt, CancellationToken cancellationToken = default)
    {
        await ledger.UpdateTrackedProcessAsync(id, TrackedProcessStatus.RequiresUserIntervention, requiresUserIntervention: true, continuationPrompt: continuationPrompt, cancellationToken: cancellationToken).ConfigureAwait(false);
        return ResultEnvelope<object>.Ok(new { id, continuation_prompt = continuationPrompt }, "Tracked process marked as requiring user intervention.", RiskLevel.Low);
    }

    public async Task<ResultEnvelope<IReadOnlyList<TrackedProcessRecord>>> ListTrackedAsync(CancellationToken cancellationToken = default)
    {
        var records = await ledger.ListTrackedProcessesAsync(cancellationToken).ConfigureAwait(false);
        return ResultEnvelope<IReadOnlyList<TrackedProcessRecord>>.Ok(records, "Tracked processes.", RiskLevel.ReadOnly);
    }

    public ResultEnvelope<object> ReadLogTail(string path, int bytes)
    {
        if (!File.Exists(path))
        {
            return ResultEnvelope<object>.Fail("log_not_found", $"Log file '{path}' does not exist.", OperationStatus.Failed, RiskLevel.ReadOnly);
        }

        using var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        var readBytes = (int)Math.Min(bytes, stream.Length);
        stream.Seek(-readBytes, SeekOrigin.End);
        var buffer = new byte[readBytes];
        var read = stream.Read(buffer, 0, buffer.Length);
        var text = Encoding.UTF8.GetString(buffer, 0, read);
        return ResultEnvelope<object>.Ok(new { path, bytes = read, text = redactor.Redact(text).Text }, "Log tail.", RiskLevel.ReadOnly);
    }

    private async Task MonitorTrackedProcessAsync(string id, Process process, string stdoutPath, string stderrPath, CancellationToken cancellationToken)
    {
        await using var stdoutWriter = new StreamWriter(stdoutPath, append: false, Encoding.UTF8);
        await using var stderrWriter = new StreamWriter(stderrPath, append: false, Encoding.UTF8);
        var stdoutTail = new BoundedTextBuffer(options.MaxStdoutCaptureBytes);
        var stderrTail = new BoundedTextBuffer(options.MaxStderrCaptureBytes);
        var stdoutTask = PumpAsync(process.StandardOutput, stdoutWriter, stdoutTail, CancellationToken.None);
        var stderrTask = PumpAsync(process.StandardError, stderrWriter, stderrTail, CancellationToken.None);

        try
        {
            await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
            await Task.WhenAll(stdoutTask, stderrTask).ConfigureAwait(false);
            await ledger.UpdateTrackedProcessAsync(id, TrackedProcessStatus.Exited, process.ExitCode, DateTimeOffset.UtcNow, cancellationToken: CancellationToken.None).ConfigureAwait(false);
        }
        catch
        {
            await ledger.UpdateTrackedProcessAsync(id, TrackedProcessStatus.Failed, exitedAt: DateTimeOffset.UtcNow, cancellationToken: CancellationToken.None).ConfigureAwait(false);
        }
        finally
        {
            _tracked.TryRemove(id, out _);
            process.Dispose();
        }
    }

    private static async Task PumpAsync(StreamReader reader, StreamWriter writer, BoundedTextBuffer tail, CancellationToken cancellationToken)
    {
        var buffer = new char[4096];
        int read;
        while ((read = await reader.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
        {
            var chunk = new string(buffer, 0, read);
            tail.Append(chunk);
            await writer.WriteAsync(chunk.AsMemory(), cancellationToken).ConfigureAwait(false);
            await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private ProcessStartInfo CreateStartInfo(ProcessStartRequest request) =>
        new()
        {
            FileName = request.FileName,
            Arguments = request.Arguments,
            WorkingDirectory = string.IsNullOrWhiteSpace(request.WorkingDirectory) ? Environment.CurrentDirectory : request.WorkingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

    private string NewProcessLogPath(string suffix)
    {
        Directory.CreateDirectory(layout.Processes);
        return Path.Combine(layout.Processes, $"{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():n}-{suffix}.log");
    }

    private static IReadOnlyDictionary<string, string> LogArtifacts(string stdoutPath, string stderrPath) =>
        new Dictionary<string, string>
        {
            ["stdout_log_path"] = stdoutPath,
            ["stderr_log_path"] = stderrPath
        };

    private static void TryKill(Process process, bool entireTree = true)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: entireTree);
            }
        }
        catch
        {
            // Best effort cancellation. The caller receives the captured status/logs.
        }
    }
}

internal sealed class BoundedTextBuffer(int maxBytes)
{
    private readonly StringBuilder _builder = new();

    public void Append(string value)
    {
        _builder.Append(value);
        var maxChars = Math.Max(1024, maxBytes / 2);
        if (_builder.Length > maxChars)
        {
            _builder.Remove(0, _builder.Length - maxChars);
        }
    }

    public override string ToString() => _builder.ToString();
}

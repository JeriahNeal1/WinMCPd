using System.Net.Sockets;
using WindowsPowerUserMcp.Core;

namespace WindowsPowerUserMcp.Orchestration;

public sealed class WaitServices(TaskLedger ledger)
{
    public async Task<ResultEnvelope<object>> WaitUntilTimeAsync(DateTimeOffset dueAt, CancellationToken cancellationToken = default)
    {
        var delay = dueAt - DateTimeOffset.Now;
        if (delay > TimeSpan.Zero)
        {
            await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
        }

        return ResultEnvelope<object>.Ok(new { due_at = dueAt, triggered_at = DateTimeOffset.UtcNow }, "Time condition reached.", RiskLevel.ReadOnly);
    }

    public async Task<ResultEnvelope<object>> WaitUntilFileExistsAsync(string path, int timeoutSeconds, CancellationToken cancellationToken = default)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(timeoutSeconds);
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (File.Exists(path) || Directory.Exists(path))
            {
                return ResultEnvelope<object>.Ok(new { path, exists = true }, "Path exists.", RiskLevel.ReadOnly);
            }

            await Task.Delay(500, cancellationToken).ConfigureAwait(false);
        }

        return ResultEnvelope<object>.Fail("wait_timeout", $"Timed out waiting for '{path}'.", OperationStatus.TimedOut, RiskLevel.ReadOnly);
    }

    public async Task<ResultEnvelope<object>> WaitUntilPortOpenAsync(string host, int port, int timeoutSeconds, CancellationToken cancellationToken = default)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(timeoutSeconds);
        while (DateTimeOffset.UtcNow < deadline)
        {
            using var client = new TcpClient();
            try
            {
                await client.ConnectAsync(host, port, cancellationToken).ConfigureAwait(false);
                return ResultEnvelope<object>.Ok(new { host, port, open = true }, "Port is open.", RiskLevel.ReadOnly);
            }
            catch
            {
                await Task.Delay(500, cancellationToken).ConfigureAwait(false);
            }
        }

        return ResultEnvelope<object>.Fail("wait_timeout", $"Timed out waiting for {host}:{port}.", OperationStatus.TimedOut, RiskLevel.ReadOnly);
    }

    public async Task<ResultEnvelope<object>> WatchFileCreatedAsync(string path, int timeoutSeconds, CancellationToken cancellationToken = default)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        var file = Path.GetFileName(path);
        if (string.IsNullOrWhiteSpace(directory) || string.IsNullOrWhiteSpace(file))
        {
            return ResultEnvelope<object>.Fail("invalid_path", "A full file path is required.", OperationStatus.Failed, RiskLevel.ReadOnly);
        }

        if (File.Exists(path))
        {
            return ResultEnvelope<object>.Ok(new { path, triggered = true, reason = "already_exists" }, "File already exists.", RiskLevel.ReadOnly);
        }

        var tcs = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var watcher = new FileSystemWatcher(directory, file)
        {
            EnableRaisingEvents = true,
            IncludeSubdirectories = false
        };
        FileSystemEventHandler handler = (_, args) =>
        {
            if (string.Equals(args.FullPath, Path.GetFullPath(path), StringComparison.OrdinalIgnoreCase))
            {
                tcs.TrySetResult(null);
            }
        };
        watcher.Created += handler;
        watcher.Renamed += (_, args) =>
        {
            if (string.Equals(args.FullPath, Path.GetFullPath(path), StringComparison.OrdinalIgnoreCase))
            {
                tcs.TrySetResult(null);
            }
        };

        var completed = await Task.WhenAny(tcs.Task, Task.Delay(TimeSpan.FromSeconds(timeoutSeconds), cancellationToken)).ConfigureAwait(false) == tcs.Task;
        return completed
            ? ResultEnvelope<object>.Ok(new { path, triggered = true }, "File creation observed.", RiskLevel.ReadOnly)
            : ResultEnvelope<object>.Fail("watch_timeout", $"Timed out watching for '{path}'.", OperationStatus.TimedOut, RiskLevel.ReadOnly);
    }

    public async Task<ResultEnvelope<PendingContinuationRecord>> QueueManualContinuationAsync(string taskId, string prompt, CancellationToken cancellationToken = default)
    {
        var continuation = await ledger.QueueContinuationAsync(taskId, ContinuationConditionType.ManualUserContinue, "{}", prompt, cancellationToken: cancellationToken).ConfigureAwait(false);
        return ResultEnvelope<PendingContinuationRecord>.Ok(continuation, "Manual continuation queued.", RiskLevel.Low);
    }
}

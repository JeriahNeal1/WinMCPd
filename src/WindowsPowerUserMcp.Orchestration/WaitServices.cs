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

    public async Task<ResultEnvelope<object>> WatchFileChangedAsync(string path, int timeoutSeconds, CancellationToken cancellationToken = default)
    {
        var fullPath = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(fullPath);
        var file = Path.GetFileName(fullPath);
        if (string.IsNullOrWhiteSpace(directory) || string.IsNullOrWhiteSpace(file))
        {
            return ResultEnvelope<object>.Fail("invalid_path", "A full file path is required.", OperationStatus.Failed, RiskLevel.ReadOnly);
        }

        Directory.CreateDirectory(directory);
        var tcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var watcher = new FileSystemWatcher(directory, file)
        {
            EnableRaisingEvents = true,
            IncludeSubdirectories = false,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size
        };

        FileSystemEventHandler changed = (_, args) => tcs.TrySetResult(args.ChangeType.ToString());
        RenamedEventHandler renamed = (_, args) => tcs.TrySetResult(args.ChangeType.ToString());
        watcher.Created += changed;
        watcher.Changed += changed;
        watcher.Renamed += renamed;

        var completed = await Task.WhenAny(tcs.Task, Task.Delay(TimeSpan.FromSeconds(timeoutSeconds), cancellationToken)).ConfigureAwait(false) == tcs.Task;
        return completed
            ? ResultEnvelope<object>.Ok(new { path = fullPath, triggered = true, change_type = await tcs.Task.ConfigureAwait(false) }, "File change observed.", RiskLevel.ReadOnly)
            : ResultEnvelope<object>.Fail("watch_timeout", $"Timed out watching for changes to '{fullPath}'.", OperationStatus.TimedOut, RiskLevel.ReadOnly);
    }

    public async Task<ResultEnvelope<object>> WatchDirectoryAsync(string path, int timeoutSeconds, CancellationToken cancellationToken = default)
    {
        var directory = Path.GetFullPath(path);
        if (!Directory.Exists(directory))
        {
            return ResultEnvelope<object>.Fail("directory_not_found", $"Directory '{directory}' does not exist.", OperationStatus.Failed, RiskLevel.ReadOnly);
        }

        var tcs = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var watcher = new FileSystemWatcher(directory)
        {
            EnableRaisingEvents = true,
            IncludeSubdirectories = false,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size
        };

        FileSystemEventHandler changed = (_, args) => tcs.TrySetResult(new { args.FullPath, change_type = args.ChangeType.ToString() });
        RenamedEventHandler renamed = (_, args) => tcs.TrySetResult(new { args.FullPath, change_type = args.ChangeType.ToString() });
        watcher.Created += changed;
        watcher.Changed += changed;
        watcher.Deleted += changed;
        watcher.Renamed += renamed;

        var completed = await Task.WhenAny(tcs.Task, Task.Delay(TimeSpan.FromSeconds(timeoutSeconds), cancellationToken)).ConfigureAwait(false) == tcs.Task;
        return completed
            ? ResultEnvelope<object>.Ok(await tcs.Task.ConfigureAwait(false), "Directory change observed.", RiskLevel.ReadOnly)
            : ResultEnvelope<object>.Fail("watch_timeout", $"Timed out watching directory '{directory}'.", OperationStatus.TimedOut, RiskLevel.ReadOnly);
    }

    public async Task<ResultEnvelope<object>> WatchDownloadCompleteAsync(string path, int timeoutSeconds, CancellationToken cancellationToken = default)
    {
        var fullPath = Path.GetFullPath(path);
        var deadline = DateTimeOffset.UtcNow.AddSeconds(timeoutSeconds);
        long? lastLength = null;
        var stableTicks = 0;

        while (DateTimeOffset.UtcNow < deadline)
        {
            if (File.Exists(fullPath) && !File.Exists(fullPath + ".crdownload") && !File.Exists(fullPath + ".part") && !File.Exists(fullPath + ".tmp"))
            {
                var length = new FileInfo(fullPath).Length;
                stableTicks = lastLength == length ? stableTicks + 1 : 0;
                lastLength = length;
                if (stableTicks >= 2)
                {
                    return ResultEnvelope<object>.Ok(new { path = fullPath, bytes = length, stable = true }, "Download appears complete.", RiskLevel.ReadOnly);
                }
            }

            await Task.Delay(500, cancellationToken).ConfigureAwait(false);
        }

        return ResultEnvelope<object>.Fail("watch_timeout", $"Timed out waiting for download '{fullPath}' to complete.", OperationStatus.TimedOut, RiskLevel.ReadOnly);
    }

    public async Task<ResultEnvelope<object>> WatchLogForPatternAsync(string path, string pattern, int timeoutSeconds, CancellationToken cancellationToken = default)
    {
        var fullPath = Path.GetFullPath(path);
        var deadline = DateTimeOffset.UtcNow.AddSeconds(timeoutSeconds);
        long offset = File.Exists(fullPath) ? new FileInfo(fullPath).Length : 0;

        while (DateTimeOffset.UtcNow < deadline)
        {
            if (File.Exists(fullPath))
            {
                await using var stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                if (stream.Length < offset)
                {
                    offset = 0;
                }

                stream.Seek(offset, SeekOrigin.Begin);
                using var reader = new StreamReader(stream, leaveOpen: true);
                var text = await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
                offset = stream.Position;
                if (text.Contains(pattern, StringComparison.OrdinalIgnoreCase))
                {
                    return ResultEnvelope<object>.Ok(new { path = fullPath, pattern, matched = true }, "Log pattern observed.", RiskLevel.ReadOnly);
                }
            }

            await Task.Delay(500, cancellationToken).ConfigureAwait(false);
        }

        return ResultEnvelope<object>.Fail("watch_timeout", $"Timed out watching '{fullPath}' for pattern.", OperationStatus.TimedOut, RiskLevel.ReadOnly);
    }

    public async Task<ResultEnvelope<PendingContinuationRecord>> QueueManualContinuationAsync(string taskId, string prompt, CancellationToken cancellationToken = default)
    {
        var continuation = await ledger.QueueContinuationAsync(taskId, ContinuationConditionType.ManualUserContinue, "{}", prompt, cancellationToken: cancellationToken).ConfigureAwait(false);
        return ResultEnvelope<PendingContinuationRecord>.Ok(continuation, "Manual continuation queued.", RiskLevel.Low);
    }
}

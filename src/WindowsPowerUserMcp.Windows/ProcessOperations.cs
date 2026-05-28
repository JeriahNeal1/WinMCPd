using System.Diagnostics;
using WindowsPowerUserMcp.Core;

namespace WindowsPowerUserMcp.Windows;

public sealed class ProcessOperations
{
    public ResultEnvelope<object> ListProcesses(string? nameFilter = null, int limit = 500)
    {
        var query = Process.GetProcesses().AsEnumerable();
        if (!string.IsNullOrWhiteSpace(nameFilter))
        {
            query = query.Where(p => p.ProcessName.Contains(nameFilter, StringComparison.OrdinalIgnoreCase));
        }

        var processes = query
            .OrderBy(p => p.ProcessName)
            .Take(Math.Max(1, limit))
            .Select(p => SafeProcessInfo(p))
            .ToArray();
        return ResultEnvelope<object>.Ok(processes, "Process list.", RiskLevel.ReadOnly);
    }

    public ResultEnvelope<object> GetProcessDetail(int pid)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            return ResultEnvelope<object>.Ok(SafeProcessInfo(process, includeDetail: true), "Process detail.", RiskLevel.ReadOnly);
        }
        catch (Exception ex)
        {
            return ResultEnvelope<object>.Fail("process_not_found", ex.Message, OperationStatus.Failed, RiskLevel.ReadOnly);
        }
    }

    public ResultEnvelope<object> StartProcess(string fileName, string arguments = "", string? workingDirectory = null)
    {
        var info = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            WorkingDirectory = string.IsNullOrWhiteSpace(workingDirectory) ? Environment.CurrentDirectory : workingDirectory,
            UseShellExecute = true
        };
        var process = Process.Start(info);
        return process is null
            ? ResultEnvelope<object>.Fail("process_start_failed", "Process.Start returned null.", OperationStatus.Failed, RiskLevel.Medium)
            : ResultEnvelope<object>.Ok(new { pid = process.Id, process.ProcessName }, "Process started.", RiskLevel.Medium);
    }

    public ResultEnvelope<object> StopProcess(int pid, bool killTree = false)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            process.Kill(entireProcessTree: killTree);
            return ResultEnvelope<object>.Ok(new { pid, kill_tree = killTree }, "Process stop requested.", RiskLevel.Destructive);
        }
        catch (Exception ex)
        {
            return ResultEnvelope<object>.Fail("process_stop_failed", ex.Message, OperationStatus.Failed, RiskLevel.Destructive);
        }
    }

    private static object SafeProcessInfo(Process process, bool includeDetail = false)
    {
        string? mainModule = null;
        DateTime? startTime = null;
        try { mainModule = process.MainModule?.FileName; } catch { }
        try { startTime = process.StartTime; } catch { }

        return new
        {
            pid = process.Id,
            name = process.ProcessName,
            main_window_title = process.MainWindowTitle,
            main_window_handle = process.MainWindowHandle,
            start_time = startTime,
            main_module = includeDetail ? mainModule : null,
            session_id = includeDetail ? process.SessionId : (int?)null,
            responding = includeDetail ? process.Responding : (bool?)null
        };
    }
}

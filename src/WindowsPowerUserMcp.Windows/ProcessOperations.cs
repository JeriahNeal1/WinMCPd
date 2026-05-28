using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
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

    public ResultEnvelope<object> GetProcessModules(int pid)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            var modules = process.Modules.Cast<ProcessModule>().Select(m => new
            {
                name = m.ModuleName,
                file_name = m.FileName,
                base_address = m.BaseAddress.ToInt64(),
                module_memory_size = m.ModuleMemorySize
            }).ToArray();
            return ResultEnvelope<object>.Ok(new { pid, modules }, "Process modules.", RiskLevel.ReadOnly);
        }
        catch (Exception ex)
        {
            return ResultEnvelope<object>.Fail("process_modules_failed", ex.Message, OperationStatus.Failed, RiskLevel.ReadOnly);
        }
    }

    public ResultEnvelope<object> GetProcessOpenWindows(int pid)
    {
        var windows = new List<object>();
        EnumWindows((hwnd, _) =>
        {
            GetWindowThreadProcessId(hwnd, out var windowPid);
            if (windowPid == pid && IsWindowVisible(hwnd))
            {
                var title = GetWindowTitle(hwnd);
                if (!string.IsNullOrWhiteSpace(title))
                {
                    windows.Add(new { hwnd = hwnd.ToInt64(), title });
                }
            }

            return true;
        }, IntPtr.Zero);

        return ResultEnvelope<object>.Ok(new { pid, windows }, "Process windows.", RiskLevel.ReadOnly);
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

    private static string GetWindowTitle(IntPtr hwnd)
    {
        var length = GetWindowTextLength(hwnd);
        if (length == 0)
        {
            return string.Empty;
        }

        var builder = new StringBuilder(length + 1);
        GetWindowText(hwnd, builder, builder.Capacity);
        return builder.ToString();
    }

    private delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll")]
    private static extern int GetWindowTextLength(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out int lpdwProcessId);
}

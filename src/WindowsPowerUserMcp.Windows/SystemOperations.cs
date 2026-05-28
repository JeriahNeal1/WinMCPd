using System.Diagnostics;
using System.Management;
using System.Runtime.InteropServices;
using System.Security.Principal;
using Microsoft.Win32;
using WindowsPowerUserMcp.Core;
using WindowsPowerUserMcp.Security;

namespace WindowsPowerUserMcp.Windows;

public sealed class SystemOperations(SecretRedactor redactor)
{
    public ResultEnvelope<object> GetSystemSummary()
    {
        var data = new
        {
            machine_name = Environment.MachineName,
            user = Environment.UserName,
            os = RuntimeInformation.OSDescription,
            architecture = RuntimeInformation.OSArchitecture.ToString(),
            process_architecture = RuntimeInformation.ProcessArchitecture.ToString(),
            framework = RuntimeInformation.FrameworkDescription,
            is_64_bit_os = Environment.Is64BitOperatingSystem,
            is_elevated = IsElevated(),
            processors = Environment.ProcessorCount,
            drives = DriveInfo.GetDrives().Select(d => new
            {
                d.Name,
                d.DriveType,
                ready = d.IsReady,
                total_bytes = d.IsReady ? d.TotalSize : (long?)null,
                free_bytes = d.IsReady ? d.AvailableFreeSpace : (long?)null
            }).ToArray()
        };

        return ResultEnvelope<object>.Ok(data, "System summary.", RiskLevel.ReadOnly);
    }

    public ResultEnvelope<object> GetOsInfo() =>
        ResultEnvelope<object>.Ok(new
        {
            description = RuntimeInformation.OSDescription,
            version = Environment.OSVersion.VersionString,
            platform = Environment.OSVersion.Platform.ToString(),
            build = Environment.OSVersion.Version.Build
        }, "OS info.", RiskLevel.ReadOnly);

    public ResultEnvelope<object> GetHardwareSummary()
    {
        var processors = QueryWmi("Win32_Processor", ["Name", "NumberOfCores", "NumberOfLogicalProcessors", "MaxClockSpeed"]);
        var memory = QueryWmi("Win32_PhysicalMemory", ["Manufacturer", "Capacity", "Speed"]);
        var video = QueryWmi("Win32_VideoController", ["Name", "AdapterRAM", "DriverVersion"]);
        return ResultEnvelope<object>.Ok(new { processors, memory, video }, "Hardware summary.", RiskLevel.ReadOnly);
    }

    public ResultEnvelope<object> GetEnvironmentSummary()
    {
        var env = Environment.GetEnvironmentVariables()
            .Cast<System.Collections.DictionaryEntry>()
            .ToDictionary(e => e.Key.ToString() ?? string.Empty, e => e.Value?.ToString(), StringComparer.OrdinalIgnoreCase);
        return ResultEnvelope<object>.Ok(redactor.RedactEnvironment(env), "Environment summary with secrets redacted.", RiskLevel.ReadOnly);
    }

    public ResultEnvelope<object> GetUserContext() =>
        ResultEnvelope<object>.Ok(new
        {
            user_name = Environment.UserName,
            domain = Environment.UserDomainName,
            is_elevated = IsElevated(),
            interactive = Environment.UserInteractive,
            session_id = Process.GetCurrentProcess().SessionId
        }, "User context.", RiskLevel.ReadOnly);

    public ResultEnvelope<object> GetAdminStatus() =>
        ResultEnvelope<object>.Ok(new { is_elevated = IsElevated() }, "Admin/elevation status.", RiskLevel.ReadOnly);

    public ResultEnvelope<object> GetSessionInfo() =>
        ResultEnvelope<object>.Ok(new
        {
            current_process_id = Environment.ProcessId,
            session_id = Process.GetCurrentProcess().SessionId,
            user_interactive = Environment.UserInteractive,
            desktop_agent_required_for_ui = true
        }, "Session info.", RiskLevel.ReadOnly);

    public ResultEnvelope<object> GetDrives() =>
        ResultEnvelope<object>.Ok(DriveInfo.GetDrives().Select(d => new
        {
            name = d.Name,
            type = d.DriveType.ToString(),
            format = d.IsReady ? d.DriveFormat : null,
            label = d.IsReady ? d.VolumeLabel : null,
            total_bytes = d.IsReady ? d.TotalSize : (long?)null,
            free_bytes = d.IsReady ? d.AvailableFreeSpace : (long?)null,
            ready = d.IsReady
        }).ToArray(), "Drive list.", RiskLevel.ReadOnly);

    public ResultEnvelope<PathInfo> GetPathInfo(string path)
    {
        var expanded = Environment.ExpandEnvironmentVariables(path);
        if (File.Exists(expanded))
        {
            var info = new FileInfo(expanded);
            return ResultEnvelope<PathInfo>.Ok(new PathInfo(info.FullName, true, true, false, info.Length, info.CreationTimeUtc, info.LastWriteTimeUtc, info.Attributes.ToString()), "Path info.", RiskLevel.ReadOnly);
        }

        if (Directory.Exists(expanded))
        {
            var info = new DirectoryInfo(expanded);
            return ResultEnvelope<PathInfo>.Ok(new PathInfo(info.FullName, true, false, true, null, info.CreationTimeUtc, info.LastWriteTimeUtc, info.Attributes.ToString()), "Path info.", RiskLevel.ReadOnly);
        }

        return ResultEnvelope<PathInfo>.Ok(new PathInfo(Path.GetFullPath(expanded), false, false, false, null, null, null, null), "Path does not exist.", RiskLevel.ReadOnly);
    }

    public ResultEnvelope<object> GetInstalledApps()
    {
        var apps = new List<object>();
        foreach (var hive in new[] { Registry.CurrentUser, Registry.LocalMachine })
        {
            foreach (var path in new[] { @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall", @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall" })
            {
                using var key = hive.OpenSubKey(path);
                if (key is null)
                {
                    continue;
                }

                foreach (var subName in key.GetSubKeyNames())
                {
                    using var sub = key.OpenSubKey(subName);
                    var name = sub?.GetValue("DisplayName")?.ToString();
                    if (string.IsNullOrWhiteSpace(name))
                    {
                        continue;
                    }

                    apps.Add(new
                    {
                        name,
                        version = sub?.GetValue("DisplayVersion")?.ToString(),
                        publisher = sub?.GetValue("Publisher")?.ToString(),
                        install_location = sub?.GetValue("InstallLocation")?.ToString(),
                        hive = hive.Name
                    });
                }
            }
        }

        return ResultEnvelope<object>.Ok(apps.OrderBy(a => a.ToString()).ToArray(), "Installed apps.", RiskLevel.ReadOnly);
    }

    public ResultEnvelope<object> GetStartupItems()
    {
        var items = new List<object>();
        AddRunKey(items, Registry.CurrentUser, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run");
        AddRunKey(items, Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run");
        AddStartupFolder(items, Environment.GetFolderPath(Environment.SpecialFolder.Startup));
        AddStartupFolder(items, Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup));
        return ResultEnvelope<object>.Ok(items, "Startup items.", RiskLevel.ReadOnly);
    }

    public static bool IsElevated()
    {
        using var identity = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);
        return principal.IsInRole(WindowsBuiltInRole.Administrator);
    }

    private static IReadOnlyList<IReadOnlyDictionary<string, object?>> QueryWmi(string className, string[] fields)
    {
        var results = new List<IReadOnlyDictionary<string, object?>>();
        try
        {
            using var searcher = new ManagementObjectSearcher($"SELECT {string.Join(",", fields)} FROM {className}");
            foreach (ManagementObject obj in searcher.Get().Cast<ManagementObject>())
            {
                results.Add(fields.ToDictionary(f => f, f => obj.Properties[f]?.Value, StringComparer.OrdinalIgnoreCase));
            }
        }
        catch
        {
            // WMI can be unavailable or blocked by policy; return partial system info.
        }

        return results;
    }

    private static void AddRunKey(List<object> items, RegistryKey hive, string path)
    {
        using var key = hive.OpenSubKey(path);
        if (key is null)
        {
            return;
        }

        foreach (var valueName in key.GetValueNames())
        {
            items.Add(new { source = $"{hive.Name}\\{path}", name = valueName, command = key.GetValue(valueName)?.ToString() });
        }
    }

    private static void AddStartupFolder(List<object> items, string folder)
    {
        if (!Directory.Exists(folder))
        {
            return;
        }

        foreach (var file in Directory.EnumerateFiles(folder))
        {
            items.Add(new { source = folder, name = Path.GetFileName(file), path = file });
        }
    }
}

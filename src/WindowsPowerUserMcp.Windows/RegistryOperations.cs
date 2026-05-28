using Microsoft.Win32;
using WindowsPowerUserMcp.Core;
using WindowsPowerUserMcp.Orchestration;

namespace WindowsPowerUserMcp.Windows;

public sealed class RegistryOperations(StorageLayout layout, CommandRunner commandRunner)
{
    public ResultEnvelope<object> Read(string keyPath, string? valueName = null)
    {
        using var key = OpenKey(keyPath, writable: false);
        if (key is null)
        {
            return ResultEnvelope<object>.Fail("registry_key_not_found", $"Registry key '{keyPath}' was not found.", OperationStatus.Failed, RiskLevel.ReadOnly);
        }

        if (valueName is null)
        {
            return ResultEnvelope<object>.Ok(new { key_path = keyPath, value_names = key.GetValueNames(), sub_keys = key.GetSubKeyNames() }, "Registry key read.", RiskLevel.ReadOnly);
        }

        var value = key.GetValue(valueName);
        return ResultEnvelope<object>.Ok(new { key_path = keyPath, value_name = valueName, value, kind = key.GetValueKind(valueName).ToString() }, "Registry value read.", RiskLevel.ReadOnly);
    }

    public ResultEnvelope<object> List(string keyPath)
    {
        using var key = OpenKey(keyPath, writable: false);
        return key is null
            ? ResultEnvelope<object>.Fail("registry_key_not_found", $"Registry key '{keyPath}' was not found.", OperationStatus.Failed, RiskLevel.ReadOnly)
            : ResultEnvelope<object>.Ok(new { key_path = keyPath, sub_keys = key.GetSubKeyNames(), value_names = key.GetValueNames() }, "Registry key listed.", RiskLevel.ReadOnly);
    }

    public async Task<ResultEnvelope<CommandExecutionResult>> ExportAsync(string keyPath, string? destinationRegFile = null, CancellationToken cancellationToken = default)
    {
        var destination = destinationRegFile ?? Path.Combine(layout.Patches, $"registry-{SanitizeFileName(keyPath)}-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}.reg");
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        return await commandRunner.RunAsync(new ProcessStartRequest("reg.exe", $"export \"{keyPath}\" \"{destination}\" /y"), cancellationToken).ConfigureAwait(false);
    }

    public async Task<ResultEnvelope<object>> WriteAsync(string keyPath, string valueName, string value, string valueKind = "String", CancellationToken cancellationToken = default)
    {
        await ExportAsync(keyPath, cancellationToken: cancellationToken).ConfigureAwait(false);
        using var key = OpenOrCreateKey(keyPath);
        key.SetValue(valueName, ConvertValue(value, valueKind), ParseKind(valueKind));
        return ResultEnvelope<object>.Ok(new { key_path = keyPath, value_name = valueName, value_kind = valueKind }, "Registry value written after backup export.", RiskLevel.High);
    }

    public async Task<ResultEnvelope<object>> DeleteAsync(string keyPath, string? valueName = null, bool recursive = false, CancellationToken cancellationToken = default)
    {
        await ExportAsync(keyPath, cancellationToken: cancellationToken).ConfigureAwait(false);
        using var key = OpenKey(keyPath, writable: true);
        if (key is null)
        {
            return ResultEnvelope<object>.Fail("registry_key_not_found", $"Registry key '{keyPath}' was not found.", OperationStatus.Failed, RiskLevel.Destructive);
        }

        if (!string.IsNullOrWhiteSpace(valueName))
        {
            key.DeleteValue(valueName, throwOnMissingValue: false);
            return ResultEnvelope<object>.Ok(new { key_path = keyPath, value_name = valueName }, "Registry value deleted after backup export.", RiskLevel.Destructive);
        }

        var (root, subPath) = SplitRoot(keyPath);
        using var rootKey = OpenRoot(root, writable: true);
        if (recursive)
        {
            rootKey.DeleteSubKeyTree(subPath, throwOnMissingSubKey: false);
        }
        else
        {
            rootKey.DeleteSubKey(subPath, throwOnMissingSubKey: false);
        }

        return ResultEnvelope<object>.Ok(new { key_path = keyPath, recursive }, "Registry key deleted after backup export.", RiskLevel.Destructive);
    }

    public Task<ResultEnvelope<CommandExecutionResult>> ImportFileAsync(string regFile, CancellationToken cancellationToken = default) =>
        commandRunner.RunAsync(new ProcessStartRequest("reg.exe", $"import \"{regFile}\""), cancellationToken);

    private static RegistryKey? OpenKey(string keyPath, bool writable)
    {
        var (root, subPath) = SplitRoot(keyPath);
        using var rootKey = OpenRoot(root, writable);
        return rootKey.OpenSubKey(subPath, writable);
    }

    private static RegistryKey OpenOrCreateKey(string keyPath)
    {
        var (root, subPath) = SplitRoot(keyPath);
        using var rootKey = OpenRoot(root, writable: true);
        return rootKey.CreateSubKey(subPath, writable: true) ?? throw new InvalidOperationException($"Unable to create/open registry key '{keyPath}'.");
    }

    private static (string Root, string SubPath) SplitRoot(string keyPath)
    {
        var normalized = keyPath.Replace('/', '\\');
        var parts = normalized.Split('\\', 2);
        if (parts.Length != 2)
        {
            throw new ArgumentException("Registry path must include a root hive and subkey.", nameof(keyPath));
        }

        return (parts[0].ToUpperInvariant(), parts[1]);
    }

    private static RegistryKey OpenRoot(string root, bool writable) =>
        root switch
        {
            "HKCU" or "HKEY_CURRENT_USER" => Registry.CurrentUser,
            "HKLM" or "HKEY_LOCAL_MACHINE" => Registry.LocalMachine,
            "HKCR" or "HKEY_CLASSES_ROOT" => Registry.ClassesRoot,
            "HKU" or "HKEY_USERS" => Registry.Users,
            "HKCC" or "HKEY_CURRENT_CONFIG" => Registry.CurrentConfig,
            _ => throw new ArgumentOutOfRangeException(nameof(root), root, "Unsupported registry hive.")
        };

    private static RegistryValueKind ParseKind(string kind) =>
        Enum.TryParse<RegistryValueKind>(kind, ignoreCase: true, out var parsed) ? parsed : RegistryValueKind.String;

    private static object ConvertValue(string value, string kind) =>
        ParseKind(kind) switch
        {
            RegistryValueKind.DWord => int.Parse(value),
            RegistryValueKind.QWord => long.Parse(value),
            RegistryValueKind.MultiString => value.Split('|'),
            _ => value
        };

    private static string SanitizeFileName(string value)
    {
        foreach (var c in Path.GetInvalidFileNameChars())
        {
            value = value.Replace(c, '_');
        }

        return value.Replace('\\', '_').Replace(':', '_');
    }
}

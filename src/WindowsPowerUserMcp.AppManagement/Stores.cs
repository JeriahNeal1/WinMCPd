using System.Text.Json;
using WindowsPowerUserMcp.Core;

namespace WindowsPowerUserMcp.AppManagement;

public sealed class AppSettingsStore(string? path = null)
{
    public string Path { get; } = path ?? System.IO.Path.Combine(PlatformPaths.LocalDataRoot, "app-settings.json");

    public async Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(Path))
        {
            return new AppSettings();
        }

        await using var stream = File.Open(Path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        return await JsonSerializer.DeserializeAsync<AppSettings>(stream, JsonDefaults.Options, cancellationToken).ConfigureAwait(false)
               ?? new AppSettings();
    }

    public async Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
        await using var stream = File.Open(Path, FileMode.Create, FileAccess.Write, FileShare.Read);
        await JsonSerializer.SerializeAsync(stream, settings, JsonDefaults.Options, cancellationToken).ConfigureAwait(false);
    }
}

public sealed class InstallStateStore(string? path = null)
{
    public string Path { get; } = path ?? System.IO.Path.Combine(PlatformPaths.LocalDataRoot, "install-state.json");

    public async Task<InstallState?> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(Path))
        {
            return null;
        }

        await using var stream = File.Open(Path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        return await JsonSerializer.DeserializeAsync<InstallState>(stream, JsonDefaults.Options, cancellationToken).ConfigureAwait(false);
    }

    public async Task SaveAsync(InstallState state, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
        await using var stream = File.Open(Path, FileMode.Create, FileAccess.Write, FileShare.Read);
        await JsonSerializer.SerializeAsync(stream, state, JsonDefaults.Options, cancellationToken).ConfigureAwait(false);
    }

    public static string PerMachinePath =>
        System.IO.Path.Combine(PlatformPaths.ProgramDataRoot, "install-state.json");

    public static string InstallRootPath(string installRoot) =>
        System.IO.Path.Combine(Environment.ExpandEnvironmentVariables(installRoot), "install-state.json");
}

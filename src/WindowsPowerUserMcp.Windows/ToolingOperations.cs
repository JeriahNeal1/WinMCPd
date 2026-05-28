using WindowsPowerUserMcp.Core;
using WindowsPowerUserMcp.Orchestration;

namespace WindowsPowerUserMcp.Windows;

public sealed class ToolingOperations(CommandRunner commandRunner)
{
    public async Task<ResultEnvelope<object>> DetectToolAsync(string executable, string versionArguments = "--version", CancellationToken cancellationToken = default)
    {
        var where = await commandRunner.RunAsync(new ProcessStartRequest("where.exe", executable, TimeoutSeconds: 10), cancellationToken).ConfigureAwait(false);
        if (!where.Success)
        {
            return ResultEnvelope<object>.Ok(new { executable, installed = false }, $"{executable} not detected.", RiskLevel.ReadOnly);
        }

        var version = await commandRunner.RunAsync(new ProcessStartRequest(executable, versionArguments, TimeoutSeconds: 10), cancellationToken).ConfigureAwait(false);
        return ResultEnvelope<object>.Ok(new
        {
            executable,
            installed = true,
            paths = where.Data?.StdoutTail.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
            version = version.Data?.StdoutTail.Trim(),
            version_error = version.Data?.StderrTail.Trim()
        }, $"{executable} detected.", RiskLevel.ReadOnly);
    }

    public Task<ResultEnvelope<CommandExecutionResult>> WingetAsync(string arguments, CancellationToken cancellationToken = default) =>
        commandRunner.RunAsync(new ProcessStartRequest("winget", arguments), cancellationToken);

    public Task<ResultEnvelope<CommandExecutionResult>> WslAsync(string arguments, CancellationToken cancellationToken = default) =>
        commandRunner.RunAsync(new ProcessStartRequest("wsl.exe", arguments), cancellationToken);

    public Task<ResultEnvelope<CommandExecutionResult>> DockerAsync(string arguments, CancellationToken cancellationToken = default) =>
        commandRunner.RunAsync(new ProcessStartRequest("docker", arguments), cancellationToken);

    public Task<ResultEnvelope<CommandExecutionResult>> AdbAsync(string arguments, CancellationToken cancellationToken = default) =>
        commandRunner.RunAsync(new ProcessStartRequest("adb", arguments), cancellationToken);
}

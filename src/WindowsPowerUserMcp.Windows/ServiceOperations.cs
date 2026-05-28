using System.ServiceProcess;
using WindowsPowerUserMcp.Core;
using WindowsPowerUserMcp.Orchestration;

namespace WindowsPowerUserMcp.Windows;

public sealed class ServiceOperations(CommandRunner commandRunner)
{
    public ResultEnvelope<object> ListServices(string? nameFilter = null)
    {
        var services = ServiceController.GetServices()
            .Where(s => string.IsNullOrWhiteSpace(nameFilter) || s.ServiceName.Contains(nameFilter, StringComparison.OrdinalIgnoreCase) || s.DisplayName.Contains(nameFilter, StringComparison.OrdinalIgnoreCase))
            .OrderBy(s => s.ServiceName)
            .Select(s => new { s.ServiceName, s.DisplayName, status = s.Status.ToString(), start_type = s.StartType.ToString(), s.CanStop })
            .ToArray();
        return ResultEnvelope<object>.Ok(services, "Service list.", RiskLevel.ReadOnly);
    }

    public ResultEnvelope<object> GetServiceDetail(string serviceName)
    {
        try
        {
            using var service = new ServiceController(serviceName);
            return ResultEnvelope<object>.Ok(new
            {
                service.ServiceName,
                service.DisplayName,
                status = service.Status.ToString(),
                start_type = service.StartType.ToString(),
                service.CanStop,
                service.CanPauseAndContinue,
                dependent_services = service.DependentServices.Select(s => s.ServiceName).ToArray(),
                services_depended_on = service.ServicesDependedOn.Select(s => s.ServiceName).ToArray()
            }, "Service detail.", RiskLevel.ReadOnly);
        }
        catch (Exception ex)
        {
            return ResultEnvelope<object>.Fail("service_not_found", ex.Message, OperationStatus.Failed, RiskLevel.ReadOnly);
        }
    }

    public async Task<ResultEnvelope<object>> StartServiceAsync(string serviceName, int timeoutSeconds = 60, CancellationToken cancellationToken = default)
    {
        using var service = new ServiceController(serviceName);
        service.Start();
        await WaitForStatusAsync(service, ServiceControllerStatus.Running, TimeSpan.FromSeconds(timeoutSeconds), cancellationToken).ConfigureAwait(false);
        return ResultEnvelope<object>.Ok(new { service_name = serviceName, status = service.Status.ToString() }, "Service started.", RiskLevel.High);
    }

    public async Task<ResultEnvelope<object>> StopServiceAsync(string serviceName, int timeoutSeconds = 60, CancellationToken cancellationToken = default)
    {
        using var service = new ServiceController(serviceName);
        service.Stop();
        await WaitForStatusAsync(service, ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(timeoutSeconds), cancellationToken).ConfigureAwait(false);
        return ResultEnvelope<object>.Ok(new { service_name = serviceName, status = service.Status.ToString() }, "Service stopped.", RiskLevel.High);
    }

    public async Task<ResultEnvelope<object>> RestartServiceAsync(string serviceName, int timeoutSeconds = 120, CancellationToken cancellationToken = default)
    {
        await StopServiceAsync(serviceName, timeoutSeconds / 2, cancellationToken).ConfigureAwait(false);
        return await StartServiceAsync(serviceName, timeoutSeconds / 2, cancellationToken).ConfigureAwait(false);
    }

    public async Task<ResultEnvelope<CommandExecutionResult>> SetStartupTypeAsync(string serviceName, string startupType, CancellationToken cancellationToken = default)
    {
        var scValue = startupType.ToLowerInvariant() switch
        {
            "auto" or "automatic" => "auto",
            "delayed" or "delayed-auto" => "delayed-auto",
            "manual" or "demand" => "demand",
            "disabled" => "disabled",
            _ => throw new ArgumentOutOfRangeException(nameof(startupType), startupType, "Use automatic, delayed, manual, or disabled.")
        };

        return await commandRunner.RunAsync(new ProcessStartRequest("sc.exe", $"config \"{serviceName}\" start= {scValue}"), cancellationToken).ConfigureAwait(false);
    }

    public Task<ResultEnvelope<CommandExecutionResult>> GetRecoveryOptionsAsync(string serviceName, CancellationToken cancellationToken = default) =>
        commandRunner.RunAsync(new ProcessStartRequest("sc.exe", $"qfailure \"{serviceName}\""), cancellationToken);

    public Task<ResultEnvelope<CommandExecutionResult>> SetRecoveryOptionsAsync(string serviceName, string actions = "restart/60000/restart/60000/none/60000", int resetSeconds = 86400, CancellationToken cancellationToken = default) =>
        commandRunner.RunAsync(new ProcessStartRequest("sc.exe", $"failure \"{serviceName}\" actions= {actions} reset= {resetSeconds}"), cancellationToken);

    private static async Task WaitForStatusAsync(ServiceController service, ServiceControllerStatus desiredStatus, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            service.Refresh();
            if (service.Status == desiredStatus)
            {
                return;
            }

            await Task.Delay(500, cancellationToken).ConfigureAwait(false);
        }

        throw new System.TimeoutException($"Service '{service.ServiceName}' did not reach {desiredStatus} within {timeout}.");
    }
}

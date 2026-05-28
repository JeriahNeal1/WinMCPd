using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using WindowsPowerUserMcp.Core;
using WindowsPowerUserMcp.Orchestration;

namespace WindowsPowerUserMcp.BrokerService;

public sealed class BrokerWorker(
    WindowsPowerUserMcpOptions options,
    TaskLedger ledger,
    BrokerToolRegistry registry,
    ILogger<BrokerWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await ledger.InitializeAsync(stoppingToken).ConfigureAwait(false);
        registry.RegisterTools();
        var server = new NamedPipeBrokerServer(options.IpcPipeName, registry, logger);
        logger.LogInformation("WindowsPowerUserMcp broker listening on pipe {PipeName}", options.IpcPipeName);
        await server.RunAsync(stoppingToken).ConfigureAwait(false);
    }
}

public sealed class NamedPipeBrokerServer(string pipeName, BrokerToolRegistry registry, ILogger logger)
{
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var pipe = new NamedPipeServerStream(
                pipeName,
                PipeDirection.InOut,
                NamedPipeServerStream.MaxAllowedServerInstances,
                PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);

            try
            {
                await pipe.WaitForConnectionAsync(cancellationToken).ConfigureAwait(false);
                _ = Task.Run(() => HandleClientAsync(pipe, cancellationToken), CancellationToken.None);
            }
            catch (OperationCanceledException)
            {
                await pipe.DisposeAsync().ConfigureAwait(false);
                break;
            }
            catch (Exception ex)
            {
                await pipe.DisposeAsync().ConfigureAwait(false);
                logger.LogError(ex, "Broker pipe accept failed.");
            }
        }
    }

    private async Task HandleClientAsync(NamedPipeServerStream pipe, CancellationToken cancellationToken)
    {
        await using (pipe.ConfigureAwait(false))
        {
            try
            {
                using var reader = new StreamReader(pipe, Encoding.UTF8, leaveOpen: true);
                await using var writer = new StreamWriter(pipe, new UTF8Encoding(false), leaveOpen: true) { AutoFlush = true };
                var line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(line))
                {
                    return;
                }

                var request = JsonSerializer.Deserialize<BrokerRequest>(line, JsonDefaults.Options);
                if (request is null)
                {
                    await WriteAsync(writer, new BrokerResponse("unknown", false, null, "bad_request", "Could not deserialize broker request.", DateTimeOffset.UtcNow), cancellationToken).ConfigureAwait(false);
                    return;
                }

                var result = await registry.InvokeAsync(request.ToolName, request.Arguments, cancellationToken).ConfigureAwait(false);
                var response = new BrokerResponse(request.RequestId, true, JsonDefaults.ToElement(result), null, null, DateTimeOffset.UtcNow);
                await WriteAsync(writer, response, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Broker request failed.");
                var response = new BrokerResponse("unknown", false, null, "broker_exception", ex.Message, DateTimeOffset.UtcNow);
                try
                {
                    await using var writer = new StreamWriter(pipe, new UTF8Encoding(false), leaveOpen: true) { AutoFlush = true };
                    await WriteAsync(writer, response, CancellationToken.None).ConfigureAwait(false);
                }
                catch
                {
                    // Client may already be gone.
                }
            }
        }
    }

    private static Task WriteAsync(StreamWriter writer, BrokerResponse response, CancellationToken cancellationToken) =>
        writer.WriteLineAsync(JsonSerializer.Serialize(response, JsonDefaults.Options).AsMemory(), cancellationToken);
}

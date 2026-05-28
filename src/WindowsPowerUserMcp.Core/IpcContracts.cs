using System.IO.Pipes;
using System.Text;
using System.Text.Json;

namespace WindowsPowerUserMcp.Core;

public sealed record BrokerRequest(
    string RequestId,
    string ToolName,
    JsonElement Arguments,
    DateTimeOffset Timestamp);

public sealed record BrokerResponse(
    string RequestId,
    bool Success,
    JsonElement? Result,
    string? ErrorCode,
    string? ErrorMessage,
    DateTimeOffset Timestamp);

public sealed class BrokerPipeClient(string pipeName, TimeSpan? connectTimeout = null)
{
    private readonly TimeSpan _connectTimeout = connectTimeout ?? TimeSpan.FromSeconds(3);

    public string PipeName { get; } = pipeName;

    public async Task<BrokerResponse> InvokeRawAsync(string toolName, object? arguments = null, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var pipe = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(_connectTimeout);
            await pipe.ConnectAsync(timeoutCts.Token).ConfigureAwait(false);

            var request = new BrokerRequest(
                Guid.NewGuid().ToString("n"),
                toolName,
                JsonDefaults.ToElement(arguments ?? new { }),
                DateTimeOffset.UtcNow);

            await using var writer = new StreamWriter(pipe, new UTF8Encoding(false), leaveOpen: true)
            {
                AutoFlush = true
            };
            using var reader = new StreamReader(pipe, Encoding.UTF8, leaveOpen: true);

            await writer.WriteLineAsync(JsonSerializer.Serialize(request, JsonDefaults.Options).AsMemory(), cancellationToken).ConfigureAwait(false);
            var line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(line))
            {
                return new BrokerResponse(request.RequestId, false, null, "broker_empty_response", "Broker returned an empty response.", DateTimeOffset.UtcNow);
            }

            return JsonSerializer.Deserialize<BrokerResponse>(line, JsonDefaults.Options)
                   ?? new BrokerResponse(request.RequestId, false, null, "broker_deserialize_failed", "Broker response could not be deserialized.", DateTimeOffset.UtcNow);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new BrokerResponse(Guid.NewGuid().ToString("n"), false, null, "broker_connect_timeout", $"Timed out connecting to named pipe '{PipeName}'.", DateTimeOffset.UtcNow);
        }
        catch (Exception ex)
        {
            return new BrokerResponse(Guid.NewGuid().ToString("n"), false, null, "broker_unavailable", $"Broker pipe '{PipeName}' is unavailable: {ex.Message}", DateTimeOffset.UtcNow);
        }
    }

    public async Task<JsonElement> InvokeForMcpAsync(string toolName, object? arguments = null, CancellationToken cancellationToken = default)
    {
        var response = await InvokeRawAsync(toolName, arguments, cancellationToken).ConfigureAwait(false);
        if (response.Success && response.Result is { } result)
        {
            return result;
        }

        var envelope = ResultEnvelope<object>.Fail(
            response.ErrorCode ?? "broker_error",
            response.ErrorMessage ?? "Broker call failed.",
            response.ErrorCode == "broker_unavailable" ? OperationStatus.BrokerUnavailable : OperationStatus.Failed,
            RiskLevel.Low);

        return JsonDefaults.ToElement(envelope);
    }
}

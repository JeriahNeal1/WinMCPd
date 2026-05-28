using System.Text.Json;
using WindowsPowerUserMcp.Core;

namespace WindowsPowerUserMcp.Security;

public interface IAuditLogger
{
    Task<string> WriteAsync(
        string component,
        string operation,
        RiskLevel riskLevel,
        string result,
        string summary,
        object? details = null,
        string severity = "info",
        CancellationToken cancellationToken = default);
}

public sealed class JsonlAuditLogger(StorageLayout layout, SecretRedactor redactor) : IAuditLogger
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task<string> WriteAsync(
        string component,
        string operation,
        RiskLevel riskLevel,
        string result,
        string summary,
        object? details = null,
        string severity = "info",
        CancellationToken cancellationToken = default)
    {
        var id = Guid.NewGuid().ToString("n");
        var detailsJson = details is null ? null : JsonSerializer.Serialize(details, JsonDefaults.Options);
        var redactedDetails = detailsJson is null ? null : redactor.Redact(detailsJson).Text;
        var evt = new AuditEventRecord(
            id,
            DateTimeOffset.UtcNow,
            severity,
            component,
            operation,
            riskLevel,
            Environment.UserName,
            result,
            redactor.Redact(summary).Text,
            redactedDetails);

        var path = Path.Combine(layout.Logs, $"audit-{DateTimeOffset.UtcNow:yyyyMMdd}.jsonl");
        var line = JsonSerializer.Serialize(evt, JsonDefaults.Options);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await File.AppendAllTextAsync(path, line + Environment.NewLine, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }

        return id;
    }
}

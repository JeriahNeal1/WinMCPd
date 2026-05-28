using Microsoft.Extensions.AI;
using ModelContextProtocol.Server;
using System.Text.Json;
using WindowsPowerUserMcp.Core;

namespace WindowsPowerUserMcp.StdioBridge;

public static class DirectBrokerToolFactory
{
    public static IReadOnlyList<McpServerTool> CreateTools(BrokerToolProxy proxy)
    {
        var tools = ToolCatalog.All
            .Select(entry => McpServerTool.Create(new DirectBrokerFunction(entry, proxy), new McpServerToolCreateOptions
            {
                Name = entry.Name,
                Description = entry.Description,
                Destructive = entry.RiskLevel is RiskLevel.Destructive,
                OpenWorld = entry.RiskLevel is RiskLevel.Medium or RiskLevel.High or RiskLevel.Destructive or RiskLevel.SecuritySensitive,
                ReadOnly = entry.RiskLevel is RiskLevel.ReadOnly,
                SerializerOptions = JsonDefaults.Options
            }))
            .ToList();

        tools.Add(McpServerTool.Create(new DeprecatedBrokerCallFunction(proxy), new McpServerToolCreateOptions
        {
            Name = "broker_call",
            Description = "Deprecated escape hatch: call a broker tool by name with a JSON object. Prefer direct tools.",
            OpenWorld = true,
            SerializerOptions = JsonDefaults.Options
        }));

        return tools;
    }
}

internal sealed class DirectBrokerFunction(ToolCatalogEntry entry, BrokerToolProxy proxy) : AIFunction
{
    public override string Name => entry.Name;
    public override string Description => entry.Description;
    public override JsonElement JsonSchema => entry.InputSchema;

    protected override async ValueTask<object?> InvokeCoreAsync(AIFunctionArguments arguments, CancellationToken cancellationToken)
    {
        var payload = BuildPayload(entry, arguments);
        var errors = ToolCatalog.ValidateArguments(entry, payload);
        if (errors.Count > 0)
        {
            return JsonDefaults.ToElement(ResultEnvelope<object>.Fail(
                "invalid_arguments",
                string.Join(" ", errors),
                OperationStatus.Failed,
                entry.RiskLevel));
        }

        return await proxy.InvokeElementAsync(entry.Name, payload, cancellationToken).ConfigureAwait(false);
    }

    private static JsonElement BuildPayload(ToolCatalogEntry entry, AIFunctionArguments arguments)
    {
        var values = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var parameter in entry.Arguments.Where(p => p.DefaultValue is not null))
        {
            values[parameter.Name] = parameter.DefaultValue;
        }

        foreach (var (key, value) in arguments)
        {
            values[key] = value is JsonElement element ? element.Clone() : value;
        }

        return JsonDefaults.ToElement(values);
    }
}

internal sealed class DeprecatedBrokerCallFunction(BrokerToolProxy proxy) : AIFunction
{
    private static readonly JsonElement Schema = ToolCatalog.CreateInputSchema(
    [
        new ToolArgumentDescriptor("tool_name", ToolArgumentKind.String, Required: true, Description: "Broker tool name."),
        new ToolArgumentDescriptor("arguments_json", ToolArgumentKind.String, Required: false, Description: "JSON object arguments.", DefaultValue: "{}")
    ]);

    public override string Name => "broker_call";
    public override string Description => "Deprecated escape hatch. Prefer direct MCP tools generated from the broker catalog.";
    public override JsonElement JsonSchema => Schema;

    protected override async ValueTask<object?> InvokeCoreAsync(AIFunctionArguments arguments, CancellationToken cancellationToken)
    {
        var toolName = arguments.TryGetValue("tool_name", out var name) ? name?.ToString() : null;
        var json = arguments.TryGetValue("arguments_json", out var args) ? args?.ToString() : "{}";
        if (string.IsNullOrWhiteSpace(toolName))
        {
            return JsonDefaults.ToElement(ResultEnvelope<object>.Fail("invalid_arguments", "tool_name is required.", OperationStatus.Failed, RiskLevel.Low));
        }

        return await proxy.InvokeJsonAsync(toolName, json ?? "{}", cancellationToken).ConfigureAwait(false);
    }
}

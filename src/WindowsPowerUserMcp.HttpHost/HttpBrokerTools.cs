using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;
using WindowsPowerUserMcp.Core;

namespace WindowsPowerUserMcp.HttpHost;

public sealed class HttpBrokerToolProxy(BrokerPipeClient client)
{
    public async Task<JsonElement> InvokeJsonAsync(string toolName, string argumentsJson)
    {
        using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(argumentsJson) ? "{}" : argumentsJson);
        return await client.InvokeForMcpAsync(toolName, doc.RootElement.Clone()).ConfigureAwait(false);
    }
}

[McpServerToolType]
public sealed class HttpBrokerTools(HttpBrokerToolProxy proxy)
{
    [McpServerTool, Description("Call any WindowsPowerUserMcp broker tool by name with a JSON object argument payload.")]
    public Task<JsonElement> broker_call(string tool_name, string arguments_json = "{}") =>
        proxy.InvokeJsonAsync(tool_name, arguments_json);
}

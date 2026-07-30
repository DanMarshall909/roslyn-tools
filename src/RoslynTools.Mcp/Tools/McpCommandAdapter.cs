using System.Text.Json;
using System.Text.Json.Nodes;
using RoslynTools.CommandDescriptors;
using RoslynTools.Core.Output;

namespace RoslynTools.Mcp.Tools;

/// <summary>
///     Generic MCP adapter for descriptor-backed commands. Port-backed descriptors bind JSON
///     directly to the typed application request; unmigrated commands use the isolated legacy
///     CLI adapter until they receive an application port.
/// </summary>
internal sealed class McpCommandAdapter
{
    private static readonly JsonSerializerOptions RequestOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly CommandDescriptorRegistry _descriptors;
    private readonly CliCommandBridge _legacyBridge;

    public McpCommandAdapter(
        CommandDescriptorRegistry descriptors,
        CliCommandBridge legacyBridge)
    {
        _descriptors = descriptors;
        _legacyBridge = legacyBridge;
    }

    public async Task<ToolResult> InvokeAsync(
        string toolName,
        JsonElement arguments,
        CancellationToken cancellationToken = default)
    {
        if (!_descriptors.TryGetByToolName(toolName, out var descriptor)
            || !descriptor.McpVisible)
            return new ToolResult(true, $"Unknown tool: {toolName}");

        var safeArguments = ApplySafetyPolicy(descriptor, arguments);
        if (descriptor.Port is null)
            return await _legacyBridge.InvokeAsync(
                descriptor,
                safeArguments,
                cancellationToken);

        try
        {
            var request = JsonSerializer.Deserialize(
                safeArguments.GetRawText(),
                descriptor.Port.RequestType,
                RequestOptions);
            if (request is null)
                return new ToolResult(true, $"Invalid input for tool '{toolName}'.");

            var result = await descriptor.Port.InvokeAsync(request, cancellationToken);
            if (result.IsError)
                return new ToolResult(true, result.Error ?? "Command failed.");

            var json = JsonSerializer.Serialize(
                result.Value,
                result.Value?.GetType() ?? typeof(object),
                CreateResponseOptions());
            return new ToolResult(false, json);
        }
        catch (Exception ex) when (ex is ArgumentException
                                   or InvalidOperationException
                                   or JsonException
                                   or IOException)
        {
            return new ToolResult(true, ex.Message);
        }
    }

    private static JsonSerializerOptions CreateResponseOptions()
    {
        return new JsonSerializerOptions(JsonEncoders.Select(RtContext.RelaxJson))
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };
    }

    internal static JsonElement ApplySafetyPolicy(
        ApplicationCommandDescriptor descriptor,
        JsonElement arguments)
    {
        if (descriptor.Safety != CommandSafetyPolicy.DryRunByDefault
            || arguments.TryGetProperty("dry-run", out _)
            || arguments.TryGetProperty("dryRun", out _))
            return arguments.Clone();

        var node = JsonNode.Parse(arguments.GetRawText())?.AsObject()
                   ?? new JsonObject();
        node["dry-run"] = true;
        return JsonSerializer.SerializeToElement(node);
    }
}

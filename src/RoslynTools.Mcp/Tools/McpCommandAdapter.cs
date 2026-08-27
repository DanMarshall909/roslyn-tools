using System.Text.Json;
using System.Text.Json.Nodes;
using RoslynTools.CommandDescriptors;
using RoslynTools.Core;
using RoslynTools.Core.Output;
using RoslynTools.Daemon.Rpc;
using RoslynTools.Mcp.Daemon;
using RoslynTools.Services;

namespace RoslynTools.Mcp.Tools;

/// <summary>
///     Generic MCP adapter for descriptor-backed commands. Port-backed descriptors bind JSON
///     directly to the typed application request; unmigrated commands use the isolated legacy
///     CLI adapter until they receive an application port.
/// </summary>
internal sealed class McpCommandAdapter(
    CommandDescriptorRegistry descriptors,
    CliCommandBridge legacyBridge,
    DaemonRpcRecovery daemonRpc)
{
    private static readonly JsonSerializerOptions RequestOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly CommandDescriptorRegistry _descriptors = descriptors;
    private readonly CliCommandBridge _legacyBridge = legacyBridge;
    private readonly DaemonRpcRecovery _daemonRpc = daemonRpc;

    public async Task<ToolResult> InvokeAsync(
        string toolName,
        JsonElement arguments,
        CancellationToken cancellationToken = default)
    {
        if (!_descriptors.TryGetByToolName(toolName, out var descriptor)
            || !descriptor.McpVisible)
            return new ToolResult(true, $"Unknown tool: {toolName}");

        var safeArguments = ApplySafetyPolicy(descriptor, arguments);

        if (descriptor.Port is not null
            && DaemonOwnedCommands.Contains(toolName))
        {
            var remote = await _daemonRpc.TryExecuteAsync(toolName, safeArguments, cancellationToken);
            if (remote is not null)
                return ToToolResult(remote);

            // The portable stdio host must remain usable before a daemon has been started,
            // and while a stale daemon is being replaced. The same typed port is the narrow
            // in-process recovery path described by ADR-0005.
        }

        try
        {
            MsBuildBootstrap.Register();
        }
        catch (InvalidOperationException ex)
        {
            return new ToolResult(true, ex.Message);
        }

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

    private static ToolResult ToToolResult(DaemonRpcResponse response)
    {
        if (response.Error is not null)
            return new ToolResult(true, response.Error.Message);

        return new ToolResult(false, response.Result?.GetRawText() ?? "null");
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

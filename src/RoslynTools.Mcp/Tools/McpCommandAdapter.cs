using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using RoslynTools.CommandDescriptors;
using RoslynTools.Core;
using RoslynTools.Core.Output;
using RoslynTools.Daemon.Rpc;
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
    DaemonRpcRecoveryCoordinator daemonRpc)
{
    private static readonly JsonSerializerOptions RequestOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly CommandDescriptorRegistry _descriptors = descriptors;
    private readonly CliCommandBridge _legacyBridge = legacyBridge;
    private readonly DaemonRpcRecoveryCoordinator _daemonRpc = daemonRpc;

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
            && (descriptor.Definition?.ExecutionPlacement == CommandExecutionPlacement.DaemonPreferred
                || descriptor.Definition is null && DaemonOwnedCommands.Contains(toolName)))
        {
            var recovery = descriptor.Definition is not null
                ? await _daemonRpc.ExecuteAsync(
                    descriptor.Definition,
                    safeArguments,
                    cancellationToken)
                : await _daemonRpc.ExecuteLegacyAsync(
                    toolName,
                    safeArguments,
                    ResolveSafety(descriptor),
                    cancellationToken);
            if (recovery.Response is not null)
                return ToToolResult(recovery.Response);
            if (!recovery.ShouldFallback)
                return new ToolResult(
                    true,
                    "The daemon accepted this mutation but its result is indeterminate; inspect the target before retrying.");

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
                return new ToolResult(
                    true,
                    result.LegacyErrorPayload
                    ?? (result.Error is null
                        ? "Command failed."
                        : SerializeError(result.Error)));

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
            return new ToolResult(
                true,
                response.Error.LegacyErrorPayload
                ?? (response.Error.Kind is null
                    ? response.Error.Message
                    : SerializeError(new RtError(
                        response.Error.Kind.Value,
                        response.Error.Message,
                        response.Error.Context))));

        return new ToolResult(false, response.Result?.GetRawText() ?? "null");
    }

    private static JsonSerializerOptions CreateResponseOptions()
    {
        return new JsonSerializerOptions(JsonEncoders.Select(RtContext.RelaxJson))
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };
    }

    private static string SerializeError(RtError error)
    {
        var options = CreateResponseOptions();
        options.Converters.Add(new JsonStringEnumConverter());
        return JsonSerializer.Serialize(error, options);
    }

    private static CommandSafety ResolveSafety(ApplicationCommandDescriptor descriptor) =>
        descriptor.Definition?.Safety
        ?? DaemonOwnedCommands.All.FirstOrDefault(command =>
            string.Equals(command.Name, descriptor.ToolName, StringComparison.Ordinal))?.Safety
        ?? (descriptor.Safety == CommandSafetyPolicy.DryRunByDefault
            ? CommandSafety.ReviewByDefault
            : CommandSafety.ReadOnly);

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

using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using RoslynTools.CommandDescriptors;
using RoslynTools.Core;
using RoslynTools.Core.CommandDescriptors;
using RoslynTools.Core.Output;
using RoslynTools.Daemon.Rpc;
using RoslynTools.Services;
using RoslynTools.Services.SemanticDiff;

namespace RoslynTools.Mcp.Tools;

/// <summary>
///     Generic MCP adapter for descriptor-backed commands. Port-backed descriptors bind JSON
///     directly to the typed application request; unmigrated commands use the isolated legacy
///     CLI adapter until they receive an application port.
/// </summary>
internal sealed class McpCommandAdapter(
    CommandDescriptorRegistry descriptors,
    CliCommandBridge legacyBridge,
    DaemonRpcRecoveryCoordinator daemonRpc,
    McpHostOptions? hostOptions = null,
    IMcpMsBuildBinding? msBuildBinding = null)
{
    private static readonly JsonSerializerOptions RequestOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly CommandDescriptorRegistry _descriptors = descriptors;
    private readonly CliCommandBridge _legacyBridge = legacyBridge;
    private readonly DaemonRpcRecoveryCoordinator _daemonRpc = daemonRpc;
    private readonly McpHostOptions _hostOptions = hostOptions ?? new McpHostOptions();
    private readonly IMcpMsBuildBinding _msBuildBinding = msBuildBinding ?? new McpMsBuildBinding();

    public async Task<ToolResult> InvokeAsync(
        string toolName,
        JsonElement arguments,
        CancellationToken cancellationToken = default)
    {
        if (!_descriptors.TryGetByToolName(toolName, out var descriptor)
            || !McpToolAvailability.IsAvailable(descriptor, _hostOptions))
            return new ToolResult(true, $"Unknown tool: {toolName}");

        var safeArguments = ApplySafetyPolicy(descriptor, arguments);
        using var trustScope = _hostOptions.EnterTrustScope();

        if (_hostOptions.TrustedRepositoryRoot is null
            && !DaemonRuntime.IsDisabled
            && descriptor.Port is not null
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
                return ToToolResult(recovery.Response, descriptor.Port.ResponseType);
            if (!recovery.ShouldFallback)
                return new ToolResult(
                    true,
                    "The daemon accepted this request but its result is indeterminate; inspect the target before retrying.");

            // The portable stdio host must remain usable before a daemon has been started,
            // and while a stale daemon is being replaced. The same typed port is the narrow
            // in-process recovery path described by ADR-0005.
        }

        if (descriptor.Port is null)
        {
            var admission = AdmitWorkspaceRequest(descriptor, safeArguments, request: null);
            if (admission is not null)
                return admission;

            return await _legacyBridge.InvokeAsync(descriptor, safeArguments, cancellationToken);
        }

        try
        {
            var request = JsonSerializer.Deserialize(
                safeArguments.GetRawText(),
                descriptor.Port.RequestType,
                RequestOptions);
            if (request is null)
                return new ToolResult(true, $"Invalid input for tool '{toolName}'.");

            if (request is not DoctorRequest { InstallationOnly: true })
            {
                var admission = AdmitWorkspaceRequest(descriptor, safeArguments, request);
                if (admission is not null)
                    return admission;
            }

            var result = await descriptor.Port.InvokeAsync(request, cancellationToken);
            if (result.IsError)
                return new ToolResult(
                    true,
                    result.LegacyErrorPayload
                    ?? (result.Error is null
                        ? "Command failed."
                        : SerializeError(result.Error)));

            return ToolResult.Structured(SerializeSuccess(result.Value));
        }
        catch (RepositoryTrustRequiredException ex)
        {
            return new ToolResult(
                true,
                SerializeError(new RtError(
                    RtErrorKind.TrustRequired,
                    ex.Message,
                    new Dictionary<string, string>
                    {
                        ["solution"] = ex.SolutionPath,
                        ["grantThrough"] = "rt trust add, trusted CLI invocation, or MCP host startup"
                    })));
        }
        catch (Exception ex) when (ex is ArgumentException
                                   or InvalidOperationException
                                   or JsonException
                                   or IOException)
        {
            return new ToolResult(true, ex.Message);
        }
    }

    private ToolResult? AdmitWorkspaceRequest(
        ApplicationCommandDescriptor descriptor,
        JsonElement arguments,
        object? request)
    {
        if (!RequiresWorkspace(descriptor))
            return null;

        var trustRegistry = _hostOptions.TrustRegistry;
        if (trustRegistry is null
            || _hostOptions.TrustedRepositoryRoot is not null)
        {
            try
            {
                MsBuildBootstrap.Register(
                    _hostOptions.TrustedRepositoryRoot ?? Environment.CurrentDirectory);
                return null;
            }
            catch (InvalidOperationException exception)
            {
                return new ToolResult(true, exception.Message);
            }
        }

        var target = ResolveWorkspaceTarget(arguments, request);
        if (target.IsErr)
            return new ToolResult(true, SerializeError(target.Match(
                _ => throw new InvalidOperationException("Expected workspace target resolution failure."),
                error => error)));

        var context = target.Match(
            value => value,
            _ => throw new InvalidOperationException("Expected a resolved workspace target."));
        if (!trustRegistry.IsTrustedFor(context.TrustPath))
        {
            return new ToolResult(true, SerializeError(new RtError(
                RtErrorKind.TrustRequired,
                $"Repository evaluation is not trusted for '{context.TrustPath}'.",
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["solution"] = context.TrustPath,
                    ["grantThrough"] = "rt trust add, trusted CLI invocation, or MCP host startup"
                })));
        }

        var binding = _msBuildBinding.EnsureRegistered(context.Directory);
        if (binding.IsErr)
        {
            return new ToolResult(true, SerializeError(binding.Match(
                _ => throw new InvalidOperationException("Expected MSBuild binding failure."),
                error => error)));
        }

        return binding.Unwrap()
            ? null
            : new ToolResult(true, SerializeError(RtError.ExternalToolFailure(
                "The MCP host did not confirm process-global MSBuild registration.",
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["mcpRestartRequired"] = "true",
                    ["requestedDirectory"] = context.Directory
                })));
    }

    internal static Result<WorkspaceTarget, RtError> ResolveWorkspaceTarget(
        JsonElement arguments,
        object? request)
    {
        try
        {
            if (request is DoctorRequest doctor)
            {
                if (!Path.IsPathFullyQualified(doctor.Directory))
                    return InvalidWorkspaceTarget();

                var fullDirectory = Path.GetFullPath(doctor.Directory);
                if (string.IsNullOrWhiteSpace(doctor.Solution))
                {
                    return Result<WorkspaceTarget, RtError>.Ok(new WorkspaceTarget(
                        fullDirectory,
                        fullDirectory));
                }

                var fullSolution = Path.GetFullPath(Path.Combine(fullDirectory, doctor.Solution));
                return Result<WorkspaceTarget, RtError>.Ok(new WorkspaceTarget(
                    fullSolution,
                    DirectoryForSolution(fullSolution)));
            }

            var requestedTarget = TryGetSolution(arguments);
            if (string.IsNullOrWhiteSpace(requestedTarget)
                || !Path.IsPathFullyQualified(requestedTarget))
                return InvalidWorkspaceTarget();

            var fullRequestedTarget = Path.GetFullPath(requestedTarget);
            return Result<WorkspaceTarget, RtError>.Ok(new WorkspaceTarget(
                fullRequestedTarget,
                DirectoryForSolution(fullRequestedTarget)));
        }
        catch (Exception exception) when (exception is ArgumentException
                                          or IOException
                                          or NotSupportedException)
        {
            return Result<WorkspaceTarget, RtError>.Err(new RtError(
                RtErrorKind.InvalidInput,
                $"The requested solution or workspace path is invalid: {exception.Message}"));
        }
    }

    private static Result<WorkspaceTarget, RtError> InvalidWorkspaceTarget() =>
        Result<WorkspaceTarget, RtError>.Err(new RtError(
            RtErrorKind.InvalidInput,
            "Persistent MCP execution requires an absolute solution or workspace directory."));

    private static string? TryGetSolution(JsonElement arguments)
    {
        if (arguments.ValueKind != JsonValueKind.Object)
            return null;

        foreach (var property in arguments.EnumerateObject())
        {
            if (string.Equals(property.Name, "solution", StringComparison.OrdinalIgnoreCase)
                && property.Value.ValueKind == JsonValueKind.String)
                return property.Value.GetString();
        }

        return null;
    }

    private static string DirectoryForSolution(string path) =>
        Path.GetDirectoryName(path)
        ?? throw new ArgumentException("The requested solution has no parent directory.", nameof(path));

    private static bool RequiresWorkspace(ApplicationCommandDescriptor descriptor) =>
        descriptor.Effects.HasFlag(CommandEffects.RepositoryRead)
        || descriptor.Effects.HasFlag(CommandEffects.RepositoryWrite)
        || descriptor.Effects.HasFlag(CommandEffects.RepositoryCodeExecution);

    internal sealed record WorkspaceTarget(string TrustPath, string Directory);

    private static ToolResult ToToolResult(
        DaemonRpcResponse response,
        Type responseType)
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

        if (response.Result is not { } envelope
            || envelope.ValueKind != JsonValueKind.Object
            || !envelope.TryGetProperty("data", out var data))
            return new ToolResult(true, "The daemon returned an invalid command response.");

        try
        {
            var value = data.Deserialize(responseType, DaemonRpcJson.Options);
            return ToolResult.Structured(SerializeSuccess(value));
        }
        catch (Exception ex) when (ex is JsonException
                                   or InvalidOperationException
                                   or NotSupportedException)
        {
            return new ToolResult(
                true,
                $"The daemon returned an invalid command result: {ex.Message}");
        }
    }

    private static JsonSerializerOptions CreateResponseOptions()
    {
        return new JsonSerializerOptions(JsonEncoders.Select(RtContext.RelaxJson))
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };
    }

    private static string SerializeSuccess(object? value)
    {
        return value switch
        {
            SemanticDiffOperationResult semanticDiff =>
                SemanticDiffJson.Serialize(semanticDiff.Deltas),
            DescribeResult describe => SerializeDescribe(describe),
            MutationExecutionResult<RenameOperationResult> rename => SerializeRename(rename),
            _ => JsonSerializer.Serialize(
                value,
                value?.GetType() ?? typeof(object),
                CreateResponseOptions())
        };
    }

    private static string SerializeDescribe(DescribeResult result)
    {
        var serialized = result switch
        {
            { Kind: DescribeResultKind.Compact, Compact: not null } =>
                JsonSerializer.SerializeToNode(result.Compact, CreateResponseOptions()),
            { Kind: DescribeResultKind.Verbose, Verbose: { } verbose } =>
                JsonNode.Parse(verbose.GetRawText()),
            _ => throw new InvalidOperationException(
                "The describe result did not contain its declared value.")
        };
        var payload = serialized?.AsObject()
            ?? throw new InvalidOperationException("The describe result was not a JSON object.");
        if (!string.IsNullOrWhiteSpace(result.SourceRevision))
            payload["sourceRevision"] = result.SourceRevision;
        return payload.ToJsonString(CreateResponseOptions());
    }

    private static string SerializeRename(MutationExecutionResult<RenameOperationResult> execution)
    {
        if (execution.Value is
            {
                Mode: RenameOperationMode.Single,
                Renames.Count: 1
            } single)
        {
            return JsonSerializer.Serialize(single.Renames[0], CreateResponseOptions());
        }

        return JsonSerializer.Serialize(execution, CreateResponseOptions());
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

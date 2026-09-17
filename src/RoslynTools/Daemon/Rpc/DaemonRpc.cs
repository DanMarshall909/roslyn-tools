using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using RoslynTools.Core;
using RoslynTools.Services;

namespace RoslynTools.Daemon.Rpc;

/// <summary>Versioned request for typed command execution owned by the daemon.</summary>
internal sealed record DaemonRpcRequest(
    int ProtocolVersion,
    string RequestId,
    string Method,
    string CommandName,
    JsonElement Arguments,
    string? WorkingDirectory = null);

/// <summary>Transport-neutral error returned by the daemon RPC boundary.</summary>
internal sealed record DaemonRpcError(
    string Code,
    string Message,
    RtErrorKind? Kind = null,
    IReadOnlyDictionary<string, string>? Context = null,
    string? LegacyErrorPayload = null);

/// <summary>Terminal response for one typed daemon request.</summary>
internal sealed record DaemonRpcResponse(
    int ProtocolVersion,
    string RequestId,
    JsonElement? Result = null,
    DaemonRpcError? Error = null);

internal static class DaemonRpcProtocol
{
    internal const int CurrentVersion = 1;
    internal const string ExecuteCommandMethod = "commands/execute";
}

internal static class DaemonRpcJson
{
    internal static JsonSerializerOptions Options { get; } = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    internal static JsonElement SerializeExecution(CommandExecutionResult execution) =>
        JsonSerializer.SerializeToElement(execution, Options);
}

internal static class DaemonRpcErrorCodes
{
    internal const string IncompatibleProtocol = "incompatible_protocol";
    internal const string MethodNotFound = "method_not_found";
    internal const string CommandNotFound = "command_not_found";
    internal const string InvalidArguments = "invalid_arguments";
    internal const string CommandFailed = "command_failed";
}

/// <summary>Shared daemon transport facts; host adapters own the pipe server lifecycle.</summary>
internal static class DaemonTransport
{
    internal const string PipeName = "rt-daemon";
}

/// <summary>Process-level daemon routing controls shared by every host adapter.</summary>
internal static class DaemonRuntime
{
    internal const string ExperimentEnvironmentVariable = "RT_DAEMON_EXPERIMENT";
    private static readonly AsyncLocal<bool?> ExperimentOverride = new();

    internal static bool IsDisabled => !IsExperimentEnabled;

    internal static bool IsExperimentEnabled =>
        ExperimentOverride.Value
        ?? Environment.GetEnvironmentVariable(ExperimentEnvironmentVariable) is "1" or "true";

    internal static IDisposable UseExperimentOverride(bool enabled)
    {
        var previous = ExperimentOverride.Value;
        ExperimentOverride.Value = enabled;
        return new DelegateDisposable(() => ExperimentOverride.Value = previous);
    }

    private sealed class DelegateDisposable(Action dispose) : IDisposable
    {
        private Action? _dispose = dispose;

        public void Dispose() => Interlocked.Exchange(ref _dispose, null)?.Invoke();
    }
}

/// <summary>Transport seam for a typed daemon command invocation.</summary>
internal interface IDaemonRpcClient
{
    Task<DaemonRpcAttempt> ExecuteAsync(
        string commandName,
        JsonElement arguments,
        CommandSafety safety,
        CancellationToken cancellationToken = default);
}

/// <summary>What the client can prove about one daemon RPC transport attempt.</summary>
internal enum DaemonRpcAttemptState
{
    Completed,
    UnavailableBeforeDispatch,
    IndeterminateAfterDispatch
}

/// <summary>
///     One structured transport attempt. <see cref="DispatchStarted" /> becomes true before
///     writing begins because a partial/flush failure cannot prove the server received nothing.
/// </summary>
internal sealed record DaemonRpcAttempt
{
    internal DaemonRpcAttempt(
        DaemonRpcAttemptState state,
        DaemonRpcResponse? response,
        bool DispatchStarted)
    {
        if (state == DaemonRpcAttemptState.Completed && (response is null || !DispatchStarted))
            throw new ArgumentException("A completed RPC attempt requires a sent request and response.");
        if (state == DaemonRpcAttemptState.UnavailableBeforeDispatch && (response is not null || DispatchStarted))
            throw new ArgumentException("An unavailable RPC attempt cannot have dispatched a request.");
        if (state == DaemonRpcAttemptState.IndeterminateAfterDispatch && (response is not null || !DispatchStarted))
            throw new ArgumentException("An indeterminate RPC attempt requires a sent request without a response.");

        State = state;
        Response = response;
        this.DispatchStarted = DispatchStarted;
    }

    public DaemonRpcAttemptState State { get; }
    public DaemonRpcResponse? Response { get; }
    public bool DispatchStarted { get; }
}

/// <summary>Starts or confirms reachability of the shared local daemon.</summary>
internal interface IDaemonLauncher
{
    Task<bool> EnsureStartedAsync(CancellationToken cancellationToken = default);
}

/// <summary>The shared recovery decision consumed by CLI and MCP host adapters.</summary>
internal sealed record DaemonRpcRecoveryResult(
    DaemonRpcResponse? Response,
    bool ShouldFallback,
    DaemonRpcAttempt LastAttempt);

/// <summary>Applies the single safe start, retry, and in-process fallback policy.</summary>
internal sealed class DaemonRpcRecoveryCoordinator(
    IDaemonRpcClient daemonRpc,
    IDaemonLauncher daemonLauncher)
{
    private readonly IDaemonRpcClient _daemonRpc = daemonRpc;
    private readonly IDaemonLauncher _daemonLauncher = daemonLauncher;

    internal async Task<DaemonRpcRecoveryResult> ExecuteAsync(
        ApplicationCommandDefinition definition,
        JsonElement arguments,
        CancellationToken cancellationToken = default) =>
        await ExecuteLegacyAsync(
            definition.Name,
            arguments,
            definition.Safety,
            cancellationToken);

    internal async Task<DaemonRpcRecoveryResult> ExecuteLegacyAsync(
        string commandName,
        JsonElement arguments,
        CommandSafety safety,
        CancellationToken cancellationToken = default)
    {
        if (safety.Effect == CommandEffect.Mutation)
        {
            return new DaemonRpcRecoveryResult(
                null,
                ShouldFallback: true,
                new DaemonRpcAttempt(
                    DaemonRpcAttemptState.UnavailableBeforeDispatch,
                    null,
                    DispatchStarted: false));
        }

        var attempt = await _daemonRpc.ExecuteAsync(
            commandName,
            arguments,
            safety,
            cancellationToken);
        if (attempt.State == DaemonRpcAttemptState.Completed)
            return new DaemonRpcRecoveryResult(attempt.Response, false, attempt);

        if (IsIndeterminateMutation(attempt, safety))
            return new DaemonRpcRecoveryResult(null, false, attempt);

        if (!await _daemonLauncher.EnsureStartedAsync(cancellationToken))
            return new DaemonRpcRecoveryResult(null, true, attempt);

        var retry = await _daemonRpc.ExecuteAsync(
            commandName,
            arguments,
            safety,
            cancellationToken);
        if (retry.State == DaemonRpcAttemptState.Completed)
            return new DaemonRpcRecoveryResult(retry.Response, false, retry);

        return new DaemonRpcRecoveryResult(
            null,
            !IsIndeterminateMutation(retry, safety),
            retry);
    }

    private static bool IsIndeterminateMutation(
        DaemonRpcAttempt attempt,
        CommandSafety safety) =>
        attempt.State == DaemonRpcAttemptState.IndeterminateAfterDispatch
        && safety.Effect == CommandEffect.Mutation;
}

/// <summary>Thin local-RPC client for a typed daemon command invocation.</summary>
internal sealed class DaemonRpcClient : IDaemonRpcClient
{
    private readonly string _pipeName;

    private static readonly UTF8Encoding NoBom = new(false, true);
    public DaemonRpcClient()
        : this(DaemonTransport.PipeName)
    {
    }

    internal DaemonRpcClient(string pipeName)
    {
        _pipeName = pipeName;
    }

    public async Task<DaemonRpcAttempt> ExecuteAsync(
        string commandName,
        JsonElement arguments,
        CommandSafety safety,
        CancellationToken cancellationToken = default)
    {
        var waitForTerminalResponse = safety.Effect == CommandEffect.Mutation;
        var dispatchStarted = false;

        await using var pipe = new NamedPipeClientStream(
            ".", _pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        if (!waitForTerminalResponse)
            deadline.CancelAfter(TimeSpan.FromSeconds(10));

        var responseToken = waitForTerminalResponse
            ? CancellationToken.None
            : deadline.Token;
        using var closePipeOnDeadline = responseToken.Register(pipe.Dispose);

        try
        {
            using var connectDeadline = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token);
            connectDeadline.CancelAfter(TimeSpan.FromSeconds(1));
            await pipe.ConnectAsync(connectDeadline.Token);
            if (waitForTerminalResponse)
                cancellationToken.ThrowIfCancellationRequested();

            var request = new DaemonRpcRequest(
                DaemonRpcProtocol.CurrentVersion,
                Guid.NewGuid().ToString("N"),
                DaemonRpcProtocol.ExecuteCommandMethod,
                commandName,
                arguments.Clone(),
                Directory.GetCurrentDirectory());
            await using var writer = new StreamWriter(pipe, NoBom, 1024, true) { AutoFlush = true };
            dispatchStarted = true;
            await writer.WriteLineAsync(
                JsonSerializer.Serialize(request, DaemonRpcJson.Options).AsMemory(),
                deadline.Token);

            using var reader = new StreamReader(pipe, NoBom, false, 1024, true);
            var line = await reader.ReadLineAsync(responseToken);
            var response = line is null
                ? null
                : JsonSerializer.Deserialize<DaemonRpcResponse>(line, DaemonRpcJson.Options);
            return response is null
                ? new DaemonRpcAttempt(
                    DaemonRpcAttemptState.IndeterminateAfterDispatch,
                    null,
                    DispatchStarted: true)
                : new DaemonRpcAttempt(
                    DaemonRpcAttemptState.Completed,
                    response,
                    DispatchStarted: true);
        }
        catch (OperationCanceledException) when (
            cancellationToken.IsCancellationRequested
            && ShouldPreserveCallerCancellation(dispatchStarted, safety))
        {
            throw;
        }
        catch (Exception ex) when (ex is IOException
                                   or JsonException
                                   or OperationCanceledException
                                   or ObjectDisposedException)
        {
            return new DaemonRpcAttempt(
                dispatchStarted
                    ? DaemonRpcAttemptState.IndeterminateAfterDispatch
                    : DaemonRpcAttemptState.UnavailableBeforeDispatch,
                null,
                dispatchStarted);
        }
    }

    internal static bool ShouldPreserveCallerCancellation(
        bool dispatchStarted,
        CommandSafety safety) =>
        !dispatchStarted || safety.Effect == CommandEffect.ReadOnly;
}

/// <summary>
/// Resolves typed command application ports and executes them without entering CLI parsing or output rendering.
/// </summary>
internal interface IDaemonSdkContextGuard
{
    Result<bool, RtError> Check(string directory);
}

internal sealed class DaemonCommandDispatcher
{
    private static readonly SemaphoreSlim WorkingDirectoryGate = new(1, 1);

    private readonly Dictionary<string, ICommandApplicationPort> _ports;
    private readonly RepositoryTrustRegistry? _trustRegistry;
    private readonly IDaemonSdkContextGuard? _sdkContextGuard;

    internal DaemonCommandDispatcher(
        IEnumerable<ICommandApplicationPort> ports,
        RepositoryTrustRegistry? trustRegistry = null,
        IDaemonSdkContextGuard? sdkContextGuard = null)
    {
        _ports = ports.ToDictionary(port => port.Definition.Name, StringComparer.Ordinal);
        _trustRegistry = trustRegistry;
        _sdkContextGuard = sdkContextGuard;
    }

    internal async Task<DaemonRpcResponse> ExecuteAsync(
        DaemonRpcRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.ProtocolVersion != DaemonRpcProtocol.CurrentVersion)
            return Failure(request, DaemonRpcErrorCodes.IncompatibleProtocol,
                $"Unsupported daemon RPC protocol {request.ProtocolVersion}; expected {DaemonRpcProtocol.CurrentVersion}.");

        if (!string.Equals(request.Method, DaemonRpcProtocol.ExecuteCommandMethod, StringComparison.Ordinal))
            return Failure(request, DaemonRpcErrorCodes.MethodNotFound, $"Unknown RPC method '{request.Method}'.");

        if (!_ports.TryGetValue(NormalizeCommandName(request.CommandName), out var port))
            return Failure(request, DaemonRpcErrorCodes.CommandNotFound,
                $"Command '{request.CommandName}' does not have a typed application port.");

        if (port.Definition.Safety.Effect == CommandEffect.Mutation)
            return Failure(
                request,
                DaemonRpcErrorCodes.CommandNotFound,
                $"Mutation command '{request.CommandName}' is in-process only during safety hardening.");

        object? typedRequest;
        try
        {
            typedRequest = JsonSerializer.Deserialize(
                request.Arguments.GetRawText(),
                port.RequestType,
                DaemonRpcJson.Options);
        }
        catch (JsonException ex)
        {
            return Failure(request, DaemonRpcErrorCodes.InvalidArguments, ex.Message);
        }

        if (typedRequest is null)
            return Failure(request, DaemonRpcErrorCodes.InvalidArguments, "Command arguments cannot be null.");

        if (_sdkContextGuard is not null)
        {
            DaemonRequestTarget target;
            if (_trustRegistry is not null)
            {
                try
                {
                    target = ResolveRequestTarget(request);
                }
                catch (Exception exception) when (exception is ArgumentException
                                                  or IOException
                                                  or NotSupportedException)
                {
                    return TrustRequired(request, RequestedTargetText(request));
                }

                if (!_trustRegistry.IsTrustedFor(target.TrustPath))
                {
                    return TrustRequired(request, target.TrustPath);
                }
            }
            else
            {
                try
                {
                    target = ResolveRequestTarget(request);
                }
                catch (Exception exception) when (exception is ArgumentException
                                                  or IOException
                                                  or NotSupportedException)
                {
                    return Failure(request, DaemonRpcErrorCodes.InvalidArguments, exception.Message);
                }
            }

            var compatibility = _sdkContextGuard.Check(target.SdkDirectory);
            if (compatibility.IsErr)
            {
                var error = compatibility.Match(
                    _ => throw new InvalidOperationException("Expected daemon SDK compatibility failure."),
                    value => value);
                return Failure(request, DaemonRpcErrorCodes.CommandFailed, error);
            }
        }

        try
        {
            await WorkingDirectoryGate.WaitAsync(cancellationToken);
            var originalWorkingDirectory = Directory.GetCurrentDirectory();
            CommandPortResult result;
            try
            {
                if (!string.IsNullOrWhiteSpace(request.WorkingDirectory))
                    Directory.SetCurrentDirectory(request.WorkingDirectory);

                using var trustScope = _trustRegistry is null
                    ? null
                    : RepositoryTrustContext.UsePersistentTrust(_trustRegistry);
                result = await port.InvokeAsync(typedRequest, cancellationToken);
            }
            finally
            {
                Directory.SetCurrentDirectory(originalWorkingDirectory);
                WorkingDirectoryGate.Release();
            }

            if (result.IsError)
                return result.Error is null
                    ? Failure(request, DaemonRpcErrorCodes.CommandFailed, "Command failed.")
                    : Failure(
                        request,
                        DaemonRpcErrorCodes.CommandFailed,
                        result.Error,
                        result.LegacyErrorPayload);

            var suggestedTools = port is ILegacyCommandApplicationPort
                ? DaemonOwnedCommands.SuggestedToolsFor(port.Definition.Name)
                : port.Definition.SuggestedTools;
            var execution = new CommandExecutionResult(result.Value, suggestedTools);
            return new DaemonRpcResponse(
                DaemonRpcProtocol.CurrentVersion,
                request.RequestId,
                DaemonRpcJson.SerializeExecution(execution));
        }
        catch (Exception ex) when (ex is ArgumentException
                                   or InvalidOperationException
                                   or JsonException
                                   or IOException
                                   or UnauthorizedAccessException)
        {
            return Failure(request, DaemonRpcErrorCodes.CommandFailed, ex.Message);
        }
    }

    private static DaemonRpcResponse Failure(DaemonRpcRequest request, string code, string message) =>
        new(DaemonRpcProtocol.CurrentVersion, request.RequestId, Error: new DaemonRpcError(code, message));

    private static DaemonRequestTarget ResolveRequestTarget(DaemonRpcRequest request)
    {
        var workingDirectory = string.IsNullOrWhiteSpace(request.WorkingDirectory)
            ? Directory.GetCurrentDirectory()
            : Path.GetFullPath(request.WorkingDirectory);
        if (request.Arguments.ValueKind != JsonValueKind.Object)
            return new DaemonRequestTarget(workingDirectory, workingDirectory);

        foreach (var property in request.Arguments.EnumerateObject())
        {
            if (!string.Equals(property.Name, "solution", StringComparison.OrdinalIgnoreCase)
                || property.Value.ValueKind != JsonValueKind.String
                || string.IsNullOrWhiteSpace(property.Value.GetString()))
                continue;

            var solution = property.Value.GetString()!;
            var resolved = Path.GetFullPath(Path.Combine(workingDirectory, solution));
            return new DaemonRequestTarget(
                resolved,
                Path.GetDirectoryName(resolved) ?? workingDirectory);
        }

        return new DaemonRequestTarget(workingDirectory, workingDirectory);
    }

    private static string RequestedTargetText(DaemonRpcRequest request)
    {
        if (request.Arguments.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in request.Arguments.EnumerateObject())
                if (string.Equals(property.Name, "solution", StringComparison.OrdinalIgnoreCase)
                    && property.Value.ValueKind == JsonValueKind.String
                    && !string.IsNullOrWhiteSpace(property.Value.GetString()))
                    return property.Value.GetString()!;
        }

        return request.WorkingDirectory ?? Directory.GetCurrentDirectory();
    }

    private static DaemonRpcResponse TrustRequired(DaemonRpcRequest request, string target)
    {
        var exception = RepositoryTrustRequiredException.ForSolution(target);
        return Failure(
            request,
            DaemonRpcErrorCodes.CommandFailed,
            new RtError(
                RtErrorKind.TrustRequired,
                exception.Message,
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["solution"] = target,
                    ["grantThrough"] = "rt trust add or an invocation-scoped trusted CLI command"
                }));
    }

    private sealed record DaemonRequestTarget(string TrustPath, string SdkDirectory);

    private static DaemonRpcResponse Failure(
        DaemonRpcRequest request,
        string code,
        RtError error,
        string? legacyErrorPayload = null) =>
        new(
            DaemonRpcProtocol.CurrentVersion,
            request.RequestId,
            Error: new DaemonRpcError(
                code,
                legacyErrorPayload ?? error.Message,
                error.Kind,
                error.Context,
                legacyErrorPayload));

    private static string NormalizeCommandName(string commandName) =>
        commandName.Replace('-', '_');
}

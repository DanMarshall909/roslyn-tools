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
internal sealed record DaemonRpcError(string Code, string Message);

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

/// <summary>Transport seam for a typed daemon command invocation.</summary>
internal interface IDaemonRpcClient
{
    Task<DaemonRpcResponse?> TryExecuteAsync(
        string commandName,
        JsonElement arguments,
        CancellationToken cancellationToken = default);
}

/// <summary>Thin local-RPC client for a typed daemon command invocation.</summary>
internal sealed class DaemonRpcClient : IDaemonRpcClient
{
    private readonly string _pipeName = DaemonTransport.PipeName;

    private static readonly UTF8Encoding NoBom = new(false, true);
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public async Task<DaemonRpcResponse?> TryExecuteAsync(
        string commandName,
        JsonElement arguments,
        CancellationToken cancellationToken = default)
    {
        var waitForTerminalResponse = DaemonOwnedCommands.All
            .FirstOrDefault(command => string.Equals(
                command.Name,
                NormalizeCommandName(commandName),
                StringComparison.Ordinal))
            ?.Safety.Effect == CommandEffect.Mutation;

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
            await writer.WriteLineAsync(
                JsonSerializer.Serialize(request, SerializerOptions).AsMemory(),
                responseToken);

            using var reader = new StreamReader(pipe, NoBom, false, 1024, true);
            var line = await reader.ReadLineAsync(responseToken);
            return line is null
                ? null
                : JsonSerializer.Deserialize<DaemonRpcResponse>(line, SerializerOptions);
        }
        catch (Exception ex) when (ex is IOException or OperationCanceledException or ObjectDisposedException)
        {
            return null;
        }
    }

    private static string NormalizeCommandName(string commandName) =>
        commandName.Replace('-', '_');
}

/// <summary>
/// Resolves typed command application ports and executes them without entering CLI parsing or output rendering.
/// </summary>
internal sealed class DaemonCommandDispatcher
{
    private static readonly SemaphoreSlim WorkingDirectoryGate = new(1, 1);

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    private readonly Dictionary<string, ICommandApplicationPort> _ports;

    internal DaemonCommandDispatcher(IEnumerable<ICommandApplicationPort> ports)
    {
        _ports = ports.ToDictionary(port => NormalizeCommandName(port.CommandName), StringComparer.Ordinal);
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

        object? typedRequest;
        try
        {
            typedRequest = JsonSerializer.Deserialize(request.Arguments.GetRawText(), port.RequestType, SerializerOptions);
        }
        catch (JsonException ex)
        {
            return Failure(request, DaemonRpcErrorCodes.InvalidArguments, ex.Message);
        }

        if (typedRequest is null)
            return Failure(request, DaemonRpcErrorCodes.InvalidArguments, "Command arguments cannot be null.");

        try
        {
            await WorkingDirectoryGate.WaitAsync(cancellationToken);
            var originalWorkingDirectory = Directory.GetCurrentDirectory();
            CommandPortResult result;
            try
            {
                if (!string.IsNullOrWhiteSpace(request.WorkingDirectory))
                    Directory.SetCurrentDirectory(request.WorkingDirectory);

                result = await port.InvokeAsync(typedRequest, cancellationToken);
            }
            finally
            {
                Directory.SetCurrentDirectory(originalWorkingDirectory);
                WorkingDirectoryGate.Release();
            }

            if (result.IsError)
                return Failure(request, DaemonRpcErrorCodes.CommandFailed, result.Error ?? "Command failed.");

            var execution = new CommandExecutionResult(
                result.Value,
                DaemonOwnedCommands.SuggestedToolsFor(request.CommandName));
            return new DaemonRpcResponse(
                DaemonRpcProtocol.CurrentVersion,
                request.RequestId,
                JsonSerializer.SerializeToElement(execution, SerializerOptions));
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

    private static string NormalizeCommandName(string commandName) =>
        commandName.Replace('-', '_');
}

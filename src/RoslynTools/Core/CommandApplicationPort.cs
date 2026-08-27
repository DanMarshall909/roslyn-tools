namespace RoslynTools.Core;

/// <summary>
///     Host-agnostic application port for one rt command. CLI and protocol adapters bind
///     their own inputs, then invoke the same typed operation through this contract.
/// </summary>
internal interface ICommandApplicationPort
{
    ApplicationCommandDefinition Definition { get; }
    Type RequestType { get; }
    Type ResponseType { get; }
    Task<CommandPortResult> InvokeAsync(
        object request,
        CancellationToken cancellationToken = default);
}

/// <summary>
///     Typed base for application ports while retaining a non-generic dispatch surface for
///     descriptor-driven hosts.
/// </summary>
internal abstract class CommandApplicationPort<TRequest, TResponse> : ICommandApplicationPort
    where TRequest : class
{
    public abstract ApplicationCommandDefinition Definition { get; }

    public Type RequestType => typeof(TRequest);

    public Type ResponseType => typeof(TResponse);

    public abstract Task<Result<TResponse, RtError>> ExecuteAsync(
        TRequest request,
        CancellationToken cancellationToken = default);

    async Task<CommandPortResult> ICommandApplicationPort.InvokeAsync(
        object request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (request is not TRequest typedRequest)
            return CommandPortResult.Failure(new RtError(
                RtErrorKind.InvalidInput,
                $"Command '{Definition.Name}' requires request type {typeof(TRequest).FullName}.",
                new Dictionary<string, string>
                {
                    ["expectedRequestType"] = typeof(TRequest).FullName!,
                    ["actualRequestType"] = request.GetType().FullName ?? request.GetType().Name
                }));

        var result = await ExecuteAsync(typedRequest, cancellationToken);
        return result.Match(
            value => CommandPortResult.Success(value),
            error => CommandPortResult.Failure(error));
    }
}

/// <summary>
///     Compatibility seam for ports that have not migrated to a host-neutral definition and
///     typed <see cref="Result{T,TError}" /> return. New ports must use
///     <see cref="CommandApplicationPort{TRequest,TResponse}" />.
/// </summary>
internal interface ILegacyCommandApplicationPort : ICommandApplicationPort;

/// <summary>Untyped compatibility base for ports with custom legacy boundary mapping.</summary>
internal abstract class LegacyCommandApplicationPort : ILegacyCommandApplicationPort
{
    private ApplicationCommandDefinition? _definition;

    public abstract string CommandName { get; }

    public ApplicationCommandDefinition Definition => _definition ??= new ApplicationCommandDefinition(
        new RtCommandInfo(
            CommandName,
            false,
            true,
            "compatibility",
            "Compatibility definition for a port awaiting migration.",
            []),
        RequestType,
        ResponseType,
        mcpVisible: false,
        CommandSafety.ReadOnly,
        CommandExecutionPlacement.InProcess,
        []);

    public abstract Type RequestType { get; }

    public abstract Type ResponseType { get; }

    protected abstract Task<CommandPortResult> InvokeLegacyAsync(
        object request,
        CancellationToken cancellationToken);

    Task<CommandPortResult> ICommandApplicationPort.InvokeAsync(
        object request,
        CancellationToken cancellationToken) => InvokeLegacyAsync(request, cancellationToken);
}

/// <summary>Typed compatibility base retained only while existing ports migrate.</summary>
internal abstract class LegacyCommandApplicationPort<TRequest, TResponse>
    : ILegacyCommandApplicationPort
    where TRequest : class
{
    private ApplicationCommandDefinition? _definition;

    public abstract string CommandName { get; }

    public ApplicationCommandDefinition Definition => _definition ??= CreateDefinition();

    public Type RequestType => typeof(TRequest);

    public Type ResponseType => typeof(TResponse);

    public abstract Task<TResponse> ExecuteAsync(
        TRequest request,
        CancellationToken cancellationToken = default);

    async Task<CommandPortResult> ICommandApplicationPort.InvokeAsync(
        object request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (request is not TRequest typedRequest)
            return CommandPortResult.Failure(new RtError(
                RtErrorKind.InvalidInput,
                $"Command '{CommandName}' requires request type {typeof(TRequest).FullName}."));

        var value = await ExecuteAsync(typedRequest, cancellationToken);
        return CommandPortResult.Success(value);
    }

    private ApplicationCommandDefinition CreateDefinition() => new(
        new RtCommandInfo(
            CommandName,
            false,
            true,
            "compatibility",
            "Compatibility definition for a port awaiting migration.",
            []),
        typeof(TRequest),
        typeof(TResponse),
        mcpVisible: false,
        CommandSafety.ReadOnly,
        CommandExecutionPlacement.InProcess,
        []);
}

/// <summary>Transport-neutral result returned by an application port.</summary>
internal sealed record CommandPortResult(
    bool IsError,
    object? Value,
    RtError? Error,
    string? LegacyErrorPayload = null)
{
    internal static CommandPortResult Success(object? value) => new(false, value, null, null);
    internal static CommandPortResult Failure(RtError error, string? legacyErrorPayload = null) =>
        new(true, null, error, legacyErrorPayload);

    internal static CommandPortResult LegacyFailure(string error) => new(
        true,
        null,
        new RtError(RtErrorKind.InvalidInput, error),
        error);
}

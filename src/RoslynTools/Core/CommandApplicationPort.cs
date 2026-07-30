namespace RoslynTools.Core;

/// <summary>
///     Host-agnostic application port for one rt command. CLI and protocol adapters bind
///     their own inputs, then invoke the same typed operation through this contract.
/// </summary>
internal interface ICommandApplicationPort
{
    string CommandName { get; }
    Type RequestType { get; }
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
    public abstract string CommandName { get; }

    public Type RequestType => typeof(TRequest);

    public abstract Task<TResponse> ExecuteAsync(
        TRequest request,
        CancellationToken cancellationToken = default);

    async Task<CommandPortResult> ICommandApplicationPort.InvokeAsync(
        object request,
        CancellationToken cancellationToken)
    {
        if (request is not TRequest typedRequest)
            throw new ArgumentException(
                $"Command '{CommandName}' requires request type {typeof(TRequest).FullName}.",
                nameof(request));

        var value = await ExecuteAsync(typedRequest, cancellationToken);
        return CommandPortResult.Success(value);
    }
}

/// <summary>Transport-neutral result returned by an application port.</summary>
internal sealed record CommandPortResult(bool IsError, object? Value, string? Error)
{
    internal static CommandPortResult Success(object? value) => new(false, value, null);
    internal static CommandPortResult Failure(string error) => new(true, null, error);
}

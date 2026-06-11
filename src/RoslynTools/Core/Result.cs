namespace RoslynTools.Core;

/// <summary>
///     A total two-branch result: either a success value of <typeparamref name="T" /> or an
///     error of <typeparamref name="TError" />. Use for routine, expected failure flows in
///     core services instead of throwing. Construct via <see cref="Ok" /> / <see cref="Err" />
///     and consume via <see cref="Match{TResult}" /> so both branches are always handled.
/// </summary>
internal sealed record Result<T, TError>
{
    private readonly T? _value;
    private readonly TError? _error;

    private Result(bool isOk, T? value, TError? error)
    {
        IsOk = isOk;
        _value = value;
        _error = error;
    }

    /// <summary>True when this is a success result.</summary>
    public bool IsOk { get; }

    /// <summary>True when this is an error result.</summary>
    public bool IsErr => !IsOk;

    /// <summary>Creates a success result carrying <paramref name="value" />.</summary>
    public static Result<T, TError> Ok(T value) => new(true, value, default);

    /// <summary>Creates an error result carrying <paramref name="error" />.</summary>
    public static Result<T, TError> Err(TError error) => new(false, default, error);

    /// <summary>Folds the result into a single value by handling both branches.</summary>
    public TResult Match<TResult>(Func<T, TResult> onOk, Func<TError, TResult> onErr)
        => IsOk ? onOk(_value!) : onErr(_error!);

    /// <summary>Runs the matching branch action for its side effects.</summary>
    public void Match(Action<T> onOk, Action<TError> onErr)
    {
        if (IsOk)
            onOk(_value!);
        else
            onErr(_error!);
    }

    /// <summary>Transforms the success value, leaving an error untouched.</summary>
    public Result<TNew, TError> Map<TNew>(Func<T, TNew> map)
        => IsOk ? Result<TNew, TError>.Ok(map(_value!)) : Result<TNew, TError>.Err(_error!);
}

/// <summary>Helpers for bridging <see cref="Result{T,TError}" /> into the exception world.</summary>
internal static class ResultExtensions
{
    /// <summary>
    ///     Returns the success value, or throws <see cref="InvalidOperationException" /> with the
    ///     error's message. This is the bridge for callers that have not yet migrated to consume
    ///     <see cref="Result{T,TError}" /> directly — it reproduces the legacy throwing behaviour.
    /// </summary>
    internal static T Unwrap<T>(this Result<T, RtError> result)
        => result.Match(value => value, error => throw new InvalidOperationException(error.Message));
}

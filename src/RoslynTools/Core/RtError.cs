namespace RoslynTools.Core;

/// <summary>
///     A typed, routine error returned (not thrown) by core services for expected
///     user-facing failure flows — symbol-not-found, ambiguous match, invalid input.
///     Pair with <see cref="Result{T,TError}" />. Genuine invariant violations and
///     unexpected failures should still throw so they surface with a stack trace.
/// </summary>
/// <param name="Kind">The error classification.</param>
/// <param name="Message">A human-readable, single-line message suitable for stderr.</param>
/// <param name="Context">Optional structured detail (e.g. the offending FQN, candidate list).</param>
internal sealed record RtError(
    RtErrorKind Kind,
    string Message,
    IReadOnlyDictionary<string, string>? Context = null)
{
    /// <summary>Creates a <see cref="RtErrorKind.NotFound" /> error.</summary>
    internal static RtError NotFound(string message, IReadOnlyDictionary<string, string>? context = null)
        => new(RtErrorKind.NotFound, message, context);

    /// <summary>Creates a <see cref="RtErrorKind.AmbiguousMatch" /> error.</summary>
    internal static RtError AmbiguousMatch(string message, IReadOnlyDictionary<string, string>? context = null)
        => new(RtErrorKind.AmbiguousMatch, message, context);

    /// <summary>Creates a failure returned by an external executable or service.</summary>
    internal static RtError ExternalToolFailure(
        string message,
        IReadOnlyDictionary<string, string>? context = null)
        => new(RtErrorKind.ExternalToolFailure, message, context);
}

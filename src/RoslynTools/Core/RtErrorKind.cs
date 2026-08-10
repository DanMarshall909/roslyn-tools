namespace RoslynTools.Core;

/// <summary>
///     Classifies a routine, user-facing failure carried by <see cref="RtError" />.
///     These are expected error states (bad input, nothing found) — not invariant
///     violations, which remain exceptions.
/// </summary>
internal enum RtErrorKind
{
    /// <summary>The requested symbol, type, or resource was not found.</summary>
    NotFound,

    /// <summary>The user supplied invalid or self-contradictory input.</summary>
    InvalidInput,

    /// <summary>A name matched multiple candidates and could not be disambiguated.</summary>
    AmbiguousMatch,

    /// <summary>The solution/workspace failed to load (missing or malformed).</summary>
    WorkspaceLoadFailure,

    /// <summary>
    ///     A required external tool (e.g. the <c>gh</c> CLI) was unavailable, unauthenticated,
    ///     or failed. Distinct from <see cref="WorkspaceLoadFailure" /> because remediation is
    ///     "fix your environment," not "fix your input."
    /// </summary>
    ExternalToolFailure
}

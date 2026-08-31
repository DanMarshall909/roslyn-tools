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

    /// <summary>Repository-controlled evaluation was requested without host-authorized trust.</summary>
    TrustRequired,

    /// <summary>A revision-bound identity or result no longer matches the current source.</summary>
    Stale,

    /// <summary>An external executable or service was unavailable or rejected the operation.</summary>
    ExternalToolFailure
}

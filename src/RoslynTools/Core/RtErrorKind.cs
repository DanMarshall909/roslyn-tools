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
    ExternalToolFailure,

    /// <summary>An existing operation identity names different canonical work.</summary>
    OperationConflict,

    /// <summary>The authenticated principal is not authorized to observe or change the operation.</summary>
    OperationAccessDenied,

    /// <summary>The durable outcome cannot yet be determined safely.</summary>
    OperationIndeterminate,

    /// <summary>The protected coordination boundary cannot be proven safe.</summary>
    UnsafeCoordination,

    /// <summary>The durable coordination store is temporarily unavailable.</summary>
    CoordinationUnavailable,

    /// <summary>Persisted coordination evidence is corrupt or internally inconsistent.</summary>
    CorruptState,

    /// <summary>The exact immutable plan is unavailable, expired, incompatible, or unverified.</summary>
    PlanUnavailable,

    /// <summary>The requested operation or mode is not supported by this host.</summary>
    Unsupported
}
